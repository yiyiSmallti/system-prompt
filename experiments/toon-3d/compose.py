"""Lay the renders out next to the reference turnaround and each other.

usage: python compose.py <reference dir> <out dir>
reads <out dir>/renders/{blender,threejs,reference_blender,reference_threejs}/<view>.png and writes
  compare_all.png          turnaround | Blender | three.js, four views each
  overlay_<route>.png      silhouettes of the turnaround and the render on top of each other
  closeup.png              bust shots: procedural ChatGPT vs the VRoid control model, both renderers
  control.png              full-body fronts: procedural ChatGPT vs the VRoid control model
"""
import pathlib
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

REF = pathlib.Path(sys.argv[1])
OUT = pathlib.Path(sys.argv[2])
VIEWS = ["front", "left", "right", "back"]
BG = (255, 255, 255)
INK = (60, 60, 80)
W, H = 454, 1024
PAD, LABEL = 8, 40
VROID = "VRoid 对照模型 VRM1_Constraint_Twist_Sample © pixiv Inc.（VRM Public License 1.0）"


def font(size):
    for f in ("/usr/share/fonts/truetype/wqy/wqy-zenhei.ttc", "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",
              "/usr/share/fonts/truetype/wqy/wqy-microhei.ttc"):
        if pathlib.Path(f).exists():
            return ImageFont.truetype(f, size)
    return ImageFont.load_default()


def render(route, view, size=None):
    im = Image.open(OUT / "renders" / route / f"{view}.png").convert("RGBA")
    if size:
        im = im.resize(size, Image.LANCZOS)
    return Image.alpha_composite(Image.new("RGBA", im.size, BG + (255,)), im).convert("RGB"), np.asarray(im)[..., 3] > 24


def ref(view):
    return Image.open(REF / f"{view}.jpg").convert("RGB")


def row(images, label, captions=None, footer=None):
    h = max(im.height for im in images)
    cap = (34 if captions else 0) + (30 if footer else 0)
    strip = Image.new("RGB", (sum(im.width + PAD for im in images) + PAD, h + 2 * PAD + LABEL + cap), BG)
    d = ImageDraw.Draw(strip)
    d.text((PAD + 4, PAD), label, fill=INK, font=font(26))
    x = PAD
    for k, im in enumerate(images):
        strip.paste(im, (x, PAD + LABEL))
        if captions:
            d.text((x + 4, PAD + LABEL + h + 4), captions[k], fill=INK, font=font(20))
        x += im.width + PAD
    if footer:
        d.text((PAD + 4, strip.height - 26), footer, fill=INK, font=font(16))
    return strip


def stack(rows):
    out = Image.new("RGB", (max(r.width for r in rows), sum(r.height for r in rows)), BG)
    y = 0
    for r in rows:
        out.paste(r, (0, y))
        y += r.height
    return out


def overlay(route, name):
    """Turnaround in grey, render in light lavender, overlap in dark lavender: proportion errors at a glance."""
    tiles = []
    for v in VIEWS:
        ref_mask = np.asarray(ref(v).convert("L")) < 235
        _, mask = render(route, v, (W, H))
        img = np.full((H, W, 3), 255, np.uint8)
        img[ref_mask] = (200, 200, 200)
        img[mask] = (190, 170, 240)
        img[ref_mask & mask] = (120, 100, 190)
        tiles.append(Image.fromarray(img))
    return row(tiles, f"轮廓叠加：灰 = 只有原画，浅紫 = 只有 {name}，深紫 = 重合")


def main():
    routes = [(r, n) for r, n in (("blender", "Blender EEVEE"), ("threejs", "three.js"))
              if (OUT / "renders" / r / "front.png").exists()]
    rows = [row([ref(v) for v in VIEWS], "四视图原画（正面 / 左侧 / 右侧 / 背面）")]
    for route, name in routes:
        rows.append(row([render(route, v, (W, H))[0] for v in VIEWS], f"程序化模型 · {name}"))
        overlay(route, name).save(OUT / f"overlay_{route}.png", optimize=True)
    stack(rows).save(OUT / "compare_all.png", optimize=True)

    have = lambda route: (OUT / "renders" / route / "bust.png").exists()  # noqa: E731
    pairs = [("blender", "Blender"), ("threejs", "three.js")]
    close = []
    for prefix, label, footer in (("", "程序化 ChatGPT", None), ("reference_", "VRoid 对照模型", VROID)):
        tiles = [(render(prefix + r, "bust", (512, 512))[0], f"{label} · {n}") for r, n in pairs if have(prefix + r)]
        if tiles:
            close.append(row([t for t, _ in tiles], label, [c for _, c in tiles], footer))
    if close:
        stack(close).save(OUT / "closeup.png", optimize=True)

    fronts = [(p + r, f"{lab} · {n}") for p, lab in (("", "程序化 ChatGPT"), ("reference_", "VRoid 对照"))
              for r, n in pairs if (OUT / "renders" / (p + r) / "front.png").exists()]
    if fronts:
        row([render(r, "front", (W, H))[0] for r, _ in fronts], "同一套赛璐璐着色 + 描边，只换模型",
            [c for _, c in fronts], VROID).save(OUT / "control.png", optimize=True)


if __name__ == "__main__":
    main()
