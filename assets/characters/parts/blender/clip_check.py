"""Count intersecting triangle pairs between clothing layers per frame, against the rest pose.

Run: blender -b ANIMATED.blend -P clip_check.py -- OUT.json [ACTION ...] [--every K]
For each pair of meshes, prints how many triangle pairs intersect and where the new ones are (left/right, height).
"""
import json, sys
import bpy, bmesh
import numpy as np
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index('--') + 1:]
OUT = argv.pop(0)
every = 4
acts = []
while argv:
    a = argv.pop(0)
    if a == '--every':
        every = int(argv.pop(0))
    else:
        acts.append(a)
acts = acts or ['Walk', 'Run']
PAIRS = [('capelet', 'blouse'), ('capelet', 'body'), ('capelet', 'hair'), ('blouse', 'body'), ('corset', 'blouse'),
         ('neck-bow', 'capelet'), ('hair', 'body'), ('hair', 'blouse'), ('glove-L', 'body'), ('glove-R', 'body'),
         ('glove-L', 'blouse'), ('glove-R', 'blouse')]
arm = bpy.data.objects['DeepSeek']
scene = bpy.context.scene


def bvhs():
    dg = bpy.context.evaluated_depsgraph_get()
    out = {}
    for name in {n for p in PAIRS for n in p}:
        o = bpy.data.objects[name]
        me = o.evaluated_get(dg).to_mesh()
        bm = bmesh.new()
        bm.from_mesh(me)
        bm.transform(o.matrix_world)
        bm.faces.ensure_lookup_table()
        cent = np.array([f.calc_center_median() for f in bm.faces])
        out[name] = (BVHTree.FromBMesh(bm), cent)
        bm.free()
        o.evaluated_get(dg).to_mesh_clear()
    return out


REST = {}


def check(label):
    """New intersecting triangles of the first mesh of each pair (not intersecting in the rest pose), with where they are."""
    T = bvhs()
    row = {}
    for a, b in PAIRS:
        tri = {p[0] for p in T[a][0].overlap(T[b][0])}
        key = a + '/' + b
        if label == 'Rest':
            REST[key] = tri
        new = sorted(tri - REST[key])
        r = {'new': len(new)}
        if new:
            c = T[a][1][new]
            for side, m in (('L', c[:, 0] > 0.02), ('R', c[:, 0] < -0.02)):
                if m.sum() > 20:
                    q = c[m]
                    r[side] = '%d at x%.2f..%.2f y%.2f..%.2f z%.2f..%.2f' % (m.sum(), *np.percentile(q[:, 0], [10, 90]), *np.percentile(q[:, 1], [10, 90]), *np.percentile(q[:, 2], [10, 90]))
        row[key] = r
    print(label, {k: v['new'] for k, v in row.items()}, flush=True)
    return row


res = {}
arm.animation_data.action = None
for pb in arm.pose.bones:
    pb.matrix_basis.identity()
res['Rest'] = check('Rest')
for act_name in acts:
    act = bpy.data.actions[act_name]
    arm.animation_data.action = act
    if hasattr(arm.animation_data, 'action_slot') and act.slots:
        arm.animation_data.action_slot = act.slots[0]
    f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
    for f in range(f0, f1, every):
        scene.frame_set(f)
        res['%s %d' % (act_name, f)] = check('%s %d' % (act_name, f))
json.dump(res, open(OUT, 'w'), indent=1)
