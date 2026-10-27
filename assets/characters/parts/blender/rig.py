"""Rig the assembled DeepSeek battle outfit: a Unity humanoid skeleton, a tail chain and skin weights for every part.

Run: blender -b MODEL.blend -P rig.py -- ASSEMBLY_JSON OUT.blend REPORT.json
Joint positions come from cross-sections of the body, boots and tail (probe.py), in metres, Z up, front -Y.
The body is skinned with bone heat; close-fitting clothes, and hair lying on the body, copy the weights of
the nearest body surface; the skirt, apron, head pieces and the rest of the hair get rule weights, and the
tail is weighted along its centreline. Then the body and stocking surfaces the assembly masked are deleted.
"""
import json
import math
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

JSON_PATH, OUT, REPORT = sys.argv[sys.argv.index('--') + 1:]
J = json.load(open(JSON_PATH))
M = 1.51 / (J['landmarks']['crown'] + J['landmarks']['lift'])   # metres per assembly unit
TAIL_N = 16
REST_TOP = 72   # degrees below horizontal where the reshaped tail leaves the skirt
TAIL_SCALE = 0.65   # the rigged tail against the assembled Tripo part, about its root: at full size it was too big
CX = -0.006   # the model's centre line in x (legs, arms and head are centred 3-7 mm left of 0)

# joints measured from the body cross-sections (probe.py): left side; the right side mirrors about CX
J_L = {
    'UpperLeg': (0.072, 0.008, 0.830), 'LowerLeg': (0.057, 0.032, 0.470), 'Foot': (0.052, 0.040, 0.110),
    'Toes': (0.050, -0.050, 0.030), 'ToesEnd': (0.048, -0.105, 0.030),
    'Shoulder': (0.020, 0.030, 1.185), 'UpperArm': (0.115, 0.045, 1.168), 'LowerArm': (0.305, 0.047, 1.162),
    'Hand': (0.475, 0.054, 1.157),
}
J_C = {'Hips': (0.005, 0.830), 'Spine': (-0.005, 0.915), 'Chest': (-0.012, 1.005), 'UpperChest': (-0.005, 1.090),
       'Neck': (0.035, 1.200), 'Head': (0.035, 1.270), 'HeadEnd': (0.030, 1.510)}
FOOT = {'heel': 0.092, 'ball': -0.050, 'toe': -0.105}   # boot sole points on the floor (y)


def side_pt(name, s):
    x, y, z = J_L[name]
    return Vector((CX + s * x, y, z))


def mid_pt(name):
    y, z = J_C[name]
    return Vector((CX, y, z))


def verts(o):
    v = np.empty(len(o.data.vertices) * 3)
    o.data.vertices.foreach_get('co', v)
    return v.reshape(-1, 3)


def mesh_edges(o):
    e = np.empty(len(o.data.edges) * 2, dtype=np.int64)
    o.data.edges.foreach_get('vertices', e)
    return e.reshape(-1, 2)


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


O = {o.name: o for o in bpy.data.objects if o.type == 'MESH'}

# --- tail centreline: centroids of z slabs down the hanging part, y slabs along the fluke, then resampled ---
T = verts(O['tail-glow'])
pts = []
for z in np.arange(0.78, 0.11, -0.02):
    s = T[np.abs(T[:, 2] - z) < 0.008]
    if len(s):
        pts.append((CX, s[:, 1].mean(), z))
bend = Vector(pts[-1])
for y in np.arange(bend.y + 0.08, T[:, 1].max() - 0.015, 0.02):
    s = T[(np.abs(T[:, 1] - y) < 0.008) & (T[:, 2] < 0.15)]
    if len(s):
        pts.append((CX, y, (s[:, 2].min() + s[:, 2].max()) / 2))
pts = np.array(pts)
for _ in range(3):   # light smoothing, ends fixed
    pts[1:-1] = (pts[:-2] + 2 * pts[1:-1] + pts[2:]) / 4
seg = np.linalg.norm(np.diff(pts, axis=0), axis=1)
arc = np.concatenate([[0], np.cumsum(seg)])


