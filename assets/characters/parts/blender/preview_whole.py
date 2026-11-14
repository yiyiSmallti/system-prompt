"""Preview renders of a whole-character model from fixed cameras, with Eevee.

Run: blender -b [MODEL.blend] -P preview_whole.py -- OUT_PREFIX [--glb PATH] [--yaw DEG] [--views a,b]
         [--size WxH] [--rig soft|hard] [--toon 1] [--albedo 1]
Writes OUT_PREFIX<view>.png with a transparent background. The model is scaled to 1.0 tall, feet on z=0,
facing -Y (yaw applied first). Open the refined .blend rather than its .glb to see sheen and subsurface.
- default: PBR under a soft studio rig (mostly sky light, one very large key); --rig hard is the three-point
  rig, which blows out the whites and makes every surface look glossier than it is;
- --toon 1: close to how the game draws characters: two flat tones with a hard edge, cool purple shade, an
  inverted-hull outline, one light from the camera's upper right, the face shaded as a ball around HEAD.
  No cast shadows: Eevee's shadow maps stripe on a mesh this coarse, and the game casts its own;
- --albedo 1: base colour only, to judge the texture without any lighting.
"""
import math, sys
import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
OUT = argv.pop(0)
o = {'glb': '', 'yaw': '0', 'views': 'front,f34,side,back,face,chest,skirt,boots', 'size': '900x1200'}
while argv:
    k = argv.pop(0)
    o[k[2:]] = argv.pop(0)
if o['glb']:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=o['glb'])
scene = bpy.context.scene
meshes = [ob for ob in scene.objects if ob.type == 'MESH']
root = bpy.data.objects.new('ViewRoot', None)
scene.collection.objects.link(root)
for ob in scene.objects:
    if ob.parent is None and ob is not root:
        ob.parent = root
root.rotation_euler = (0, 0, math.radians(float(o['yaw'])))
bpy.context.view_layer.update()
pts = [ob.matrix_world @ Vector(c) for ob in meshes for c in ob.bound_box]
zmin, zmax = min(p.z for p in pts), max(p.z for p in pts)
s = 1.0 / (zmax - zmin)
root.scale = (s, s, s)
bpy.context.view_layer.update()
pts = [ob.matrix_world @ Vector(c) for ob in meshes for c in ob.bound_box]
root.location = (-(min(p.x for p in pts) + max(p.x for p in pts)) / 2, 0, -min(p.z for p in pts))

