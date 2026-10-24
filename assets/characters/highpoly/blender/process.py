"""Rebuild a Tripo 2M-triangle DeepSeek battle-suit model as game-ready low-poly meshes.

Runs on the bpy wheel (pip install bpy==5.2.3 on Python 3.13) or inside Blender:
    python process.py [stage ...] [--model 8k|mv] [--src GLB] [--work DIR]
    blender -b --python process.py -- [stage ...] [--model 8k|mv] [--src GLB] [--work DIR]

--model picks the measured layout of one source mesh (joint positions, regions, cameras)
and the output prefix; --src overrides the GLB it reads (default: that model's file).

Stages (default: all but cross, in this order):
    import    load the GLB, report size / triangles / materials / UVs
    baseline  Cycles renders of the high-poly model (front, side, face)
    decimate  clean up + region-weighted decimation to ~40k and ~100k triangles,
              plus a uniform 40k reference
    uv        xatlas charts per region (Smart UV Project fallback); face and hands get
              extra texel density
    bake      Cycles selected-to-active bakes (base color, normal, metallic/roughness)
    export    one GLB per tier with embedded textures
    compare   same-camera renders: high poly vs both tiers, base color only / + normal map
    rig       Mixamo-named humanoid + tail bones, automatic weights on the 100k tier,
              test poses, FBX for Unity Humanoid
    cross     40k tier of every model side by side (needs each model's decimate/uv/bake
              in the same --work)

Also uses xatlas (pip install xatlas) for UVs when present and Pillow for the comparison
sheets. Intermediate .blend files go to --work (default: <tmp>/deepseek-blender-work).
"""

import argparse
import json
import math
import os
import sys
import tempfile
import time

import bpy  # must come first: the pip wheel only exposes bmesh/mathutils after bpy loads
import bmesh
import numpy as np
from bpy_extras.bmesh_utils import bmesh_linked_uv_islands
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
MODELS = os.path.join(HERE, 'models')
TEXTURES = os.path.join(HERE, 'textures')
RENDERS = os.path.join(HERE, 'renders')
STATS = os.path.join(HERE, 'stats.json')
WORK = None  # set from --work

# After glTF axis conversion the Tripo meshes face +X. Turn them to face -Y like Mixamo
# characters, so the FBX arrives in Unity facing +Z.
FACE_MINUS_Y = Matrix.Rotation(math.radians(-90), 4, 'Z')

# Triangle budgets. "face" covers face + bangs, "hands" both hands up to the wrist cuffs.
TIER_BUDGETS = {
    'lp40k': dict(total=40_000, face=6_000, hands=1_600),
    'lp100k': dict(total=100_000, face=14_000, hands=4_000),
}

# Extra linear texel density for face and hands islands before packing.
UV_SCALE = {'face': 2.0, 'hands': 1.5}
UV_PADDING = 6  # texels between xatlas charts at its ~4-5k working resolution
UV_MARGIN = 0.0015  # island margin for the Smart UV fallback, fraction of the texture

ARMS_UP = [('LeftArm', 'Y', -75), ('RightArm', 'Y', 75),
           ('LeftForeArm', 'Y', -15), ('RightForeArm', 'Y', 15)]
STEP = [('LeftUpLeg', 'X', -40), ('LeftLeg', 'X', 55), ('LeftFoot', 'X', -10),
        ('RightUpLeg', 'X', 18), ('RightLeg', 'X', 10)]


class Model:
    """One source mesh: where it is, what its outputs are called, and coordinates measured on it.

    All coordinates are metres in the turned frame: +X is the character's left, -Y front, Z up.
    Joint lists hold the character's left side and the centre line.
    """

    def __init__(self, key, src, prefix, stats_key, label, **measured):
        self.key, self.src, self.prefix, self.stats_key, self.label = key, src, prefix, stats_key, label
        self.__dict__.update(measured)
        self.tiers = {prefix + k: v for k, v in TIER_BUDGETS.items()}
        # Uniform decimation with no density control: stands in for Tripo's own 40k export,
        # which is not available in this repository.
        self.reference = (prefix + 'uni40k', 40_000)
        self.rig_tier = prefix + 'lp100k'
        self.bakes = {t: self.bake_maps for t in self.tiers}
        self.poses = {'arms_up': ARMS_UP, 'step': STEP, 'tail_swing': self.tail_swing}

    @property
    def tail_bones(self):
        return [f'Tail{i}' for i in range(1, len(self.tail))]


PRESETS = {
    # Tripo H3.1 from one 4K T-pose picture, 8K base colour. Tail hangs to the floor behind the
    # heels and ends in a fin standing up behind the feet.
    '8k': Model(
        '8k', os.path.join(HERE, '..', 'deepseek-4-battle-2m-8k.glb'), '', None, '2m-8k',
        face_box=dict(z=(0.765, 0.90), y_max=-0.02, x_max=0.075), hands_x=0.335,
        bake_maps=[('basecolor', 4096), ('normal', 4096), ('mr', 2048), ('basecolor', 8192)],
        views={
            'face': ((0, -0.04, 0.825), (0, -1, 0.04), 0.17, (800, 800)),
            'face34': ((0, -0.04, 0.825), (-0.75, -1, 0.08), 0.17, (800, 800)),
            'eyes': ((0, -0.08, 0.832), (0, -1, 0.02), 0.06, (800, 480)),
            # left hand: the right one faces the key light and the white glove clips
            'hand': ((0.395, -0.05, 0.70), (0.35, -1, 0.45), 0.13, (800, 800)),
            'cape': ((0.12, -0.01, 0.69), (0.8, -0.9, 0.35), 0.22, (800, 800)),
            'tailfin': ((0, 0.07, 0.08), (0.35, 1, 0.75), 0.36, (800, 800)),
        },
        spine=[('Hips', (0, -0.035, 0.475)), ('Spine', (0, -0.035, 0.53)), ('Spine1', (0, -0.035, 0.59)),
               ('Spine2', (0, -0.035, 0.65)), ('Neck', (0, -0.035, 0.725)), ('Head', (0, -0.03, 0.765))],
        head_top=(0, -0.025, 0.93),
        arm=[('Shoulder', (0.015, -0.04, 0.715)), ('Arm', (0.075, -0.045, 0.707)),
             ('ForeArm', (0.21, -0.045, 0.708)), ('Hand', (0.345, -0.048, 0.705))],
        hand_end=(0.435, -0.052, 0.697),
        leg=[('UpLeg', (0.045, -0.045, 0.455)), ('Leg', (0.04, -0.055, 0.255)),
             ('Foot', (0.037, -0.035, 0.07)), ('ToeBase', (0.037, -0.095, 0.022))],
        toe_end=(0.037, -0.13, 0.022),
        # The tail leaves the body under the skirt behind the hips and ends in the fin behind the feet.
        tail=[(0, 0.03, 0.45), (0, 0.03, 0.34), (0, 0.035, 0.22), (0, 0.07, 0.12), (0, 0.15, 0.065)],
        tail_radius=0.025, tail_z_max=0.47,
        # Cutting out and refilling the 5 pinched spots works here and is what the committed
        # 8k outputs were made with; 'mv' needs 'separate' (refilling diverges on its spots).
        fold_repair='refill',
        hair_bones=('Head', 'Neck'),  # bone heat gives the long hair to the head
        fin=dict(z_max=0.14, y_min=0.015),  # the fin behind the heels
        z_hem=0.35,  # lowest point of the skirt frills
        leg_reach=None,  # below the hem everything that is not tail belongs to a leg
        skirt_z=(0.30, 0.60),
        tail_swing=[('Tail1', 'Z', 12), ('Tail2', 'Z', 18), ('Tail3', 'Z', 24), ('Tail4', 'Z', 28),
                    ('Tail2', 'X', -8), ('Tail3', 'X', -10)],
    ),
    # Tripo H3.1 multi-view (front, left, back T-pose renders), 4K textures. Tripo centres the
    # bounding box, and the tail lies along the floor behind, so the body sits ~13 cm further
    # forward than in '8k'. Apron ribbons hang behind the legs down to the calves.
    'mv': Model(
        'mv', os.path.join(HERE, '..', 'deepseek-4-battle-2m-mv3d.glb'), 'mv_', 'mv3d', '2m-mv3d',
        face_box=dict(z=(0.765, 0.90), y_max=-0.16, x_max=0.075), hands_x=0.335,
        # The source base colour is 4K; an 8K bake would only resample it.
        bake_maps=[('basecolor', 4096), ('normal', 4096), ('mr', 2048)],
        views={
            'face': ((0, -0.175, 0.825), (0, -1, 0.04), 0.17, (800, 800)),
            'face34': ((0, -0.175, 0.825), (-0.75, -1, 0.08), 0.17, (800, 800)),
            'eyes': ((0, -0.215, 0.846), (0, -1, 0.02), 0.06, (800, 480)),
            'hand': ((0.395, -0.17, 0.70), (0.35, -1, 0.45), 0.13, (800, 800)),
            'cape': ((0.12, -0.13, 0.69), (0.8, -0.9, 0.35), 0.22, (800, 800)),
            'tailfin': ((0, 0.22, 0.02), (0.3, 0.9, 1.0), 0.5, (800, 800)),
        },
        spine=[('Hips', (0, -0.16, 0.48)), ('Spine', (0, -0.16, 0.535)), ('Spine1', (0, -0.16, 0.595)),
               ('Spine2', (0, -0.16, 0.655)), ('Neck', (0, -0.155, 0.725)), ('Head', (0, -0.15, 0.765))],
        head_top=(0, -0.145, 0.93),
        arm=[('Shoulder', (0.015, -0.165, 0.715)), ('Arm', (0.075, -0.167, 0.708)),
             ('ForeArm', (0.21, -0.166, 0.711)), ('Hand', (0.345, -0.169, 0.708))],
        hand_end=(0.435, -0.172, 0.698),
        leg=[('UpLeg', (0.042, -0.19, 0.455)), ('Leg', (0.038, -0.178, 0.265)),
             ('Foot', (0.034, -0.17, 0.075)), ('ToeBase', (0.033, -0.225, 0.022))],
        toe_end=(0.033, -0.26, 0.022),
        # Down from the back of the waist, a near right-angle bend just above the floor, then
        # along the floor to the flat two-lobed fin: three bones upright, two along the floor
        # up to the fin and one for the fin.
        tail=[(0, -0.07, 0.48), (0, -0.045, 0.34), (0, -0.037, 0.20), (0, -0.03, 0.07),
              (0, 0.04, 0.03), (0, 0.14, 0.021), (0, 0.30, 0.013)],
        tail_radius=0.035, tail_z_max=0.50,
        fold_repair='separate',
        # The hair hangs further off the back and bone heat gives its lower half to Spine2;
        # every Spine2-led vertex sits behind the back or out at the sides, i.e. is hair.
        hair_bones=('Head', 'Neck', 'Spine2'),
        fin=dict(z_max=0.045, y_min=-0.02),  # the tail and fin lying on the floor
        z_hem=0.375,
        leg_reach=0.075,  # the ribbons hang below the hem too: only what is close to a leg is leg
        foot_box=dict(z_max=0.12, y_max=-0.10),  # boots, heels and soles
        skirt_z=(0.12, 0.60),  # skirt plus the ribbons down to the calves
        tail_swing=[('Tail4', 'Z', 20), ('Tail5', 'Z', 18), ('Tail6', 'Z', 15),
                    ('Tail4', 'X', 15), ('Tail5', 'X', 10)],
    ),
}
M = PRESETS['8k']  # set from --model


