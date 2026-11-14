"""Rig a refined whole-character model by taking the skeleton and weights of the model it replaces.

Run: blender -b MODEL-refined.blend -P rig_whole.py -- DONOR.fbx OUT_DIR [--name DeepSeek] [--size 4096]

The replacement comes from the same drawings as the donor, so the two surfaces lie within millimetres of each
other (median 2.7 mm on DeepSeek's battle dress, a few centimetres on the hair down her back and the tail).
Keeping the donor's armature as it is means the game's avatar, its motions and everything posed bone by bone
(the tail) carry over untouched; only the skin changes. Steps:
1. one material: the grafted ahoge's texture is pasted into the largest square of the body's atlas that no
   triangle uses, and its uvs moved there; the base colour is written at --size as OUT_DIR/<name>_BaseColor.png
   (the only map the game reads);
2. weights from the donor mesh by nearest surface point, at most four bones a vertex, summing to one;
3. the mesh takes the donor mesh's name and material name, is bound to the donor's armature and exported as
   OUT_DIR/<name>.fbx with the settings the donor was exported with;
4. the exported file is read back and its bones compared with the donor's.
Also saves OUT_DIR/<name>-rigged.blend for posing checks.
"""
import math
import os
import sys

import bpy
import numpy as np

argv = sys.argv[sys.argv.index('--') + 1:]
DONOR, OUT = argv[0], argv[1]
opt = {'name': 'DeepSeek', 'size': '4096'}
rest = argv[2:]
while rest:
    key = rest.pop(0)[2:]
    opt[key] = rest.pop(0)
NAME, SIZE = opt['name'], int(opt['size'])
GRID = 1024        # the atlas is searched for free room at this resolution
GRID_MARGIN = 3    # cells kept clear round every triangle
PATCH_INSET = 0.1  # share of the free square left as a border round the pasted texture


def activate(*obs):
    bpy.ops.object.select_all(action='DESELECT')
    for o in obs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = obs[-1]


def pixels(img):
    a = np.empty(img.size[0] * img.size[1] * 4, dtype=np.float32)
    img.pixels.foreach_get(a)
    return a.reshape(img.size[1], img.size[0], 4)


def loop_uvs(me):
    uv = np.empty(len(me.loops) * 2, dtype=np.float32)
    me.uv_layers.active.data.foreach_get('uv', uv)
    return uv.reshape(-1, 2)


def free_square(uv_tris):
    """The largest square of the atlas no triangle comes near: (column, row, side) in GRID cells."""
    used = np.zeros((GRID, GRID), dtype=bool)
    px = uv_tris * GRID
    edge = np.linalg.norm(px - np.roll(px, 1, axis=1), axis=2).max(1)
    steps = np.clip(np.ceil(edge / 0.7), 1, 400).astype(int)
    for n in np.unique(steps):   # points every 0.7 cells over each triangle, the triangles grouped by size
        t = px[steps == n]
        i, j = np.meshgrid(np.arange(n + 1), np.arange(n + 1))
        keep = i + j <= n
        a, b = (i[keep] / n)[None, :, None], (j[keep] / n)[None, :, None]
        p = t[:, None, 0] * (1 - a - b) + t[:, None, 1] * a + t[:, None, 2] * b
        c = np.clip(p.reshape(-1, 2).astype(int), 0, GRID - 1)
        used[c[:, 1], c[:, 0]] = True
    grown = used.copy()
    for dy in range(-GRID_MARGIN, GRID_MARGIN + 1):
        for dx in range(-GRID_MARGIN, GRID_MARGIN + 1):
            grown |= np.roll(np.roll(used, dy, 0), dx, 1)
    grown[[0, -1], :] = True
    grown[:, [0, -1]] = True
    best, prev = (0, 0, 0), [0] * GRID
    for y in range(GRID):
        row, cur = grown[y].tolist(), [0] * GRID
        for x in range(GRID):
            if row[x]:
                continue
            cur[x] = 1 + (min(prev[x], cur[x - 1], prev[x - 1]) if x and y else 0)
            if cur[x] > best[2]:
                best = (x - cur[x] + 1, y - cur[x] + 1, cur[x])
        prev = cur
    print('ATLAS: %.1f%% of the cells used, largest free square %d cells at (%d, %d)' % (100 * used.mean(), best[2], best[0], best[1]))
    return best


