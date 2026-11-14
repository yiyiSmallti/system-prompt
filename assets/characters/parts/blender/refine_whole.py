"""Refine a whole-character Tripo GLB into a game-ready mesh.

Run: blender -b -P refine_whole.py -- NEW.glb OUT_DIR [--donor OLD.glb --donor-yaw -90] [--tris 120000]

Steps:
1. weld the duplicate vertices Tripo leaves along UV seams and drop its split normals (smooth shading);
2. graft what the new model lacks from a donor model of the same character (the ahoge): donor geometry above
   GRAFT_Z that lies more than GRAFT_GAP from the new surface, re-baked to its own small texture;
3. decimate to --tris triangles, keeping the face denser than the rest, and bake the high-poly surface into a
   new normal map: Tripo's own normal map holds only fine grain, not the shape lost in decimation, so without
   the bake the face and hair turn faceted;
4. give each kind of surface its own roughness, specular level, sheen and subsurface (refine_materials.py).
Writes OUT_DIR/<name>-refined.blend and <name>-refined.glb. The model keeps Tripo's size (about 0.9 units arm
span), stands on z=0 and faces -Y.
"""
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, kdtree

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import refine_materials  # noqa: E402

argv = sys.argv[sys.argv.index('--') + 1:]
NEW, OUT = argv[0], argv[1]
opt = {'donor': '', 'donor-yaw': '-90', 'tris': '120000'}
rest = argv[2:]
while rest:
    key = rest.pop(0)[2:]
    opt[key] = rest.pop(0)
NAME = os.path.splitext(os.path.basename(NEW))[0]

GRAFT_Z = 0.925     # donor geometry is only looked at above this height (top of the headband frill)
GRAFT_GAP = 0.004   # ... and counts as missing when this far from the new surface
GRAFT_RINGS = 3     # face rings added below the cut, so the stem ends inside the hair
GRAFT_SINK = 0.002  # those rings are pushed in by this much, under the new surface
AHOGE_TEX = 1024
AHOGE_TRIS = 2500
# the face and the bangs over it: decimated less (box in model units, model facing -Y)
FACE_BOX = ((-0.065, 0.065), (-0.26, -0.185), (0.775, 0.875))
FACE_TRIS = 12000   # triangles left there (an even collapse leaves about 3,300, the high-poly has 43,700)
NORMAL_TEX = 4096
BAKE_CAGE, BAKE_RAY = 0.004, 0.008


def import_glb(path, yaw_deg):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    ob = next(o for o in bpy.data.objects if o not in before and o.type == 'MESH')
    mw = Matrix.Rotation(math.radians(yaw_deg), 4, 'Z') @ ob.matrix_world
    ob.parent = None
    ob.data.transform(mw)
    ob.matrix_world = Matrix.Identity(4)
    return ob


def coords(me):
    co = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get('co', co)
    return co.reshape(-1, 3)


def weld(ob):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    n = len(bm.verts)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    print('WELD %s: %d -> %d verts' % (ob.name, n, len(bm.verts)))
    bm.to_mesh(ob.data)
    bm.free()
    a = ob.data.attributes.get('custom_normal')
    if a:
        ob.data.attributes.remove(a)
    ob.data.shade_smooth()


def activate(*obs):
    bpy.ops.object.select_all(action='DESELECT')
    for o in obs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = obs[-1]


def bsdf(mat):
    return next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')


