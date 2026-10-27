"""Walk and run cycles for the DeepSeek rig, in place, with the foot IK solved here and a lagged-wave tail.

Run: blender -b RIGGED.blend -P anim.py -- OUT.blend REPORT.json
Every frame is posed in armature space as a rotation D per bone (posed frame = D x rest frame): pelvis bob,
sway and turn; analytic two-bone leg IK onto planted or swinging foot targets with heel strike and toe-off
roll; a counter-turning chest; arm swing; and a tail that is lifted into an arc and swings and bobs with a
delay that grows toward the tip. The poses are keyed as quaternions, one action per cycle, last frame = first.
"""
import json
import math
import sys

import bpy
from mathutils import Matrix, Vector

OUT, REPORT = sys.argv[sys.argv.index('--') + 1:]
arm = bpy.data.objects['DeepSeek']
RIG = json.loads(arm['rig'])
BONES = {b.name: b for b in arm.data.bones}
R3 = {n: b.matrix_local.to_3x3() for n, b in BONES.items()}
HEAD = {n: b.head_local.copy() for n, b in BONES.items()}
TAILS = sorted((n for n in BONES if n.startswith('Tail_')), key=lambda n: int(n[5:]))
ORDER = []   # parents before children


def visit(b):
    ORDER.append(b.name)
    for c in b.children:
        visit(c)


for b in arm.data.bones:
    if b.parent is None:
        visit(b)

I3 = Matrix.Identity(3)
X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))


def rot(axis, deg):
    return Matrix.Rotation(math.radians(deg), 3, axis)


def smooth(t):
    t = min(max(t, 0.0), 1.0)
    return t * t * (3 - 2 * t)


def lerp(a, b, t):
    return a + (b - a) * t


def frame_rot(rest_dir, rest_hint, new_dir, new_hint):
    """Rotation taking the frame (rest_dir, rest_hint) to (new_dir, new_hint); the hints fix the twist."""
    def frame(y, h):
        y = y.normalized()
        z = (h - y * h.dot(y)).normalized()
        return Matrix((y.cross(z), y, z)).transposed()
    return frame(new_dir, new_hint) @ frame(rest_dir, rest_hint).transposed()


GAITS = {
    # stride = ground covered per cycle (m); stance = share of the cycle a foot is planted
    'Walk': dict(frames=32, stride=0.80, stance=0.62, base=-0.035, bob=0.012, low=0.0, sway=0.018, yaw=6.0,
                 roll=3.5, lean=3.0, arm_down=60, arm_fwd=12, arm_swing=13, elbow=16, elbow_swing=14, strike=14, off=30,
                 lift=0.075, kick=0.0, toe_out=4, fingers=(28, 38, 22), thumb=(14, 18, 12),
                 tail=dict(root_lift=0.0, bend=-0.6, curl=3.5, side=6.0, side_lag=0.05, side_phase=0.12,
                           up=2.2, up_lag=0.035, up_phase=0.08, fin=12, fin_bias=0)),
    'Run': dict(frames=20, stride=1.60, stance=0.36, base=-0.075, bob=0.024, low=0.18, sway=0.012, yaw=9.0,
                roll=4.0, lean=11.0, arm_down=64, arm_fwd=14, arm_swing=22, elbow=72, elbow_swing=8, strike=6, off=42,
                lift=0.10, kick=0.24, toe_out=3, fingers=(50, 65, 40), thumb=(22, 30, 20),
                tail=dict(root_lift=6.0, bend=0.6, curl=2.5, side=4.5, side_lag=0.045, side_phase=0.12,
                          up=3.2, up_lag=0.032, up_phase=0.1, fin=15, fin_bias=4)),
}


def tail_base(g):
    """Per-joint pitch (about X, degrees) on top of the rest arc: the root tips the whole tail and every joint
    adds the same bend, half as much along the flat fluke, so the curve changes evenly with no kink.
    Negative bends lower the tail (walk), positive ones lift it to stream behind (run). The curl lifts the last
    third more and more toward the tip, so the fluke turns up instead of lying out straight."""
    joints = [HEAD[n] for n in TAILS] + [BONES[TAILS[-1]].tail_local.copy()]
    out = []
    for i in range(len(TAILS)):
        d = joints[i + 1] - joints[i]
        flat = math.degrees(math.atan2(-d.z, d.y)) < 10
        curl = g['curl'] * smooth((i / (len(TAILS) - 1) - 0.55) / 0.45)
        out.append(curl + (g['root_lift'] if i == 0 else g['bend'] * (0.5 if flat else 1.0)))
    return out


