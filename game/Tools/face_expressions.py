#!/usr/bin/env python3
"""Draws the expressions the toon shader lays over the character faces (Dungeon/SeaFigure.cs).

Reads the front captures from Editor/FaceCapture.cs (menu: System Prompt/采集角色正脸): Tools/FaceArt/<model>_capture.png
and Resources/Models/Characters/<model>_FaceDepth.png, with the eyes and the mouth from Tools/FaceArt/landmarks.json.
Writes Resources/Models/Characters/<model>_Faces.png, a 3 x 3 atlas of 512 px cells, row by row from the top, in
SeaFigure's order: closed, happy, half-shut and small-pupilled eyes; smiling, open, pouting and round mouths; blush.
A cell is transparent but where it changes the face. Where a lock of hair hangs in front of the face (nearer than
the face around it) the cell stays transparent, so the hair keeps covering what is drawn.

Needs Pillow and numpy. python3 game/Tools/face_expressions.py [--preview DIR]
"""
import json
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
ART = os.path.join(HERE, "FaceArt")
CHARACTERS = os.path.join(HERE, "..", "Assets", "SystemPrompt", "Resources", "Models", "Characters")
N = 512
SS = 4  # strokes are drawn this many times larger, then scaled down for smooth edges
HAIR_MARGIN = 0.025  # a texel this much (of the box depth, about 8 mm) nearer than the face is hair

LASH_FALLBACK = (0.12, 0.12, 0.16)
MOUTH = (0.55, 0.17, 0.23)
MOUTH_LINE = (0.36, 0.11, 0.15)
TONGUE = (0.9, 0.5, 0.54)
BLUSH = (0.97, 0.55, 0.6)
BLUSH_LINE = (0.88, 0.4, 0.48)


def hsv(rgb):
    mx, mn = rgb.max(-1), rgb.min(-1)
    d = np.maximum(mx - mn, 1e-6)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    h = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60
    s = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
    return h, s, mx


def ellipse(cx, cy, a, b, feather=1.5):
    """Coverage of an ellipse, soft over `feather` pixels."""
    y, x = np.mgrid[0:N, 0:N] + 0.5
    r = np.sqrt(((x - cx) / a) ** 2 + ((y - cy) / b) ** 2)
    return np.clip((1 - r) * min(a, b) / feather + 0.5, 0, 1)


