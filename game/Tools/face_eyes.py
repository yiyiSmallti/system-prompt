#!/usr/bin/env python3
"""Paints new eyes on the player models, whose generated eyes came out wrong (Dungeon/SeaFigure.cs).

The turnarounds Tripo worked from (assets/characters/turnarounds) have faces about 60 px wide with the fringe down
to the eyes, and the eyes it baked into the base colour show it: grey and soft with locks of hair drawn across them
(PlayerMale), one eye a flat grey with no pupil (PlayerFemale), the lids smeared white (PlayerFemaleBattle). The
eyes are painted again from the front, in the front captures from Editor/FaceCapture.cs (menu: System Prompt/采集角色正脸),
at the eyes in Tools/FaceArt/landmarks.json: the eye, and the skin around it carried in from clean skin further out,
so whatever was drawn there goes. They are carried back onto the base colour through the triangles the capture
writes (Tools/FaceArt/<model>_mesh.bin), onto the face's own surface only: not onto a lock of hair hanging in front
of it, which keeps its own colour. Writes Resources/Models/Characters/<model>_Eyes.png, the size of the base colour
and transparent but where it repaints, which Editor/ModelAssetImporter.cs lays over the base colour on import.

Then capture again, so the captures show the new eyes, and run face_expressions.py, which draws the expressions over
them. The eyes are drawn inside the part of the face the closed eyes cover, and the skin they are carried in from is
outside what they paint, so painting over painted eyes gives the same again.

Needs Pillow and numpy. python3 game/Tools/face_eyes.py [--preview DIR]
"""
import json
import os
import sys

import numpy as np
from PIL import Image

from face_expressions import ART, CHARACTERS, HAIR_MARGIN, N, SS, Face, curve, hsv, membrane, paint, strokes

# Each model's eyes: the iris (the design's dark grey, design/characters/00-protagonist.md, and in the battle coat
# the green the latent space lights them with, brighter toward the bottom), and how far above the eye, in the eye's
# half heights, the skin is painted again (PlayerFemaleBattle's lids are smeared white up to the brows).
STYLES = {
    "PlayerMale": {"iris": (0.29, 0.30, 0.34), "glow": False, "reach": 1.6},
    "PlayerFemale": {"iris": (0.29, 0.30, 0.34), "glow": False, "reach": 1.6},
    "PlayerMaleBattle": {"iris": (0.12, 0.66, 0.54), "glow": True, "reach": 1.6},
    "PlayerFemaleBattle": {"iris": (0.12, 0.66, 0.54), "glow": True, "reach": 2.3},
}
LASH = (0.11, 0.11, 0.14)  # the near-black of the hair
WHITE = (0.94, 0.935, 0.95)
WHITE_SHADE = (0.68, 0.69, 0.77)
SKIN_SHADE = (0.95, 0.89, 0.88)  # the skin under the fringe, as a share of the skin in the light
BACK_MARGIN = 0.04  # a texel this much (of the box depth, about 13 mm) behind the face is no longer the face
HEAD = 0.1  # how far (of the box depth, about 3 cm) a line of sight runs on inside the head; a lock is millimetres through
# A part of the base colour lying for the most part (seen from the front) this much (about 4 mm) in front of the
# face is a lock of hair: the face's own parts lie within 2.5 mm (Tripo's upper lids stand out from the eye a
# little, the line of sight going through them), the locks over the eyes 5 mm and more, though some touch the
# skin at their roots.
HAIR_LIFT = 0.012
FRONT_MARGIN = 0.05  # a texel of the face this much (about 16 mm) in front of where the face goes in is not on it
GROW = 1.5  # texels each triangle is grown by on the base colour, so filtering and mips find the paint at its edges


def mix(a, b, t):
    t = np.asarray(t, float)
    return np.asarray(a, float) * (1 - t[..., None]) + np.asarray(b, float) * t[..., None] if t.ndim else np.asarray(a, float) * (1 - t) + np.asarray(b, float) * t


