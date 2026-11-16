# 转发型（箭头刺猬）的身体：几个椭球拼出的刺猬——圆滚滚的身子、往前下方伸的尖鼻子、鼻头、
# 两只小圆耳朵和四只小短脚，体素重建焊成一个光滑网格、减面后导出 FBX。背上的箭头刺是游戏里代码做的
# （NoiseLook.ForkedArrow），这里只做身子。
#
#   blender -b -P repost_body.py -- <out.fbx> [preview.png]
#
# 正面朝 Blender 的 -Y（导出后是 Unity 的 +z），向上 +Z，单位米；身子中心在原点。
import bpy, sys, math
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
out = args[0] if args else "/tmp/RepostBody.fbx"
preview = args[1] if len(args) > 1 else None

bpy.ops.wm.read_factory_settings(use_empty=True)
parts = []

def ball(at, size, turn=(0, 0, 0)):
    """一个椭球：中心 at，三个半轴 size（米），绕 X/Y/Z 转 turn 度。"""
    bpy.ops.mesh.primitive_uv_sphere_add(segments=40, ring_count=20, radius=1, location=at,
                                         rotation=tuple(math.radians(a) for a in turn))
    o = bpy.context.active_object
    o.scale = size
    parts.append(o)

# 身子：前后略长、背上圆、肚子稍平。
ball((0, 0.03, 0.02), (0.5, 0.56, 0.47))
ball((0, 0.02, -0.16), (0.45, 0.5, 0.33))
# 尖鼻子：从脸的下半往前、略往下伸，鼻头一颗小圆球。
ball((0, -0.4, -0.12), (0.26, 0.26, 0.2), (-8, 0, 0))
ball((0, -0.62, -0.17), (0.14, 0.24, 0.115), (-10, 0, 0))
ball((0, -0.86, -0.21), (0.06, 0.055, 0.055))
# 小圆耳朵，在眼睛斜上方，略朝外。
for s in (-1, 1):
    ball((0.33 * s, -0.2, 0.36), (0.1, 0.06, 0.12), (0, 25 * s, 0))
# 四只小短脚。
for s in (-1, 1):
    for y in (-0.24, 0.26):
        ball((0.25 * s, y, -0.47), (0.11, 0.13, 0.09))

for o in parts:
    o.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
bpy.ops.object.join()
mesh_obj = bpy.context.active_object
mesh_obj.name = "RepostBody"
# 焊成一个整体（相交处成了一个面），再磨出各部分接缝处的圆角。
rm = mesh_obj.modifiers.new("Remesh", 'REMESH')
rm.mode = 'VOXEL'
rm.voxel_size = 0.018
bpy.ops.object.modifier_apply(modifier=rm.name)
sm = mesh_obj.modifiers.new("Smooth", 'LAPLACIANSMOOTH')
sm.lambda_factor = 0.6
sm.iterations = 6
bpy.ops.object.modifier_apply(modifier=sm.name)
# 减面到两千来个三角，保持光滑。
dec = mesh_obj.modifiers.new("Decimate", 'DECIMATE')
dec.ratio = min(1.0, 2400 / max(1, len(mesh_obj.data.polygons)))
bpy.ops.object.modifier_apply(modifier=dec.name)
bpy.ops.object.shade_smooth()
bb = [mesh_obj.matrix_world @ Vector(c) for c in mesh_obj.bound_box]
lo = Vector((min(v.x for v in bb), min(v.y for v in bb), min(v.z for v in bb)))
hi = Vector((max(v.x for v in bb), max(v.y for v in bb), max(v.z for v in bb)))
print("RepostBody tris", sum(len(p.vertices) - 2 for p in mesh_obj.data.polygons), "bounds", tuple(round(x, 3) for x in lo), tuple(round(x, 3) for x in hi))

bpy.ops.export_scene.fbx(filepath=out, use_selection=True, axis_forward='-Z', axis_up='Y', apply_unit_scale=True,
                         apply_scale_options='FBX_SCALE_UNITS', mesh_smooth_type='FACE', add_leaf_bones=False, bake_anim=False)

if preview:
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'SINGLE'
    scene.display.shading.single_color = (0.35, 0.6, 0.95)
    scene.display.shading.show_cavity = True
    scene.render.resolution_x, scene.render.resolution_y = 900, 450
    scene.render.film_transparent = False
    world = bpy.data.worlds.new("W"); scene.world = world
    # 两个机位：前侧 3/4 和正侧面，各渲一张拼起来太麻烦，就并排放两个副本。
    side = mesh_obj.copy(); side.data = mesh_obj.data; scene.collection.objects.link(side)
    mesh_obj.location = (-0.9, 0, 0); mesh_obj.rotation_euler = (0, 0, math.radians(-35))
    side.location = (0.9, 0, 0); side.rotation_euler = (0, 0, math.radians(-90))
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); scene.collection.objects.link(cam)
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = 3.8
    cam.location = (0, -6, 0.6); cam.rotation_euler = (math.radians(84), 0, 0)
    scene.camera = cam
    scene.render.filepath = preview
    bpy.ops.render.render(write_still=True)