def foot_state(g, p, side):
    """Ankle target, foot pitch (toes up +), relative toe bend and whether the foot is planted."""
    r = RIG['feet'][side]
    A0, heel0, ball0, toe0 = (Vector(r[k]) for k in ('ankle', 'heel', 'ball', 'toe'))
    half = g['stride'] * g['stance'] / 2

    def stance(s):
        u = half - 2 * half * s                               # forward offset of the planted foot
        if s < 0.14:
            pitch, piv = g['strike'] * (1 - s / 0.14) ** 1.5, heel0
        elif s < 0.62:
            pitch, piv = 0.0, ball0
        else:
            pitch, piv = -g['off'] * smooth((s - 0.62) / 0.38), ball0
        P = piv + Vector((0, -u, 0))
        R = rot(X, -pitch)
        return P + R @ (A0 - piv), pitch

    if p < g['stance']:
        s = p / g['stance']
        A, pitch = stance(s)
        rel = max(0.0, -pitch)
        return A, pitch, rel, True
    s = (p - g['stance']) / (1 - g['stance'])
    A_end, p_end = stance(1.0)
    A_start, p_start = stance(0.0)
    e = smooth(s) if g['kick'] == 0 else smooth(s ** 1.3)
    A = A_end.lerp(A_start, e)
    A.z += g['lift'] * math.sin(math.pi * s) + g['kick'] * math.sin(math.pi * min(s / 0.75, 1)) ** 2 * (1 - s)
    pitch = lerp(p_end, p_start, smooth(s / 0.8))
    rel = g['off'] * (1 - smooth(s / 0.35))
    # keep the toe and heel off the floor while the foot swings
    R = rot(X, -pitch)
    low = min((A + R @ (q - A0)).z for q in (heel0, toe0))
    if low < 0.012:
        A.z += 0.012 - low
    return A, pitch, rel, False


