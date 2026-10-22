"""Cloud Blender Eevee toon material variant. Never overwrites source geometry/file.

Original atlas colors are retained; banded real-light shading, conservative face
shadow reduction and a thin inverted hull are stored in a separate .blend.
"""
import argparse
import hashlib
import json
from pathlib import Path
import sys
import time

import bpy
import numpy as np
from mathutils import Matrix, Vector

def make_camera(scene, name, direction, center, corners, distance, aspect):
    """Self-contained copy of the camera fitting used for the delivered renders."""
    data = bpy.data.cameras.new('Inspection_' + name)
    obj = bpy.data.objects.new('Inspection_' + name, data)
    scene.collection.objects.link(obj)
    obj.location = center + direction.normalized() * distance
    obj.rotation_euler = (center - obj.location).to_track_quat('-Z', 'Y').to_euler()
    data.type = 'ORTHO'
    q = obj.rotation_euler.to_quaternion()
    right, up = q @ Vector((1, 0, 0)), q @ Vector((0, 1, 0))
    x = [(point - center).dot(right) for point in corners]
    y = [(point - center).dot(up) for point in corners]
    data.ortho_scale = max(max(y) - min(y), (max(x) - min(x)) / aspect) * 1.12
    data.clip_start = max(distance / 10000, .00001)
    data.clip_end = distance * 10
    return obj

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--input', required=True, type=Path)
p.add_argument('--output', required=True, type=Path)
p.add_argument('--character', choices=['chatgpt', 'claude'], required=True)
p.add_argument('--views', default='front,quarter,back,face-front')
p.add_argument('--size', type=int, default=1400)
p.add_argument('--face-size', type=int, default=1100)
p.add_argument('--samples', type=int, default=64)
p.add_argument('--outline', type=float, default=.00065, help='Fraction of character height')
p.add_argument('--draft', action='store_true', help='Do not save a final blend')
a = p.parse_args(sys.argv[sys.argv.index('--') + 1:])
views = a.views.split(',')
if set(views) - {'front', 'quarter', 'back', 'face-front', 'face-quarter'}:
    p.error('Unsupported view')
a.output.mkdir(parents=True, exist_ok=True)
if any((a.output / (name + '.png')).exists() for name in views):
    raise FileExistsError('Preview already exists; choose a new output directory')
if (a.output / 'toon.blend').exists():
    raise FileExistsError('Toon project exists')
digest = hashlib.sha256(a.input.read_bytes()).hexdigest()
bpy.ops.wm.open_mainfile(filepath=str(a.input), use_scripts=False)
s = bpy.context.scene
objects = [o for o in s.objects if o.type == 'MESH' and not o.hide_render]
if not objects:
    raise ValueError('No source mesh')
world_points = []
for ob in objects:
    co = np.empty((len(ob.data.vertices), 3), dtype=np.float32)
    ob.data.vertices.foreach_get('co', co.ravel())
    m = np.asarray(ob.matrix_world)
    world_points.append(co @ m[:3, :3].T + m[:3, 3])
xyz = np.concatenate(world_points)
lo, hi = xyz.min(axis=0), xyz.max(axis=0)
height = float(hi[2] - lo[2])
center = Vector((lo + hi) * .5)
corners = [Vector((x, y, z)) for x in (lo[0], hi[0])
           for y in (lo[1], hi[1]) for z in (lo[2], hi[2])]
front, left, up = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))

def linear_hex(color):
    c = [int(color[i:i+2], 16) / 255 for i in (0, 2, 4)]
    return tuple(v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4 for v in c) + (1,)