def graft_from(donor, body):
    """Cut the donor's geometry that the body lacks into its own object, with a freshly baked texture."""
    dco, bco = coords(donor.data), coords(body.data)
    cand = np.where(dco[:, 2] > GRAFT_Z)[0]
    near = bco[bco[:, 2] > GRAFT_Z - 0.02]
    kd = kdtree.KDTree(len(near))
    for i, c in enumerate(near):
        kd.insert(c, i)
    kd.balance()
    core = {int(i) for i in cand if kd.find(dco[i])[1] > GRAFT_GAP}
    bm = bmesh.new()
    bm.from_mesh(donor.data)
    bm.verts.ensure_lookup_table()
    faces = [f for f in bm.faces if all(v.index in core for v in f.verts)]
    # keep the largest connected piece: specks where the two surfaces merely differ are not a missing part
    left, best = set(faces), []
    while left:
        comp, stack = [], [left.pop()]
        while stack:
            f = stack.pop()
            comp.append(f)
            for v in f.verts:
                for g in v.link_faces:
                    if g in left:
                        left.remove(g)
                        stack.append(g)
        if len(comp) > len(best):
            best = comp
    keep = set(best)
    core_verts = {v for f in keep for v in f.verts}
    for _ in range(GRAFT_RINGS):
        keep |= {g for f in list(keep) for v in f.verts for g in v.link_faces}
    for v in {v for f in keep for v in f.verts} - core_verts:
        v.co -= v.normal * GRAFT_SINK
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f not in keep], context='FACES')
    me = bpy.data.meshes.new('Ahoge')
    bm.to_mesh(me)
    bm.free()
    for m in donor.data.materials:
        me.materials.append(m)
    me.shade_smooth()
    src = bpy.data.objects.new('AhogeSource', me)
    bpy.context.scene.collection.objects.link(src)
    co = coords(me)
    print('GRAFT: %d core verts, %d faces kept, bbox %s .. %s' % (len(core), len(me.polygons), co.min(0).round(3), co.max(0).round(3)))

    # target: same shape, its own UV layout and a small texture baked from the donor's atlas
    dst = bpy.data.objects.new('Ahoge', me.copy())
    bpy.context.scene.collection.objects.link(dst)
    while dst.data.uv_layers:
        dst.data.uv_layers.remove(dst.data.uv_layers[0])
    dst.data.uv_layers.new(name='UVMap')
    activate(dst)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.02)
    bpy.ops.object.mode_set(mode='OBJECT')
    img = bpy.data.images.new(NAME + '_ahoge_basecolor', AHOGE_TEX, AHOGE_TEX, alpha=False)
    mat = bpy.data.materials.new('DeepSeekAhoge')
    mat.use_nodes = True
    node = mat.node_tree.nodes.new('ShaderNodeTexImage')
    node.image = img
    mat.node_tree.links.new(node.outputs['Color'], bsdf(mat).inputs['Base Color'])
    refine_materials.apply_hair(mat)
    mat.node_tree.nodes.active = node
    dst.data.materials.clear()
    dst.data.materials.append(mat)

    scene = bpy.context.scene
    hidden = [o for o in scene.objects if o not in (src, dst)]
    for o in hidden:
        o.hide_render = True
    bk = cycles_bake_setup(4)
    bk.use_selected_to_active = True
    bk.cage_extrusion = 0.002
    bk.max_ray_distance = 0.01
    bk.margin = 8
    bk.use_pass_direct = bk.use_pass_indirect = False
    bk.use_pass_color = True
    activate(src, dst)
    bpy.ops.object.bake(type='DIFFUSE')
    img.pack()
    for o in hidden:
        o.hide_render = False
    bpy.data.objects.remove(src)
    return dst


def cycles_bake_setup(samples):
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = samples
    try:
        prefs = bpy.context.preferences.addons['cycles'].preferences
        prefs.compute_device_type = 'METAL'
        prefs.get_devices()
        for d in prefs.devices:
            d.use = True
        scene.cycles.device = 'GPU'
    except Exception as e:
        print('GPU bake unavailable, using CPU:', e)
    return scene.render.bake


def in_box(pts, box):
    return np.all([(pts[:, a] > lo) & (pts[:, a] < hi) for a, (lo, hi) in enumerate(box)], axis=0)


def face_centers(me):
    c = np.empty(len(me.polygons) * 3, dtype=np.float32)
    me.polygons.foreach_get('center', c)
    return c.reshape(-1, 3)


def collapse(ob, ratio, only=None):
    """Decimate by edge collapse; with `only` (vertex indices) the other vertices are left as they are."""
    mod = ob.modifiers.new('Decimate', 'DECIMATE')
    mod.ratio = ratio
    if only is not None:
        vg = ob.vertex_groups.new(name='Only')
        vg.add(only.tolist(), 1.0, 'REPLACE')
        mod.vertex_group = vg.name
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
    ob.modifiers.clear()
    old = ob.data
    ob.data = me
    bpy.data.meshes.remove(old)
    # group names live on the mesh: clear them on the new one, or the next pass would reuse this pass's weights
    ob.vertex_groups.clear()
    me.validate()
    me.shade_smooth()