def one_material(ob):
    """Move the second material's faces (the ahoge) into free room on the first material's atlas."""
    me = ob.data
    base_node = next(n for n in me.materials[0].node_tree.nodes if n.type == 'BSDF_PRINCIPLED').inputs['Base Color'].links[0].from_node
    base = base_node.image.copy()
    base.scale(SIZE, SIZE)
    px = pixels(base)
    if len(me.materials) > 1:
        mat_index = np.empty(len(me.polygons), dtype=np.int32)
        me.polygons.foreach_get('material_index', mat_index)
        uv = loop_uvs(me)
        tri = uv.reshape(-1, 3, 2)   # the mesh is triangles throughout after decimation
        x, y, side = free_square(tri[mat_index == 0])
        if side * SIZE // GRID < 48:
            raise SystemExit('no room on the atlas for the second material (%d cells)' % side)
        inset = side * PATCH_INSET
        u0, v0, span = (x + inset) / GRID, (y + inset) / GRID, (side - 2 * inset) / GRID
        other = next(n for n in me.materials[1].node_tree.nodes if n.type == 'TEX_IMAGE').image.copy()
        n = int(round(side * SIZE / GRID))
        other.scale(n, n)   # scaled to the whole square: its border beyond the uvs is the texture's own edge colour
        patch = pixels(other)
        bpy.data.images.remove(other)
        # the uvs sit inside the inset; the texture is laid so that its 0..1 lands on that inner span
        inner0, inner = int(round(inset * SIZE / GRID)), int(round((side - 2 * inset) * SIZE / GRID))
        small = patch[np.clip((np.arange(inner) * n / inner).astype(int), 0, n - 1)][:, np.clip((np.arange(inner) * n / inner).astype(int), 0, n - 1)]
        fill = small.reshape(-1, 4).mean(0)
        px0, py0 = int(round(x * SIZE / GRID)), int(round(y * SIZE / GRID))
        px[py0:py0 + n, px0:px0 + n] = fill
        px[py0 + inner0:py0 + inner0 + inner, px0 + inner0:px0 + inner0 + inner] = small
        tri[mat_index == 1] = np.array([u0, v0], dtype=np.float32) + tri[mat_index == 1] * span
        me.uv_layers.active.data.foreach_set('uv', tri.reshape(-1))
        me.polygons.foreach_set('material_index', np.zeros(len(me.polygons), dtype=np.int32))
        while len(me.materials) > 1:
            me.materials.pop(index=len(me.materials) - 1)
        print('ATLAS: second material pasted at %d px square, uv (%.4f, %.4f) span %.4f' % (n, u0, v0, span))
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, NAME + '_BaseColor.png')
    out = bpy.data.images.new(NAME + '_BaseColor', SIZE, SIZE, alpha=False)
    px[..., 3] = 1
    out.pixels.foreach_set(px.ravel())
    out.filepath_raw, out.file_format = path, 'PNG'
    out.save()
    bpy.data.images.remove(base)
    base_node.image = out
    return path


def take_weights(ob, donor):
    for g in donor.vertex_groups:
        ob.vertex_groups.new(name=g.name)
    mod = ob.modifiers.new('Weights', 'DATA_TRANSFER')
    mod.object = donor
    mod.use_vert_data = True
    mod.data_types_verts = {'VGROUP_WEIGHTS'}
    mod.vert_mapping = 'POLYINTERP_NEAREST'
    mod.layers_vgroup_select_src, mod.layers_vgroup_select_dst = 'ALL', 'NAME'
    activate(ob)
    bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.ops.object.vertex_group_limit_total(group_select_mode='ALL', limit=4)
    bpy.ops.object.vertex_group_normalize_all(group_select_mode='ALL', lock_active=False)
    counts = np.array([len(v.groups) for v in ob.data.vertices])
    sums = np.array([sum(g.weight for g in v.groups) for v in ob.data.vertices])
    print('WEIGHTS: %d vertices, %d with none, most bones on one %d, sums %.3f..%.3f'
          % (len(counts), int((counts == 0).sum()), int(counts.max()), float(sums.min()), float(sums.max())))


def bones_of(arm):
    return {b.name: (b.parent.name if b.parent else None, np.array(b.matrix_local)) for b in arm.data.bones}


before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=DONOR)
new = [o for o in bpy.data.objects if o not in before]
arm = next(o for o in new if o.type == 'ARMATURE')
donor = next(o for o in new if o.type == 'MESH')
body = bpy.data.objects['DeepSeekBattle']
donor_bones = bones_of(arm)
mesh_name, material_name = donor.name, donor.data.materials[0].name

texture = one_material(body)
take_weights(body, donor)
for m in list(donor.data.materials):
    bpy.data.materials.remove(m)
bpy.data.objects.remove(donor)
body.name = body.data.name = mesh_name
body.data.materials[0].name = material_name
body.parent = arm
body.modifiers.new('Armature', 'ARMATURE').object = arm

bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, NAME + '-rigged.blend'))
fbx = os.path.join(OUT, NAME + '.fbx')
activate(body, arm)
# as the donor was exported (assets/characters/highpoly/blender/process.py on claude/vibrant-gauss-5cdv9b)
bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, object_types={'ARMATURE', 'MESH'},
                         apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
                         axis_forward='-Z', axis_up='Y', use_mesh_modifiers=False,
                         mesh_smooth_type='FACE', use_tspace=True, add_leaf_bones=False,
                         primary_bone_axis='Y', secondary_bone_axis='X',
                         use_armature_deform_only=True, armature_nodetype='NULL',
                         bake_anim=False, path_mode='RELATIVE')

# read back: the same bones in the same places as the donor's
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx)
back = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
mesh = next(o for o in bpy.data.objects if o.type == 'MESH')
got = bones_of(back)
missing = sorted(set(donor_bones) ^ set(got))
worst = max(float(np.abs(got[k][1] - donor_bones[k][1]).max()) for k in donor_bones if k in got)
parents = [k for k in donor_bones if k in got and got[k][0] != donor_bones[k][0]]
print('FBX: %s, %.1f MB, %d bones (%d differ in name, %d in parent), largest difference in a bone matrix %.6f'
      % (fbx, os.path.getsize(fbx) / 2**20, len(got), len(missing), len(parents), worst))
print('FBX: mesh %s, %d vertices, %d triangles, %d vertex groups, material %s, texture %s (%.1f MB)'
      % (mesh.name, len(mesh.data.vertices), len(mesh.data.polygons), len(mesh.vertex_groups),
         mesh.data.materials[0].name if mesh.data.materials else None, texture, os.path.getsize(texture) / 2**20))