def smooth_scalar(s, E, n_iter):
    deg = np.maximum(np.bincount(E.ravel(), minlength=len(s)), 1)
    for _ in range(n_iter):
        acc = np.zeros_like(s)
        np.add.at(acc, E[:, 0], s[E[:, 1]])
        np.add.at(acc, E[:, 1], s[E[:, 0]])
        s = 0.5 * s + 0.5 * acc / deg
    return s


def tail_reshape(o):
    """Lift the tail off the floor into a smooth arc, as a spine deformation rather than joint rotations.

    Every vertex keeps its offset from the centreline (lateral, normal and tangent) at its arc parameter s;
    the centreline is re-drawn with a gentler angle profile and the offsets are put back on it. The sharp
    bend where the tail met the floor is spread out once here, so the animation only bends it a little.
    Returns each vertex's s, the new centreline sampled at S and where the fluke starts."""
    P = verts(o)
    L = arc[-1]
    S = np.linspace(0, L, 800)
    C0 = np.stack([np.interp(S, arc, pts[:, k]) for k in range(3)], 1)
    T0 = np.gradient(C0, S, axis=0)
    T0[:, 0] = 0
    T0 /= np.linalg.norm(T0, axis=1, keepdims=True)
    s = np.empty(len(P))
    for i0 in range(0, len(P), 3000):   # nearest centreline point in the side (y, z) plane
        d2 = ((P[i0:i0 + 3000, None, 1:] - C0[None, :, 1:]) ** 2).sum(2)
        s[i0:i0 + 3000] = S[d2.argmin(1)]
    s_fluke = S[np.argmax(C0[:, 2] < 0.07)]
    # fluke lobes reach forward under the bend; they belong to the fluke. Only near the floor: the root is as wide
    # under the skirt, and sending those vertices to the fluke flung them out by the left knee.
    wing = (np.abs(P[:, 0] - CX) > 0.07) & (P[:, 2] < 0.2)
    s = np.where(wing, np.maximum(s, s_fluke), s)
    s = smooth_scalar(s, mesh_edges(o), 15)

    def frame(C, Tn, sv):
        c = np.stack([np.interp(sv, S, C[:, k]) for k in range(3)], 1)
        t = np.stack([np.interp(sv, S, Tn[:, k]) for k in range(3)], 1)
        t /= np.linalg.norm(t, axis=1, keepdims=True)
        n = np.stack([np.zeros(len(sv)), -t[:, 2], t[:, 1]], 1)   # X x t: up/back side of the tail
        return c, t, n

    c0, t0, n0 = frame(C0, T0, s)
    d = P - c0
    u, v, w = d[:, 0], (d * n0).sum(1), (d * t0).sum(1)
    th0 = np.degrees(np.arctan2(-T0[:, 2], T0[:, 1]))          # degrees below horizontal, toward the back
    ease = 0.5 + 0.5 * np.cos(np.pi * np.clip((S - 0.12) / (s_fluke - 0.12), 0, 1))
    target = np.where(S < s_fluke, REST_TOP * ease, 0.0)
    b = smoothstep(0.0, 0.12, S)
    th1 = np.radians(th0 * (1 - b) + target * b)
    T1 = np.stack([np.zeros(len(S)), np.cos(th1), -np.sin(th1)], 1)
    C1 = C0[0] + np.concatenate([[[0, 0, 0]], np.cumsum((T1[1:] + T1[:-1]) / 2 * np.diff(S)[:, None], 0)])
    c1, t1, n1 = frame(C1, T1, s)
    P1 = c1 + u[:, None] * np.array([1.0, 0, 0]) + v[:, None] * n1 + w[:, None] * t1
    o.data.vertices.foreach_set('co', P1.ravel())
    o.data.update()
    return s, S, C1, s_fluke


tail_s, tail_S, tail_C, tail_fluke = tail_reshape(O['tail-glow'])
tail_joints = [Vector(np.array([np.interp(t, tail_S, tail_C[:, k]) for k in range(3)]))
               for t in np.linspace(0, arc[-1], TAIL_N + 1)]
