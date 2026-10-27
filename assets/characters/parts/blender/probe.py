"""Print cross-section profiles of the body, boots and tail, to place the joints.

Run: blender -b MODEL.blend -P probe.py
"""
import bpy
import numpy as np


def verts(name):
    me = bpy.data.objects[name].data
    v = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get('co', v)
    return v.reshape(-1, 3)


for o in bpy.data.objects:
    if o.type == 'MESH':
        v = verts(o.name)
        print('bbox %-15s n=%6d min %s max %s' % (o.name, len(v), np.round(v.min(0), 3), np.round(v.max(0), 3)))

B = verts('body')
print('\nbody torso/leg slabs (z: side, count, x range, y range, centroid)')
for z in np.arange(0.02, 1.36, 0.03):
    s = B[np.abs(B[:, 2] - z) < 0.006]
    if not len(s):
        continue
    for side, m in (('L', s[:, 0] > 0), ('R', s[:, 0] < 0)):
        t = s[m & (np.abs(s[:, 0]) < 0.3)]
        if len(t):
            print('z %.2f %s n%4d x[%.3f %.3f] y[%.3f %.3f] c(%.3f %.3f)' % (
                z, side, len(t), t[:, 0].min(), t[:, 0].max(), t[:, 1].min(), t[:, 1].max(), t[:, 0].mean(), t[:, 1].mean()))

print('\nleft arm slabs (x: z range, y range, centroid)')
for x in np.arange(0.08, 0.64, 0.02):
    s = B[(np.abs(B[:, 0] - x) < 0.005) & (B[:, 2] > 1.0)]
    if len(s):
        print('x %.2f n%4d z[%.3f %.3f] y[%.3f %.3f] c(%.3f %.3f)' % (
            x, len(s), s[:, 2].min(), s[:, 2].max(), s[:, 1].min(), s[:, 1].max(), s[:, 1].mean(), s[:, 2].mean()))

K = verts('boots')
print('\nleft boot slabs')
for z in np.arange(0.0, 0.42, 0.02):
    s = K[(np.abs(K[:, 2] - z) < 0.005) & (K[:, 0] > 0)]
    if len(s):
        print('z %.2f n%4d x[%.3f %.3f] y[%.3f %.3f] c(%.3f %.3f)' % (
            z, len(s), s[:, 0].min(), s[:, 0].max(), s[:, 1].min(), s[:, 1].max(), s[:, 0].mean(), s[:, 1].mean()))

T = verts('tail-glow')
print('\ntail slabs by z (x range, y range)')
for z in np.arange(0.0, 0.85, 0.04):
    s = T[np.abs(T[:, 2] - z) < 0.006]
    if len(s):
        print('z %.2f n%4d x[%.3f %.3f] y[%.3f %.3f]' % (z, len(s), s[:, 0].min(), s[:, 0].max(), s[:, 1].min(), s[:, 1].max()))
print('tail slabs by y (z range)')
for y in np.arange(0.0, 0.8, 0.04):
    s = T[np.abs(T[:, 1] - y) < 0.006]
    if len(s):
        print('y %.2f n%4d x[%.3f %.3f] z[%.3f %.3f]' % (y, len(s), s[:, 0].min(), s[:, 0].max(), s[:, 2].min(), s[:, 2].max()))
H = verts('head')
print('\nhead min z %.3f at y %.3f; hair z range %s' % (H[:, 2].min(), H[H[:, 2].argmin(), 1], np.round([verts('hair')[:, 2].min(), verts('hair')[:, 2].max()], 3)))
for img in bpy.data.images:
    print('image', img.name, tuple(img.size), img.packed_file is not None)