def decimate(ob, tris, protect=None):
    """Collapse to `tris` triangles. With a `protect` box, the part inside it ends at FACE_TRIS triangles instead of
    the few an even collapse would leave it: first everything outside the box is reduced, then the box itself.
    (The modifier's vertex group is all-or-nothing: vertices with any weight are collapsed, those at 0 are kept.)"""
    n = len(ob.data.polygons)
    if n <= tris:
        return
    if protect:
        inside_tris = int(in_box(face_centers(ob.data), protect).sum())
        keep = min(inside_tris, FACE_TRIS)
        collapse(ob, (tris - keep + inside_tris) / n, np.where(~in_box(coords(ob.data), protect))[0])
        collapse(ob, tris / len(ob.data.polygons), np.where(in_box(coords(ob.data), protect))[0])
    else:
        collapse(ob, tris / n)
    msg = 'DECIMATE %s: %d -> %d tris' % (ob.name, n, len(ob.data.polygons))
    if protect:
        msg += ', %d of them in the face box' % int(in_box(face_centers(ob.data), protect).sum())
    print(msg)


def bake_normal(hi, lo):
    """Bake the high-poly surface (with its own normal map) into a new tangent-space normal map on lo's UVs."""
    mat = lo.data.materials[0]
    nt = mat.node_tree
    img = bpy.data.images.new(NAME + '_normal_baked', NORMAL_TEX, NORMAL_TEX, alpha=False)
    img.colorspace_settings.name = 'Non-Color'
    node = nt.nodes.new('ShaderNodeTexImage')
    node.image = img
    nt.nodes.active = node
    scene = bpy.context.scene
    hidden = [o for o in scene.objects if o not in (hi, lo)]
    for o in hidden:
        o.hide_render = True
    bk = cycles_bake_setup(1)
    bk.use_selected_to_active = True
    bk.cage_extrusion, bk.max_ray_distance = BAKE_CAGE, BAKE_RAY
    bk.margin = 4
    bk.normal_space = 'TANGENT'
    activate(hi, lo)
    bpy.ops.object.bake(type='NORMAL')
    img.pack()
    for o in hidden:
        o.hide_render = False
    nm = bsdf(mat).inputs['Normal'].links[0].from_node
    old = nm.inputs['Color'].links[0].from_node
    nt.links.new(node.outputs['Color'], nm.inputs['Color'])
    nt.nodes.remove(old)
    print('BAKE normal %dpx from %d tris onto %d' % (NORMAL_TEX, len(hi.data.polygons), len(lo.data.polygons)))


bpy.ops.wm.read_factory_settings(use_empty=True)
body = import_glb(NEW, 0)
body.name = 'DeepSeekBattle'
weld(body)
parts = [body]
if opt['donor']:
    donor = import_glb(opt['donor'], float(opt['donor-yaw']))
    weld(donor)
    ahoge = graft_from(donor, body)
    for m in list(donor.data.materials):
        bpy.data.materials.remove(m)
    bpy.data.objects.remove(donor)
    parts.append(ahoge)
budget = int(opt['tris'])
hi = body
body = hi.copy()
body.data = hi.data.copy()
bpy.context.scene.collection.objects.link(body)
hi.name, body.name = 'HighPoly', 'DeepSeekBattle'
body.data.materials[0] = hi.data.materials[0].copy()
body.data.materials[0].name = 'DeepSeekBattle'
decimate(body, budget - (AHOGE_TRIS if len(parts) > 1 else 0), FACE_BOX)
bake_normal(hi, body)
bpy.data.objects.remove(hi)
parts[0] = body
if len(parts) > 1:
    decimate(parts[1], AHOGE_TRIS)
refine_materials.apply(body.data.materials[0], NAME, os.path.join(OUT, 'textures'))
activate(*reversed(parts))
if len(parts) > 1:
    bpy.ops.object.join()
bpy.ops.outliner.orphans_purge(do_local_ids=True, do_linked_ids=False, do_recursive=True)
os.makedirs(OUT, exist_ok=True)
stem = os.path.join(OUT, NAME + '-refined')
bpy.ops.wm.save_as_mainfile(filepath=stem + '.blend')
activate(body)
bpy.ops.export_scene.gltf(filepath=stem + '.glb', export_format='GLB', use_selection=True)
co = coords(body.data)
print('OUT %s tris %d bbox %s .. %s' % (stem, len(body.data.polygons), co.min(0).round(3), co.max(0).round(3)))