def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def band(draw, points, width, profile, hang=0.5):
    """A stroke along `points` (in pixels), `width` across where `profile` (0 to 1 along it) is 1; `hang` of it on
    the side the points' y grows toward."""
    pts = np.asarray(points) * SS
    tangent = np.gradient(pts, axis=0)
    normal = np.stack([-tangent[:, 1], tangent[:, 0]], 1)
    normal /= np.maximum(np.linalg.norm(normal, axis=1, keepdims=True), 1e-6)
    normal = np.where(normal[:, 1:2] < 0, -normal, normal)
    half = width * SS * profile(np.linspace(0, 1, len(pts)))[:, None]
    draw.polygon([tuple(p) for p in np.concatenate([pts - normal * half * (1 - hang), (pts + normal * half * hang)[::-1]])], fill=255)


class Eye:
    """One eye's frame: x toward its outer corner, y down, turned with the head, in the eye's half width and height."""

    def __init__(self, cx, cy, a, b, out, tilt):
        self.cx, self.cy, self.a, self.b, self.out = cx, cy, a, b, out
        self.c, self.s = np.cos(tilt), np.sin(tilt)
        y, x = np.mgrid[0:N, 0:N] + 0.5
        dx, dy = x - cx, y - cy
        self.x = (dx * self.c + dy * self.s) * out / a
        self.y = (-dx * self.s + dy * self.c) / b

    def at(self, points):
        """Points in the eye's frame, in the picture."""
        p = np.asarray(points, float)
        lx, ly = p[..., 0] * self.out * self.a, p[..., 1] * self.b
        return np.stack([self.cx + lx * self.c - ly * self.s, self.cy + lx * self.s + ly * self.c], -1)

    def oval(self, x, y, rx, ry, feather=1.2):
        """Coverage of an oval in the eye's frame, soft over `feather` pixels."""
        r = np.sqrt(((self.x - x) / rx) ** 2 + ((self.y - y) / ry) ** 2)
        return np.clip((1 - r) * min(rx * self.a, ry * self.b) / feather + 0.5, 0, 1)

    def shape(self, points):
        return strokes(lambda d: d.polygon([tuple(p * SS) for p in self.at(points)], fill=255))


def eye_region(eye, style):
    """Where the skin around an eye is painted again: from a little under the lower lid to `reach` above, fading
    out at the top into the skin as it was, and further toward the nose, where Tripo's irises run on into the
    inner corner behind the hair (seen when the head is turned)."""
    reach, low = style["reach"], 1.5
    return eye.oval(-0.15, (low - reach - 0.3) / 2, 1.5, (low + reach + 0.3) / 2, 3.0) * smooth(-reach - 0.3, -reach + 0.4, eye.y)


