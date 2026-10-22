"""Procedural mesh helpers: ring stacks, swept tubes and a builder that turns them into Blender meshes.

Everything is built from closed rings of vertices. A part is either a stack of horizontal rings
(head, torso, legs, skirt) or a tube swept along a 3D path (arms, hair strands, horns, tail).
Units are metres; the character stands at the origin facing -Y with +Z up, +X is her left.
"""
import bmesh
import bpy
import numpy as np


def interp(z, table):
    """Piecewise-linear lookup in a table of rows (x, v0, v1, ...)."""
    t = np.asarray(table, dtype=float)
    t = t[np.argsort(t[:, 0])]
    return [np.interp(z, t[:, 0], t[:, k]) for k in range(1, t.shape[1])]


def catmull_rom(points, n):
    """Resample a polyline of control points into n points on a centripetal Catmull-Rom curve."""
    p = np.asarray(points, dtype=float)
    p = np.vstack([2 * p[0] - p[1], p, 2 * p[-1] - p[-2]])
    seg = np.linalg.norm(np.diff(p, axis=0), axis=1) ** 0.5 + 1e-9
    knots = np.concatenate([[0], np.cumsum(seg)])
    t = np.linspace(knots[1], knots[-2], n)
    out = np.empty((n, p.shape[1]))
    for k, tt in enumerate(t):
        i = np.clip(np.searchsorted(knots, tt) - 1, 1, len(p) - 3)
        t0, t1, t2, t3 = knots[i - 1:i + 3]
        p0, p1, p2, p3 = p[i - 1:i + 3]
        a1 = (t1 - tt) / (t1 - t0) * p0 + (tt - t0) / (t1 - t0) * p1
        a2 = (t2 - tt) / (t2 - t1) * p1 + (tt - t1) / (t2 - t1) * p2
        a3 = (t3 - tt) / (t3 - t2) * p2 + (tt - t2) / (t3 - t2) * p3
        b1 = (t2 - tt) / (t2 - t0) * a1 + (tt - t0) / (t2 - t0) * a2
        b2 = (t3 - tt) / (t3 - t1) * a2 + (tt - t1) / (t3 - t1) * a3
        out[k] = (t2 - tt) / (t2 - t1) * b1 + (tt - t1) / (t2 - t1) * b2
    return out


def arclength(path):
    d = np.linalg.norm(np.diff(path, axis=0), axis=1)
    return np.concatenate([[0], np.cumsum(d)])


def frames(path, up=(0, 0, 1)):
    """Rotation-minimising frames (tangent, normal, binormal) along a path."""
    T = np.gradient(path, axis=0)
    T /= np.linalg.norm(T, axis=1, keepdims=True)
    up = np.asarray(up, dtype=float)
    n = up - np.dot(up, T[0]) * T[0]
    if np.linalg.norm(n) < 1e-6:
        n = np.cross(T[0], [1, 0, 0])
    N = np.empty_like(T)
    for i in range(len(T)):
        n = n - np.dot(n, T[i]) * T[i]
        n /= np.linalg.norm(n)
        N[i] = n
    B = np.cross(T, N)
    return T, N, B


def ring_xy(cx, cy, z, w, dfront, dback, m, power=2.0):
    """Horizontal superellipse ring; the front half (-Y) and back half (+Y) have separate depths."""
    t = np.linspace(0, 2 * np.pi, m, endpoint=False)
    c, s = np.cos(t), np.sin(t)
    e = 2.0 / power
    x = cx + w * np.sign(c) * np.abs(c) ** e
    d = np.where(s < 0, dfront, dback)
    y = cy + d * np.sign(s) * np.abs(s) ** e
    return np.stack([x, y, np.full(m, z)], axis=1)


def sweep(path, ra, rb, m, up=(0, 0, 1), power=2.0, twist=0.0):
    """Rings of radii (ra along the normal, rb along the binormal) swept along a path."""
    T, N, B = frames(path, up)
    t = np.linspace(0, 2 * np.pi, m, endpoint=False)
    ra = np.broadcast_to(np.asarray(ra, dtype=float), (len(path),))
    rb = np.broadcast_to(np.asarray(rb, dtype=float), (len(path),))
    tw = np.broadcast_to(np.asarray(twist, dtype=float), (len(path),))
    e = 2.0 / power
    rings = np.empty((len(path), m, 3))
    for i in range(len(path)):
        c, s = np.cos(t + tw[i]), np.sin(t + tw[i])
        c = np.sign(c) * np.abs(c) ** e
        s = np.sign(s) * np.abs(s) ** e
        rings[i] = path[i] + np.outer(ra[i] * c, N[i]) + np.outer(rb[i] * s, B[i])
    return rings


