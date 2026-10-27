"""Rebuild the assembled DeepSeek battle outfit in Blender from the Tripo GLBs and the assembly JSON.

Run: blender -b -P build_model.py -- ASSEMBLY_JSON GLB_DIR DECAL_DIR OUT.blend
Each part is imported from its full GLB, placed with the JSON transform, converted to Blender axes
(Z up, front -Y) and metres (crown at 1.51 m), decimated to a working triangle budget, given the JSON
material changes, and the face decals are rebuilt as thin meshes cut from the head.
"""
import json
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

args = sys.argv[sys.argv.index('--') + 1:]
JSON_PATH, GLB_DIR, DECAL_DIR, OUT = args
J = json.load(open(JSON_PATH))

CROWN = J['landmarks']['crown'] + J['landmarks']['lift']   # crown height in assembly units
METRES = 1.51 / CROWN                                      # 151 cm to the crown
# three.js (Y up, front +Z) -> Blender (Z up, front -Y): (x, y, z) -> (x, -z, y)
C = Matrix(((1, 0, 0, 0), (0, 0, -1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))
TO_BL = Matrix.Scale(METRES, 4) @ C

# triangle budget per part after decimation (the Tripo originals are ~1.9M each)
BUDGET = {'body': 50000, 'head': 40000, 'hair': 90000, 'skirt': 50000, 'capelet': 30000, 'corset': 30000,
          'blouse': 30000, 'apron': 25000, 'boots': 30000, 'stockings': 20000, 'tail-glow': 30000,
          'headband': 20000, 'neck-bow': 10000}
DEFAULT_BUDGET = 10000


def rot(axis, deg):
    return Matrix.Rotation(math.radians(deg), 4, axis)


def three_matrix(p):
    """The part's world matrix in three.js terms, as composed by asm.html."""
    rx, ry, rz = p.get('rotYXZ', [0, 0, 0])
    sx, sy, sz = p['scale'] if isinstance(p['scale'], list) else [p['scale']] * 3
    S = Matrix.Diagonal((sx, sy, sz, 1))
    M = Matrix.Translation(p['position']) @ rot('X', p.get('tiltX', 0)) @ S
    M = M @ rot('Y', ry) @ rot('X', rx) @ rot('Z', rz)
    if p.get('mirrorX'):
        M = M @ Matrix.Diagonal((-1, 1, 1, 1))
    return M @ rot('Y', p['yaw'])


def import_part(name, p):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=os.path.join(GLB_DIR, p['file']))
    new = [o for o in bpy.data.objects if o not in before]
    # imported nodes are already Z up: world = C @ N_gltf, so the assembly matrix is conjugated by C
    Mb = TO_BL @ three_matrix(p) @ C.inverted()
    for o in new:
        if o.parent is None:
            o.matrix_world = Mb @ o.matrix_world
    bpy.context.view_layer.update()
    meshes = [o for o in new if o.type == 'MESH']
    for o in meshes:
        mw = o.matrix_world.copy()
        o.parent = None
        o.matrix_world = mw
    for o in new:
        if o.type != 'MESH':
            bpy.data.objects.remove(o)
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    flip = o.matrix_world.determinant() < 0
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    o.name = o.data.name = name
    return o, flip


def outwardness(o):
    """Mean of normal . (face centre - mesh centre); positive when faces point outward."""
    me = o.data
    n = np.empty(len(me.polygons) * 3); c = np.empty(len(me.polygons) * 3)
    me.polygons.foreach_get('normal', n); me.polygons.foreach_get('center', c)
    n, c = n.reshape(-1, 3), c.reshape(-1, 3)
    return float(((c - c.mean(0)) * n).sum(1).mean())


def decimate(o, budget):
    tris = sum(len(p.vertices) - 2 for p in o.data.polygons)
    if tris <= budget:
        return tris, tris
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.mesh.customdata_custom_splitnormals_clear()
    # Tripo splits vertices along UV seams; collapse cannot cross those cuts until they are welded
    bm = bmesh.new(); bm.from_mesh(o.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bm.to_mesh(o.data); bm.free()
    m = o.modifiers.new('Decimate', 'DECIMATE')
    m.ratio = budget / len(o.data.polygons)
    m.delimit = {'UV'}
    m.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=m.name)
    bpy.ops.object.shade_smooth()
    return tris, len(o.data.polygons)


def principled(mat):
    return next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')