def paint_eye(layer, face, eye, style, clean):
    y = eye.y
    # The lids, inner corner to outer: the upper rises steeply from the inner corner and runs on nearly flat, a
    # little over the iris, so the eye looks calm and a little tired (the turnarounds).
    inner, outer = (-0.95, 0.18), (1.0, -0.12)
    upper = curve(inner, (-0.25, -1.22), outer, 60)
    lower = curve(inner, (0.2, 1.05), outer, 60)

    # The skin, carried in over the eye that was there and whatever was drawn around it, from clean skin further
    # out (Tripo drew the skin under the fringe a grey that, carried in, comes out as a grey patch), a little
    # shaded toward the top under the fringe.
    region = eye_region(eye, style)
    skin = np.clip(membrane(face.rgb, clean), 0, 1)
    skin *= mix((1, 1, 1), SKIN_SHADE, smooth(-0.7, -style["reach"], y))
    paint(layer, skin, region)

    opening = eye.shape(np.concatenate([upper, lower[::-1]]))
    paint(layer, mix(WHITE_SHADE, WHITE, smooth(-0.55, 0.0, y)), opening)

    # The iris, nearly round, its top under the upper lid and its bottom under the lower: dark under the lid,
    # lighter toward the bottom, with a dark rim and pupil; in the battle coat it glows at the bottom.
    iris = np.asarray(style["iris"], float)
    ix, iy, rx, ry = 0.03, 0.0, 0.47, 0.74
    disc = eye.oval(ix, iy, rx, ry) * opening
    t = (y - (iy - ry)) / (2 * ry)
    light = mix(iris, (1, 1, 1), 0.5 if style["glow"] else 0.28)
    colour = mix(mix(iris * 0.35, iris, smooth(0.15, 0.55, t)), light, smooth(0.55, 1.0, t))
    r = np.sqrt(((eye.x - ix) / rx) ** 2 + ((y - iy) / ry) ** 2)
    colour = mix(colour, iris * 0.28, smooth(0.8, 0.98, r))
    paint(layer, colour, disc)
    paint(layer, iris * 0.2, eye.oval(ix, iy - 0.06, 0.4 * rx, 0.48 * ry) * opening)
    if style["glow"]:
        paint(layer, mix(iris, (1, 1, 1), 0.7), eye.oval(ix, iy + 0.5 * ry, 0.62 * rx, 0.3 * ry, 2.0) * disc * 0.6)
    # The upper lid's shadow over the top of the iris and the white.
    paint(layer, iris * 0.25, opening * (1 - smooth(-0.6, -0.25, y)) * 0.5)

    # Highlights, both eyes lit from the same side of the picture: a large one high, a small one low across.
    side = -eye.out  # the picture's left, in the eye's frame
    paint(layer, (1, 1, 1), eye.oval(ix + side * 0.4 * rx, iy - 0.34 * ry, 0.27 * rx, 0.25 * ry, 0.9) * opening)
    paint(layer, (1, 1, 1), eye.oval(ix - side * 0.42 * rx, iy + 0.4 * ry, 0.13 * rx, 0.12 * ry, 0.8) * opening * 0.85)

    # The lashes: a heavy line hanging from the upper lid, thin at the inner corner and thickest past the middle,
    # with a short flick past the outer corner; a light line under the outer part of the lower lid; and faintly,
    # the fold of the lid above. All inside the part of the face the closed eyes paint over (face_expressions.py).
    width = max(2.6 / eye.b, 0.3)
    flick = curve(upper[-8], (1.08, -0.1), (1.14, 0.08), 12)
    upper_px, flick_px = eye.at(upper), eye.at(flick)
    lash = strokes(lambda d: (
        band(d, upper_px, width * eye.b, lambda t: (0.3 + 0.7 * smooth(0.0, 0.45, t)) * (1 - 0.7 * smooth(0.85, 1.0, t)), 0.75),
        band(d, flick_px, 0.55 * width * eye.b, lambda t: 1 - 0.8 * t)))
    paint(layer, LASH, lash)
    paint(layer, mix(LASH, skin, 0.45), strokes(lambda d: band(d, eye.at(lower[24:]), max(1.2, 0.08 * eye.b), lambda t: 0.2 + 0.8 * np.sin(np.pi * t) ** 0.7)) * 0.8)
    fold = curve((-0.4, -0.92), (0.2, -1.25), (0.85, -0.7), 30)
    paint(layer, mix(LASH, skin, 0.6), strokes(lambda d: band(d, eye.at(fold), max(1.0, 0.06 * eye.b), lambda t: np.sin(np.pi * t) ** 0.7)) * 0.4)


