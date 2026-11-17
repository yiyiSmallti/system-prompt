# 光鲸虚影的身子：一条座头鲸（修长的身体、喉部微鼓、很长的胸鳍、小背鳍、带缺口的尾叶），
# 给大招里 PV 那样的鲸鱼虚影用（game/.../Resources/Models/Props/WhalePhantom.fbx，Phantom.shader）。
# 体长 1 米，正面朝 Blender 的 -Y（导出后是 Unity 的 +z），身体中心在原点。
#   blender -b -P whale_phantom.py -- <out.fbx> [preview.png]
import bpy, bmesh, math, sys
from mathutils import Vector

args = sys.argv[sys.argv.index('--') + 1:]
out = args[0]
preview = args[1] if len(args) > 1 else None
bpy.ops.wm.read_factory_settings(use_empty=True)


def curve(keys, s):
    """A smooth curve through (s, value) keys (Catmull-Rom): straight lines between them crease the body."""
    s = max(keys[0][0], min(keys[-1][0], s))
    for i in range(len(keys) - 1):
        (s0, v0), (s1, v1) = keys[i], keys[i + 1]
        if s <= s1:
            vp = keys[i - 1][1] if i > 0 else v0
            vn = keys[i + 2][1] if i + 2 < len(keys) else v1
            sp = keys[i - 1][0] if i > 0 else s0 - (s1 - s0)
            sn = keys[i + 2][0] if i + 2 < len(keys) else s1 + (s1 - s0)
            k = (s - s0) / (s1 - s0)
            m0 = (v1 - vp) / (s1 - sp) * (s1 - s0)
            m1 = (vn - v0) / (sn - s0) * (s1 - s0)
            k2, k3 = k * k, k * k * k
            return (2 * k3 - 3 * k2 + 1) * v0 + (k3 - 2 * k2 + k) * m0 + (-2 * k3 + 3 * k2) * v1 + (k3 - k2) * m1
    return keys[-1][1]


def loft(name, rings):
    """Closed rings of equal size joined in order, both ends capped."""
    bm = bmesh.new()
    verts = [[bm.verts.new(p) for p in ring] for ring in rings]
    n = len(rings[0])
    for a, b in zip(verts, verts[1:]):
        for j in range(n):
            bm.faces.new((a[j], a[(j + 1) % n], b[(j + 1) % n], b[j]))
    bm.faces.new(list(reversed(verts[0])))
    bm.faces.new(verts[-1])
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    return o


def superellipse(t, n=2.4):
    c, s = math.cos(t), math.sin(t)
    return math.copysign(abs(c) ** (2 / n), c), math.copysign(abs(s) ** (2 / n), s)


M = 40
BODY_END = 0.42           # where the body meets the flukes (y)
NOSE = -0.5

# The body: half width, half height above and the throat's sag below its middle line, along it from the nose.
W = [(0, 0.012), (0.05, 0.045), (0.15, 0.082), (0.32, 0.118), (0.45, 0.115), (0.6, 0.09), (0.75, 0.058), (0.88, 0.034), (1.0, 0.02)]
H = [(0, 0.01), (0.05, 0.026), (0.15, 0.05), (0.32, 0.085), (0.45, 0.095), (0.6, 0.088), (0.75, 0.066), (0.88, 0.046), (1.0, 0.03)]
BELLY = [(0, 1.2), (0.1, 1.5), (0.3, 1.45), (0.45, 1.15), (0.6, 1.0), (1.0, 1.0)]
MID = [(0, -0.018), (0.15, -0.01), (0.4, 0.0), (1.0, 0.006)]
rings = []
for i in range(61):
    s = i / 60
    y = NOSE + s * (BODY_END - NOSE)
    w, h, b, c = curve(W, s), curve(H, s), curve(BELLY, s), curve(MID, s)
    ring = []
    for j in range(M):
        cx, cz = superellipse(2 * math.pi * j / M)
        ring.append(Vector((w * cx, y, c + h * cz * (b if cz < 0 else 1.0))))
    rings.append(ring)
parts = [loft("Body", rings)]


def blade(name, root, out_dir, back_dir, span, chord, thick, sweep, droop=None, sections=14, lead=0.3):
    """A fin: airfoil sections along `out_dir` from `root`, chord toward `back_dir` (`lead` of it ahead of the
    span line), chord and thickness tapering by the curves given over 0..1 of the span; `sweep` moves each
    section back by that much at the tip (squared), `droop` bends it down."""
    out_dir = out_dir.normalized()
    back = (back_dir - out_dir * back_dir.dot(out_dir)).normalized()
    up = out_dir.cross(back).normalized()
    rings = []
    for i in range(sections + 1):
        u = i / sections
        centre = root + out_dir * (span * u) + back * (sweep * u * u)
        if droop:
            centre += Vector((0, 0, -droop * u * u))
        ch, th = curve(chord, u), curve(thick, u)
        ring = []
        for j in range(16):
            a = 2 * math.pi * j / 16
            x = (math.cos(a) * 0.5 + 0.5 - lead) * ch
            ring.append(centre + back * x + up * (math.sin(a) * th * 0.5 * (1.0 - 0.5 * (math.cos(a) * 0.5 + 0.5))))
        rings.append(ring)
    return loft(name, rings)