def material_for(source, mask_space):
    source.use_fake_user = True
    source['toon_backup_note'] = 'Original imported PBR, retained for comparison.'
    old_bsdf = next(n for n in source.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    old_color = old_bsdf.inputs['Base Color']
    if not old_color.is_linked or old_color.links[0].from_node.type != 'TEX_IMAGE':
        raise ValueError('Expected the actual source atlas directly connected to Base Color')
    atlas = old_color.links[0].from_node.image
    mat = bpy.data.materials.new('TOON_' + a.character + '_Atlas')
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    nodes.clear()
    def node(kind, name, x, y):
        n = nodes.new(kind); n.name = name; n.label = name; n.location = (x, y)
        return n
    def math(op, name, first, second=0):
        n = node('ShaderNodeMath', name, -400, -400)
        n.operation = op
        for i, value in enumerate((first, second)):
            if isinstance(value, (int, float)):
                n.inputs[i].default_value = value
            else:
                links.new(value, n.inputs[i])
        return n.outputs[0]
    def window(value, start, full, end_full, end):
        n1 = node('ShaderNodeMapRange', 'Mask fade in', -700, -500)
        n2 = node('ShaderNodeMapRange', 'Mask fade out', -700, -700)
        for n, v0, v1, t0, t1 in ((n1, start, full, 0, 1), (n2, end_full, end, 1, 0)):
            links.new(value, n.inputs['Value'])
            for k, v in [('From Min', v0), ('From Max', v1), ('To Min', t0), ('To Max', t1)]:
                n.inputs[k].default_value = v
            n.clamp = True
        return math('MULTIPLY', 'Soft mask window', n1.outputs[0], n2.outputs[0])
    tex = node('ShaderNodeTexImage', 'Original color atlas — unchanged', -1100, 350)
    tex.image = atlas
    hsv = node('ShaderNodeHueSaturation', 'Subtle color preservation', -850, 350)
    hsv.inputs['Saturation'].default_value = 1.04
    hsv.inputs['Value'].default_value = 1.0
    links.new(tex.outputs['Color'], hsv.inputs['Color'])
    diffuse = node('ShaderNodeBsdfDiffuse', 'Actual scene lighting / geometry normals', -1000, 100)
    diffuse.inputs['Color'].default_value = (1, 1, 1, 1)
    diffuse.inputs['Roughness'].default_value = 0
    rgb = node('ShaderNodeShaderToRGB', 'Eevee lighting', -780, 100)
    links.new(diffuse.outputs[0], rgb.inputs[0])
    ramp = node('ShaderNodeValToRGB', 'CEL bands — shadow / mid / light', -560, 100)
    ramp.color_ramp.interpolation = 'CONSTANT'
    ramp.color_ramp.elements.remove(ramp.color_ramp.elements[1])
    palette = [(0, (.68, .65, .80, 1)), (.28, (.86, .85, .94, 1)), (.60, (1, 1, 1, 1))]
    for i, (pos, col) in enumerate(palette):
        e = ramp.color_ramp.elements[0] if i == 0 else ramp.color_ramp.elements.new(pos)
        e.position, e.color = pos, col
    links.new(rgb.outputs[0], ramp.inputs[0])
    # A conservative warm/light-color mask, restricted to the front head region.
    # It changes only NEW shading strength, never recolors or separates the mesh.
    sep = node('ShaderNodeSeparateColor', 'Conservative skin-color signal', -1050, -250)
    sep.mode = 'HSV'; links.new(tex.outputs['Color'], sep.inputs[0])
    hue_low = math('LESS_THAN', 'Warm hue low', sep.outputs[0], .12)
    hue_high = math('GREATER_THAN', 'Warm hue high', sep.outputs[0], .94)
    warm = math('MAXIMUM', 'Warm hues', hue_low, hue_high)
    sat = window(sep.outputs[1], .02, .06, .40, .55)
    value = window(sep.outputs[2], .20, .42, 1.05, 1.1)
    geo = node('ShaderNodeTexCoord', 'Face mask rest coordinates', -1100, -650)
    geo.object = mask_space
    pos = node('ShaderNodeSeparateXYZ', 'Rest-pose XYZ', -900, -650)
    links.new(geo.outputs['Object'], pos.inputs[0])
    z0, z1, z2, z3 = ((.76, .79, .88, .92) if a.character == 'chatgpt' else (.80, .84, .95, .99))
    zmask = window(pos.outputs['Z'], *(float(lo[2]) + height * z for z in (z0, z1, z2, z3)))
    xmask = window(pos.outputs['X'], float(lo[0]) + .48 * float(hi[0]-lo[0]), float(lo[0]) + .60 * float(hi[0]-lo[0]), float(hi[0])+height, float(hi[0])+2*height)
    mask = warm
    for signal in (sat, value, zmask, xmask):
        mask = math('MULTIPLY', 'Conservative face shadow weight', mask, signal)
    strength = math('MULTIPLY', 'Face shadow reduction', mask, .72)
    soften = node('ShaderNodeMixRGB', 'Face: retain bright readable planes', -260, 100)
    soften.blend_type = 'MIX'; links.new(strength, soften.inputs[0])
    links.new(ramp.outputs[0], soften.inputs[1]); soften.inputs[2].default_value = (1, 1, 1, 1)
    color = node('ShaderNodeMixRGB', 'Atlas × cel shade', 0, 250)
    color.blend_type = 'MULTIPLY'; color.inputs[0].default_value = 1
    links.new(hsv.outputs[0], color.inputs[1]); links.new(soften.outputs[0], color.inputs[2])
    emit = node('ShaderNodeEmission', 'Clean illustrated surface', 230, 250)
    links.new(color.outputs[0], emit.inputs['Color'])
    out = node('ShaderNodeOutputMaterial', 'TOON Output', 450, 250)
    links.new(emit.outputs[0], out.inputs[0])
    mat['design_note'] = 'Original atlas × real Eevee light bands. No ORM/normal-map shine. Face mask reduces only added shading, not semantic segmentation.'
    return mat

material_map = {}
for ob in objects:
    mask_space = bpy.data.objects.new('TOON_FaceMask_RestSpace', None)
    s.collection.objects.link(mask_space)
    mask_space.parent = ob
    mask_space.matrix_parent_inverse = ob.matrix_world.inverted()
    mask_space.matrix_basis = Matrix.Identity(4)
    mask_space.empty_display_size = height * .035
    mask_space.hide_render = True
    mask_space['note'] = 'Identity in the delivered rest pose; follows source mesh rigid transforms. Not a deforming face mask.'
    for slot in ob.material_slots:
        source = slot.material
        if source.name not in material_map:
            material_map[source.name] = material_for(source, mask_space)
        slot.material = material_map[source.name]

outline_mat = bpy.data.materials.new('TOON_Contour_DeepMutedInk')
outline_mat.use_nodes = True; outline_mat.use_backface_culling = True
n, l = outline_mat.node_tree.nodes, outline_mat.node_tree.links
n.clear()
em = n.new('ShaderNodeEmission')
em.inputs['Color'].default_value = linear_hex('42364f' if a.character == 'chatgpt' else '50352e')
path = n.new('ShaderNodeLightPath'); tr = n.new('ShaderNodeBsdfTransparent')
mix = n.new('ShaderNodeMixShader'); out = n.new('ShaderNodeOutputMaterial')
l.new(path.outputs['Is Shadow Ray'], mix.inputs[0]); l.new(em.outputs[0], mix.inputs[1])
l.new(tr.outputs[0], mix.inputs[2]); l.new(mix.outputs[0], out.inputs[0])
outline_collection = bpy.data.collections.new('TOON_Outlines — toggle this collection')
s.collection.children.link(outline_collection)
for ob in objects:
    mesh = ob.data.copy()
    co = np.empty((len(mesh.vertices), 3), dtype=np.float32)
    normals = np.empty_like(co)
    mesh.vertices.foreach_get('co', co.ravel()); mesh.vertices.foreach_get('normal', normals.ravel())
    m = np.asarray(ob.matrix_world)
    world = co @ m[:3, :3].T + m[:3, 3]
    weights = np.ones(len(co), dtype=np.float32)
    head = (world[:, 2] > lo[2] + height * .78) & (world[:, 0] > lo[0] + (hi[0]-lo[0])*.57)
    weights[head] = .35
    co += normals * (height * a.outline * weights[:, None])
    mesh.vertices.foreach_set('co', co.ravel())
    mesh.flip_normals(); mesh.update()
    mesh.materials.clear(); mesh.materials.append(outline_mat)
    outline = bpy.data.objects.new(ob.name + '_TOON_Outline', mesh)
    outline_collection.objects.link(outline)
    outline.parent = ob
    outline.matrix_parent_inverse = Matrix.Identity(4)
    outline.matrix_basis = Matrix.Identity(4)
    outline.visible_shadow = False
    outline['note'] = 'Derived thin outline hull only. Source body geometry is unchanged.'

for ob in s.objects:
    if ob.type == 'LIGHT':
        ob.hide_render = True
        ob.hide_viewport = True

def sun(name, direction, energy, shadow):
    data = bpy.data.lights.new(name, type='SUN'); data.energy = energy
    data.angle = .035; data.use_shadow = shadow
    ob = bpy.data.objects.new(name, data); s.collection.objects.link(ob)
    ob.location = center + direction.normalized() * height * 3
    ob.rotation_euler = (-direction).to_track_quat('-Z', 'Y').to_euler()
    return ob

sun('TOON_Key — rotate to move shadow bands', Vector((1, -.55, 1.0)), 2.0, True)
sun('TOON_Fill', Vector((-.45, .6, .8)), .45, False)
world = bpy.data.worlds.new('TOON_Paper_Background'); world.use_nodes = True
world.node_tree.nodes['Background'].inputs['Color'].default_value = linear_hex('f5f3f8')
world.node_tree.nodes['Background'].inputs['Strength'].default_value = 1
s.world = world
s.render.engine = 'BLENDER_EEVEE_NEXT'
s.eevee.taa_render_samples = a.samples
s.render.threads_mode = 'FIXED'; s.render.threads = 2
s.render.resolution_percentage = 100
s.render.image_settings.file_format = 'PNG'; s.render.film_transparent = False
s.view_settings.view_transform = 'Standard'; s.view_settings.look = 'None'
s.view_settings.exposure = 0; s.view_settings.gamma = 1
directions = {'front': front, 'quarter': front + left * .55 + up * .035, 'back': -front}
cameras = {name: make_camera(s, 'TOON_' + name, d, center, corners, height*4, 1)
           for name, d in directions.items()}
lat_center = float((lo[1] + hi[1])*.5)
head = xyz[(xyz[:, 2] >= lo[2] + height*.78) & (np.abs(xyz[:, 1] - lat_center) <= height*.20)]
hlo, hhi = head.min(axis=0), head.max(axis=0)
hc = [Vector((x, y, z)) for x in (hlo[0], hhi[0]) for y in (hlo[1], hhi[1]) for z in (hlo[2], hhi[2])]
for name, direction in [('face-front', front), ('face-quarter', front + left*.55 + up*.035)]:
    cameras[name] = make_camera(s, 'TOON_' + name, direction, Vector((hlo+hhi)*.5), hc, height*4, 1)
s.camera = cameras['quarter']
s.render.resolution_x = s.render.resolution_y = a.size
s.render.filepath = '//quarter.png'
s['toon_notes'] = 'Eevee native banded light + original atlas + outline hull. PBR backup materials retained. Not a portable glTF toon shader. Unrigged source geometry unchanged.'
for image in bpy.data.images:
    if image.source == 'FILE' and not image.packed_file:
        image.pack()
if not a.draft:
    bpy.ops.wm.save_as_mainfile(filepath=str(a.output / 'toon.blend'), compress=True)
report = {'source': str(a.input), 'source_sha256': digest, 'character': a.character,
          'source_geometry_modified': False, 'source_textures_modified': False,
          'source_pbr_backup_materials': list(material_map), 'engine': 'BLENDER_EEVEE_NEXT',
          'outline_height_fraction': a.outline, 'samples': a.samples,
          'face_mask_scope': 'Approximate warm bright colors restricted to front head in mesh-following rest coordinates; reduces only newly added shade. Not deformation-aware.',
          'limitations': ['Source atlas contains baked shading.', 'Hair/face/body remain one source mesh.',
                          'No rigging, retopology or portable engine shader conversion.'], 'renders': []}
for name in views:
    s.camera = cameras[name]
    size = a.face_size if name.startswith('face') else a.size
    s.render.resolution_x = s.render.resolution_y = size
    target = a.output / (name + '.png'); s.render.filepath = str(target)
    t = time.monotonic(); bpy.ops.render.render(write_still=True)
    report['renders'].append({'view': name, 'path': str(target), 'seconds': round(time.monotonic()-t, 2)})
report['source_file_unchanged'] = hashlib.sha256(a.input.read_bytes()).hexdigest() == digest
assert report['source_file_unchanged']
(a.output / 'toon-report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2)+'\n')
print('TOON_COMPLETE ' + json.dumps(report['renders']), flush=True)