def front_layer(face, style):
    """The new eyes as seen from the front (colour, and alpha where they paint), and where the face shows. All that
    is read off the capture is read outside where the eyes are painted, so a capture with the eyes painted gives
    the same as one without."""
    (x0, y0, _, _), (x1, y1, _, _) = face.eyes
    tilt = np.arctan2(y1 - y0, x1 - x0)
    eyes = [Eye(cx, cy, a, b, -1 if side == 0 else 1, tilt) for side, (cx, cy, a, b) in enumerate(face.eyes)]
    outside = np.all([eye_region(eye, style) < 0.02 for eye in eyes], 0)
    # As face_expressions.py does: the face's depth carried under the hair from the skin, and nearer than that, hair.
    surface = membrane(face.depth, face.skin & outside)
    shows = face.hit & (face.depth > surface - HAIR_MARGIN)
    h, s, v = hsv(face.rgb)
    clean = face.skin & shows & outside & (s > 0.06) & (v > 0.75)
    layer = np.zeros((N, N, 4))
    for eye in eyes:
        paint_eye(layer, face, eye, style, clean)
    return layer, shows


def bilinear(img, px, py):
    h, w = img.shape[:2]
    px, py = np.clip(px, 0, w - 1.001), np.clip(py, 0, h - 1.001)
    x0, y0 = px.astype(int), py.astype(int)
    fx, fy = px - x0, py - y0
    if img.ndim == 3:
        fx, fy = fx[..., None], fy[..., None]
    top = img[y0, x0] * (1 - fx) + img[y0, x0 + 1] * fx
    bottom = img[y0 + 1, x0] * (1 - fx) + img[y0 + 1, x0 + 1] * fx
    return top * (1 - fy) + bottom * fy


def parts(triangles):
    """Which part of the base colour (a run of triangles joined at their uv corners) each triangle is in."""
    keys = np.round(triangles[..., :2] * 65536).astype(np.int64)
    keys = keys[..., 0] * (1 << 20) + keys[..., 1]
    parent = np.arange(len(triangles))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i
    first = {}
    for t, corners in enumerate(keys):
        for k in corners:
            if k in first:
                parent[find(t)] = find(first[k])
            else:
                first[k] = t
    return np.array([find(t) for t in range(len(triangles))])


def face_depth(front, where):
    """How deep the face is at each pixel of the capture in `where` (elsewhere infinite), from the triangles' corners
    in the capture: where the line of sight goes into the head to stay, past any lock of hair it goes through first
    (in and out within millimetres)."""
    ys, xs = np.nonzero(where)
    top, bottom, left, right = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
    pixels, depths, facing = [], [], []
    for p in front:
        x0, y0 = np.maximum(np.floor(p[:, :2].min(0)).astype(int), (left, top))
        x1, y1 = np.minimum(np.ceil(p[:, :2].max(0)).astype(int) + 1, (right, bottom))
        det = (p[1, 0] - p[0, 0]) * (p[2, 1] - p[0, 1]) - (p[2, 0] - p[0, 0]) * (p[1, 1] - p[0, 1])
        if x1 <= x0 or y1 <= y0 or abs(det) < 1e-9:
            continue
        ty, tx = np.mgrid[y0:y1, x0:x1] + 0.5
        w = np.stack([((p[(k + 2) % 3, 0] - p[(k + 1) % 3, 0]) * (ty - p[(k + 1) % 3, 1]) - (p[(k + 2) % 3, 1] - p[(k + 1) % 3, 1]) * (tx - p[(k + 1) % 3, 0])) / det for k in range(3)], -1)
        inside = (w >= 0).all(-1)
        pixels.append(((ty - 0.5) * N + tx - 0.5).astype(int)[inside])
        depths.append((w @ p[:, 2])[inside])
        facing.append(np.full(inside.sum(), np.sign(det)))
    pixels, depths, facing = np.concatenate(pixels), np.concatenate(depths), np.concatenate(facing)
    order = np.lexsort((depths, pixels))
    pixels, depths, facing = pixels[order], depths[order], facing[order]
    # Which way the triangles facing the capture wind: the way of the nearest at each pixel, mostly.
    first = np.r_[True, pixels[1:] != pixels[:-1]]
    towards = np.sign(facing[first].sum())
    # Going in where the count of surfaces gone in through minus out through goes from 0 to 1; staying in where
    # it is not back to 0 within HEAD.
    step = np.where(facing == towards, 1, -1)
    starts = np.flatnonzero(first)
    running = np.cumsum(step)
    count = running - np.repeat(np.r_[0, running[starts[1:] - 1]], np.diff(np.r_[starts, len(pixels)]))
    entering = (step == 1) & (count == 1)
    out = np.full(N * N, np.inf)
    last = np.r_[starts[1:], len(pixels)] - 1  # each pixel's last entry
    owner = np.repeat(np.arange(len(starts)), np.diff(np.r_[starts, len(pixels)]))
    for i in np.flatnonzero(entering):
        if np.isfinite(out[pixels[i]]):
            continue
        j = i + 1
        while j <= last[owner[i]] and count[j] > 0:
            j += 1
        leave = depths[j] if j <= last[owner[i]] else np.inf
        if leave - depths[i] > HEAD:
            out[pixels[i]] = depths[i]
    return out.reshape(N, N)


