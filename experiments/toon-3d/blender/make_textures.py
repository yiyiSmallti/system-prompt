"""Draw the two textures the model uses: the anime face and the tail's diamond scales.

face.png is a front planar projection of the head: x in [-0.1, 0.1] m maps to u, z in [1.42, 1.62] m
to v, so eye and mouth positions are in the same metres as character.py. Drawn at 2x and downsampled.

usage: python make_textures.py <output dir>
"""
import pathlib
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

OUT = pathlib.Path(sys.argv[1])
SIZE = 1024
SS = 2                      # supersampling
S = SIZE * SS

SKIN = (255, 243, 238)
IRIS_DARK = (92, 86, 178)
IRIS_LIGHT = (176, 170, 236)
PUPIL = (58, 50, 120)
LASH = (66, 54, 92)
BROW = (184, 172, 214)
MOUTH = (205, 128, 128)
BLUSH = (255, 190, 196)


def px(x, z):
    """Metres on the face plane -> supersampled pixel."""
    return ((0.5 + x / 0.2) * S, (1 - (z - 1.42) / 0.2) * S)


def m(v):
    return v / 0.2 * S


def eye(img, cx, sgn):
    """One droopy anime eye; sgn=+1 is her left eye (image right)."""
    d = ImageDraw.Draw(img)
    x0, y0 = px(cx, 1.506)
    w, h = m(0.034), m(0.027)
    # sclera
    d.ellipse([x0 - w / 2, y0 - h / 2, x0 + w / 2, y0 + h / 2], fill=(255, 255, 255))
    # iris with a vertical gradient, clipped to the eye opening by the lash drawn on top
    iw, ih = m(0.021), m(0.026)
    iris = Image.new("RGBA", (int(iw), int(ih)))
    di = ImageDraw.Draw(iris)
    for k in range(int(ih)):
        t = k / ih
        col = tuple(int(a + (b - a) * t) for a, b in zip(IRIS_DARK, IRIS_LIGHT))
        di.line([(0, k), (iw, k)], fill=col + (255,))
    ix, iy = int(x0 - iw / 2 - sgn * m(0.001)), int(y0 - ih / 2 + m(0.002))
    mask = Image.new("L", img.size)
    ImageDraw.Draw(mask).ellipse([ix, iy, ix + iw - 1, iy + ih - 1], fill=255)
    sclera = Image.new("L", img.size)
    ImageDraw.Draw(sclera).ellipse([x0 - w / 2, y0 - h / 2, x0 + w / 2, y0 + h / 2], fill=255)
    layer = Image.new("RGBA", img.size)
    layer.paste(iris, (ix, iy))
    pupil = ImageDraw.Draw(layer)
    pupil.ellipse([x0 - m(0.0055), y0 - m(0.004), x0 + m(0.0055), y0 + m(0.008)], fill=PUPIL + (255,))
    img.paste(layer, (0, 0), Image.fromarray(np.minimum(np.asarray(mask), np.asarray(sclera))))
    # highlights
    d.ellipse([x0 - m(0.007), y0 - m(0.009), x0 - m(0.001), y0 - m(0.003)], fill=(255, 255, 255))
    d.ellipse([x0 + m(0.003), y0 + m(0.006), x0 + m(0.0065), y0 + m(0.0095)], fill=(240, 238, 255))
    # upper lash: thick arc, lower at the outer corner (droopy eyes), small flick outward
    inner = (x0 - sgn * w * 0.52, y0 - h * 0.18)
    top = (x0, y0 - h * 0.55)
    outer = (x0 + sgn * w * 0.55, y0 - h * 0.05)
    pts = bezier(inner, top, outer, 24)
    for k in range(len(pts) - 1):
        t = k / (len(pts) - 1)
        width = int(m(0.0018) + m(0.0026) * t)
        d.line([pts[k], pts[k + 1]], fill=LASH, width=width)
    d.line([outer, (outer[0] + sgn * m(0.004), outer[1] + m(0.003))], fill=LASH, width=int(m(0.0025)))
    # cover the sclera above the lash line with skin so the opening follows the lash
    poly = [(inner[0] - sgn * m(0.002), y0 - h), (outer[0] + sgn * m(0.004), y0 - h)] + \
        [(p[0], p[1] - m(0.0012)) for p in pts[::-1]]
    d.polygon(poly, fill=SKIN)
    # lower lash, short and thin
    low = bezier((x0 - sgn * w * 0.25, y0 + h * 0.48), (x0 + sgn * w * 0.10, y0 + h * 0.58), (x0 + sgn * w * 0.42, y0 + h * 0.36), 12)
    d.line(low, fill=(150, 120, 150), width=int(m(0.0008)))
    # brow (mostly hidden by the bangs)
    brow = bezier((x0 - sgn * w * 0.45, y0 - m(0.026)), (x0, y0 - m(0.032)), (x0 + sgn * w * 0.5, y0 - m(0.028)), 12)
    d.line(brow, fill=BROW, width=int(m(0.0012)))


def bezier(a, b, c, n):
    out = []
    for k in range(n):
        t = k / (n - 1)
        out.append(((1 - t) ** 2 * a[0] + 2 * t * (1 - t) * b[0] + t * t * c[0],
                    (1 - t) ** 2 * a[1] + 2 * t * (1 - t) * b[1] + t * t * c[1]))
    return out


def face():
    img = Image.new("RGB", (S, S), SKIN)
    blush = Image.new("L", (S, S), 0)
    db = ImageDraw.Draw(blush)
    for sgn in (-1, 1):
        x, y = px(sgn * 0.046, 1.482)
        db.ellipse([x - m(0.016), y - m(0.006), x + m(0.016), y + m(0.006)], fill=110)
    blush = blush.filter(ImageFilter.GaussianBlur(m(0.004)))
    img = Image.composite(Image.new("RGB", (S, S), BLUSH), img, blush)
    for sgn in (-1, 1):
        eye(img, sgn * 0.040, sgn)
    d = ImageDraw.Draw(img)
    nx, ny = px(0.0, 1.470)
    d.line([(nx, ny), (nx + m(0.0015), ny + m(0.002))], fill=(232, 190, 186), width=int(m(0.0012)))
    mouth = bezier(px(-0.007, 1.4475), px(0.0, 1.4440), px(0.007, 1.4475), 12)
    d.line(mouth, fill=MOUTH, width=int(m(0.0011)))
    img.resize((SIZE, SIZE), Image.LANCZOS).save(OUT / "face.png")


def scales():
    """Tileable diamond lattice for the dragon tail (UV u around, v along the tail)."""
    n = 256 * SS
    img = Image.new("RGB", (n, n), (250, 249, 255))
    d = ImageDraw.Draw(img)
    col, w = (178, 166, 222), 5 * SS
    for off in (-n, 0, n):
        d.line([(off, 0), (off + n, n)], fill=col, width=w)
        d.line([(off + n, 0), (off, n)], fill=col, width=w)
    img.resize((256, 256), Image.LANCZOS).save(OUT / "tail_scales.png")


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    face()
    scales()