def region_face(p):
    x, y, z = p[:, 0], p[:, 1], p[:, 2]
    box = M.face_box
    return (z > box['z'][0]) & (z < box['z'][1]) & (y < box['y_max']) & (np.abs(x) < box['x_max'])


def region_hands(p):
    return (np.abs(p[:, 0]) > M.hands_x) & (p[:, 2] > 0.62)


def region_rest(p):
    return ~(region_face(p) | region_hands(p))


REGIONS = {'face': region_face, 'hands': region_hands, 'rest': region_rest}


# ---------------------------------------------------------------- helpers

def log(*args):
    print('[process]', *args, flush=True)


def read_stats(model=None):
    """stats.json section of a model: the top level for '8k', a sub-dict for the others."""
    model = model or M
    stats = {}
    if os.path.exists(STATS):
        with open(STATS, encoding='utf-8') as f:
            stats = json.load(f)
    return stats, (stats.setdefault(model.stats_key, {}) if model.stats_key else stats)


def update_stats(key, value):
    stats, section = read_stats()
    section[key] = value
    with open(STATS, 'w', encoding='utf-8') as f:
        json.dump(stats, f, indent=2, ensure_ascii=False)
        f.write('\n')


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def activate(ob, select=()):
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in select:
        o.select_set(True)
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob


def new_material(name):
    mat = bpy.data.materials.new(name)
    if mat.node_tree is None:  # Blender < 5.0
        mat.use_nodes = True
    return mat


def import_highpoly():
    reset()
    bpy.ops.import_scene.gltf(filepath=M.src)
    ob = next(o for o in bpy.data.objects if o.type == 'MESH')
    ob.data.transform(FACE_MINUS_Y @ ob.matrix_world)
    ob.matrix_world = Matrix()
    ob.name = 'HighPoly'
    return ob


def save_lowpoly(ob, name):
    os.makedirs(WORK, exist_ok=True)
    ob.name = name
    ob.data.name = name
    bpy.data.libraries.write(os.path.join(WORK, f'{name}.blend'), {ob}, fake_user=True)


def load_lowpoly(name):
    with bpy.data.libraries.load(os.path.join(WORK, f'{name}.blend')) as (src, dst):
        dst.objects = [name]
    ob = dst.objects[0]
    bpy.context.scene.collection.objects.link(ob)
    return ob