def carry_back(model, layer, size):
    """`layer` (front, N x N) on the base colour (size x size, rows from the top), on the face's own surface."""
    triangles = np.fromfile(os.path.join(ART, model + "_mesh.bin"), dtype="<f4").reshape(-1, 3, 5)
    # Corners on the base colour in texels (x right, y down), and in the capture: pixels and depth.
    uv = np.stack([triangles[..., 0] * size, (1 - triangles[..., 1]) * size], -1)
    front = np.stack([triangles[..., 2] * N, (1 - triangles[..., 3]) * N, triangles[..., 4]], -1)
    centre = front.mean(1)
    # Triangles reaching anywhere the layer paints: how much of their extent in the capture it paints, from
    # sums of it over the capture's rows and columns so far.
    sums = np.pad((layer[..., 3] > 0).cumsum(0).cumsum(1), ((1, 0), (1, 0)))
    x0, y0 = np.clip(np.floor(front[..., 0].min(1)), 0, N).astype(int), np.clip(np.floor(front[..., 1].min(1)), 0, N).astype(int)
    x1, y1 = np.clip(np.ceil(front[..., 0].max(1)) + 1, 0, N).astype(int), np.clip(np.ceil(front[..., 1].max(1)) + 1, 0, N).astype(int)
    near = sums[y1, x1] - sums[y0, x1] - sums[y1, x0] + sums[y0, x0] > 0
    spread = np.pad(layer[..., 3] > 0, 8)
    spread = np.any([spread[8 + dy:8 + dy + N, 8 + dx:8 + dx + N] for dy in range(-8, 9, 4) for dx in range(-8, 9, 4)], 0)
    surface = face_depth(front, spread)

    def depth_at(x, y):
        return surface[np.clip(y.astype(int), 0, N - 1), np.clip(x.astype(int), 0, N - 1)]

    # A lock of hair hanging in front of the face may touch it at its root; a part of the base colour that is for
    # the most part in front of the face is hair all through, so a lock is either all painted or not at all.
    part = parts(triangles)
    lift = depth_at(centre[:, 0], centre[:, 1]) - centre[:, 2]
    area = 0.5 * np.abs((front[:, 1, 0] - front[:, 0, 0]) * (front[:, 2, 1] - front[:, 0, 1]) - (front[:, 2, 0] - front[:, 0, 0]) * (front[:, 1, 1] - front[:, 0, 1]))
    hair = np.zeros(len(triangles), bool)
    for p in np.unique(part[near]):
        mine = np.flatnonzero(near & (part == p) & np.isfinite(lift))
        if len(mine) == 0:
            continue
        order = mine[np.argsort(lift[mine])]
        middle = order[min(np.searchsorted(np.cumsum(area[order]), area[order].sum() / 2), len(order) - 1)]  # the median by area
        hair[part == p] = lift[middle] > HAIR_LIFT

    # Sampled premultiplied, so the soft edge doesn't pick up the black of where nothing is painted.
    layer = np.concatenate([layer[..., :3] * layer[..., 3:], layer[..., 3:]], -1)
    patch = np.zeros((size, size, 4))
    best = np.full((size, size), np.inf)  # how far outside its triangle each texel was taken, inside first
    for t in np.flatnonzero(near & ~hair):
        p, q = uv[t], front[t]
        det = (p[1, 0] - p[0, 0]) * (p[2, 1] - p[0, 1]) - (p[2, 0] - p[0, 0]) * (p[1, 1] - p[0, 1])
        if abs(det) < 1e-6:
            continue
        x0, y0 = np.maximum(np.floor(p.min(0) - GROW).astype(int), 0)
        x1, y1 = np.minimum(np.ceil(p.max(0) + GROW).astype(int), size)
        if x1 <= x0 or y1 <= y0:
            continue
        ty, tx = np.mgrid[y0:y1, x0:x1] + 0.5
        # Barycentric weights, and from them how far outside the triangle each texel is, in texels.
        w = np.stack([((p[(k + 2) % 3, 0] - p[(k + 1) % 3, 0]) * (ty - p[(k + 1) % 3, 1]) - (p[(k + 2) % 3, 1] - p[(k + 1) % 3, 1]) * (tx - p[(k + 1) % 3, 0])) / det for k in range(3)], -1)
        edges = np.array([np.linalg.norm(p[(k + 2) % 3] - p[(k + 1) % 3]) for k in range(3)])
        outside = np.max(-w * (abs(det) / np.maximum(edges, 1e-6)), -1).clip(0)
        take = (outside <= GROW) & (outside < best[y0:y1, x0:x1])
        if not take.any():
            continue
        at = w @ q  # each texel in the capture: x, y and depth
        depth = depth_at(at[..., 0], at[..., 1])
        colour = bilinear(layer, at[..., 0] - 0.5, at[..., 1] - 0.5)
        colour[..., :3] /= np.maximum(colour[..., 3:], 1e-6)
        colour[..., 3] *= (at[..., 2] > depth - FRONT_MARGIN) & (at[..., 2] < depth + BACK_MARGIN)
        take &= colour[..., 3] > 0
        best[y0:y1, x0:x1][take] = outside[take]
        patch[y0:y1, x0:x1][take] = colour[take]
    return patch