def apply_mat(o, mat_cfg):
    """The assembly's material changes, as Principled BSDF settings."""
    for slot in o.material_slots:
        mat = slot.material
        nt = mat.node_tree; b = principled(mat)
        base_link = b.inputs['Base Color'].links[0] if b.inputs['Base Color'].links else None
        tex_out = base_link.from_socket if base_link else None
        if 'roughness' in mat_cfg:
            for l in list(b.inputs['Roughness'].links): nt.links.remove(l)
            b.inputs['Roughness'].default_value = mat_cfg['roughness']
        if 'metalness' in mat_cfg:
            for l in list(b.inputs['Metallic'].links): nt.links.remove(l)
            b.inputs['Metallic'].default_value = mat_cfg['metalness']
        if 'color' in mat_cfg and tex_out:
            mul = nt.nodes.new('ShaderNodeVectorMath'); mul.operation = 'MULTIPLY'
            mul.inputs[1].default_value = mat_cfg['color']
            nt.links.new(tex_out, mul.inputs[0]); nt.links.new(mul.outputs[0], b.inputs['Base Color'])
        if 'emissive' in mat_cfg and tex_out:
            mul = nt.nodes.new('ShaderNodeVectorMath'); mul.operation = 'MULTIPLY'
            mul.inputs[1].default_value = mat_cfg['emissive']
            nt.links.new(tex_out, mul.inputs[0]); nt.links.new(mul.outputs[0], b.inputs['Emission Color'])
            b.inputs['Emission Strength'].default_value = 1.0


def make_decal(name, d, head):
    """Cut the head faces inside the decal box into their own mesh, with UVs projected through the box."""
    c = TO_BL @ Vector(d['centre'])
    w, h, depth = (v * METRES for v in d['size'])
    yaw = math.radians(d['yaw'])
    # box axes in Blender: right (+x of the image), up (+y of the image), out (projection normal)
    right = (C @ Vector((math.cos(yaw), 0, -math.sin(yaw), 0))).to_3d()
    up = Vector((0, 0, 1))
    out = (C @ Vector((math.sin(yaw), 0, math.cos(yaw), 0))).to_3d()
    bm = bmesh.new(); bm.from_mesh(head.data)
    keep = []
    for f in bm.faces:
        if f.normal.dot(out) <= 0.2:
            continue
        inside = False
        for v in f.verts:
            r = v.co - c
            if abs(r.dot(right)) <= w / 2 and abs(r.dot(up)) <= h / 2 and abs(r.dot(out)) <= depth / 2:
                inside = True; break
        if inside:
            keep.append(f)
    nb = bmesh.new(); vmap = {}
    uv = nb.loops.layers.uv.new('UVMap')
    for f in keep:
        vs = []
        for v in f.verts:
            if v not in vmap:
                vmap[v] = nb.verts.new(v.co + v.normal * 0.0004)   # lift 0.4 mm off the skin
            vs.append(vmap[v])
        nf = nb.faces.new(vs)
        for loop in nf.loops:
            r = loop.vert.co - c
            loop[uv].uv = (r.dot(right) / w + 0.5, r.dot(up) / h + 0.5)
    me = bpy.data.meshes.new(name); nb.to_mesh(me); nb.free(); bm.free()
    ob = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(ob)
    for p in me.polygons: p.use_smooth = True
    mat = bpy.data.materials.new(name); mat.use_nodes = True
    nt = mat.node_tree; b = principled(mat)
    img = bpy.data.images.load(os.path.join(DECAL_DIR, d['image'].split('/')[-1]))
    tex = nt.nodes.new('ShaderNodeTexImage'); tex.image = img; tex.extension = 'CLIP'
    nt.links.new(tex.outputs['Color'], b.inputs['Base Color'])
    nt.links.new(tex.outputs['Alpha'], b.inputs['Alpha'])
    b.inputs['Roughness'].default_value = 0.6
    me.materials.append(mat)
    return ob


bpy.ops.wm.read_factory_settings(use_empty=True)
report = {}
objs = {}
for name, p in J['parts'].items():
    o, flip = import_part(name, p)
    out0 = outwardness(o)
    before, after = decimate(o, BUDGET.get(name, DEFAULT_BUDGET))
    if p.get('mat'):
        apply_mat(o, p['mat'])
    o['assembly_clip'] = json.dumps(p.get('clip') or [])
    objs[name] = o
    report[name] = {'tris': [before, after], 'mirrored': flip, 'outward': round(out0, 5)}
    print('part', name, report[name], flush=True)

for name, d in J['decals'].items():
    ob = make_decal(name, d, objs['head'])
    report[name] = {'decal_faces': len(ob.data.polygons)}
    print('decal', name, report[name], flush=True)

bpy.context.scene.unit_settings.system = 'METRIC'
bpy.ops.wm.save_as_mainfile(filepath=OUT)
json.dump({'metres_per_unit': METRES, 'parts': report}, open(OUT.replace('.blend', '-build.json'), 'w'), indent=1)
print('saved', OUT)