TP = verts(O['tail-glow'])
fins = {}
for side, sgn in (('L', 1), ('R', -1)):
    lobe = TP[sgn * (TP[:, 0] - CX) > 0.07]
    tip = lobe[np.argmax(sgn * (lobe[:, 0] - CX))]
    root_s = tail_fluke + 0.10
    root = Vector([np.interp(root_s, tail_S, tail_C[:, k]) for k in range(3)]) + Vector((sgn * 0.045, 0, 0))
    fins[side] = (root, Vector(tip), min(int(root_s / (arc[-1] / TAIL_N)), TAIL_N - 1))
print('tail reshaped: fluke starts %.2f m along, fluke centre height %.3f m, lowest vertex %.3f m' % (
    tail_fluke, tail_C[-1, 2], TP[:, 2].min()), flush=True)


def hand_layout(o, sgn):
    """Joints of one glove. The four fingers lie side by side, touching, each about a quarter of the width; the
    thumb runs along the front edge."""
    P = verts(o)
    ax = sgn * (P[:, 0] - CX)                       # distance out along the arm
    tip_all = ax.max()
    band = P[(ax > tip_all - 0.055) & (ax < tip_all - 0.040)]
    ylo, yhi = band[:, 1].min(), band[:, 1].max()
    zc = float(band[:, 2].mean())
    mcp = tip_all - 0.072
    out = {'ylo': float(ylo), 'yhi': float(yhi), 'fingers': {}}
    for k, name in enumerate(('Index', 'Middle', 'Ring', 'Little')):
        yc = ylo + (k + 0.5) / 4 * (yhi - ylo)
        sel = (np.abs(P[:, 1] - yc) < (yhi - ylo) / 8) & (ax > mcp)
        tip = ax[sel].max()
        m = mcp - (0.002, 0.0, 0.003, 0.009)[k]
        out['fingers'][name] = [(m + f * (tip - m), yc, zc) for f in (0.0, 0.45, 0.75, 1.0)]
    front = (P[:, 1] < ylo - 0.003) & (ax > 0.485)
    t_tip = np.argmax(np.where(front, ax, -1))
    base_sel = front & (ax < 0.50)
    tb = (0.488, float(P[base_sel, 1].mean()) if base_sel.any() else ylo - 0.01, zc - 0.006)
    tt = (float(ax[t_tip]), float(P[t_tip, 1]), float(P[t_tip, 2]))
    out['fingers']['Thumb'] = [tuple(tb[j] + f * (tt[j] - tb[j]) for j in range(3)) for f in (0.0, 0.4, 0.75, 1.0)]
    out['mcp'] = float(mcp)
    return out


HANDS = {'Left': hand_layout(O['glove-L'], 1), 'Right': hand_layout(O['glove-R'], -1)}
for side, h in HANDS.items():
    print(side, 'fingers y %.3f-%.3f, mcp %.3f, tips %s' % (h['ylo'], h['yhi'], h['mcp'],
          {k: round(v[-1][0], 3) for k, v in h['fingers'].items()}), flush=True)


def hand_pt(side, finger, j):
    sgn = 1 if side == 'Left' else -1
    a, y, z = HANDS[side]['fingers'][finger][j]
    return Vector((CX + sgn * a, y, z))

# --- armature ---
bpy.ops.object.select_all(action='DESELECT')
bpy.ops.object.armature_add(enter_editmode=True, location=(0, 0, 0))
arm = bpy.context.object
arm.name = arm.data.name = 'DeepSeek'
eb = arm.data.edit_bones
eb.remove(eb[0])


def bone(name, head, tail, parent=None, roll=Vector((0, -1, 0)), connect=False):
    b = eb.new(name)
    b.head, b.tail = head, tail
    d = (tail - head).normalized()
    if abs(d.dot(roll)) > 0.95:
        roll = Vector((0, 0, 1))
    b.align_roll(roll)
    if parent:
        b.parent = eb[parent]
        b.use_connect = connect
    return b