def pose(g, p, tail_b):
    """Bone rotations D (armature space) and the pelvis offset for cycle phase p in [0, 1)."""
    D = {}
    c = math.cos(2 * math.pi * p)
    yaw = -g['yaw'] * c                                       # left hip forward at left heel strike (p = 0)
    roll = -g['roll'] * math.sin(2 * math.pi * p)
    hips_off = Vector((g['sway'] * math.sin(2 * math.pi * p), 0,
                       g['base'] - g['bob'] * math.cos(4 * math.pi * (p - g['low']))))
    D['Hips'] = rot(Z, yaw) @ rot(Y, roll) @ rot(X, g['lean'] * 0.3)
    lean = g['lean']
    D['Spine'] = rot(Z, 0.3 * yaw) @ rot(Y, -0.3 * roll) @ rot(X, lean * 0.5)
    D['Chest'] = rot(Z, -0.4 * yaw) @ rot(Y, -0.5 * roll) @ rot(X, lean * 0.8)
    D['UpperChest'] = rot(Z, -0.7 * yaw) @ rot(Y, -0.5 * roll) @ rot(X, lean)
    D['Neck'] = rot(Z, -0.3 * yaw) @ rot(X, lean * 0.4)
    D['Head'] = rot(X, lean * 0.1 - 2 * g['bob'] / 0.024 * math.cos(4 * math.pi * (p - g['low'])))
    chest_yaw = -0.7 * yaw

    # posed joint positions down the leg chain
    hips_head = HEAD['Hips'] + hips_off
    feet = {}
    for side, ph in (('Left', p), ('Right', (p + 0.5) % 1.0)):
        sgn = 1 if side == 'Left' else -1
        up, lo, ft = side + 'UpperLeg', side + 'LowerLeg', side + 'Foot'
        H = hips_head + D['Hips'] @ (HEAD[up] - HEAD['Hips'])
        A, pitch, rel, planted = foot_state(g, ph, side)
        L1 = (HEAD[lo] - HEAD[up]).length
        L2 = (HEAD[ft] - HEAD[lo]).length
        d = A - H
        dist = d.length
        reach = dist / (L1 + L2)
        if dist > (L1 + L2) * 0.9995:                         # out of reach: straight leg toward the target
            dist = (L1 + L2) * 0.9995
        u = d.normalized()
        pole = rot(Z, yaw * 0.5) @ Vector((sgn * 0.08, -1, 0))
        n = (pole - u * pole.dot(u)).normalized()
        a = (L1 * L1 - L2 * L2 + dist * dist) / (2 * dist)
        h = math.sqrt(max(L1 * L1 - a * a, 0.0))
        K = H + u * a + n * h
        Af = K + (H + u * dist - K).normalized() * L2
        fwd = Vector((0, -1, 0))
        D[up] = frame_rot(HEAD[lo] - HEAD[up], fwd, K - H, n)
        D[lo] = frame_rot(HEAD[ft] - HEAD[lo], fwd, Af - K, n)
        foot_yaw = rot(Z, -sgn * g['toe_out'] * 0.0)
        D[ft] = foot_yaw @ rot(X, -pitch)
        D[side + 'Toes'] = D[ft] @ rot(X, rel)
        feet[side] = dict(reach=reach, slip=(Af - A).length, planted=planted, ankle=Af)

    # arms: lowered from the T-pose, swung opposite to the legs, elbows bending more on the forward swing
    for side, sgn in (('Left', 1), ('Right', -1)):
        osc = sgn * g['arm_swing'] * math.cos(2 * math.pi * (p - 0.03))
        swing = osc - g['arm_fwd']                    # + = backward; biased forward, clear of the back hair
        fwd_amt = max(0.0, -osc / g['arm_swing'])
        lower = rot(Z, chest_yaw) @ rot(X, swing) @ rot(Y, sgn * g['arm_down'])
        D[side + 'Shoulder'] = rot(Z, chest_yaw) @ rot(X, swing * 0.12)
        D[side + 'UpperArm'] = lower
        bend = g['elbow'] + g['elbow_swing'] * fwd_amt
        D[side + 'LowerArm'] = lower @ rot(Z, -sgn * bend)
        D[side + 'Hand'] = D[side + 'LowerArm'] @ rot(X, 4)
        # fingers curl toward the palm (palm faces down in the rest pose); they touch side by side, so all four curl together
        for finger in ('Index', 'Middle', 'Ring', 'Little'):
            prev_f = D[side + 'Hand']
            for part, c in zip(('Proximal', 'Intermediate', 'Distal'), g['fingers']):
                prev_f = D[side + finger + part] = prev_f @ rot(Y, sgn * c)
        d = HEAD[side + 'ThumbIntermediate'] - HEAD[side + 'ThumbProximal']
        k = d.cross(Vector((0, 0.5, -1)).normalized()).normalized()   # swing the thumb in under the palm
        prev_f = D[side + 'Hand']
        for part, c in zip(('Proximal', 'Intermediate', 'Distal'), g['thumb']):
            prev_f = D[side + 'Thumb' + part] = prev_f @ rot(k, c)

    # tail: lifted arc, side sway once per cycle and a bob twice per cycle, each joint later than the last. The delay
    # adds up to most of a cycle along the tail, so it bends into an S that travels to the fluke, not one rigid swing.
    t = g['tail']
    n = len(TAILS)
    prev = D['Hips']
    for i, name in enumerate(TAILS):
        k = 0.35 + 0.65 * i / max(n - 1, 1)
        side = t['side'] * k * math.sin(2 * math.pi * (p - t['side_phase'] - i * t['side_lag']))
        upd = t['up'] * k * math.sin(2 * math.pi * (2 * p - t['up_phase'] - i * t['up_lag']))
        dr = HEAD[TAILS[i + 1]] - HEAD[name] if i + 1 < n else BONES[name].tail_local - HEAD[name]
        axis = X.cross(dr).normalized()
        D[name] = prev @ rot(X, tail_b[i] + upd) @ rot(axis, side)
        prev = D[name]
    # fluke lobes flex up and down a beat after the tail tip bobs
    flex = t['fin_bias'] + t['fin'] * math.sin(2 * math.pi * (2 * p - t['up_phase'] - n * t['up_lag'] - 0.1))
    for side in ('L', 'R'):
        fb = BONES['TailFin_' + side]
        k = (fb.tail_local - fb.head_local).cross(Z).normalized()
        D['TailFin_' + side] = D[fb.parent.name] @ rot(k, flex)
    return D, hips_off, feet


