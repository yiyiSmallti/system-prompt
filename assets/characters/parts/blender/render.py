"""Render preview frames of each action from three cameras with Eevee.

Run: blender -b ANIMATED.blend -P render.py -- OUT_DIR [ACTION ...] [--views front34,side,back34] [--every K] [--size WxH]
Frames land in OUT_DIR/<action>/<view>/####.png; textures are cut to 2K in memory first.
Close-up views (hand, hand_top, hand_front, hand_in, sh_front, sh_back, sh_side, sh_top, shR_front, shR_back) follow a
bone; --joints 1 marks the finger joints, --hide a,b leaves meshes out.
"""
import math
import os
import sys

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
OUT = argv.pop(0)
opts = {'views': 'front34,side,back34', 'every': '1', 'size': '540x720', 'samples': '16'}
actions = []
while argv:
    a = argv.pop(0)
    if a.startswith('--'):
        opts[a[2:]] = argv.pop(0)
    else:
        actions.append(a)
actions = actions or ['Walk', 'Run']
scene = bpy.context.scene
arm = bpy.data.objects['DeepSeek']

for img in bpy.data.images:
    if max(img.size) > 2048:
        img.scale(2048, 2048)

scene.render.engine = 'BLENDER_EEVEE'
scene.eevee.taa_render_samples = int(opts['samples'])
w, h = (int(v) for v in opts['size'].split('x'))
scene.render.resolution_x, scene.render.resolution_y = w, h
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
scene.view_settings.view_transform = 'Standard'
scene.render.fps = 30

world = bpy.data.worlds.new('Preview')
world.use_nodes = True
bg = world.node_tree.nodes['Background']
bg.inputs['Color'].default_value = (0.62, 0.66, 0.72, 1)
bg.inputs['Strength'].default_value = 0.45
scene.world = world