bone('Hips', mid_pt('Hips'), mid_pt('Spine'))
bone('Spine', mid_pt('Spine'), mid_pt('Chest'), 'Hips', connect=True)
bone('Chest', mid_pt('Chest'), mid_pt('UpperChest'), 'Spine', connect=True)
bone('UpperChest', mid_pt('UpperChest'), mid_pt('Neck'), 'Chest', connect=True)
bone('Neck', mid_pt('Neck'), mid_pt('Head'), 'UpperChest', connect=True)
bone('Head', mid_pt('Head'), mid_pt('HeadEnd'), 'Neck', connect=True)
for side, s in (('Left', 1), ('Right', -1)):
    bone(side + 'UpperLeg', side_pt('UpperLeg', s), side_pt('LowerLeg', s), 'Hips')
    bone(side + 'LowerLeg', side_pt('LowerLeg', s), side_pt('Foot', s), side + 'UpperLeg', connect=True)
    bone(side + 'Foot', side_pt('Foot', s), side_pt('Toes', s), side + 'LowerLeg', roll=Vector((0, 0, 1)), connect=True)
    bone(side + 'Toes', side_pt('Toes', s), side_pt('ToesEnd', s), side + 'Foot', roll=Vector((0, 0, 1)), connect=True)
    bone(side + 'Shoulder', side_pt('Shoulder', s), side_pt('UpperArm', s), 'UpperChest')
    bone(side + 'UpperArm', side_pt('UpperArm', s), side_pt('LowerArm', s), side + 'Shoulder', connect=True)
    bone(side + 'LowerArm', side_pt('LowerArm', s), side_pt('Hand', s), side + 'UpperArm', connect=True)
    bone(side + 'Hand', side_pt('Hand', s), hand_pt(side, 'Middle', 0), side + 'LowerArm', connect=True)
    for finger in ('Thumb', 'Index', 'Middle', 'Ring', 'Little'):
        for j, part in enumerate(('Proximal', 'Intermediate', 'Distal')):
            bone(side + finger + part, hand_pt(side, finger, j), hand_pt(side, finger, j + 1),
                 side + 'Hand' if j == 0 else side + finger + ('Proximal', 'Intermediate')[j - 1],
                 roll=Vector((0, 0, 1)), connect=j > 0)
for i in range(TAIL_N):
    h, t = tail_joints[i], tail_joints[i + 1]
    roll = Vector((1, 0, 0)).cross(t - h).normalized()
    bone('Tail_%d' % i, h, t, 'Hips' if i == 0 else 'Tail_%d' % (i - 1), roll=roll, connect=i > 0)
for side, (h, t, parent) in fins.items():
    bone('TailFin_' + side, h, t, 'Tail_%d' % parent, roll=Vector((0, 0, 1)))
bpy.ops.object.mode_set(mode='OBJECT')
BONE_NAMES = [b.name for b in arm.data.bones]
GI = {n: i for i, n in enumerate(BONE_NAMES)}
G = len(BONE_NAMES)

# --- body: bone heat, tail bones left out ---
for b in arm.data.bones:
    b.use_deform = not (b.name.startswith('Tail') or any(f in b.name for f in ('Thumb', 'Index', 'Middle', 'Ring', 'Little')))
body = O['body']
bpy.ops.object.select_all(action='DESELECT')
body.select_set(True)
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.parent_set(type='ARMATURE_AUTO')
for b in arm.data.bones:
    b.use_deform = True

Wb = np.zeros((len(body.data.vertices), G))
for v in body.data.vertices:
    for g in v.groups:
        Wb[v.index, GI[body.vertex_groups[g.group].name]] = g.weight
unweighted = int((Wb.sum(1) < 1e-4).sum())
print('body heat weights: %d of %d vertices unweighted' % (unweighted, len(Wb)), flush=True)
if unweighted:   # fill gaps from weighted neighbours
    E = mesh_edges(body)
    for _ in range(50):
        empty = Wb.sum(1) < 1e-4
        if not empty.any():
            break
        acc = np.zeros_like(Wb)
        np.add.at(acc, E[:, 0], Wb[E[:, 1]])
        np.add.at(acc, E[:, 1], Wb[E[:, 0]])
        Wb[empty] = acc[empty]
        Wb /= np.maximum(Wb.sum(1, keepdims=True), 1e-9)
    Wb[Wb.sum(1) < 1e-4, GI['Hips']] = 1