# The flippers: a third of the body long, narrow, out from low on the sides behind the head, swept back and down.
for side in (-1, 1):
    parts.append(blade("Flipper", Vector((side * 0.09, NOSE + 0.3 * 0.92, -0.07)), Vector((side, 0.5, -0.5)), Vector((0, 1, 0)),
                       0.34, [(0, 0.09), (0.5, 0.06), (0.85, 0.036), (1, 0.008)], [(0, 0.018), (1, 0.005)], 0.06))
# The flukes: a broad tail, each half swept back from the middle to a point, with a notch between them.
for side in (-1, 1):
    parts.append(blade("Fluke", Vector((0, BODY_END - 0.02, 0.004)), Vector((side, 0.28, 0.02)), Vector((0, 1, 0)),
                       0.21, [(0, 0.075), (0.25, 0.1), (0.6, 0.075), (0.9, 0.035), (1, 0.008)], [(0, 0.02), (1, 0.004)], 0.07, lead=0.15))
# The small dorsal fin on a low hump two thirds of the way back.
parts.append(blade("Dorsal", Vector((0, NOSE + 0.66 * 0.92, 0.05)), Vector((0, 0.35, 1)), Vector((0, 1, 0)),
                   0.05, [(0, 0.1), (1, 0.012)], [(0, 0.018), (1, 0.004)], 0.035, sections=6, lead=0.2))

bpy.ops.object.select_all(action='DESELECT')
for o in parts:
    o.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
bpy.ops.object.join()
whale = bpy.context.active_object
whale.name = "WhalePhantom"
rm = whale.modifiers.new("Remesh", 'REMESH')
rm.mode = 'VOXEL'
rm.voxel_size = 0.0035
bpy.ops.object.modifier_apply(modifier=rm.name)
sm = whale.modifiers.new("Smooth", 'SMOOTH')
sm.factor = 0.5
sm.iterations = 10
bpy.ops.object.modifier_apply(modifier=sm.name)
dec = whale.modifiers.new("Decimate", 'DECIMATE')
dec.ratio = min(1.0, 9000 / max(1, len(whale.data.polygons)))
bpy.ops.object.modifier_apply(modifier=dec.name)
bpy.ops.object.shade_smooth()
bb = [whale.matrix_world @ Vector(c) for c in whale.bound_box]
print("WhalePhantom tris", sum(len(p.vertices) - 2 for p in whale.data.polygons),
      "bounds", tuple(round(min(v[k] for v in bb), 3) for k in range(3)), tuple(round(max(v[k] for v in bb), 3) for k in range(3)))

bpy.ops.export_scene.fbx(filepath=out, use_selection=True, axis_forward='-Z', axis_up='Y', apply_unit_scale=True,
                         apply_scale_options='FBX_SCALE_UNITS', mesh_smooth_type='FACE', add_leaf_bones=False, bake_anim=False)

if preview:
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'SINGLE'
    scene.display.shading.single_color = (0.35, 0.6, 0.95)
    scene.display.shading.show_cavity = True
    scene.render.resolution_x, scene.render.resolution_y = 1200, 900
    world = bpy.data.worlds.new("W"); scene.world = world
    # 三个副本，镜头从 -Y 看过去：上面是正侧面（头朝左），中间是俯视，下面是从前下方看的 3/4。
    from mathutils import Matrix
    length_x = Matrix.Rotation(math.radians(-90), 4, 'Z')        # nose (-Y) to -X, the left of the picture
    views = [((0, 0, 0.62), length_x),
             ((0, 0, 0.0), Matrix.Rotation(math.radians(90), 4, 'X') @ length_x),
             ((0, 0, -0.62), Matrix.Rotation(math.radians(-35), 4, 'X') @ Matrix.Rotation(math.radians(-40), 4, 'Z'))]
    for k, (at, turn) in enumerate(views):
        o = whale if k == 0 else whale.copy()
        if k:
            o.data = whale.data
            scene.collection.objects.link(o)
        o.matrix_world = Matrix.Translation(at) @ turn
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); scene.collection.objects.link(cam)
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = 2.1
    cam.location = (0, -6, 0.1); cam.rotation_euler = (math.radians(90), 0, 0)
    scene.camera = cam
    scene.render.filepath = preview
    bpy.ops.render.render(write_still=True)