class MeshBuilder:
    """Accumulates ring-based geometry (with UVs and material indices) for one Blender object."""

    def __init__(self):
        self.verts, self.faces, self.uvs, self.mats = [], [], [], []
        self.nv = 0

    def add_vertices(self, v):
        v = np.asarray(v, dtype=float).reshape(-1, 3)
        base = self.nv
        self.verts.append(v)
        self.nv += len(v)
        return base

    def add_face(self, idx, uv, mat=0):
        self.faces.append(tuple(idx))
        self.uvs.append(tuple(uv))
        self.mats.append(mat)

    def add_rings(self, rings, mat=0, cap0=True, cap1=True, u_scale=1.0, v_coords=None, mat_fn=None):
        """Connect consecutive closed rings with quads; optionally fan-cap both ends.

        mat_fn(face_centre) -> material index (or None to leave the face out) lets a part change
        material by position (stockings vs skin, skirt stripe, the face opening in the hair cap)."""
        rings = np.asarray(rings, dtype=float)
        n, m, _ = rings.shape
        base = self.add_vertices(rings)
        v = np.linspace(0, 1, n) if v_coords is None else np.asarray(v_coords)
        for i in range(n - 1):
            for j in range(m):
                j1 = (j + 1) % m
                mi = mat if mat_fn is None else mat_fn((rings[i, j] + rings[i, j1] + rings[i + 1, j] + rings[i + 1, j1]) / 4)
                if mi is None:
                    continue
                a, b = base + i * m + j, base + i * m + j1
                c, d = base + (i + 1) * m + j1, base + (i + 1) * m + j
                u0, u1 = j / m * u_scale, (j + 1) / m * u_scale
                self.add_face((a, b, c, d), ((u0, v[i]), (u1, v[i]), (u1, v[i + 1]), (u0, v[i + 1])), mi)
        for cap, i in ((cap0, 0), (cap1, n - 1)):
            if not cap:
                continue
            ci = self.add_vertices(rings[i].mean(axis=0))
            mi = mat if mat_fn is None else mat_fn(rings[i].mean(axis=0))
            if mi is None:
                continue
            for j in range(m):
                j1 = (j + 1) % m
                tri = (base + i * m + j, base + i * m + j1, ci)
                self.add_face(tri if i else tri[::-1], ((j / m * u_scale, v[i]), ((j + 1) / m * u_scale, v[i]), (0.5, v[i])), mi)

    def add_fan(self, center, boundary, mat=0):
        """Triangle fan from a centre point to an open boundary polyline (wing membrane)."""
        c = self.add_vertices(center)
        b = self.add_vertices(boundary)
        for k in range(len(boundary) - 1):
            self.add_face((c, b + k, b + k + 1), ((0.5, 1), (k / len(boundary), 0), ((k + 1) / len(boundary), 0)), mat)

    def build(self, name, materials, smooth=True, uv_fn=None, recalc=True):
        verts = np.vstack(self.verts) if self.verts else np.zeros((0, 3))
        me = bpy.data.meshes.new(name)
        me.from_pydata(verts.tolist(), [], self.faces)
        uv_layer = me.uv_layers.new(name="UVMap")
        if uv_fn is not None:
            # planar projection overrides the per-ring UVs (used for the face texture)
            loops = np.array([li for f in self.faces for li in f])
            uv = uv_fn(verts[loops])
            uv_layer.data.foreach_set("uv", uv.ravel())
        else:
            uv_layer.data.foreach_set("uv", np.array([c for f in self.uvs for c in f], dtype=float).ravel())
        me.polygons.foreach_set("material_index", self.mats)
        me.polygons.foreach_set("use_smooth", [smooth] * len(self.faces))
        for mat in materials:
            me.materials.append(mat)
        if recalc:
            bm = bmesh.new()
            bm.from_mesh(me)
            bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-7)
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
            bm.to_mesh(me)
            bm.free()
        me.validate()
        ob = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(ob)
        return ob


def blend_normals(ob, proxy_fn, weight):
    """Anime-style normal editing: bend each vertex normal towards a smooth proxy normal.

    Cel shading on lots of small locks gives noisy light/shadow patches; shading hair (or a face)
    with the normals of a simple shape gives the clean, large colour blocks of 2D art. The result is
    stored as custom normals, so glTF exports it and three.js / Unity shade with it too."""
    me = ob.data
    n = len(me.vertices)
    co = np.empty(n * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    vn = np.empty(n * 3)
    me.vertex_normals.foreach_get("vector", vn)
    vn = vn.reshape(-1, 3)
    pn = proxy_fn(co)
    pn /= np.linalg.norm(pn, axis=1, keepdims=True) + 1e-9
    out = (1 - weight) * vn + weight * pn
    out /= np.linalg.norm(out, axis=1, keepdims=True) + 1e-9
    me.normals_split_custom_set_from_vertices(out.tolist())