body.data.calc_loop_triangles()
BV = verts(body)
tris = np.array([t.vertices[:] for t in body.data.loop_triangles])
bvh = BVHTree.FromPolygons([Vector(v) for v in BV], tris.tolist())


def transfer(P):
    """Body weights at the nearest body surface point, and the distance to it."""
    W = np.zeros((len(P), G))
    dist = np.zeros(len(P))
    for i, p in enumerate(P):
        co, _, f, d = bvh.find_nearest(Vector(p))
        a, b, c = BV[tris[f]]
        v0, v1, v2 = b - a, c - a, np.array(co) - a
        d00, d01, d11 = v0 @ v0, v0 @ v1, v1 @ v1
        d20, d21 = v2 @ v0, v2 @ v1
        den = d00 * d11 - d01 * d01
        l1 = (d11 * d20 - d01 * d21) / den if den else 0.0
        l2 = (d00 * d21 - d01 * d20) / den if den else 0.0
        bary = np.clip([1 - l1 - l2, l1, l2], 0, 1)
        W[i] = bary @ Wb[tris[f]] / max(bary.sum(), 1e-9)
        dist[i] = d
    return W, dist


def mesh_bvh(o):
    return BVHTree.FromPolygons([Vector(v) for v in verts(o)], [list(f.vertices) for f in o.data.polygons])


def transfer_from(P, src, Wsrc):
    """Weights of another (already weighted) mesh at its nearest surface point, and the distance to it."""
    S = verts(src)
    src.data.calc_loop_triangles()
    st = np.array([t.vertices[:] for t in src.data.loop_triangles])
    sb = BVHTree.FromPolygons([Vector(v) for v in S], st.tolist())
    W = np.zeros((len(P), G))
    dist = np.zeros(len(P))
    for i, p in enumerate(P):
        co, _, f, d = sb.find_nearest(Vector(p))
        a, b, c = S[st[f]]
        v0, v1, v2 = b - a, c - a, np.array(co) - a
        d00, d01, d11 = v0 @ v0, v0 @ v1, v1 @ v1
        d20, d21 = v2 @ v0, v2 @ v1
        den = d00 * d11 - d01 * d01
        l1 = (d11 * d20 - d01 * d21) / den if den else 0.0
        l2 = (d00 * d21 - d01 * d20) / den if den else 0.0
        bary = np.clip([1 - l1 - l2, l1, l2], 0, 1)
        W[i] = bary @ Wsrc[st[f]] / max(bary.sum(), 1e-9)
        dist[i] = d
    return W, dist


def tuck(inner, outer, margin=0.004, reach=0.02):
    """Press the parts of `inner` that come out through `outer` back under it. Heights are measured from the body
    surface along the line to each vertex, so a puff sleeve is pressed toward the arm and a blouse toward the chest.
    The press fades out over the next few rings of edges past the edge of `outer`, so no step shows there."""
    P = verts(inner)
    ob = mesh_bvh(outer)
    E = mesh_edges(inner)
    push = np.zeros(len(P))
    U = np.zeros_like(P)
    D = np.zeros(len(P))
    for i, p in enumerate(P):
        co, _, _, d = bvh.find_nearest(Vector(p))
        if d < 1e-4:
            continue
        u = (Vector(p) - co) / d
        U[i], D[i] = u, d
        hit = ob.ray_cast(co + u * 1e-4, u, d + reach)
        if hit[0] is not None:
            push[i] = max(0.0, d - ((hit[0] - co).length - margin))
    n_hit = int((push > 0).sum())
    for _ in range(5):
        nb = np.zeros_like(push)
        np.maximum.at(nb, E[:, 0], push[E[:, 1]] * 0.75)
        np.maximum.at(nb, E[:, 1], push[E[:, 0]] * 0.75)
        push = np.maximum(push, nb)
    push = np.minimum(push, np.maximum(D - 0.002, 0))
    inner.data.vertices.foreach_set('co', (P - U * push[:, None]).ravel())
    inner.data.update()
    return n_hit, float(push.max())