def light(name, kind, loc, energy, size=1.0, color=(1, 1, 1)):
    d = bpy.data.lights.new(name, kind)
    d.energy = energy
    d.color = color
    if kind == 'AREA':
        d.size = size
    o = bpy.data.objects.new(name, d)
    scene.collection.objects.link(o)
    o.location = loc
    o.rotation_euler = (Vector((0, 0.2, 0.9)) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    return o


light('Key', 'AREA', (-1.6, -2.2, 2.6), 260, 2.0)
light('Fill', 'AREA', (2.4, -1.4, 1.4), 90, 3.0, (0.85, 0.9, 1.0))
light('Rim', 'AREA', (0.6, 2.6, 2.2), 220, 2.0, (0.9, 0.95, 1.0))

bpy.ops.mesh.primitive_plane_add(size=8, location=(0, 0, 0))
floor = bpy.context.object
fm = bpy.data.materials.new('Floor')
fm.use_nodes = True
fb = next(n for n in fm.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
fb.inputs['Base Color'].default_value = (0.5, 0.52, 0.56, 1)
fb.inputs['Roughness'].default_value = 0.9
# a faint grid so foot sliding shows
tex = fm.node_tree.nodes.new('ShaderNodeTexChecker')
tex.inputs['Scale'].default_value = 32
tex.inputs['Color1'].default_value = (0.20, 0.21, 0.23, 1)
tex.inputs['Color2'].default_value = (0.25, 0.26, 0.28, 1)
fm.node_tree.links.new(tex.outputs['Color'], fb.inputs['Base Color'])
floor.data.materials.append(fm)

VIEWS = {   # camera position, aim point, lens
    'front34': ((1.75, -2.85, 1.0), (0.0, 0.2, 0.72), 50),
    'side': ((3.5, 0.4, 0.85), (0.0, 0.4, 0.72), 42),
    'back34': ((-2.0, 3.1, 1.2), (0.0, 0.3, 0.65), 50),
    'front': ((0.0, -3.0, 0.95), (0.0, 0.0, 0.78), 50),
    'hips': ((0.9, -1.25, 0.95), (0.0, 0.1, 0.72), 50),
    'shoulders': ((-0.75, -1.05, 1.35), (0.0, 0.0, 1.12), 50),
    'tail': ((1.6, 1.9, 0.75), (0.0, 0.45, 0.35), 50),
    'cape': ((0.35, -0.85, 1.25), (0.0, 0.0, 1.12), 50),
    'cape_side': ((0.85, 0.15, 1.25), (0.1, 0.05, 1.12), 50),
    'hair_back': ((0.0, 1.55, 1.15), (0.0, 0.1, 1.05), 50),
    'skirt': ((0.45, -1.0, 0.8), (0.0, 0.0, 0.85), 50),
    'hand': ((0.55, -0.45, 1.3), (0.52, 0.05, 1.15), 50),
    'tail_side': ((1.7, 0.45, 0.45), (0.0, 0.45, 0.4), 50),
}
# views that follow a bone: (bone, camera offset from the point 6 cm down the bone)
FOLLOW = {
    'hand': ('LeftHand', Vector((0.26, -0.30, 0.10))),
    'hand_top': ('LeftHand', Vector((0.04, -0.06, 0.36))),
    'hand_front': ('LeftHand', Vector((0.04, -0.36, 0.0))),
    'hand_in': ('LeftHand', Vector((-0.28, -0.16, -0.06))),
    'sh_front': ('LeftUpperArm', Vector((0.05, -0.50, 0.05))),
    'sh_back': ('LeftUpperArm', Vector((0.05, 0.50, 0.08))),
    'sh_side': ('LeftUpperArm', Vector((0.50, -0.05, 0.03))),
    'sh_top': ('LeftUpperArm', Vector((0.10, -0.12, 0.48))),
    'shR_front': ('RightUpperArm', Vector((-0.05, -0.50, 0.05))),
    'shR_back': ('RightUpperArm', Vector((-0.05, 0.50, 0.08))),
}
for _v in FOLLOW:
    VIEWS.setdefault(_v, ((0, -1, 1), (0, 0, 1), 50))

# --joints 1: red dots on the finger joints and a see-through glove, to check bones against the mesh
JOINTS = []
if opts.get('joints') == '1':
    dot = bpy.data.materials.new('JointDot')
    dot.use_nodes = True
    db = next(n for n in dot.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    db.inputs['Base Color'].default_value = (1, 0.05, 0.05, 1)
    db.inputs['Emission Color'].default_value = (1, 0.05, 0.05, 1)
    db.inputs['Emission Strength'].default_value = 3.0
    for pb in arm.pose.bones:
        if any(f in pb.name for f in ('Thumb', 'Index', 'Middle', 'Ring', 'Little')) or pb.name.endswith('Hand'):
            for end in ('head', 'tail'):
                bpy.ops.mesh.primitive_uv_sphere_add(radius=0.0035, segments=12, ring_count=6)
                o = bpy.context.object
                o.data.materials.append(dot)
                JOINTS.append((o, pb, end))
    for gname in ('glove-L', 'glove-R'):
        gm = bpy.data.objects[gname].material_slots[0].material
        gb = next(n for n in gm.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
        gb.inputs['Alpha'].default_value = 0.35
        gm.surface_render_method = 'BLENDED'


def aim_follow(v, co):
    bpy.context.view_layer.update()   # pose-bone heads are stale right after a pose reset
    for o, pb, end in JOINTS:
        o.location = arm.matrix_world @ getattr(pb, end)
    if v not in FOLLOW:
        return
    bone, off = FOLLOW[v]
    pb = arm.pose.bones[bone]
    p = arm.matrix_world @ (pb.head + (pb.tail - pb.head).normalized() * 0.06)
    co.location = p + off
    co.rotation_euler = (p - co.location).to_track_quat('-Z', 'Y').to_euler()


for nm in filter(None, opts.get('hide', '').split(',')):   # --hide blouse,hair: leave meshes out
    bpy.data.objects[nm].hide_render = True

cams = {}
for v in opts['views'].split(','):
    loc, aim, lens = VIEWS[v]
    cd = bpy.data.cameras.new(v)
    cd.lens = lens
    co = bpy.data.objects.new(v, cd)
    scene.collection.objects.link(co)
    co.location = loc
    co.rotation_euler = (Vector(aim) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    cams[v] = co

every = int(opts['every'])
for act_name in actions:
    if act_name == 'Rest':   # the T-pose, one frame
        if arm.animation_data:
            arm.animation_data.action = None
        for pb in arm.pose.bones:
            pb.matrix_basis.identity()
        for v, co in cams.items():
            scene.camera = co
            aim_follow(v, co)
            scene.render.filepath = os.path.join(OUT, 'Rest', v, '0000.png')
            bpy.ops.render.render(write_still=True)
        print('rendered Rest', flush=True)
        continue
    act = bpy.data.actions[act_name]
    arm.animation_data.action = act
    if hasattr(arm.animation_data, 'action_slot') and act.slots:
        arm.animation_data.action_slot = act.slots[0]
    f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
    for v, co in cams.items():
        scene.camera = co
        d = os.path.join(OUT, act_name, v)
        os.makedirs(d, exist_ok=True)
        for f in range(f0, f1, every):
            scene.frame_set(f)
            aim_follow(v, co)
            scene.render.filepath = os.path.join(d, '%04d.png' % f)
            bpy.ops.render.render(write_still=True)
        print('rendered', act_name, v, flush=True)