if o.get('albedo') == '1':
    for mat in bpy.data.materials:
        nt = mat.node_tree
        b = next((n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'), None)
        if not b or not b.inputs['Base Color'].links:
            continue
        src = b.inputs['Base Color'].links[0].from_socket
        e = nt.nodes.new('ShaderNodeEmission')
        nt.links.new(src, e.inputs['Color'])
        out = next(n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL')
        nt.links.new(e.outputs[0], out.inputs['Surface'])
TOON = o.get('toon') == '1'
HEAD = (0.0, -0.165, 0.83)      # head centre in model units: the face is shaded as a ball around it
SHADE_TINT = (0.70, 0.68, 0.90, 1)
OUTLINE = 0.0016


def toon(mat):
    nt = mat.node_tree
    N, L = nt.nodes, nt.links
    b = next((n for n in N if n.type == 'BSDF_PRINCIPLED'), None)
    if not b or not b.inputs['Base Color'].links:
        return
    src = b.inputs['Base Color'].links[0].from_socket
    diff = N.new('ShaderNodeBsdfDiffuse')
    diff.inputs['Color'].default_value = (1, 1, 1, 1)
    normal = b.inputs['Normal'].links[0].from_socket if b.inputs['Normal'].links else None
    split = N.get('SurfaceSplit')
    if split:   # face: skin above the chin takes ball normals
        tc = N.new('ShaderNodeTexCoord')
        sub = N.new('ShaderNodeVectorMath'); sub.operation = 'SUBTRACT'; sub.inputs[1].default_value = HEAD
        L.new(tc.outputs['Object'], sub.inputs[0])
        nrm = N.new('ShaderNodeVectorMath'); nrm.operation = 'NORMALIZE'
        L.new(sub.outputs[0], nrm.inputs[0])
        vt = N.new('ShaderNodeVectorTransform'); vt.vector_type = 'NORMAL'; vt.convert_from = 'OBJECT'; vt.convert_to = 'WORLD'
        L.new(nrm.outputs[0], vt.inputs[0])
        xyz = N.new('ShaderNodeSeparateXYZ'); L.new(tc.outputs['Object'], xyz.inputs[0])
        up = N.new('ShaderNodeMath'); up.operation = 'GREATER_THAN'; up.inputs[1].default_value = 0.775
        L.new(xyz.outputs['Z'], up.inputs[0])
        sk = N.new('ShaderNodeMath'); sk.operation = 'GREATER_THAN'; sk.inputs[1].default_value = 0.12
        L.new(split.outputs['Blue'], sk.inputs[0])
        both = N.new('ShaderNodeMath'); both.operation = 'MULTIPLY'
        L.new(up.outputs[0], both.inputs[0]); L.new(sk.outputs[0], both.inputs[1])
        mixn = N.new('ShaderNodeMix'); mixn.data_type = 'VECTOR'
        L.new(both.outputs[0], mixn.inputs['Factor'])
        if normal:
            L.new(normal, mixn.inputs[4])
        else:
            geo = N.new('ShaderNodeNewGeometry'); L.new(geo.outputs['Normal'], mixn.inputs[4])
        L.new(vt.outputs[0], mixn.inputs[5])
        normal = mixn.outputs[1]
    if normal:
        L.new(normal, diff.inputs['Normal'])
    s2r = N.new('ShaderNodeShaderToRGB'); L.new(diff.outputs[0], s2r.inputs[0])
    ramp = N.new('ShaderNodeValToRGB')
    ramp.color_ramp.elements[0].position, ramp.color_ramp.elements[1].position = 0.30, 0.33
    L.new(s2r.outputs[0], ramp.inputs[0])
    dark = N.new('ShaderNodeMix'); dark.data_type = 'RGBA'; dark.blend_type = 'MULTIPLY'; dark.inputs['Factor'].default_value = 1
    L.new(src, dark.inputs[6]); dark.inputs[7].default_value = SHADE_TINT
    mix = N.new('ShaderNodeMix'); mix.data_type = 'RGBA'
    L.new(ramp.outputs['Color'], mix.inputs['Factor'])
    L.new(dark.outputs[2], mix.inputs[6]); L.new(src, mix.inputs[7])
    em = N.new('ShaderNodeEmission'); L.new(mix.outputs[2], em.inputs['Color'])
    out = next(n for n in N if n.type == 'OUTPUT_MATERIAL')
    L.new(em.outputs[0], out.inputs['Surface'])


if TOON:
    for mat in list(bpy.data.materials):
        toon(mat)
    line = bpy.data.materials.new('Outline'); line.use_nodes = True
    ln = line.node_tree
    e = ln.nodes.new('ShaderNodeEmission'); e.inputs['Color'].default_value = (0.05, 0.05, 0.12, 1)
    ln.links.new(e.outputs[0], next(n for n in ln.nodes if n.type == 'OUTPUT_MATERIAL').inputs['Surface'])
    line.use_backface_culling = True
    for ob in meshes:
        ob.data.materials.append(line)
        m = ob.modifiers.new('Outline', 'SOLIDIFY')
        m.thickness, m.offset, m.use_flip_normals, m.use_rim = OUTLINE, 1, True, False
        m.material_offset = len(ob.data.materials) - 1
scene.render.engine = 'BLENDER_EEVEE'
scene.eevee.taa_render_samples = 32
w, h = (int(v) for v in o['size'].split('x'))
scene.render.resolution_x, scene.render.resolution_y = w, h
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGBA'
scene.render.image_settings.quality = 92
scene.view_settings.view_transform = 'Standard'
world = bpy.data.worlds.new('W'); world.use_nodes = True
bg = world.node_tree.nodes['Background']
bg.inputs['Color'].default_value = (0.55, 0.58, 0.63, 1)
bg.inputs['Strength'].default_value = 0.35
scene.world = world
scene.render.film_transparent = True


def light(name, loc, energy, size, color=(1, 1, 1)):
    d = bpy.data.lights.new(name, 'AREA'); d.energy = energy; d.size = size; d.color = color
    ob = bpy.data.objects.new(name, d); scene.collection.objects.link(ob)
    ob.location = loc
    ob.rotation_euler = (Vector((0, 0, 0.55)) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()


rig = 'toon' if TOON else o.get('rig', 'soft')
sun = None
if rig == 'toon':
    bg.inputs['Strength'].default_value = 0.0
    sd = bpy.data.lights.new('Sun', 'SUN'); sd.energy = math.pi; sd.angle = math.radians(1.5)
    sun = bpy.data.objects.new('Sun', sd); scene.collection.objects.link(sun)
    sd.use_shadow = False
if rig == 'hard':
    light('Key', (-1.4, -2.2, 2.0), 150, 1.5)
    light('Fill', (2.0, -1.6, 1.0), 50, 2.5, (0.9, 0.95, 1))
    light('Rim', (0.8, 2.4, 1.8), 160, 1.2)
elif rig == 'soft':
    # soft studio: mostly sky light, one very large key, a faint rim; whites stay under 1.0
    bg.inputs['Color'].default_value = (0.85, 0.85, 0.86, 1)
    bg.inputs['Strength'].default_value = 0.62
    light('Key', (-1.6, -2.6, 2.2), 95, 5.0, (1.0, 0.97, 0.94))
    light('Rim', (1.0, 2.4, 1.6), 45, 3.0, (0.9, 0.95, 1.0))
cam_d = bpy.data.cameras.new('Cam'); cam = bpy.data.objects.new('Cam', cam_d)
scene.collection.objects.link(cam); scene.camera = cam
cam_d.lens = 85
# view: (azimuth deg from front, target z, distance, target x)
V = {'front': (0, 0.5, 4.3, 0), 'f34': (-35, 0.5, 4.3, 0), 'side': (-90, 0.5, 4.3, 0), 'back': (180, 0.5, 4.3, 0),
     'b34': (145, 0.5, 4.3, 0), 'face': (0, 0.84, 0.95, 0), 'face34': (-30, 0.84, 0.95, 0), 'chest': (0, 0.66, 1.1, 0),
     'skirt': (-15, 0.47, 1.25, 0), 'boots': (-20, 0.13, 1.1, 0), 'hand': (0, 0.72, 0.8, -0.4), 'tail': (150, 0.2, 2.0, 0)}
for v in o['views'].split(','):
    az, tz, dist, tx = V[v]
    a = math.radians(az)
    tgt = Vector((tx, 0, tz))
    cam.location = tgt + Vector((math.sin(a) * dist, -math.cos(a) * dist, 0.05 * dist))
    cam.rotation_euler = (tgt - cam.location).to_track_quat('-Z', 'Y').to_euler()
    if sun:   # from the camera's upper right
        right = Vector((math.cos(a), math.sin(a), 0))
        back = Vector((math.sin(a), -math.cos(a), 0))
        d = (right * 0.9 + back * 1.0 + Vector((0, 0, 1.1))).normalized()
        sun.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    scene.render.filepath = OUT + v + '.png'
    bpy.ops.render.render(write_still=True)
print('DONE')