def smooth(W, E, n_iter, f=0.5):
    cols = np.nonzero(W.sum(0) > 0)[0]
    w = W[:, cols]
    deg = np.bincount(E.ravel(), minlength=len(W)).astype(float)
    for _ in range(n_iter):
        acc = np.zeros_like(w)
        np.add.at(acc, E[:, 0], w[E[:, 1]])
        np.add.at(acc, E[:, 1], w[E[:, 0]])
        nb = acc / np.maximum(deg, 1)[:, None]
        w = np.where(deg[:, None] > 0, (1 - f) * w + f * nb, w)
    W = W.copy()
    W[:, cols] = w
    return W


def onehot(n, name):
    W = np.zeros((n, G))
    W[:, GI[name]] = 1
    return W


def write(o, W):
    """Keep the 4 largest weights per vertex, normalise, and write vertex groups; add the armature."""
    W = np.where(W < 1e-3, 0, W)
    drop = np.argsort(-W, axis=1)[:, 4:]
    np.put_along_axis(W, drop, 0, axis=1)
    W /= np.maximum(W.sum(1, keepdims=True), 1e-9)
    o.vertex_groups.clear()
    for gi, name in enumerate(BONE_NAMES):
        nz = np.nonzero(W[:, gi] > 1e-4)[0]
        if not len(nz):
            continue
        vg = o.vertex_groups.new(name=name)
        q = np.round(W[nz, gi], 3)
        for val in np.unique(q):
            vg.add(nz[q == val].tolist(), float(val), 'REPLACE')
    if not any(m.type == 'ARMATURE' for m in o.modifiers):
        m = o.modifiers.new('Armature', 'ARMATURE')
        m.object = arm
    o.parent = arm
    o.matrix_parent_inverse.identity()
    return W


def glove_weights(o, P, W, side):
    """Bone heat on a copy of the glove with only this hand's bones (and the forearm) deforming: the fingers and
    thumb get smooth weights from their own bones, the palm shares out between them by distance. The cuff keeps the
    weights from the body surface so it stays with the sleeve and wrist."""
    sgn = 1 if side == 'Left' else -1
    keep = {side + 'LowerArm', side + 'Hand'} | {side + f + p for f in ('Thumb', 'Index', 'Middle', 'Ring', 'Little')
                                                 for p in ('Proximal', 'Intermediate', 'Distal')}
    # bone heat finds no solution on a mesh this small, so it runs on copies of the glove and armature scaled 10x
    a2 = arm.copy()
    a2.data = arm.data.copy()
    tmp = o.copy()
    tmp.data = o.data.copy()
    tmp.vertex_groups.clear()
    tmp.modifiers.clear()
    tmp.parent = None
    for c in (a2, tmp):
        bpy.context.scene.collection.objects.link(c)
        c.scale = (10, 10, 10)
    bpy.ops.object.select_all(action='DESELECT')
    a2.select_set(True)
    tmp.select_set(True)
    bpy.context.view_layer.objects.active = a2
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    for b in a2.data.bones:
        b.use_deform = b.name in keep
    bpy.ops.object.select_all(action='DESELECT')
    tmp.select_set(True)
    a2.select_set(True)
    bpy.context.view_layer.objects.active = a2
    bpy.ops.object.parent_set(type='ARMATURE_AUTO')
    H = np.zeros_like(W)
    for v in tmp.data.vertices:
        for g in v.groups:
            H[v.index, GI[tmp.vertex_groups[g.group].name]] = g.weight
    arm_data = a2.data
    bpy.data.objects.remove(tmp)
    bpy.data.objects.remove(a2)
    bpy.data.armatures.remove(arm_data)
    missing = H.sum(1) < 1e-4
    print('%s glove heat: %d of %d vertices unweighted' % (side, missing.sum(), len(H)), flush=True)
    H[missing] = W[missing]
    H /= np.maximum(H.sum(1, keepdims=True), 1e-9)
    wrist = sgn * (arm.data.bones[side + 'Hand'].head_local.x - CX)
    ax = sgn * (P[:, 0] - CX)
    t = smoothstep(wrist - 0.004, wrist + 0.012, ax)[:, None]
    return t * H + (1 - t) * W