def membrane(values, known, sweeps=40):
    """`values` where `known`, and a smooth fill between, each filled texel the mean of its four neighbours. Solved
    from coarse to fine: the solution at half the size is where each finer one starts."""
    h, w = known.shape
    v = values.reshape(h, w, -1)
    if min(h, w) >= 16:
        eh, ew = h - h % 2, w - w % 2
        weight = known[:eh, :ew].reshape(eh // 2, 2, ew // 2, 2).sum((1, 3))
        coarse = (v[:eh, :ew] * known[:eh, :ew, None]).reshape(eh // 2, 2, ew // 2, 2, -1).sum((1, 3)) / np.maximum(weight, 1)[..., None]
        start = membrane(coarse, weight > 0, sweeps).reshape(eh // 2, ew // 2, -1).repeat(2, 0).repeat(2, 1)
        start = np.pad(start, ((0, h % 2), (0, w % 2), (0, 0)), mode="edge")
    else:
        start = np.broadcast_to(v[known].mean(0) if known.any() else 0.0, v.shape)
    out = np.where(known[..., None], v, start)
    for _ in range(sweeps):
        p = np.pad(out, ((1, 1), (1, 1), (0, 0)), mode="edge")
        out = np.where(known[..., None], v, (p[:-2, 1:-1] + p[2:, 1:-1] + p[1:-1, :-2] + p[1:-1, 2:]) / 4)
    return out.reshape(values.shape)


class Face:
    def __init__(self, model, marks):
        capture = np.asarray(Image.open(os.path.join(ART, model + "_capture.png")).convert("RGBA")).astype(float) / 255
        self.rgb, self.hit = capture[..., :3], capture[..., 3] > 0.5
        self.depth = np.asarray(Image.open(os.path.join(CHARACTERS, model + "_FaceDepth.png")).convert("L")).astype(float) / 255
        self.eyes = [tuple(e) for e in marks["eyes"]]
        self.mouth = tuple(marks["mouth"])
        self.span = self.eyes[1][0] - self.eyes[0][0]  # between the eyes, the measure for the mouth and the blush
        h, s, v = hsv(self.rgb)
        self.skin = self.hit & ((h < 45) | (h > 340)) & (s > 0.02) & (s < 0.35) & (v > 0.6)
        # The face's own depth, carried smoothly under the hair from the skin around it (hair is never skin-coloured);
        # a texel well nearer than that is a lock of hair hanging in front of the face. The face itself runs on
        # smoothly from brow to lashes to eye, so its own relief stays well inside the margin.
        self.open = self.hit & (self.depth > membrane(self.depth, self.skin) - HAIR_MARGIN)
        self.lash = self.darkest()

    def darkest(self):
        lum = self.rgb.mean(-1)
        picks = [lum[int(cy - b):int(cy + b), int(cx - a):int(cx + a)] for cx, cy, a, b in self.eyes]
        values = np.concatenate([p[self.open[int(cy - b):int(cy + b), int(cx - a):int(cx + a)]] for p, (cx, cy, a, b) in zip(picks, self.eyes)])
        if values.size == 0:
            return LASH_FALLBACK
        cut = np.percentile(values, 4)
        colours = np.concatenate([self.rgb[int(cy - b):int(cy + b), int(cx - a):int(cx + a)][(p <= cut)] for p, (cx, cy, a, b) in zip(picks, self.eyes)])
        return tuple(np.clip(colours.mean(0) * 0.85, 0, 1))

    def skin_fill(self, region):
        """The skin across `region`, carried in smoothly from the skin around it so no edge shows."""
        return np.clip(membrane(self.rgb, self.skin & self.open & (region < 0.02)), 0, 1)

    def cell(self):
        return np.zeros((N, N, 4))


def paint(cell, colour, coverage):
    """Lays `colour` (an image or a single colour) over the cell where `coverage` says."""
    colour = np.broadcast_to(np.asarray(colour, float), (N, N, 3))
    a = np.clip(coverage, 0, 1)[..., None]
    out_a = a + cell[..., 3:] * (1 - a)
    cell[..., :3] = np.where(out_a > 0, (colour * a + cell[..., :3] * cell[..., 3:] * (1 - a)) / np.maximum(out_a, 1e-6), cell[..., :3])
    cell[..., 3:] = out_a


def strokes(draw_fn):
    """Coverage of what draw_fn draws (in SS times larger pixels) on a blank canvas."""
    canvas = Image.new("L", (N * SS, N * SS), 0)
    draw_fn(ImageDraw.Draw(canvas))
    return np.asarray(canvas.resize((N, N), Image.LANCZOS)).astype(float) / 255


def curve(p0, p1, p2, steps=40):
    t = np.linspace(0, 1, steps)[:, None]
    return (1 - t) ** 2 * np.array(p0) + 2 * (1 - t) * t * np.array(p1) + t ** 2 * np.array(p2)


def tapered(draw, points, width, taper=0.35):
    """A brush stroke along `points`, `width` across in the middle and thinning toward both ends."""
    pts = np.asarray(points) * SS
    tangent = np.gradient(pts, axis=0)
    normal = np.stack([-tangent[:, 1], tangent[:, 0]], 1)
    normal /= np.maximum(np.linalg.norm(normal, axis=1, keepdims=True), 1e-6)
    t = np.linspace(0, 1, len(pts))
    half = width * SS / 2 * (taper + (1 - taper) * np.sin(np.pi * t) ** 0.6)
    left, right = pts + normal * half[:, None], pts - normal * half[:, None]
    draw.polygon([tuple(p) for p in np.concatenate([left, right[::-1]])], fill=255)


def eye_cells(face):
    closed, happy, half, small = face.cell(), face.cell(), face.cell(), face.cell()
    for side, (cx, cy, a, b) in enumerate(face.eyes):
        out = -1 if side == 0 else 1  # the outer corner's side in the picture
        width = max(2.5, 0.16 * b + 0.05 * a)
        region = ellipse(cx, cy, 1.18 * a, 1.25 * b, 2.5)
        fill = face.skin_fill(region)

        # Closed: the lid's line where the eye was, sagging a little, with a flick of lashes at the outer corner.
        def shut(d):
            tapered(d, curve((cx - 0.95 * a, cy + 0.05 * b), (cx, cy + 0.55 * b), (cx + 0.95 * a, cy + 0.05 * b)), width)
            o = cx + out * 0.95 * a
            tapered(d, curve((o, cy + 0.05 * b), (o + out * 0.18 * a, cy + 0.05 * b), (o + out * 0.32 * a, cy - 0.2 * b), 12), width * 0.6, 0.2)
        paint(closed, fill, region)
        paint(closed, face.lash, strokes(shut))

        # Happy: an arch, the eye shut in a smile.
        paint(happy, fill, region)
        paint(happy, face.lash, strokes(lambda d: tapered(d, curve((cx - 0.9 * a, cy + 0.4 * b), (cx, cy - 0.55 * b), (cx + 0.9 * a, cy + 0.4 * b)), width)))

        # Half-shut: the upper lid down to the middle of the eye, the lower half of the eye as it was.
        y = np.mgrid[0:N, 0:N][0] + 0.5
        lid = cy - 0.1 * b
        above = np.clip(lid - y + 1, 0, 1)
        paint(half, fill, region * above)
        paint(half, face.lash, strokes(lambda d: tapered(d, curve((cx - 1.0 * a, lid + 0.08 * b), (cx, lid - 0.12 * b), (cx + 1.0 * a, lid + 0.08 * b)), width * 1.1, 0.5)))

        # Small pupils: the iris painted out with the white of the eye, a smaller one in the middle.
        inside = face.rgb[int(cy - b):int(cy + b), int(cx - a):int(cx + a)].reshape(-1, 3)
        h, s, v = hsv(inside)
        white = inside[(s < 0.15) & (v > 0.75)]
        white = np.median(white, 0) if len(white) > 8 else np.array([0.95, 0.96, 0.98])
        iris = inside[s > 0.3]
        iris = np.median(iris, 0) if len(iris) > 8 else np.array(face.lash) * 2
        white_area = ellipse(cx, cy + 0.12 * b, 0.82 * a, 0.72 * b) * np.clip(y - (cy - 0.42 * b), 0, 1)
        shade = np.clip((y - (cy - 0.42 * b)) / (1.1 * b), 0, 1)[..., None]
        paint(small, white * (0.86 + 0.14 * shade), white_area)
        r = 0.36 * min(a, b * 1.3)
        ix, iy = cx, cy + 0.12 * b
        paint(small, np.clip(iris * 0.6, 0, 1), ellipse(ix, iy, r * 1.08, r * 1.15) * white_area)
        paint(small, iris, ellipse(ix, iy, r * 0.92, r, 1.2) * white_area)
        paint(small, np.clip(iris * 0.3, 0, 1), ellipse(ix, iy + 0.1 * r, r * 0.42, r * 0.48, 1.0) * white_area)
        paint(small, (1, 1, 1), ellipse(ix - 0.35 * r, iy - 0.35 * r, r * 0.26, r * 0.26, 0.8) * white_area)
    return [closed, happy, half, small]


def mouth_cells(face):
    mx, my = face.mouth
    d = face.span
    region = ellipse(mx, my, 0.17 * d, 0.07 * d, 2)
    fill = face.skin_fill(region)
    line = max(1.5, 0.012 * d)
    y = np.mgrid[0:N, 0:N][0] + 0.5

    def opening(shape, tongue_from):
        cell = face.cell()
        paint(cell, fill, region)
        paint(cell, MOUTH_LINE, strokes(lambda dr: dr.polygon(shape(line * 1.2), fill=255)))
        inner = strokes(lambda dr: dr.polygon(shape(0), fill=255))
        paint(cell, MOUTH, inner)
        paint(cell, TONGUE, inner * np.clip(y - tongue_from, 0, 1))
        return cell

    # Smiling: an open smile, the top nearly straight, the bottom a deep curve.
    def smile(grow):
        top = curve((mx - 0.13 * d - grow, my - 0.015 * d - grow), (mx, my + 0.005 * d - grow), (mx + 0.13 * d + grow, my - 0.015 * d - grow))
        bottom = curve((mx + 0.13 * d + grow, my - 0.015 * d), (mx, my + 0.16 * d + grow), (mx - 0.13 * d - grow, my - 0.015 * d))
        return [tuple(p * SS) for p in np.concatenate([top, bottom])]

    # Open, mid-word: a small rounded opening.
    def talk(grow):
        t = np.linspace(0, 2 * np.pi, 48)
        return [((mx + (0.075 * d + grow) * np.cos(a)) * SS, (my + 0.012 * d + (0.045 * d + grow) * np.sin(a) * (1.15 if np.sin(a) > 0 else 0.8)) * SS) for a in t]

    # Round: a small "o".
    def round_(grow):
        t = np.linspace(0, 2 * np.pi, 48)
        return [((mx + (0.04 * d + grow) * np.cos(a)) * SS, (my + 0.02 * d + (0.055 * d + grow) * np.sin(a)) * SS) for a in t]

    pout = face.cell()
    paint(pout, fill, region)
    paint(pout, MOUTH_LINE, strokes(lambda dr: tapered(dr, curve((mx - 0.065 * d, my + 0.02 * d), (mx, my - 0.03 * d), (mx + 0.065 * d, my + 0.02 * d)), line * 1.6, 0.4)))
    return [opening(smile, my + 0.07 * d), opening(talk, my + 0.03 * d), pout, opening(round_, my + 0.05 * d)]


def blush_cell(face):
    cell = face.cell()
    d = face.span
    for side, (cx, cy, a, b) in enumerate(face.eyes):
        out = -1 if side == 0 else 1
        bx, by = cx + out * 0.12 * a, cy + b + 0.12 * d
        glow = np.asarray(Image.fromarray((ellipse(bx, by, 0.6 * a, 0.2 * a, 1) * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.12 * a))).astype(float) / 255
        paint(cell, BLUSH, glow * 0.5)

        def hatch(dr):
            for k in (-1, 0, 1):
                x = bx + k * 0.28 * a
                tapered(dr, curve((x + 0.1 * a, by - 0.14 * a), (x, by), (x - 0.1 * a, by + 0.14 * a), 8), max(1.2, 0.05 * a), 0.3)
        paint(cell, BLUSH_LINE, strokes(hatch) * 0.7)
    return cell


def build(model, marks, preview):
    face = Face(model, marks)
    cells = eye_cells(face) + mouth_cells(face) + [blush_cell(face)]
    for c in cells:
        c[..., 3] *= face.open  # hair in front of the face keeps covering it
    atlas = np.zeros((3 * N, 3 * N, 4))
    for i, c in enumerate(cells):
        atlas[(i // 3) * N:(i // 3 + 1) * N, (i % 3) * N:(i % 3 + 1) * N] = c
    Image.fromarray((np.clip(atlas, 0, 1) * 255 + 0.5).astype(np.uint8)).save(os.path.join(CHARACTERS, model + "_Faces.png"))
    if preview:
        base = np.concatenate([face.rgb, face.hit[..., None].astype(float)], -1)
        looks = {"neutral": [], "blink": [0], "smile": [8, 1, 4], "pout": [8, 2, 6], "scared": [3, 7], "sleepy": [2], "talk": [5]}
        tiles = []
        for name, layers in looks.items():
            img = base.copy()
            for k in layers:
                paint(img, cells[k][..., :3], cells[k][..., 3])
            tiles.append(img[..., :3])
        tiles.append(np.repeat(face.open[..., None], 3, -1).astype(float))
        sheet = np.concatenate(tiles, 1)
        Image.fromarray((sheet * 255).astype(np.uint8)).save(os.path.join(preview, model + ".png"))
    print(f"{model}: {model}_Faces.png")


def main():
    preview = sys.argv[sys.argv.index("--preview") + 1] if "--preview" in sys.argv else None
    if preview:
        os.makedirs(preview, exist_ok=True)
    with open(os.path.join(ART, "landmarks.json"), encoding="utf-8") as f:
        landmarks = json.load(f)
    for model, marks in landmarks.items():
        if not model.startswith("_"):
            build(model, marks, preview)


if __name__ == "__main__":
    main()