def build(model, marks, preview):
    face = Face(model, marks)
    layer, shows = front_layer(face, STYLES[model])
    base = Image.open(os.path.join(CHARACTERS, model + "_BaseColor.png"))
    patch = carry_back(model, layer, base.width)
    Image.fromarray((np.clip(patch, 0, 1) * 255 + 0.5).astype(np.uint8)).save(os.path.join(CHARACTERS, model + "_Eyes.png"))
    if preview:
        before = np.concatenate([np.where(face.hit[..., None], face.rgb, 1.0), np.ones((N, N, 1))], -1)
        after = before.copy()
        paint(after, layer[..., :3], layer[..., 3] * shows)
        (cx0, cy0, a0, _), (cx1, cy1, a1, _) = face.eyes
        y, x0, x1 = int((cy0 + cy1) / 2), int(cx0 - 1.8 * a0), int(cx1 + 1.8 * a1)
        sheet = np.concatenate([img[max(0, y - 60):y + 60, max(0, x0):x1, :3] for img in (before, after)], 0)
        im = Image.fromarray((sheet * 255).astype(np.uint8))
        im.resize((im.width * 3, im.height * 3), Image.LANCZOS).save(os.path.join(preview, model + "_eyes.png"))
    print(f"{model}: {model}_Eyes.png，{int((patch[..., 3] > 0).sum())} 个纹素")


def main():
    preview = sys.argv[sys.argv.index("--preview") + 1] if "--preview" in sys.argv else None
    if preview:
        os.makedirs(preview, exist_ok=True)
    with open(os.path.join(ART, "landmarks.json"), encoding="utf-8") as f:
        landmarks = json.load(f)
    for model in STYLES:
        build(model, landmarks[model], preview)


if __name__ == "__main__":
    main()