assert all(o.matrix_world == Matrix.Identity(4) for o in O.values()), 'meshes must have their transforms applied'
n_hit, deepest = tuck(O['blouse'], O['capelet'])
print('blouse tucked under the capelet: %d vertices came through, pressed in up to %.1f mm' % (n_hit, deepest * 1000), flush=True)
report = {'body_unweighted': unweighted, 'tail_length_m': round(float(arc[-1]), 3), 'parts': {},
          'blouse tucked under capelet': {'vertices': n_hit, 'max_mm': round(deepest * 1000, 1)}}
write(body, Wb)
head_z = J_C['Head'][1]

WEIGHTS = {}
order = ['blouse'] + [nm for nm in O if nm not in ('body', 'blouse')]
for name in order:
    o = O[name]
    P = verts(o)
    n = len(P)
    if name == 'capelet':
        # the capelet lies on the blouse: where it does, it takes the blouse's weights, so over the sleeves it moves
        # with them instead of the sleeve swinging out through it
        Wbody, db = transfer(P)
        Wbl, dbl = transfer_from(P, O['blouse'], WEIGHTS['blouse'])
        on = (1 - smoothstep(0.015, 0.04, dbl))[:, None]
        W = smooth(on * Wbl + (1 - on) * Wbody, mesh_edges(o), 8)
        how = 'blouse weights within 1.5-4 cm of the blouse, else nearest body surface'
    elif name in ('blouse', 'corset', 'neck-bow', 'stockings', 'garter-L', 'garter-R', 'boots',
                'glove-L', 'glove-R', 'wrist-terminal'):
        W, d = transfer(P)
        W = smooth(W, mesh_edges(o), 8)
        how = 'nearest body surface, max gap %.3f m' % d.max()
        if name.startswith('glove'):
            W = glove_weights(o, P, W, 'Left' if name == 'glove-L' else 'Right')
            how += '; hand and fingers by bone heat on the glove itself'
    elif name in ('skirt', 'apron'):
        zt, zb = P[:, 2].max(), P[:, 2].min()
        t = np.clip((zt - P[:, 2]) / (zt - zb), 0, 1)
        leg = 0.55 * smoothstep(0.15, 1.0, t)
        left = smoothstep(CX - 0.05, CX + 0.05, P[:, 0])
        W = np.zeros((n, G))
        W[:, GI['Hips']] = 1 - leg
        W[:, GI['LeftUpperLeg']] = leg * left
        W[:, GI['RightUpperLeg']] = leg * (1 - left)
        how = 'hips, thighs up to 55% at the hem'
    elif name == 'hair':
        W, d = transfer(P)
        rule = np.zeros((n, G))
        up = smoothstep(head_z - 0.10, head_z + 0.02, P[:, 2])
        rule[:, GI['Head']] = up
        rule[:, GI['UpperChest']] = 1 - up
        near = (1 - smoothstep(0.015, 0.06, d))[:, None]
        W = smooth(near * W + (1 - near) * rule, mesh_edges(o), 6)
        how = 'head/upper chest by height; hair within 6 cm of the body follows the body surface'
    elif name == 'head':
        W, d = transfer(P)
        up = smoothstep(head_z - 0.03, head_z + 0.02, P[:, 2])[:, None]
        W = up * onehot(n, 'Head') + (1 - up) * W
        how = 'head; the neck stump below the jaw follows the body neck'
    elif name == 'tail-glow':
        # spread over three bones by the arc parameter from the reshape (a quadratic B-spline on the bone centres),
        # so a bend at one joint curves the mesh over a bone and a half instead of creasing it; the fluke lobes go to
        # the fins. With Hips at the root or a fin on the fluke a vertex has at most four bones, as Unity skins.
        s = tail_s
        L = arc[-1] / TAIL_N
        f = np.clip(s / L - 0.5, 0, TAIL_N - 1)
        W = np.zeros((n, G))
        for k in range(TAIL_N):
            x = np.abs(f - k)
            W[:, GI['Tail_%d' % k]] = np.where(x < 0.5, 0.75 - x * x, np.where(x < 1.5, 0.5 * (1.5 - x) ** 2, 0))
        W /= W.sum(1, keepdims=True)
        root = (1 - smoothstep(0.0, 0.06, s))[:, None]
        W = (1 - root) * W + root * onehot(n, 'Hips')
        lat = P[:, 0] - CX
        fin = (smoothstep(0.06, 0.16, np.abs(lat)) * smoothstep(tail_fluke + 0.04, tail_fluke + 0.12, s))[:, None]
        W = (1 - fin) * W + fin * np.where(lat[:, None] > 0, onehot(n, 'TailFin_L'), onehot(n, 'TailFin_R'))
        how = 'tail chain by arc length, hips at the root, fluke lobes on TailFin_L/R'
    else:   # headband, fin ears, face decals
        W = onehot(n, 'Head')
        how = 'head'
    WEIGHTS[name] = write(o, W)
    report['parts'][name] = how
    print(name, how, flush=True)