def vert_co(me):
    co = np.empty(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get('co', co)
    return co.reshape(-1, 3)


def face_centers(me):
    c = np.empty(len(me.polygons) * 3, np.float32)
    me.polygons.foreach_get('center', c)
    return c.reshape(-1, 3)


def face_normals(me):
    n = np.empty(len(me.polygons) * 3, np.float32)
    me.polygons.foreach_get('normal', n)
    return n.reshape(-1, 3)


def face_areas(me):
    a = np.empty(len(me.polygons), np.float32)
    me.polygons.foreach_get('area', a)
    return a


def edge_verts(me):
    ev = np.empty(len(me.edges) * 2, np.int32)
    me.edges.foreach_get('vertices', ev)
    return ev.reshape(-1, 2)


def components(n, edges):
    """Connected-component label per vertex (vectorised union-find, no scipy needed)."""
    parent = np.arange(n)
    while True:
        a, b = parent[edges[:, 0]], parent[edges[:, 1]]
        diff = a != b
        if not diff.any():
            return parent
        np.minimum.at(parent, np.maximum(a, b)[diff], np.minimum(a, b)[diff])
        while True:
            jumped = parent[parent]
            if (jumped == parent).all():
                break
            parent = jumped


def topology(me):
    """Holes, non-manifold edges, components, winding and degenerate faces."""
    nv, ne = len(me.vertices), len(me.edges)
    loop_edge = np.empty(len(me.loops), np.int32)
    me.loops.foreach_get('edge_index', loop_edge)
    loop_vert = np.empty(len(me.loops), np.int32)
    me.loops.foreach_get('vertex_index', loop_vert)
    faces_per_edge = np.bincount(loop_edge, minlength=ne)
    labels = components(nv, edge_verts(me))
    sizes = np.bincount(labels)
    sizes = sizes[sizes > 0]
    # Consistently wound neighbours walk a shared edge in opposite directions.
    start = np.empty(len(me.polygons), np.int32)
    me.polygons.foreach_get('loop_start', start)
    total = np.empty(len(me.polygons), np.int32)
    me.polygons.foreach_get('loop_total', total)
    nxt = np.arange(len(loop_vert)) + 1
    last = start + total - 1
    nxt[last] = start
    directed = loop_vert.astype(np.int64) * nv + loop_vert[nxt]
    _, counts = np.unique(directed, return_counts=True)
    tris = int((total - 2).sum())
    return dict(
        verts=nv, tris=tris,
        boundary_edges=int((faces_per_edge == 1).sum()),
        nonmanifold_edges=int((faces_per_edge > 2).sum()),
        components=int(len(sizes)), largest_component_verts=int(sizes.max()),
        winding_conflicts=int((counts > 1).sum()),
        degenerate_faces=int((face_areas(me) < 1e-12).sum()),
    )


def nonmanifold_verts(ob):
    """Blender's own Select Non-Manifold (wire, boundary, multi-face, non-contiguous, bow-tie)."""
    activate(ob)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_mode(type='VERT')
    bpy.ops.mesh.select_all(action='DESELECT')
    bpy.ops.mesh.select_non_manifold(extend=False, use_wire=True, use_boundary=True,
                                     use_multi_face=True, use_non_contiguous=True, use_verts=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    sel = np.empty(len(ob.data.vertices), bool)
    ob.data.vertices.foreach_get('select', sel)
    return int(sel.sum())


def select_faces(me, mask):
    loop_vert = np.empty(len(me.loops), np.int32)
    me.loops.foreach_get('vertex_index', loop_vert)
    start = np.empty(len(me.polygons), np.int32)
    me.polygons.foreach_get('loop_start', start)
    total = np.empty(len(me.polygons), np.int32)
    me.polygons.foreach_get('loop_total', total)
    loop_face = np.repeat(np.arange(len(me.polygons)), total)
    vsel = np.zeros(len(me.vertices), bool)
    vsel[loop_vert[mask[loop_face]]] = True
    ev = edge_verts(me)
    me.vertices.foreach_set('select', vsel)
    me.edges.foreach_set('select', vsel[ev[:, 0]] & vsel[ev[:, 1]])
    me.polygons.foreach_set('select', mask)
    touching = np.zeros(len(me.polygons), bool)
    np.logical_or.at(touching, loop_face, vsel[loop_vert])
    return int(touching.sum())


def region_counts(me):
    fc = face_centers(me)
    return {k: int(fn(fc).sum()) for k, fn in REGIONS.items()}


# ---------------------------------------------------------------- render setup

# Full-body cameras shared by every model; close-ups come from the model's own layout.
VIEWS = {
    # name: (target, direction to camera, frame height in m, resolution)
    'front': ((0, 0, 0.49), (0, -1, 0), 1.04, (900, 1000)),
    'side': ((0, 0, 0.49), (1, 0, 0), 1.04, (900, 1000)),
    'back': ((0, 0, 0.49), (0, 1, 0), 1.04, (900, 1000)),
}


def view_spec(name):
    return M.views.get(name) or VIEWS[name]


def look_at(ob, loc, target):
    ob.location = loc
    ob.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()


def setup_studio(samples=64):
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'
    sc.cycles.samples = samples
    sc.cycles.use_denoising = True
    sc.render.resolution_percentage = 100
    sc.render.film_transparent = False
    sc.view_settings.view_transform = 'Standard'
    sc.view_settings.look = 'None'
    sc.render.image_settings.file_format = 'PNG'
    sc.render.image_settings.color_mode = 'RGB'
    world = bpy.data.worlds.new('Studio')
    sc.world = world
    if world.node_tree is None:  # Blender < 5.0
        world.use_nodes = True
    bg = world.node_tree.nodes['Background']
    bg.inputs['Color'].default_value = (0.30, 0.31, 0.34, 1)
    bg.inputs['Strength'].default_value = 0.4
    for name, loc, power, size in (('Key', (-1.1, -1.8, 1.6), 90, 1.2),
                                   ('Fill', (1.6, -1.2, 0.9), 30, 1.5),
                                   ('Rim', (0.4, 1.8, 1.7), 120, 1.0)):
        light = bpy.data.lights.new(name, 'AREA')
        light.energy = power
        light.size = size
        ob = bpy.data.objects.new(name, light)
        sc.collection.objects.link(ob)
        look_at(ob, loc, (0, 0, 0.55))
    cam = bpy.data.objects.new('Camera', bpy.data.cameras.new('Camera'))
    sc.collection.objects.link(cam)
    sc.camera = cam
    cam.data.lens = 85
    cam.data.sensor_fit = 'VERTICAL'
    cam.data.sensor_height = 24
    return cam


def render_view(cam, view, path):
    target, direction, height, res = view_spec(view)
    sc = bpy.context.scene
    sc.render.resolution_x, sc.render.resolution_y = res
    dist = height / 2 * cam.data.lens / (cam.data.sensor_height / 2)
    loc = Vector(target) + Vector(direction).normalized() * dist
    look_at(cam, loc, target)
    cam.data.clip_start = dist * 0.05
    cam.data.clip_end = dist * 10
    sc.render.filepath = path
    t = time.time()
    bpy.ops.render.render(write_still=True)
    log(f'rendered {os.path.relpath(path, HERE)} in {time.time() - t:.0f}s')


# ---------------------------------------------------------------- stages

def stage_import():
    t = time.time()
    hp = import_highpoly()
    load_s = time.time() - t
    me = hp.data
    co = vert_co(me)
    mn, mx = co.min(0), co.max(0)
    mat = me.materials[0]
    textures = {}
    for node in mat.node_tree.nodes:
        if node.type == 'TEX_IMAGE' and node.image:
            img = node.image
            role = img.name.rsplit('_', 1)[-1]
            textures[role] = dict(name=img.name, size=list(img.size), format=img.file_format,
                                  colorspace=img.colorspace_settings.name)
    uv = me.uv_layers[0]
    uvs = np.empty(len(me.loops) * 2, np.float32)
    uv.data.foreach_get('uv', uvs)
    uvs = uvs.reshape(-1, 2)
    tri_uv = uvs.reshape(-1, 3, 2)
    e1, e2 = tri_uv[:, 1] - tri_uv[:, 0], tri_uv[:, 2] - tri_uv[:, 0]
    uv_area = np.abs(e1[:, 0] * e2[:, 1] - e1[:, 1] * e2[:, 0]) / 2
    fc, area = face_centers(me), face_areas(me)
    bc_size = textures['basecolor']['size'][0]
    texels = {k: round(float(bc_size * math.sqrt(uv_area[fn(fc)].sum() / area[fn(fc)].sum())))
              for k, fn in REGIONS.items()}
    texels['uv_coverage_pct'] = round(float(100 * uv_area.sum()), 1)
    raw = topology(me)
    # The glTF importer leaves vertices split along UV seams; weld them to see the real surface.
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    welded = bpy.data.meshes.new('welded')
    bm.to_mesh(welded)
    bm.free()
    stats = dict(
        blender=bpy.app.version_string,
        source=os.path.relpath(M.src, HERE),
        import_seconds=round(load_s, 1),
        height_m=round(float(mx[2] - mn[2]), 4),
        size_xyz_m=[round(float(v), 4) for v in (mx - mn)],
        note_axes='turned to face -Y; X = arm span (T-pose), Y = depth incl. tail fin',
        triangles=raw['tris'],
        vertices_as_imported=len(me.vertices),
        materials=[m.name for m in me.materials],
        textures=textures,
        uv_layers=[l.name for l in me.uv_layers],
        uv_range=[[round(float(v), 4) for v in uvs.min(0)], [round(float(v), 4) for v in uvs.max(0)]],
        basecolor_texels_per_m=texels,
        topology_as_imported=raw,
        topology_welded=topology(welded),
        weld_distance_m=1e-6,
    )
    update_stats('highpoly', stats)
    log(json.dumps(stats, indent=2, ensure_ascii=False))


def stage_baseline():
    import_highpoly()
    cam = setup_studio(samples=96)
    os.makedirs(RENDERS, exist_ok=True)
    for view in ('front', 'side', 'face'):
        render_view(cam, view, os.path.join(RENDERS, f'{M.prefix}baseline_highpoly_{view}.png'))


def separate_pinches(bm):
    """Give each sheet its own copy of the vertices where two sheets touch; returns how many."""
    bad = [v for v in bm.verts if not v.is_manifold]
    for v in bad:
        if v.is_valid and not v.is_manifold:
            bmesh.utils.vert_separate(v, [e for e in v.link_edges if len(e.link_faces) != 2])
    return len(bad)


def loop_edges(me):
    """Each loop's directed edge (from, to) and its face."""
    lv = np.empty(len(me.loops), np.int32)
    me.loops.foreach_get('vertex_index', lv)
    start = np.empty(len(me.polygons), np.int32)
    me.polygons.foreach_get('loop_start', start)
    total = np.empty(len(me.polygons), np.int32)
    me.polygons.foreach_get('loop_total', total)
    nxt = np.arange(len(lv)) + 1
    nxt[start + total - 1] = start
    return lv, lv[nxt], np.repeat(np.arange(len(me.polygons)), total)


def rewind(me):
    """Flip the patches wound against the rest of the surface, then collapse any twisted
    sub-millimetre sliver that no flip can fix. A global normal recalculation is no use here:
    on this self-intersecting surface it turns 90% of the faces inside out."""
    nf, nv = len(me.polygons), len(me.vertices)
    a, b, face = loop_edges(me)
    key = np.minimum(a, b).astype(np.int64) * nv + np.maximum(a, b)
    order = np.argsort(key, kind='stable')
    same = key[order][1:] == key[order][:-1]
    i0, i1 = order[:-1][same], order[1:][same]
    agree = a[i0] != a[i1]  # neighbours walk the shared edge in opposite directions
    patch = components(nf, np.stack([face[i0][agree], face[i1][agree]], 1).astype(np.int64))
    flip = np.flatnonzero(patch != np.bincount(patch).argmax())
    for i in flip:
        me.polygons[int(i)].flip()
    me.update()
    a, b, _ = loop_edges(me)
    _, inv, counts = np.unique(a.astype(np.int64) * nv + b, return_inverse=True, return_counts=True)
    clash = counts[inv] > 1
    groups = []
    if clash.any():
        pairs = np.stack([a[clash], b[clash]], 1).astype(np.int64)
        verts = np.unique(pairs)
        label = components(nv, pairs)[verts]
        groups = [verts[label == g] for g in np.unique(label)]
        bm = bmesh.new()
        bm.from_mesh(me)
        bm.verts.ensure_lookup_table()
        for group in groups:
            vs = [bm.verts[int(i)] for i in group]
            bmesh.ops.pointmerge(bm, verts=vs, merge_co=sum((v.co for v in vs), Vector()) / len(vs))
        bmesh.ops.dissolve_degenerate(bm, dist=1e-7, edges=bm.edges)
        bm.to_mesh(me)
        bm.free()
    return dict(faces_flipped=int(len(flip)), slivers_collapsed=len(groups))


def clean_copy(hp):
    """Welded, floater-free, fold-repaired copy of the high poly without UVs or materials."""
    me = hp.data.copy()
    ob = bpy.data.objects.new('Clean', me)
    bpy.context.scene.collection.objects.link(ob)
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bm.verts.ensure_lookup_table()
    bm.verts.index_update()
    edges = np.array([(e.verts[0].index, e.verts[1].index) for e in bm.edges], np.int64)
    labels = components(len(bm.verts), edges)
    sizes = np.bincount(labels, minlength=len(bm.verts))
    floating = sizes[labels] < 0.01 * len(bm.verts)
    n_floaters = len(np.unique(labels[floating]))
    bmesh.ops.delete(bm, geom=[v for v, f in zip(bm.verts, floating) if f], context='VERTS')
    # The Tripo surface has a few spots where two sheets are pinched together along an edge
    # or at a vertex (non-manifold edges / bow-tie vertices).
    repaired = 0
    if M.fold_repair == 'separate':
        # Give each sheet its own copy of the shared vertices: nothing removed, no hole opened.
        repaired = separate_pinches(bm)
    for _ in range(3 if M.fold_repair == 'refill' else 0):
        # Cut two rings of faces around each spot and fill the holes again.
        bad = {v for v in bm.verts if not v.is_manifold}
        bad |= {v for e in bm.edges if len(e.link_faces) != 2 for v in e.verts}
        if not bad:
            break
        repaired += len(bad)
        faces = {f for v in bad for f in v.link_faces}
        for _ in range(2):
            faces |= {g for f in faces for v in f.verts for g in v.link_faces}
        bmesh.ops.delete(bm, geom=list(faces), context='FACES')
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
        holes = bmesh.ops.holes_fill(bm, edges=bm.edges, sides=0)
        bmesh.ops.triangulate(bm, faces=holes['faces'])
    bm.to_mesh(me)
    bm.free()
    winding = None
    if M.fold_repair == 'separate':
        # The 'mv' surface also has a few triangles wound against their neighbours.
        winding = rewind(me)
        bm = bmesh.new()
        bm.from_mesh(me)
        winding['pinches_after_collapse'] = separate_pinches(bm)
        bm.to_mesh(me)
        bm.free()
    while me.uv_layers:
        me.uv_layers.remove(me.uv_layers[0])
    me.materials.clear()
    for name in ('custom_normal', 'sharp_edge'):
        if name in me.attributes:
            me.attributes.remove(me.attributes[name])
    me.shade_smooth()
    info = dict(floating_fragments_removed=int(n_floaters),
                floating_verts_removed=int(floating.sum()),
                fold_repair=M.fold_repair, nonmanifold_verts_repaired=repaired)
    if winding:
        info['winding'] = winding
    return ob, info


def decimate_selected(ob, mask, target):
    """Collapse-decimate only the masked faces (unselected vertices stay locked)."""
    me = ob.data
    current = int(mask.sum())
    if current <= target:
        return
    touching = select_faces(me, mask)
    activate(ob)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_mode(type='FACE')
    # The operator spreads the removal over every face touching a selected vertex, which
    # at 2M -> 40k is thousands more than the selection; convert the face count accordingly.
    bpy.ops.mesh.decimate(ratio=1 - (current - target) / touching)
    bpy.ops.object.mode_set(mode='OBJECT')


def decimate_tier(clean, budget):
    """Rest of the body first, then face and hands, then one global pass for the seams."""
    ob = bpy.data.objects.new('Work', clean.data.copy())
    bpy.context.scene.collection.objects.link(ob)
    if isinstance(budget, int):  # uniform reference
        decimate_selected(ob, np.ones(len(ob.data.polygons), bool), budget)
        return ob
    rest = budget['total'] - budget['face'] - budget['hands']
    plan = [('rest', rest * 0.97), ('face', budget['face']), ('hands', budget['hands'])]
    for region, target in plan:
        for _ in range(3):  # the operator's ratio is approximate on partial selections
            mask = REGIONS[region](face_centers(ob.data))
            if mask.sum() <= target * 1.02:
                break
            decimate_selected(ob, mask, int(target))
    total = len(ob.data.polygons)
    if total > budget['total'] * 1.01:
        decimate_selected(ob, np.ones(total, bool), budget['total'])
    return ob


def tidy(ob):
    activate(ob)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.dissolve_degenerate(threshold=1e-7)
    bpy.ops.mesh.delete_loose()
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.quads_convert_to_tris()
    bpy.ops.object.mode_set(mode='OBJECT')
    ob.data.shade_smooth()


def surface_check(ob, hp_bvh):
    """Distance to the high poly and faces whose normal points against the nearest high-poly face."""
    me = ob.data
    fc, fn, fa = face_centers(me), face_normals(me), face_areas(me)
    flipped = np.zeros(len(fc), bool)
    for i, c in enumerate(fc):
        _, n, _, _ = hp_bvh.find_nearest(Vector(c))
        flipped[i] = n is not None and n.dot(Vector(fn[i])) < 0
    co = vert_co(me)
    dist = np.array([hp_bvh.find_nearest(Vector(c))[3] for c in co])
    return dict(
        faces_against_highpoly=int(flipped.sum()),
        faces_against_highpoly_area_pct=round(float(100 * fa[flipped].sum() / fa.sum()), 3),
        vertex_distance_mm=dict(p50=round(float(np.percentile(dist, 50)) * 1000, 3),
                                p95=round(float(np.percentile(dist, 95)) * 1000, 3),
                                p99=round(float(np.percentile(dist, 99)) * 1000, 3),
                                max=round(float(dist.max()) * 1000, 3)),
    )


def stage_decimate():
    hp = import_highpoly()
    hp_bvh = BVHTree.FromObject(hp, bpy.context.evaluated_depsgraph_get())
    t = time.time()
    clean, info = clean_copy(hp)
    info['seconds'] = round(time.time() - t, 1)
    info['topology'] = topology(clean.data)
    info['nonmanifold_verts'] = nonmanifold_verts(clean)
    info['regions_tris'] = region_counts(clean.data)
    log('clean', info)
    results = {'clean_highpoly': info}
    tiers = dict(M.tiers)
    tiers[M.reference[0]] = M.reference[1]
    for name, budget in tiers.items():
        t = time.time()
        ob = decimate_tier(clean, budget)
        tidy(ob)
        r = dict(seconds=round(time.time() - t, 1), budget=budget)
        r['topology'] = topology(ob.data)
        r['nonmanifold_verts'] = nonmanifold_verts(ob)
        r['regions_tris'] = region_counts(ob.data)
        r.update(surface_check(ob, hp_bvh))
        log(name, r)
        results[name] = r
        save_lowpoly(ob, name)
    update_stats('decimate', results)


def unwrap(ob):
    """xatlas charts and packing, one sub-mesh per region so face/hands can be scaled up.

    Falls back to Blender's Smart UV Project when the xatlas module is missing; on these
    meshes that packs about 40% of the square against xatlas' 47%.
    """
    me = ob.data
    if not me.uv_layers:
        me.uv_layers.new(name='UVMap')
    try:
        import xatlas
    except ImportError:
        unwrap_smart(ob)
        return 'smart_uv_project'
    co = vert_co(me)
    tri = np.empty(len(me.loops), np.int32)
    me.loops.foreach_get('vertex_index', tri)
    tri = tri.reshape(-1, 3)
    start = np.empty(len(me.polygons), np.int32)
    me.polygons.foreach_get('loop_start', start)
    assert (start == np.arange(len(start)) * 3).all(), 'expects a triangulated mesh'
    fc = face_centers(me)
    order = ('rest', 'face', 'hands')
    masks = {k: REGIONS[k](fc) for k in order}
    atlas = xatlas.Atlas()
    local = {}
    for region in order:
        used, inv = np.unique(tri[masks[region]], return_inverse=True)
        local[region] = inv.reshape(-1, 3)
        atlas.add_mesh(co[used] * UV_SCALE.get(region, 1.0), local[region].astype(np.uint32))
    pack = xatlas.PackOptions()
    pack.resolution = 4096
    pack.padding = UV_PADDING
    pack.bilinear = True
    atlas.generate(xatlas.ChartOptions(), pack)
    side = max(atlas.width, atlas.height)
    aspect = np.array([atlas.width, atlas.height]) / side
    loop_uv = np.zeros((len(tri), 3, 2), np.float32)
    for i, region in enumerate(order):
        vmap, idx, uvs = atlas[i]
        assert (vmap[idx] == local[region]).all(), 'xatlas reordered faces'
        loop_uv[masks[region]] = uvs[idx] * aspect
    e1, e2 = loop_uv[:, 1] - loop_uv[:, 0], loop_uv[:, 2] - loop_uv[:, 0]
    if (e1[:, 0] * e2[:, 1] - e1[:, 1] * e2[:, 0]).sum() < 0:  # keep charts unmirrored
        loop_uv[..., 1] = 1 - loop_uv[..., 1]
    me.uv_layers.active.data.foreach_set('uv', loop_uv.ravel())
    return 'xatlas'


def unwrap_smart(ob):
    me = ob.data
    bpy.context.scene.tool_settings.use_uv_select_sync = True
    fc = face_centers(me)
    masks = {k: fn(fc) for k, fn in REGIONS.items()}
    # Unwrapping each region on its own keeps every island inside one region.
    for region in ('rest', 'face', 'hands'):
        select_faces(me, masks[region])
        activate(ob)
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.0,
                                 area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
        bpy.ops.object.mode_set(mode='OBJECT')
    select_faces(me, np.ones(len(me.polygons), bool))
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.uv.average_islands_scale()
    bm = bmesh.from_edit_mesh(me)
    uv = bm.loops.layers.uv.active
    bm.faces.ensure_lookup_table()
    for island in bmesh_linked_uv_islands(bm, uv):
        idx = [f.index for f in island]
        scale = 1.0
        for region, s in UV_SCALE.items():
            if masks[region][idx].mean() > 0.5:
                scale = s
        if scale == 1.0:
            continue
        loops = [l for f in island for l in f.loops]
        pts = np.array([l[uv].uv for l in loops])
        center = (pts.min(0) + pts.max(0)) / 2
        for l in loops:
            l[uv].uv = Vector(center + (np.array(l[uv].uv) - center) * scale)
    bmesh.update_edit_mesh(me)
    bpy.ops.uv.pack_islands(rotate=True, rotate_method='ANY', scale=True, margin_method='FRACTION',
                            margin=UV_MARGIN, shape_method='CONCAVE')
    bpy.ops.object.mode_set(mode='OBJECT')


def texel_density(me):
    """Texels per metre at 4096 for each region (sqrt of UV area / 3D area)."""
    uvs = np.empty(len(me.loops) * 2, np.float32)
    me.uv_layers.active.data.foreach_get('uv', uvs)
    uvs = uvs.reshape(-1, 3, 2)  # triangles only
    e1, e2 = uvs[:, 1] - uvs[:, 0], uvs[:, 2] - uvs[:, 0]
    uv_area = np.abs(e1[:, 0] * e2[:, 1] - e1[:, 1] * e2[:, 0]) / 2
    area = face_areas(me)
    fc = face_centers(me)
    out = {k: round(float(4096 * math.sqrt(uv_area[fn(fc)].sum() / area[fn(fc)].sum())))
           for k, fn in REGIONS.items()}
    out['uv_coverage_pct'] = round(float(100 * uv_area.sum()), 1)
    return out


def stage_uv():
    results = {}
    for name in M.tiers:
        reset()
        ob = load_lowpoly(name)
        t = time.time()
        method = unwrap(ob)
        results[name] = dict(method=method, seconds=round(time.time() - t, 1),
                             texels_per_m_at_4k=texel_density(ob.data))
        log(name, results[name])
        save_lowpoly(ob, name)
    update_stats('uv', results)


def texture_path(tier, kind, size):
    ext = 'png' if kind == 'normal' else 'jpg'
    return os.path.join(TEXTURES, f'ds_{tier}_{kind}_{size // 1024}k.{ext}')


def highpoly_bake_source(hp):
    """Returns use(kind): wires the high-poly material for an EMIT (texture lookup) or NORMAL bake."""
    nt = hp.data.materials[0].node_tree
    out = next(n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL')
    bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    tex = {n.image.name.rsplit('_', 1)[-1]: n for n in nt.nodes if n.type == 'TEX_IMAGE' and n.image}
    emit = nt.nodes.new('ShaderNodeEmission')

    def use(kind):
        if kind == 'normal':  # shading normal of the high poly, including its own normal map
            nt.links.new(bsdf.outputs['BSDF'], out.inputs['Surface'])
            return
        # Base color and the packed metallic/roughness map (G = roughness, B = metallic) are
        # copied straight through emission, so lighting and metallic darkening stay out.
        nt.links.new(tex['basecolor' if kind == 'basecolor' else 'rm'].outputs['Color'],
                     emit.inputs['Color'])
        nt.links.new(emit.outputs['Emission'], out.inputs['Surface'])
    return use


def cage_distance(tier):
    """Cage extrusion from the decimation's 99th-percentile distance to the high poly."""
    p99 = read_stats()[1]['decimate'][tier]['vertex_distance_mm']['p99'] / 1000
    return min(max(2 * p99, 0.0015), 0.005)


BAKE_TILE = 4096  # an 8K selected-to-active bake needs ~14 GB; bake it in 4K windows


def bake_image(lp, node, kind, size, extrusion):
    """Selected-to-active bake into a new image, through overlapping BAKE_TILE windows when larger.

    Windows overlap by half a tile and each keeps only its middle, at least a quarter tile from
    any window edge it shares, so Blender's margin extension never sees a cut-off neighbour.
    """
    def run(target, uv_layer):
        node.image = target
        bpy.ops.object.bake(type='NORMAL' if kind == 'normal' else 'EMIT',
                            use_selected_to_active=True, cage_extrusion=extrusion,
                            max_ray_distance=extrusion * 2, margin=size // 256,
                            margin_type='EXTEND', use_clear=True, target='IMAGE_TEXTURES',
                            normal_space='TANGENT', normal_r='POS_X', normal_g='POS_Y',
                            normal_b='POS_Z', uv_layer=uv_layer)

    def new_image(name, res):
        img = bpy.data.images.new(name, res, res, alpha=False)
        img.colorspace_settings.name = 'sRGB' if kind == 'basecolor' else 'Non-Color'
        return img

    me = lp.data
    img = new_image(f'{lp.name}_{kind}_{size}', size)
    if size <= BAKE_TILE:
        run(img, 'UVMap')
        return img
    T, stride = BAKE_TILE, BAKE_TILE // 2
    n = (size - T) // stride + 1
    keep = [(k * stride + (T // 4 if k else 0), k * stride + T - (T // 4 if k < n - 1 else 0))
            for k in range(n)]
    base = np.empty(len(me.loops) * 2, np.float32)
    me.uv_layers['UVMap'].data.foreach_get('uv', base)
    base = base.reshape(-1, 2) * size
    tmp = me.uv_layers.new(name='BakeTile')
    tile = new_image('tile', T)
    full = np.zeros((size, size, 4), np.float32)
    px = np.empty(T * T * 4, np.float32)
    for j in range(n):
        for i in range(n):
            # Pixel (x, y) of the big image is pixel (x - i*stride, y - j*stride) of the window.
            tmp.data.foreach_set('uv', ((base - (i * stride, j * stride)) / T).ravel())
            run(tile, tmp.name)
            tile.pixels.foreach_get(px)
            (x0, x1), (y0, y1) = keep[i], keep[j]
            full[y0:y1, x0:x1] = px.reshape(T, T, 4)[y0 - j * stride:y1 - j * stride,
                                                     x0 - i * stride:x1 - i * stride]
    img.pixels.foreach_set(full.ravel())
    del full
    me.uv_layers.remove(tmp)
    me.uv_layers.active = me.uv_layers['UVMap']
    bpy.data.images.remove(tile)
    return img


def stage_bake():
    os.makedirs(TEXTURES, exist_ok=True)
    results = {}
    for tier, maps in M.bakes.items():
        hp = import_highpoly()
        lp = load_lowpoly(tier)
        source = highpoly_bake_source(hp)
        target = new_material('BakeTarget')
        lp.data.materials.append(target)
        node = target.node_tree.nodes.new('ShaderNodeTexImage')
        target.node_tree.nodes.active = node
        sc = bpy.context.scene
        sc.render.engine = 'CYCLES'
        sc.cycles.device = 'CPU'
        sc.cycles.samples = 4  # a little anti-aliasing; emission and normals need no more
        extrusion = cage_distance(tier)
        results[tier] = dict(cage_extrusion_mm=round(extrusion * 1000, 2),
                             max_ray_distance_mm=round(extrusion * 2000, 2), maps={})
        for kind, size in maps:
            source(kind)
            activate(lp, select=[hp])
            t = time.time()
            img = bake_image(lp, node, kind, size, extrusion)
            seconds = time.time() - t
            path = texture_path(tier, kind, size)
            img.file_format = 'PNG' if kind == 'normal' else 'JPEG'
            img.save(filepath=path, quality=92)
            results[tier]['maps'][os.path.basename(path)] = dict(
                seconds=round(seconds), size=size, megabytes=round(os.path.getsize(path) / 2**20, 1))
            log(tier, kind, size, f'{seconds:.0f}s', path)
            bpy.data.images.remove(img)
    update_stats('bake', results)


def lowpoly_material(tier, normal=True, bc_size=4096):
    mat = new_material(f'DeepSeek_{tier}')
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')

    def tex(kind, size, colorspace, x):
        n = nt.nodes.new('ShaderNodeTexImage')
        n.image = bpy.data.images.load(texture_path(tier, kind, size), check_existing=True)
        n.image.colorspace_settings.name = colorspace
        n.location = (x, 300 - 300 * len([m for m in nt.nodes if m.type == 'TEX_IMAGE']))
        return n

    bc = tex('basecolor', bc_size, 'sRGB', -600)
    nt.links.new(bc.outputs['Color'], bsdf.inputs['Base Color'])
    mr = tex('mr', 2048, 'Non-Color', -600)
    sep = nt.nodes.new('ShaderNodeSeparateColor')
    sep.location = (-300, mr.location.y)
    nt.links.new(mr.outputs['Color'], sep.inputs['Color'])
    nt.links.new(sep.outputs['Green'], bsdf.inputs['Roughness'])
    nt.links.new(sep.outputs['Blue'], bsdf.inputs['Metallic'])
    if normal:
        nm = tex('normal', 4096, 'Non-Color', -600)
        nmap = nt.nodes.new('ShaderNodeNormalMap')
        nmap.location = (-300, nm.location.y)
        nt.links.new(nm.outputs['Color'], nmap.inputs['Color'])
        nt.links.new(nmap.outputs['Normal'], bsdf.inputs['Normal'])
    return mat


def unity_metallic_smoothness(tier):
    """Unity Standard / URP Lit layout from the glTF one: R = metallic, A = 1 - roughness."""
    src = bpy.data.images.load(texture_path(tier, 'mr', 2048))
    src.colorspace_settings.name = 'Non-Color'
    w, h = src.size
    px = np.empty(w * h * 4, np.float32)
    src.pixels.foreach_get(px)
    px = px.reshape(-1, 4)
    out = np.zeros_like(px)
    out[:, 0] = out[:, 1] = out[:, 2] = px[:, 2]
    out[:, 3] = 1 - px[:, 1]
    img = bpy.data.images.new(f'{tier}_ms', w, h, alpha=True)
    img.colorspace_settings.name = 'Non-Color'
    img.pixels.foreach_set(out.ravel())
    path = os.path.join(TEXTURES, f'ds_{tier}_metallicsmoothness_2k_unity.png')
    img.file_format = 'PNG'
    img.save(filepath=path)
    return os.path.basename(path)


def stage_export():
    os.makedirs(MODELS, exist_ok=True)
    results = {}
    for tier in M.tiers:
        results[unity_metallic_smoothness(tier)] = 'R = metallic, A = smoothness'
        reset()
        lp = load_lowpoly(tier)
        lp.data.materials.clear()
        lp.data.materials.append(lowpoly_material(tier))
        lp.name = f'DeepSeek_{tier}'
        activate(lp)
        path = os.path.join(MODELS, f'ds_{tier}.glb')
        bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True,
                                  export_tangents=True, export_image_format='AUTO',
                                  export_jpeg_quality=92)
        results[os.path.basename(path)] = dict(
            triangles=len(lp.data.polygons), vertices=len(lp.data.vertices),
            megabytes=round(os.path.getsize(path) / 2**20, 1))
        log(path, results[os.path.basename(path)])
    update_stats('export', results)


def clay_material():
    mat = new_material('Clay')
    bsdf = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    bsdf.inputs['Base Color'].default_value = (0.55, 0.55, 0.55, 1)
    bsdf.inputs['Roughness'].default_value = 0.45
    return mat


COMPARE_TEXTURED = ['face', 'face34', 'eyes', 'hand', 'cape', 'tailfin', 'front']
COMPARE_CLAY = ['face34', 'hand']
# (name, caption, tier without prefix or None for the high poly, look, views); looks: 'bc' base
# colour only, 'bcn' + normal map, 'bcn8k' + normal map with the 8K base colour, 'clay' untextured
VARIANTS = [
    ('highpoly', 'High poly (Tripo)', None, 'source', COMPARE_TEXTURED),
    ('lp40k_bc', '40k  base color only', 'lp40k', 'bc', COMPARE_TEXTURED),
    ('lp40k_bcn', '40k  + normal map', 'lp40k', 'bcn', COMPARE_TEXTURED),
    ('lp100k_bc', '100k  base color only', 'lp100k', 'bc', COMPARE_TEXTURED),
    ('lp100k_bcn', '100k  + normal map', 'lp100k', 'bcn', COMPARE_TEXTURED),
    ('lp40k_bcn8k', '40k  + normal, 8K base color', 'lp40k', 'bcn8k', ['face', 'eyes']),
    ('highpoly_clay', 'High poly, no textures', None, 'clay', COMPARE_CLAY),
    ('uni40k_clay', 'Uniform 40k (no density control)', 'uni40k', 'clay', COMPARE_CLAY),
    ('lp40k_clay', '40k  weighted', 'lp40k', 'clay', COMPARE_CLAY),
    ('lp100k_clay', '100k  weighted', 'lp100k', 'clay', COMPARE_CLAY),
]


def has_8k_bake():
    return ('basecolor', 8192) in M.bake_maps


def load_variant(tier, look):
    reset()
    if tier is None:
        ob = import_highpoly()
    else:
        ob = load_lowpoly(M.prefix + tier)
        ob.data.materials.clear()
        if look != 'clay':
            ob.data.materials.append(lowpoly_material(
                M.prefix + tier, normal=look != 'bc', bc_size=8192 if look == 'bcn8k' else 4096))
    if look == 'clay':
        ob.data.materials.clear()
        ob.data.materials.append(clay_material())
    return ob


def contact_sheet(grid, path, tile=640, legend=None):
    """grid: rows of (caption, image path); legend: optional {bone name: sRGB} swatches."""
    from PIL import Image, ImageDraw, ImageFont
    try:
        font = ImageFont.truetype('DejaVuSans.ttf', 20)
    except OSError:
        font = ImageFont.load_default(20)
    cap = 34
    strips = []
    for row in grid:
        ims = [Image.open(p).convert('RGB') for _, p in row]
        ims = [im.resize((tile, round(im.height * tile / im.width)), Image.LANCZOS) for im in ims]
        strip = Image.new('RGB', (tile * len(ims), max(im.height for im in ims) + cap), (20, 20, 22))
        draw = ImageDraw.Draw(strip)
        for i, ((label, _), im) in enumerate(zip(row, ims)):
            strip.paste(im, (i * tile, cap))
            draw.text((i * tile + 10, 6), label, fill=(235, 235, 235), font=font)
        strips.append(strip)
    width = max(s.width for s in strips)
    if legend:
        cols = max(1, width // 300)
        leg = Image.new('RGB', (width, -(-len(legend) // cols) * 30 + 12), (20, 20, 22))
        draw = ImageDraw.Draw(leg)
        for k, (name, rgb) in enumerate(sorted(legend.items())):
            x, y = (k % cols) * 300 + 10, (k // cols) * 30 + 8
            draw.rectangle([x, y, x + 22, y + 22], fill=tuple(round(255 * c) for c in rgb))
            draw.text((x + 30, y), name.replace(PREFIX, ''), fill=(235, 235, 235), font=font)
        strips.append(leg)
    sheet = Image.new('RGB', (width, sum(s.height for s in strips)), (20, 20, 22))
    y = 0
    for strip in strips:
        sheet.paste(strip, (0, y))
        y += strip.height
    sheet.save(path)


def stage_compare():
    shots = os.path.join(WORK, M.prefix + 'compare')
    os.makedirs(shots, exist_ok=True)
    os.makedirs(RENDERS, exist_ok=True)
    tris = read_stats()[1].get('highpoly', {}).get('triangles')
    variants = []
    for name, label, tier, look, views in VARIANTS:
        if look == 'bcn8k' and not has_8k_bake():
            continue
        if name == 'highpoly' and tris:
            label = label.replace('High poly', f'High poly {tris / 1e6:.2f}M')
        variants.append((name, label, tier, look, views))
    for name, _, tier, look, views in variants:
        load_variant(tier, look)
        cam = setup_studio(samples=64)
        for view in views:
            render_view(cam, view, os.path.join(shots, f'{name}_{view}.png'))
    try:
        import PIL  # noqa: F401
    except ImportError:
        log('Pillow missing: individual renders are in', shots)
        return
    for view in COMPARE_TEXTURED + ['clay_' + v for v in COMPARE_CLAY]:
        clay = view.startswith('clay_')
        v = view[5:] if clay else view
        rows = [(label, os.path.join(shots, f'{name}_{v}.png')) for name, label, _, look, views in variants
                if v in views and (look == 'clay') == clay]
        contact_sheet([rows], os.path.join(RENDERS, f'{M.prefix}compare_{view}.png'))


CROSS_VIEWS = ['front', 'side', 'back', 'face34', 'tailfin']


def stage_cross():
    """The 40k tier of every model, textured and untextured, one row per view.

    Full-body cameras are shared; the 3/4 face and the fin use each model's own close-up
    camera, since faces and fins sit in different places.
    """
    global M
    current = M
    shots = os.path.join(WORK, 'cross')
    os.makedirs(shots, exist_ok=True)
    os.makedirs(RENDERS, exist_ok=True)
    columns = []
    for model in PRESETS.values():
        M = model
        for look, caption in (('bcn', '40k, textured'), ('clay', '40k, no textures')):
            load_variant('lp40k', look)
            cam = setup_studio(samples=64)
            for view in CROSS_VIEWS:
                render_view(cam, view, os.path.join(shots, f'{model.key}_{look}_{view}.png'))
            columns.append((f'{model.label} {caption}', model.key, look))
    M = current
    try:
        import PIL  # noqa: F401
    except ImportError:
        log('Pillow missing: individual renders are in', shots)
        return
    contact_sheet([[(f'{caption}, {view}', os.path.join(shots, f'{key}_{look}_{view}.png'))
                    for caption, key, look in columns] for view in CROSS_VIEWS],
                  os.path.join(RENDERS, 'cross_lp40k_' + '_vs_'.join(PRESETS) + '.png'), tile=480)
    update_stats('cross', dict(sheet='renders/cross_lp40k_' + '_vs_'.join(PRESETS) + '.png',
                               columns=[c[0] for c in columns], views=CROSS_VIEWS))


# ---------------------------------------------------------------- rig

PREFIX = 'mixamorig:'


def build_armature():
    data = bpy.data.armatures.new('Armature')
    rig = bpy.data.objects.new('Armature', data)
    bpy.context.scene.collection.objects.link(rig)
    activate(rig)
    bpy.ops.object.mode_set(mode='EDIT')
    eb = data.edit_bones

    def chain(names_heads, end, parent, mirror=1):
        prev = eb[PREFIX + parent] if parent else None
        heads = [Vector((h[0] * mirror, h[1], h[2])) for _, h in names_heads]
        heads.append(Vector((end[0] * mirror, end[1], end[2])))
        for i, (name, _) in enumerate(names_heads):
            b = eb.new(PREFIX + name)
            b.head, b.tail = heads[i], heads[i + 1]
            b.parent = prev
            b.use_connect = i > 0
            prev = b

    chain(M.spine, M.head_top, None)
    for side, mirror in (('Left', 1), ('Right', -1)):
        chain([(side + n, h) for n, h in M.arm], M.hand_end, 'Spine2', mirror)
        chain([(side + n, h) for n, h in M.leg], M.toe_end, 'Hips', mirror)
    chain(list(zip(M.tail_bones, M.tail[:-1])), M.tail[-1], 'Hips')
    bpy.ops.armature.select_all(action='SELECT')
    bpy.ops.armature.calculate_roll(type='GLOBAL_NEG_Y')
    bpy.ops.object.mode_set(mode='OBJECT')
    return rig


def weight_arrays(mesh_ob):
    """(n_verts, n_groups) weight matrix and the group names."""
    names = [g.name for g in mesh_ob.vertex_groups]
    w = np.zeros((len(mesh_ob.data.vertices), len(names)), np.float32)
    for v in mesh_ob.data.vertices:
        for g in v.groups:
            w[v.index, g.group] = g.weight
    return w, names


def weight_report(mesh_ob):
    w, names = weight_arrays(mesh_ob)
    total = w.sum(1)
    dominant = np.array(names)[w.argmax(1)]
    return dict(unweighted_verts=int((total < 1e-6).sum()),
                max_influences=int((w > 1e-4).sum(1).max()),
                dominant_verts={n: int((dominant == n).sum()) for n in names})


# Dominant-bone colours (sRGB): neighbouring bones and the left/right sides always differ.
BONE_COLORS = {
    'Hips': '#1f77b4', 'Spine': '#bcbd22', 'Spine1': '#17becf', 'Spine2': '#9467bd',
    'Neck': '#ffe119', 'Head': '#d62728',
    'LeftShoulder': '#2ca02c', 'LeftArm': '#ff7f0e', 'LeftForeArm': '#8c564b', 'LeftHand': '#e377c2',
    'RightShoulder': '#98df8a', 'RightArm': '#ffbb78', 'RightForeArm': '#c49c94', 'RightHand': '#f7b6d2',
    'LeftUpLeg': '#f032e6', 'LeftLeg': '#911eb4', 'LeftFoot': '#4363d8', 'LeftToeBase': '#42d4f4',
    'RightUpLeg': '#ff9896', 'RightLeg': '#dbdb8d', 'RightFoot': '#9edae5', 'RightToeBase': '#c7c7c7',
    'Tail1': '#7f7f7f', 'Tail2': '#3cb44b', 'Tail3': '#ffd8b1', 'Tail4': '#aaffc3',
    'Tail5': '#e6beff', 'Tail6': '#808000',
}


def bone_colors(names):
    return {n: tuple(int(BONE_COLORS[n[len(PREFIX):]][i:i + 2], 16) / 255 for i in (1, 3, 5))
            for n in names}


def dominant_bone_material(mesh_ob):
    """Vertex colours by strongest bone, black where nothing is weighted."""
    w, names = weight_arrays(mesh_ob)
    colors = bone_colors(names)
    srgb = np.array([colors[n] for n in names] + [(0, 0, 0)], np.float32)
    palette = np.where(srgb <= 0.04045, srgb / 12.92, ((srgb + 0.055) / 1.055) ** 2.4)  # attribute is linear
    idx = np.where(w.sum(1) > 1e-6, w.argmax(1), len(names))
    attr = mesh_ob.data.color_attributes.new('DominantBone', 'FLOAT_COLOR', 'POINT')
    rgba = np.hstack([palette[idx], np.ones((len(idx), 1), np.float32)])
    attr.data.foreach_set('color', rgba.ravel())
    mat = new_material('DominantBone')
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    node = nt.nodes.new('ShaderNodeVertexColor')
    node.layer_name = 'DominantBone'
    nt.links.new(node.outputs['Color'], bsdf.inputs['Base Color'])
    bsdf.inputs['Roughness'].default_value = 0.8
    return mat, colors


def rotate_bone(rig, name, axis, degrees):
    """Rotate a pose bone about a world axis through its head."""
    pb = rig.pose.bones[PREFIX + name]
    pb.rotation_mode = 'QUATERNION'
    rest = pb.bone.matrix_local.to_3x3()
    turn = Matrix.Rotation(math.radians(degrees), 3, axis)
    pb.rotation_quaternion = (rest.inverted() @ turn @ rest).to_quaternion() @ pb.rotation_quaternion


def seg_distance(p, a, b):
    """Distance from points p (n, 3) to segment ab, and the clamped parameter along it."""
    a, b = np.asarray(a, np.float32), np.asarray(b, np.float32)
    ab = b - a
    t = np.clip((p - a) @ ab / (ab @ ab), 0, 1)
    return np.linalg.norm(p - (a + t[:, None] * ab), axis=1), t


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


SKIRT_LEG_SHARE = 0.5  # at the hem the skirt follows its side's thigh this much


def classify_vertices(mesh):
    """Body part per vertex, read off the automatic weights and the joint layout."""
    w, names = weight_arrays(mesh)
    col = {n: i for i, n in enumerate(names)}

    def bone(n):
        return col[PREFIX + n]

    p = vert_co(mesh.data)
    x, y, z = p[:, 0], p[:, 1], p[:, 2]
    dominant = w.argmax(1)
    hair = np.isin(dominant, [bone(b) for b in M.hair_bones])
    arms = np.isin(dominant, [bone(s + n) for s in ('Left', 'Right') for n in ('Arm', 'ForeArm', 'Hand')])
    keep = hair | arms

    # Tail: close to the tail chain, the fin, or already given to the lower tail bones by bone
    # heat (the tail flattens and widens towards the fin). Tail1 alone is no evidence: bone
    # heat also hands it the back of the skirt.
    n = len(M.tail) - 1
    d = np.stack([seg_distance(p, M.tail[i], M.tail[i + 1])[0] for i in range(n)], 1)
    t = np.stack([seg_distance(p, M.tail[i], M.tail[i + 1])[1] for i in range(n)], 1)
    seg = d.argmin(1)
    lower_tail = np.isin(dominant, [bone(b) for b in M.tail_bones[1:]])
    fin = (z < M.fin['z_max']) & (y > M.fin['y_min'])
    tail = ~keep & (z < M.tail_z_max) & ((d.min(1) < M.tail_radius) | fin | lower_tail)

    parts = dict(hair=hair, arms=arms, tail=tail)
    for side, sx in (('Left', 1), ('Right', -1)):
        joints = [(h[0] * sx, h[1], h[2]) for _, h in M.leg] + [(M.toe_end[0] * sx,) + M.toe_end[1:]]
        thigh = seg_distance(p, joints[0], joints[1])[0] < 0.045
        if M.leg_reach is None:
            below = z < M.z_hem
        else:  # ribbons hang below the hem as well: only what is near the leg chain or the boot
            near = np.min([seg_distance(p, a, b)[0] for a, b in zip(joints, joints[1:])], 0) < M.leg_reach
            foot = (z < M.foot_box['z_max']) & (y < M.foot_box['y_max'])
            below = (z < M.z_hem) & (near | foot)
        parts[side + '_leg'] = ~keep & ~tail & (sx * x >= 0) & (below | ((z < 0.47) & thigh))
    parts['skirt'] = (~keep & ~tail & ~parts['Left_leg'] & ~parts['Right_leg']
                      & (z > M.skirt_z[0]) & (z < M.skirt_z[1]))
    parts['tail_segment'] = seg
    parts['tail_t'] = t[np.arange(len(p)), seg]
    return parts


def clean_weights(mesh, parts):
    """Fix the automatic weights where they repeat the Tripo export's mistakes.

    Bone heat diffuses across touching parts: the tail between the legs picks up the
    thighs, the inner boots pick up the other leg, the skirt splits down the middle
    between the thighs (its back goes to the tail) and the chest goes to the clavicles.
    Hair and arms come out mostly right: they keep their weights, minus any pull from
    arms, legs or tail on the hair.
    """
    w, names = weight_arrays(mesh)
    col = {n: i for i, n in enumerate(names)}

    def bone(n):
        return col[PREFIX + n]

    p = vert_co(mesh.data)
    x, z = p[:, 0], p[:, 2]
    hair, tail, skirt = parts['hair'], parts['tail'], parts['skirt']
    keep = hair | parts['arms']
    legs = {'Left': parts['Left_leg'], 'Right': parts['Right_leg']}
    seg, frac = parts['tail_segment'], parts['tail_t']

    new = w.copy()
    # Head and hair (down to the hair tips at the hips): no arm, leg or tail bone may pull,
    # or raising an arm or taking a step drags the long hair along.
    allowed = [bone(n) for n in ('Head', 'Neck', 'Spine2', 'Spine1', 'Spine', 'Hips',
                                 'LeftShoulder', 'RightShoulder')]
    new[np.ix_(hair, np.setdiff1d(np.arange(len(names)), allowed))] = 0
    # Tail: each bone owns its segment, blending half-and-half at the joints (Hips at the root).
    chain = [bone('Hips')] + [bone(b) for b in M.tail_bones]
    new[tail] = 0
    rows = np.flatnonzero(tail)
    for r in rows:
        b, f = seg[r] + 1, frac[r]
        if f < 0.25:
            new[r, chain[b - 1]], new[r, chain[b]] = 0.5 - 2 * f, 0.5 + 2 * f
        elif f > 0.75 and b < len(chain) - 1:
            new[r, chain[b + 1]], new[r, chain[b]] = 2 * (f - 0.75), 1 - 2 * (f - 0.75)
        else:
            new[r, chain[b]] = 1
    # Legs and boots: only the same side's leg chain (and Hips) may pull.
    for side, mask in legs.items():
        allowed = [bone('Hips')] + [bone(side + n) for n, _ in M.leg]
        drop = np.setdiff1d(np.arange(len(names)), allowed)
        new[np.ix_(mask, drop)] = 0
    # Skirt: Hips, sharing more with the thigh on its side towards the hem, blended across
    # the middle; near the waist it fades back into the torso part of the heat weights.
    torso = [bone('Hips'), bone('Spine'), bone('Spine1')]
    heat = np.zeros_like(w)
    heat[:, torso] = w[:, torso]
    a = SKIRT_LEG_SHARE * np.clip((0.52 - z) / (0.52 - 0.38), 0, 1)
    left = smoothstep(-0.05, 0.05, x)
    proc = np.zeros_like(w)
    proc[:, bone('Hips')] = 1 - a
    proc[:, bone('LeftUpLeg')] = a * left
    proc[:, bone('RightUpLeg')] = a * (1 - left)
    fade = np.clip((0.60 - z) / 0.06, 0, 1)[:, None]
    has_heat = heat.sum(1, keepdims=True) > 1e-6
    heat = np.where(has_heat, heat / np.maximum(heat.sum(1, keepdims=True), 1e-6), proc)
    new[skirt] = (fade * proc + (1 - fade) * heat)[skirt]
    # Chest and upper back: clavicles only take over away from the middle.
    centre = ~keep & (np.abs(x) < 0.075)
    share = np.clip((np.abs(x) - 0.03) / 0.045, 0, 1)
    shifted = np.zeros(len(p), bool)
    for side in ('Left', 'Right'):
        moved = new[:, bone(side + 'Shoulder')] * (1 - share) * centre
        new[:, bone(side + 'Shoulder')] -= moved
        new[:, bone('Spine2')] += moved
        shifted |= moved > 0.01

    # Unity skins with 4 bones per vertex by default.
    order = np.argsort(-new, 1)
    new[np.arange(len(p))[:, None], order[:, 4:]] = 0
    total = new.sum(1, keepdims=True)
    empty = total[:, 0] < 1e-6
    new = np.where(empty[:, None], w, new / np.maximum(total, 1e-6))
    for g in mesh.vertex_groups:
        g.remove(list(range(len(p))))
    for v, g in zip(*np.nonzero(new > 1e-4)):
        mesh.vertex_groups[g].add([int(v)], float(new[v, g]), 'REPLACE')
    return dict(head_and_hair=int(hair.sum()), arms=int(parts['arms'].sum()), tail=int(tail.sum()),
                left_leg=int(legs['Left'].sum()), right_leg=int(legs['Right'].sum()),
                skirt=int(skirt.sum()), chest_verts_shifted=int(shifted.sum()))


# Parts that a pose must leave where they are: none of their bones move.
LEAK_CHECKS = {'step': ['tail', 'hair'], 'arms_up': ['hair', 'skirt', 'tail'],
               'tail_swing': ['Left_leg', 'Right_leg', 'skirt']}


def pose_leaks(mesh, rig, parts):
    """Displacement (mm) of parts each test pose should not move: weights leaking from moved bones."""
    rest = vert_co(mesh.data)
    out = {}
    for pose, names in LEAK_CHECKS.items():
        set_pose(rig, M.poses[pose])
        ev = mesh.evaluated_get(bpy.context.evaluated_depsgraph_get())
        me = ev.to_mesh()
        d = np.linalg.norm(vert_co(me) - rest, axis=1) * 1000
        ev.to_mesh_clear()
        out[pose] = {n: dict(mean_mm=round(float(d[parts[n]].mean()), 2),
                             max_mm=round(float(d[parts[n]].max()), 1),
                             verts_over_5mm=int((d[parts[n]] > 5).sum()), verts=int(parts[n].sum()))
                     for n in names}
    set_pose(rig, [])
    return out


def set_pose(rig, moves):
    for pb in rig.pose.bones:
        pb.rotation_mode = 'QUATERNION'
        pb.rotation_quaternion = (1, 0, 0, 0)
    for name, axis, degrees in moves:
        rotate_bone(rig, name, axis, degrees)
    bpy.context.view_layer.update()


def render_rig(cam, mesh, rig, textured, tag, poses, shots):
    """Dominant-bone views plus test poses; returns the bone colour legend."""
    if 'DominantBone' in mesh.data.color_attributes:
        mesh.data.color_attributes.remove(mesh.data.color_attributes['DominantBone'])
    weight_mat, colors = dominant_bone_material(mesh)
    set_pose(rig, [])
    mesh.data.materials[0] = weight_mat
    for view in ('front', 'back', 'side'):
        render_view(cam, view, os.path.join(shots, f'{tag}_weights_{view}.png'))
    mesh.data.materials[0] = textured
    for pose in poses:
        set_pose(rig, M.poses[pose])
        for view in ('front', 'back', 'side'):
            render_view(cam, view, os.path.join(shots, f'{tag}_{pose}_{view}.png'))
    set_pose(rig, [])
    mesh.data.color_attributes.remove(mesh.data.color_attributes['DominantBone'])
    return colors


def check_fbx(path):
    """Re-import the FBX and compare its skeleton with the one we built."""
    reset()
    bpy.ops.import_scene.fbx(filepath=path)
    rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    mesh = next(o for o in bpy.data.objects if o.type == 'MESH')
    rig.data.pose_position = 'REST'
    bpy.context.view_layer.update()
    expected = {PREFIX + 'Hips': M.spine[0][1], PREFIX + 'LeftHand': M.arm[3][1],
                PREFIX + 'RightFoot': (-M.leg[2][1][0],) + M.leg[2][1][1:],
                PREFIX + M.tail_bones[-1]: M.tail[-2]}
    error = max((rig.matrix_world @ rig.data.bones[n].head_local - Vector(h)).length
                for n, h in expected.items())
    co = vert_co(mesh.data)
    co = co @ np.array(mesh.matrix_world)[:3, :3].T + np.array(mesh.matrix_world)[:3, 3]
    return dict(bones=len(rig.data.bones),
                root=[b.name for b in rig.data.bones if b.parent is None],
                tail_bones=len(M.tail_bones),
                tail_parent=rig.data.bones[PREFIX + 'Tail1'].parent.name,
                vertices=len(mesh.data.vertices), vertex_groups=len(mesh.vertex_groups),
                armature_modifier=any(m.type == 'ARMATURE' for m in mesh.modifiers),
                height_m=round(float(co[:, 2].max() - co[:, 2].min()), 4),
                joint_position_error_mm=round(error * 1000, 3))


def stage_rig():
    reset()
    mesh = load_lowpoly(M.rig_tier)
    mesh.name = f'DeepSeek_{M.rig_tier}'
    rig = build_armature()
    activate(rig, select=[mesh])
    t = time.time()
    bpy.ops.object.parent_set(type='ARMATURE_AUTO')
    report = dict(auto_weight_seconds=round(time.time() - t, 1), auto=weight_report(mesh))

    shots = os.path.join(WORK, M.prefix + 'rig')
    os.makedirs(shots, exist_ok=True)
    cam = setup_studio(samples=48)
    textured = lowpoly_material(M.rig_tier)
    mesh.data.materials.clear()
    mesh.data.materials.append(textured)
    render_rig(cam, mesh, rig, textured, 'auto', ['step'], shots)
    parts = classify_vertices(mesh)
    report['leaks_auto'] = pose_leaks(mesh, rig, parts)
    report['cleanup'] = clean_weights(mesh, parts)
    report['final'] = weight_report(mesh)
    report['leaks_final'] = pose_leaks(mesh, rig, parts)
    log('weights', report)
    colors = render_rig(cam, mesh, rig, textured, 'final', list(M.poses), shots)

    os.makedirs(MODELS, exist_ok=True)
    fbx = os.path.join(MODELS, f'ds_{M.rig_tier}_rigged.fbx')
    activate(rig, select=[mesh])
    bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, object_types={'ARMATURE', 'MESH'},
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
                             axis_forward='-Z', axis_up='Y', use_mesh_modifiers=False,
                             mesh_smooth_type='FACE', use_tspace=True, add_leaf_bones=False,
                             primary_bone_axis='Y', secondary_bone_axis='X',
                             use_armature_deform_only=True, armature_nodetype='NULL',
                             bake_anim=False, path_mode='RELATIVE')
    for ob in [o for o in bpy.data.objects if o.type in ('CAMERA', 'LIGHT')]:
        bpy.data.objects.remove(ob)
    bpy.context.preferences.filepaths.save_version = 0  # no .blend1 backup next to it
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(MODELS, f'ds_{M.rig_tier}_rigged.blend'),
                                relative_remap=True, compress=True)
    report['fbx'] = dict(file=os.path.basename(fbx), megabytes=round(os.path.getsize(fbx) / 2**20, 1))
    report['fbx'].update(check_fbx(fbx))
    log('fbx', report['fbx'])
    update_stats('rig', report)

    try:
        import PIL  # noqa: F401
    except ImportError:
        return
    os.makedirs(RENDERS, exist_ok=True)
    views = ('front', 'back', 'side')
    contact_sheet([[(f'{tag} weights, {v}', os.path.join(shots, f'{tag}_weights_{v}.png')) for v in views]
                   for tag in ('auto', 'final')],
                  os.path.join(RENDERS, f'{M.prefix}rig_weights.png'), tile=540, legend=colors)
    contact_sheet([[(f'{tag}: step, {v}', os.path.join(shots, f'{tag}_step_{v}.png')) for v in views]
                   for tag in ('auto', 'final')],
                  os.path.join(RENDERS, f'{M.prefix}rig_step_auto_vs_final.png'), tile=540)
    contact_sheet([[(f'{pose}, {v}', os.path.join(shots, f'final_{pose}_{v}.png')) for v in views]
                   for pose in M.poses],
                  os.path.join(RENDERS, f'{M.prefix}rig_poses.png'), tile=540)


STAGES = {
    'import': stage_import,
    'baseline': stage_baseline,
    'decimate': stage_decimate,
    'uv': stage_uv,
    'bake': stage_bake,
    'export': stage_export,
    'compare': stage_compare,
    'rig': stage_rig,
    'cross': stage_cross,
}
DEFAULT_STAGES = [name for name in STAGES if name != 'cross']


def main():
    global WORK, M
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else sys.argv[1:]
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    parser.add_argument('stages', nargs='*', metavar='stage', help=' '.join(STAGES))
    parser.add_argument('--model', choices=list(PRESETS), default='8k',
                        help='measured layout and output prefix of the source mesh')
    parser.add_argument('--src', help="source GLB (default: the model's own file)")
    parser.add_argument('--work', default=os.path.join(tempfile.gettempdir(), 'deepseek-blender-work'))
    args = parser.parse_args(argv)
    unknown = set(args.stages) - set(STAGES)
    if unknown:
        parser.error(f'unknown stage(s): {" ".join(sorted(unknown))}')
    WORK = args.work
    M = PRESETS[args.model]
    if args.src:
        M.src = os.path.abspath(args.src)
    if not os.path.exists(M.src):
        parser.error(f'source not found: {M.src}')
    log(f'model {M.key}: {os.path.relpath(M.src, HERE)}')
    for name in args.stages or DEFAULT_STAGES:
        t = time.time()
        log(f'=== {name}')
        STAGES[name]()
        log(f'=== {name} done in {time.time() - t:.0f}s')


if __name__ == '__main__':
    main()