def posed_points(D, hips_off):
    """Posed bone heads (armature space), following the hierarchy."""
    pos = {}
    for n in ORDER:
        b = BONES[n]
        if b.parent is None:
            pos[n] = HEAD[n] + hips_off
        else:
            pn = b.parent.name
            pos[n] = pos[pn] + D.get(pn, I3) @ (HEAD[n] - HEAD[pn])
    last = TAILS[-1]
    pos['TailTip'] = pos[last] + D[last] @ (BONES[last].tail_local - HEAD[last])
    return pos


def key(D, hips_off, frame, prevq):
    full = {}
    for n in ORDER:
        b = BONES[n]
        full[n] = D.get(n, full[b.parent.name] if b.parent else I3)
    for n in ORDER:
        b = BONES[n]
        Dp = full[b.parent.name] if b.parent else I3
        q = (R3[n].transposed() @ Dp.transposed() @ full[n] @ R3[n]).to_quaternion()
        if n in prevq and q.dot(prevq[n]) < 0:
            q.negate()
        prevq[n] = q
        pb = arm.pose.bones[n]
        pb.rotation_mode = 'QUATERNION'
        pb.rotation_quaternion = q
        pb.keyframe_insert('rotation_quaternion', frame=frame, group=n)
        if b.parent is None:
            pb.location = R3[n].transposed() @ hips_off
            pb.keyframe_insert('location', frame=frame, group=n)


bpy.context.preferences.edit.keyframe_new_interpolation_type = 'LINEAR'
arm.animation_data_create()
report = {}
for name, g in GAITS.items():
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    arm.animation_data.action = act
    tail_b = tail_base(g['tail'])
    N = g['frames']
    prevq = {}
    stats = dict(max_reach=0.0, max_slip=0.0, tail_min_z=9.0, tail_min_z_near_legs=9.0, tip_z=[], tail_leg_gap=9.0)
    for f in range(N + 1):
        p = (f % N) / N
        D, hips_off, feet = pose(g, p, tail_b)
        key(D, hips_off, f, prevq)
        pos = posed_points(D, hips_off)
        for side in feet.values():
            stats['max_reach'] = max(stats['max_reach'], side['reach'])
            stats['max_slip'] = max(stats['max_slip'], side['slip'])
        tail_pts = [pos[n] for n in TAILS] + [pos['TailTip']]
        for q in tail_pts:
            stats['tail_min_z'] = min(stats['tail_min_z'], q.z)
            if q.y < 0.45:
                stats['tail_min_z_near_legs'] = min(stats['tail_min_z_near_legs'], q.z)
            for s in ('Left', 'Right'):
                for j in (s + 'LowerLeg', s + 'Foot'):
                    stats['tail_leg_gap'] = min(stats['tail_leg_gap'], (q - pos[j]).length)
        stats['tip_z'].append(round(pos['TailTip'].z, 3))
    act.frame_range = (0, N)
    act.use_frame_range = True
    act.use_cyclic = True
    stats['tip_z'] = [min(stats['tip_z']), max(stats['tip_z'])]
    stats['tail_base_deg'] = [round(v, 1) for v in tail_b]
    stats['speed_m_s'] = round(g['stride'] * 30 / N, 2)
    report[name] = {k: (round(v, 4) if isinstance(v, float) else v) for k, v in stats.items()}
    print(name, report[name], flush=True)

arm.animation_data.action = bpy.data.actions['Walk']
bpy.context.scene.render.fps = 30
bpy.context.scene.frame_start, bpy.context.scene.frame_end = 0, GAITS['Walk']['frames'] - 1
bpy.ops.wm.save_as_mainfile(filepath=OUT)
json.dump(report, open(REPORT, 'w'), indent=1)
print('saved', OUT)