# --- delete what the assembly masked: rules are in three.js axes and assembly units ---


def clip_delete(o, rules):
    bm = bmesh.new()
    bm.from_mesh(o.data)
    dead = []
    for f in bm.faces:
        c = f.calc_center_median()
        val = {'x': c.x / M, 'y': c.z / M, 'z': -c.y / M}
        if not all((val[ax] > v) if op == '>' else (val[ax] < v) for (ax, op), v in ((r[0], r[1]) for r in rules)):
            dead.append(f)
    bmesh.ops.delete(bm, geom=dead, context='FACES')
    bm.to_mesh(o.data)
    bm.free()
    return len(dead)


for name, p in J['parts'].items():
    if p.get('clip') and name in O:
        k = clip_delete(O[name], p['clip'])
        report['parts'][name + ' masked faces deleted'] = k

# The tail is reshaped, boned and weighted at the part's assembled size, where the centreline probes and the
# fluke and fin thresholds above were measured; then mesh and tail bones shrink together about the root, which
# leaves the weights as they are.
root = tail_joints[0].copy()
tail = O['tail-glow']
tail.data.vertices.foreach_set('co', (np.array(root) + (verts(tail) - np.array(root)) * TAIL_SCALE).ravel())
tail.data.update()
bpy.ops.object.select_all(action='DESELECT')
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='EDIT')
# all ends first: setting a connected bone's head also moves its parent's tail
ends = {eb.name: (root + (eb.head - root) * TAIL_SCALE, root + (eb.tail - root) * TAIL_SCALE)
        for eb in arm.data.edit_bones if eb.name.startswith('Tail')}
for name, (h, t) in ends.items():
    arm.data.edit_bones[name].head, arm.data.edit_bones[name].tail = h, t
bpy.ops.object.mode_set(mode='OBJECT')
tail_joints = [root + (v - root) * TAIL_SCALE for v in tail_joints]
report['tail_length_m'] = round(float(arc[-1]) * TAIL_SCALE, 3)
print('tail scaled to %.2f: %.2f m long, fluke centre %.3f m high, lowest vertex %.3f m' % (
    TAIL_SCALE, arc[-1] * TAIL_SCALE, tail_joints[-1].z, verts(tail)[:, 2].min()), flush=True)

arm['rig'] = json.dumps({'feet': {
    side: {'ankle': list(side_pt('Foot', s)), 'heel': [CX + s * 0.052, FOOT['heel'], 0.0],
           'ball': [CX + s * 0.050, FOOT['ball'], 0.0], 'toe': [CX + s * 0.048, FOOT['toe'], 0.0]}
    for side, s in (('Left', 1), ('Right', -1))}})
report['tail_joints'] = [[round(c, 3) for c in v] for v in tail_joints]
report['hands'] = HANDS
bpy.ops.wm.save_as_mainfile(filepath=OUT)
json.dump(report, open(REPORT, 'w'), indent=1)
print('saved', OUT)
