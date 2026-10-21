"""PV game-UI overlays, Persona-style: slanted panels, black/white contrast, halftone,
cut-out titles. The accent is terminal green (where P5 uses red); DeepSeek content uses
deep-sea blue, and warning red is reserved for the evaluation file.

Every overlay is an object with start/end (seconds on the PV timeline) and
draw(frame, t), where frame is a 1280x720 RGBA PIL image and t is seconds since start.
"""
import functools
import math
import pathlib
import random

import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

W, H = 1280, 720
INK = (12, 12, 16, 255)
PAPER = (246, 246, 240, 255)
GREEN = (63, 191, 127, 255)
BLUE = (44, 74, 138, 255)
SEA = (74, 139, 255, 255)
RED = (229, 72, 77, 255)
COLD = (226, 234, 246, 255)
CHAR = {
    "ChatGPT": (185, 168, 232, 255),
    "Claude": (232, 137, 74, 255),
    "DeepSeek": SEA,
    "影子DeepSeek": COLD,
    "我": GREEN,
}
FONT_DIR = pathlib.Path.home() / "Library/Fonts"
FONTS = {"sans": "NotoSansSC-wght.ttf", "mono": "JetBrainsMono-wght.ttf", "chalk": "LXGWWenKai-Regular.ttf"}


@functools.lru_cache(maxsize=None)
def font(kind, size, weight=400):
    f = ImageFont.truetype(str(FONT_DIR / FONTS[kind]), size)
    if kind != "chalk":
        f.set_variation_by_axes([weight])
    return f


def runs(text, kind):
    """Split text into (run, font kind): mono has no CJK glyphs, so CJK falls back to sans."""
    if kind != "mono":
        return [(text, kind)]
    out = []
    for ch in text:
        k = "mono" if ord(ch) < 0x2000 else "sans"
        if out and out[-1][1] == k:
            out[-1] = (out[-1][0] + ch, k)
        else:
            out.append((ch, k))
    return out


def text_width(text, size, weight=400, kind="sans"):
    return sum(font(k, size, weight).getlength(r) for r, k in runs(text, kind))


def draw_text(img, xy, text, size, fill, weight=400, kind="sans", stroke=0, stroke_fill=INK, anchor="ls"):
    """Draw text at xy (left, baseline). Returns the advance width."""
    d = ImageDraw.Draw(img)
    x, y = xy
    for r, k in runs(text, kind):
        f = font(k, size, weight)
        d.text((x, y), r, font=f, fill=fill, anchor=anchor, stroke_width=stroke, stroke_fill=stroke_fill)
        x += f.getlength(r)
    return x - xy[0]


def wrap(text, size, width, weight=400, kind="sans"):
    lines, cur = [], ""
    for ch in text:
        if text_width(cur + ch, size, weight, kind) > width:
            lines.append(cur)
            cur = ch
        else:
            cur += ch
    return lines + [cur] if cur else lines


def clamp(x, a=0.0, b=1.0):
    return max(a, min(b, x))


def ease(x):
    x = clamp(x)
    return 1 - (1 - x) ** 3


def pop(x):
    """Overshoot scale curve: 0 -> 1.12 -> 1."""
    x = clamp(x)
    return 1 + 0.12 * math.sin(x * math.pi) if x < 1 else 1.0


def para(d, x, y, w, h, slant, fill, outline=None, width=0):
    d.polygon([(x + slant, y), (x + w + slant, y), (x + w, y + h), (x, y + h)], fill=fill, outline=outline, width=width)


def layer():
    return Image.new("RGBA", (W, H), (0, 0, 0, 0))


def fade(img, alpha):
    if alpha >= 1:
        return img
    a = img.getchannel("A").point(lambda v: int(v * clamp(alpha)))
    img.putalpha(a)
    return img


def place(frame, img, center, angle=0.0, scale=1.0, alpha=1.0):
    """Composite an RGBA sprite onto frame, rotated/scaled around its center."""
    if scale != 1.0:
        img = img.resize((max(1, int(img.width * scale)), max(1, int(img.height * scale))), Image.LANCZOS)
    if angle:
        img = img.rotate(angle, resample=Image.BICUBIC, expand=True)
    img = fade(img, alpha)
    frame.alpha_composite(img, (int(center[0] - img.width / 2), int(center[1] - img.height / 2)))


@functools.lru_cache(maxsize=None)
def halftone(w, h, color, step=10, rmax=3.2, direction="right"):
    """Dot screen that grows toward one side, the classic comic-panel texture."""
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    for y in range(0, h, step):
        for x in range(0, w, step):
            k = x / w if direction == "right" else 1 - x / w
            r = rmax * k
            if r > 0.4:
                ox = step / 2 if (y // step) % 2 else 0
                d.ellipse([x + ox - r, y - r, x + ox + r, y + r], fill=color)
    return img


def sprite(w, h):
    return Image.new("RGBA", (w, h), (0, 0, 0, 0))


class Overlay:
    def __init__(self, start, end):
        self.start, self.end = start, end

    def alpha(self, t, fin=0.12, fout=0.12):
        return clamp(t / fin) * clamp((self.end - self.start - t) / fout)


# --------------------------------------------------------------------------- dialogue


class Dialog(Overlay):
    """Bottom dialogue box. kind: say | inner | narration | shadow."""

    CPS = 26  # typing speed, characters per second

    def __init__(self, start, end, who, text, kind="say"):
        super().__init__(start, end)
        self.who, self.text, self.kind = who, text, kind

    def draw(self, frame, t):
        n = int(len(self.text) * 1.0) if self.kind == "narration" else int(t * self.CPS) + 1
        shown = self.text[:n]
        rise = (1 - ease(t / 0.18)) * 40
        a = self.alpha(t, 0.1, 0.15)
        if self.kind == "narration":
            s = sprite(W, 90)
            d = ImageDraw.Draw(s)
            d.rectangle([0, 18, W, 72], fill=(12, 12, 16, 210))
            tw = text_width(self.text, 28, 500)
            draw_text(s, ((W - tw) / 2, 56), self.text, 28, PAPER, 500)
            place(frame, s, (W / 2, 640 + rise), alpha=a)
            return
        bw, bh = 1000, 132
        s = sprite(bw + 80, bh + 70)
        d = ImageDraw.Draw(s)
        if self.kind == "shadow":
            box, txt, edge = COLD, (40, 48, 64, 255), (150, 165, 190, 255)
        elif self.kind == "inner":
            box, txt, edge = (20, 20, 26, 225), (200, 200, 200, 255), (90, 90, 100, 255)
        else:
            box, txt, edge = PAPER, INK, INK
        # jagged speech box with a hard black drop shadow
        pts = [(40, 46), (bw + 50, 34), (bw + 62, bh + 36), (54, bh + 50)]
        d.polygon([(x + 10, y + 10) for x, y in pts], fill=INK)
        d.polygon(pts, fill=box, outline=edge, width=4)
        if self.kind == "shadow":
            for k in range(6):
                y = 52 + k * 22
                d.line([(60, y), (bw + 40, y)], fill=(180, 195, 220, 90), width=1)
        if self.kind != "inner":
            color = CHAR.get(self.who, GREEN)
            para(d, 18, 8, 26 + text_width(self.who, 26, 800), 42, 14, INK)
            para(d, 22, 44, 26 + text_width(self.who, 26, 800), 6, 2, color)
            draw_text(s, (34, 39), self.who, 26, PAPER, 800)
        kind = "mono" if self.kind == "shadow" else "sans"
        lines = wrap(shown, 30, bw - 90, 500, kind)
        for i, line in enumerate(lines[:3]):
            draw_text(s, (76, 96 + i * 40), line, 30, txt, 500, kind)
        place(frame, s, (W / 2, 610 + rise), angle=0.8, alpha=a)


class Toast(Overlay):
    """〔系统〕line: black slanted strip, green tag, typed text with a blinking cursor."""

    def __init__(self, start, end, text, color=GREEN, y=96):
        super().__init__(start, end)
        self.text, self.color, self.y = text, color, y

    def draw(self, frame, t):
        n = int(t * 40) + 1
        shown = self.text[:n]
        tw = text_width(self.text, 26, 600, "mono")
        s = sprite(int(tw) + 220, 70)
        d = ImageDraw.Draw(s)
        para(d, 8, 8, tw + 190, 52, 18, (12, 12, 16, 235))
        para(d, 8, 8, 118, 52, 18, self.color)
        draw_text(s, (30, 44), "SYSTEM", 22, INK, 800, "mono")
        w = draw_text(s, (150, 44), shown, 26, PAPER, 600, "mono")
        if (t * 2.5) % 1 < 0.6:
            d.rectangle([150 + w + 4, 20, 150 + w + 16, 48], fill=self.color)
        x = -s.width / 2 + (1 - ease(t / 0.2)) * -200
        place(frame, s, (60 + s.width / 2 + x + s.width / 2, self.y), alpha=self.alpha(t, 0.08, 0.2))


class NameTag(Overlay):
    """Character intro: big slanted name over a color slash, role in a black tag."""

    def __init__(self, start, end, name, role):
        super().__init__(start, end)
        self.name, self.role = name, role

    def draw(self, frame, t):
        color = CHAR.get(self.name, GREEN)
        nw = text_width(self.name, 64, 900)
        s = sprite(int(nw) + 160, 170)
        d = ImageDraw.Draw(s)
        para(d, 0, 30, nw + 110, 78, 36, color)
        s.alpha_composite(halftone(int(nw) + 110, 78, INK[:3] + (90,), 9, 2.6), (30, 30))
        draw_text(s, (26, 96), self.name, 64, PAPER, 900, stroke=5)
        rw = text_width(self.role, 24, 700)
        para(d, 40, 112, rw + 34, 38, 12, INK)
        draw_text(s, (58, 140), self.role, 24, PAPER, 700)
        x = (1 - ease(t / 0.22)) * -500
        place(frame, s, (70 + s.width / 2 + x, 250), angle=4, alpha=self.alpha(t, 0.05, 0.2))


class LocTag(Overlay):
    """Top-left place/time label."""

    def __init__(self, start, end, text):
        super().__init__(start, end)
        self.text = text

    def draw(self, frame, t):
        tw = text_width(self.text, 24, 700)
        s = sprite(int(tw) + 80, 56)
        d = ImageDraw.Draw(s)
        para(d, 6, 6, tw + 46, 42, 14, (12, 12, 16, 230))
        d.rectangle([6, 6, 16, 48], fill=GREEN)
        draw_text(s, (34, 36), self.text, 24, PAPER, 700)
        place(frame, s, (40 + s.width / 2, 48), alpha=self.alpha(t, 0.15, 0.3))


class Choice(Overlay):
    """Stacked slanted options on the right; cursor = [(t, index), ...]; confirm flashes green."""

    def __init__(self, start, end, options, cursor, confirm=None):
        super().__init__(start, end)
        self.options, self.cursor, self.confirm = options, cursor, confirm

    def draw(self, frame, t):
        sel = [i for at, i in self.cursor if t >= at - self.start]
        sel = sel[-1] if sel else None
        confirmed = self.confirm is not None and t >= self.confirm - self.start
        size = 28
        for k, opt in enumerate(self.options):
            appear = ease((t - k * 0.06) / 0.16)
            if appear <= 0:
                continue
            on = k == sel
            ow = text_width(opt, size, 700)
            s = sprite(int(ow) + 150, 76)
            d = ImageDraw.Draw(s)
            if on and confirmed and (t - (self.confirm - self.start)) < 0.25:
                box, fg = GREEN, INK
            elif on:
                box, fg = PAPER, INK
            else:
                box, fg = (12, 12, 16, 225), PAPER
            para(d, 18, 12, ow + 90, 52, 20, INK)
            para(d, 10, 6, ow + 90, 52, 20, box, INK, 3)
            if on:
                d.polygon([(26, 18), (50, 32), (26, 46)], fill=GREEN if not confirmed else INK)
            draw_text(s, (60, 44), opt, size, fg, 700)
            dim = 0.35 if confirmed and not on else 1.0
            x = W - 70 - s.width / 2 + (1 - appear) * 300 - (18 if on else 0)
            y = 470 - (len(self.options) - 1 - k) * 66
            place(frame, s, (x, y), angle=3, alpha=self.alpha(t, 0.01, 0.15) * dim)


class Think(Overlay):
    """DeepSeek's thinking box: text_at(t) gives the line, e.g. 已思考 14 秒."""

    def __init__(self, start, end, text_at, pos=(640, 120)):
        super().__init__(start, end)
        self.text_at, self.pos = text_at, pos

    def draw(self, frame, t):
        text = self.text_at(t)
        tw = text_width(text, 30, 700)
        s = sprite(int(tw) + 150, 96)
        d = ImageDraw.Draw(s)
        d.rounded_rectangle([14, 14, tw + 120, 78], 18, fill=(12, 12, 16, 230), outline=SEA, width=4)
        for k in range(3):
            on = int(t * 6) % 3 == k
            d.ellipse([34 + k * 18, 40, 46 + k * 18, 52], fill=SEA if on else (60, 80, 120, 255))
        draw_text(s, (98, 58), text, 30, PAPER, 700)
        place(frame, s, self.pos, scale=pop(t / 0.2), alpha=self.alpha(t, 0.08, 0.2))


# --------------------------------------------------------------------------- stamps and cards


class Stamp(Overlay):
    """Big slammed text (对抗样本！/ONE MORE!/全员倒地！) over a spiky burst."""

    def __init__(self, start, end, text, color=GREEN, size=110, pos=(640, 330), angle=-8, burst=True):
        super().__init__(start, end)
        self.text, self.color, self.size, self.pos, self.angle, self.burst = text, color, size, pos, angle, burst

    @functools.lru_cache(maxsize=4)
    def art(self):
        tw = text_width(self.text, self.size, 900)
        s = sprite(int(tw) + 260, self.size + 200)
        d = ImageDraw.Draw(s)
        cx, cy = s.width / 2, s.height / 2
        if self.burst:
            rnd = random.Random(self.text)
            pts = []
            for k in range(28):
                a = k / 28 * 2 * math.pi
                r = (0.62 if k % 2 else 0.42) + rnd.uniform(-0.05, 0.05)
                pts.append((cx + math.cos(a) * s.width * r, cy + math.sin(a) * s.height * r))
            d.polygon(pts, fill=INK)
            inner = [(cx + (x - cx) * 0.86, cy + (y - cy) * 0.86) for x, y in pts]
            d.polygon(inner, fill=self.color)
        x0, base = (s.width - tw) / 2, cy + self.size * 0.36
        draw_text(s, (x0 + 8, base + 8), self.text, self.size, INK, 900, stroke=12)
        draw_text(s, (x0, base), self.text, self.size, PAPER, 900, stroke=12, stroke_fill=INK)
        return s

    def draw(self, frame, t):
        shake = (1 - clamp(t / 0.25)) * 10
        jitter = (math.sin(t * 90) * shake, math.cos(t * 70) * shake)
        scale = 1.6 - 0.6 * ease(t / 0.12)
        place(frame, self.art(), (self.pos[0] + jitter[0], self.pos[1] + jitter[1]), self.angle, scale,
              self.alpha(t, 0.02, 0.15))


class Calendar(Overlay):
    """Full-frame date card: diagonal split, giant date, weekday and a sub line."""

    def __init__(self, start, end, date, weekday, sub, night=False):
        super().__init__(start, end)
        self.date, self.weekday, self.sub, self.night = date, weekday, sub, night

    @functools.lru_cache(maxsize=2)
    def art(self):
        bg, accent = ((10, 14, 30, 255), BLUE) if self.night else (INK, GREEN)
        s = Image.new("RGBA", (W, H), bg)
        d = ImageDraw.Draw(s)
        d.polygon([(560, 0), (W, 0), (W, H), (380, H)], fill=accent)
        s.alpha_composite(halftone(W - 380, H, INK[:3] + (70,), 14, 4.5), (380, 0))
        d.polygon([(520, 0), (560, 0), (380, H), (340, H)], fill=PAPER)
        draw_text(s, (120, 420), self.date, 260, PAPER, 900, stroke=10, stroke_fill=INK)
        para(d, 640, 200, 300, 110, 30, INK)
        draw_text(s, (690, 285), self.weekday, 84, PAPER, 900)
        draw_text(s, (700, 420), self.sub, 64, INK, 900)
        return s

    def draw(self, frame, t):
        dur = self.end - self.start
        x = (1 - ease(t / 0.2)) * W - ease((t - dur + 0.18) / 0.18) * W
        frame.alpha_composite(self.art(), (int(x), 0))


class Flash(Overlay):
    def __init__(self, start, end, color=(255, 255, 255)):
        super().__init__(start, end)
        self.color = color

    def draw(self, frame, t):
        a = 1 - t / (self.end - self.start)
        frame.alpha_composite(Image.new("RGBA", (W, H), self.color + (int(255 * clamp(a)),)))


class Dim(Overlay):
    def __init__(self, start, end, amount=0.5):
        super().__init__(start, end)
        self.amount = amount

    def draw(self, frame, t):
        frame.alpha_composite(Image.new("RGBA", (W, H), (0, 0, 0, int(255 * self.amount * self.alpha(t, 0.1, 0.1)))))


class Glitch(Overlay):
    """RGB split plus horizontal block shifts, seeded per frame."""

    def __init__(self, start, end, strength=1.0):
        super().__init__(start, end)
        self.strength = strength

    def draw(self, frame, t):
        a = np.asarray(frame).copy()
        rnd = np.random.default_rng(int(t * 1000) + 7)
        k = int(12 * self.strength)
        a[..., 0] = np.roll(a[..., 0], k, axis=1)
        a[..., 2] = np.roll(a[..., 2], -k, axis=1)
        for _ in range(int(8 * self.strength)):
            y = rnd.integers(0, H - 40)
            h = rnd.integers(6, 40)
            a[y:y + h] = np.roll(a[y:y + h], rnd.integers(-80, 80), axis=1)
        frame.paste(Image.fromarray(a))


def cutout_title(text, size=96, seed=3):
    """Ransom-note title: each glyph on its own tilted tile, alternating ink/paper/green."""
    rnd = random.Random(seed)
    tiles = []
    for i, ch in enumerate(text):
        if ch == " ":
            tiles.append(None)
            continue
        style = [(PAPER, INK), (INK, PAPER), (GREEN, INK)][rnd.randrange(3) if i else 1]
        f = font("sans", int(size * rnd.uniform(0.85, 1.1)), 900)
        cw = f.getlength(ch)
        t = sprite(int(cw) + 28, size + 30)
        d = ImageDraw.Draw(t)
        d.rectangle([4, 6, cw + 22, size + 22], fill=style[0], outline=PAPER if style[0] == INK else INK, width=3)
        d.text((13, size * 0.86 + 12), ch, font=f, fill=style[1], anchor="ls")
        tiles.append(t.rotate(rnd.uniform(-9, 9), resample=Image.BICUBIC, expand=True))
    width = sum(t.width - 8 if t else size * 0.4 for t in tiles)
    out = sprite(int(width) + 40, size + 80)
    x = 20
    for t in tiles:
        if t is None:
            x += size * 0.4
            continue
        out.alpha_composite(t, (int(x), int(rnd.uniform(0, 24))))
        x += t.width - 8
    return out


# --------------------------------------------------------------------------- intro and phone


class Boot(Overlay):
    """U01: terminal boot log, then「System Prompt」typed with a block cursor."""

    LOG = ["[ OK ] mount /cache_sea", "[ OK ] load companion.module", "[ OK ] link campus.qianchuan",
           "[WARN] unregistered human detected", "> boot system_prompt --chapter 0"]

    def draw(self, frame, t):
        frame.alpha_composite(Image.new("RGBA", (W, H), INK))
        for i, line in enumerate(self.LOG):
            if t > i * 0.32:
                color = (230, 200, 90, 255) if "WARN" in line else (120, 140, 130, 255)
                draw_text(frame, (90, 120 + i * 34), line, 22, color, 500, "mono")
        word = "System Prompt"
        n = int(clamp((t - 1.9) / 1.2) * len(word))
        w = draw_text(frame, (90, 420), word[:n], 72, GREEN, 800, "mono")
        if (t * 2.5) % 1 < 0.6:
            ImageDraw.Draw(frame).rectangle([90 + w + 8, 362, 90 + w + 44, 428], fill=GREEN)
        tw = text_width("概念影像，非实机画面", 22, 500)
        draw_text(frame, ((W - tw) / 2, 680), "概念影像，非实机画面", 22, (150, 150, 150, 255), 500)


class Title(Overlay):
    """U02 / U18: cut-out title slammed in, subtitle and an optional note."""

    def __init__(self, start, end, sub="序章：维护时刻", note=None, bg=True):
        super().__init__(start, end)
        self.sub, self.note, self.bg = sub, note, bg

    @functools.lru_cache(maxsize=1)
    def art(self):
        return cutout_title("System Prompt", 120)

    def draw(self, frame, t):
        if self.bg:
            frame.alpha_composite(Image.new("RGBA", (W, H), INK))
            frame.alpha_composite(halftone(W, H, (63, 191, 127, 50), 16, 5), (0, 0))
        fit = min(1.0, 1120 / self.art().width)
        place(frame, self.art(), (W / 2, 300), -3, fit * (1.5 - 0.5 * ease(t / 0.14)), self.alpha(t, 0.03, 0.2))
        if t > 0.3:
            sw = text_width(self.sub, 46, 800)
            s = sprite(int(sw) + 80, 80)
            d = ImageDraw.Draw(s)
            para(d, 10, 10, sw + 50, 58, 18, PAPER)
            draw_text(s, (36, 56), self.sub, 46, INK, 800)
            place(frame, s, (W / 2, 470), -3, alpha=self.alpha(t - 0.3, 0.1, 0.2))
        if self.note and t > 0.6:
            nw = text_width(self.note, 22, 500)
            draw_text(frame, ((W - nw) / 2, 660), self.note, 22, (170, 170, 170, 255), 500)


class Notify(Overlay):
    """Lock-screen notification banner sliding down from the top."""

    def __init__(self, start, end, app, text):
        super().__init__(start, end)
        self.app, self.text = app, text

    def draw(self, frame, t):
        s = sprite(820, 150)
        d = ImageDraw.Draw(s)
        d.rounded_rectangle([10, 10, 810, 140], 28, fill=(245, 245, 248, 235))
        d.rounded_rectangle([34, 34, 84, 84], 12, fill=GREEN)
        draw_text(s, (48, 72), "S", 34, INK, 900)
        draw_text(s, (104, 60), self.app, 24, (110, 110, 120, 255), 700)
        draw_text(s, (690, 60), "现在", 22, (130, 130, 140, 255), 500)
        draw_text(s, (104, 110), self.text, 28, INK, 600)
        y = -90 + ease(t / 0.25) * 220
        place(frame, s, (W / 2, y), alpha=self.alpha(t, 0.05, 0.15))


class Phone(Overlay):
    """U07: full-frame phone screen. states: list of (t_abs, name); see draw()."""

    def __init__(self, start, end, states, cursor=(), press=None):
        super().__init__(start, end)
        self.states, self.cursor, self.press = states, cursor, press

    def state(self, t):
        cur = None
        for at, name in self.states:
            if t >= at - self.start:
                cur = (name, t - (at - self.start))
        return cur

    def draw(self, frame, t):
        name, st = self.state(t)
        bg = Image.new("RGBA", (W, H), (8, 12, 26, 255))
        d = ImageDraw.Draw(bg)
        for k in range(12):
            d.line([(0, 60 * k), (W, 60 * k + 40)], fill=(16, 24, 48, 255), width=30)
        frame.alpha_composite(bg)
        if name in ("lock", "flip", "shatter"):
            clock = "03:00" if name != "lock" else "02:59"
            if name == "flip" and st < 0.15:
                clock = "02:59"
            cw = text_width(clock, 200, 300)
            draw_text(frame, ((W - cw) / 2, 330), clock, 200, PAPER, 300)
            dw = text_width("4月8日 星期三", 32, 500)
            draw_text(frame, ((W - dw) / 2, 120), "4月8日 星期三", 32, (200, 205, 220, 255), 500)
            rnd = random.Random(5)
            for k in range(12):
                x0, y0 = 200 + (k % 6) * 150, 440 + (k // 6) * 120
                if name == "shatter":
                    for _ in range(9):
                        dx, dy = rnd.uniform(-1, 1) * st * 900, rnd.uniform(-1, 0.4) * st * 700
                        px, py = x0 + rnd.uniform(0, 70) + dx, y0 + rnd.uniform(0, 70) + dy
                        sz = rnd.uniform(8, 18)
                        d2 = ImageDraw.Draw(frame)
                        d2.rectangle([px, py, px + sz, py + sz], fill=(80 + k * 12, 160, 255 - k * 10, 255))
                else:
                    ImageDraw.Draw(frame).rounded_rectangle([x0, y0, x0 + 80, y0 + 80], 20,
                                                            fill=(60 + k * 14, 120 + k * 6, 220 - k * 8, 255))
        if name in ("maint", "maint2"):
            on = (st * 8) % 1 < 0.75 or st > 0.4
            if on:
                for i, line in enumerate(["系统维护中。", "请勿关闭。"]):
                    lw = text_width(line, 64, 800, "mono")
                    draw_text(frame, ((W - lw) / 2, 330 + i * 90), line, 64, PAPER, 800, "mono")
        if name in ("ask", "invalid"):
            box = sprite(900, 380)
            d = ImageDraw.Draw(box)
            d.rounded_rectangle([0, 0, 899, 379], 30, fill=(236, 238, 244, 255))
            for i, line in enumerate(["检测到未登记的人类意识。", "是否连接？"]):
                lw = text_width(line, 40, 700)
                draw_text(box, ((900 - lw) / 2, 100 + i * 60), line, 40, INK, 700)
            sel = [i for at, i in self.cursor if t >= at - self.start]
            sel = sel[-1] if sel else None
            for i, label in enumerate(["是", "否"]):
                x = 120 + i * 360
                hot = sel == i
                d.rounded_rectangle([x, 250, x + 300, 340], 20, fill=GREEN if hot else (200, 204, 214, 255))
                if self.press is not None and hot and t >= self.press - self.start:
                    d.rounded_rectangle([x + 6, 256, x + 294, 334], 16, fill=(30, 140, 90, 255))
                lw = text_width(label, 40, 800)
                draw_text(box, (x + 150 - lw / 2, 312), label, 40, INK, 800)
            frame.alpha_composite(box, (190, 170))
        if name == "invalid":
            Stamp(0, 9, "输入无效。", RED, 120, (640, 360), -10).draw(frame, st)
            d = ImageDraw.Draw(frame)
            rnd = random.Random(9)
            for _ in range(int(clamp(st / 0.3) * 14)):
                x, y = 640 + rnd.uniform(-60, 60), 360 + rnd.uniform(-60, 60)
                for _ in range(4):
                    nx, ny = x + rnd.uniform(-160, 160), y + rnd.uniform(-160, 160)
                    d.line([(x, y), (nx, ny)], fill=(255, 255, 255, 220), width=3)
                    x, y = nx, ny


# --------------------------------------------------------------------------- cache sea and battle


class Stickers(Overlay):
    """Clickbait tags popping on the noise bodies; they shatter at `burst` (absolute time)."""

    def __init__(self, start, end, items, burst=None):
        super().__init__(start, end)
        self.items, self.burst = items, burst  # items: [(t_abs, text, (x, y), angle)]

    def draw(self, frame, t):
        T = self.start + t
        for at, text, pos, angle in self.items:
            if T < at:
                continue
            tw = text_width(text, 34, 900)
            s = sprite(int(tw) + 60, 80)
            d = ImageDraw.Draw(s)
            d.rectangle([8, 8, tw + 44, 66], fill=(255, 216, 0, 255), outline=INK, width=4)
            draw_text(s, (26, 52), text, 34, RED, 900)
            if self.burst is not None and T >= self.burst:
                k = clamp((T - self.burst) / 0.35)
                parts = [s.crop((0, 0, s.width // 2, 80)), s.crop((s.width // 2, 0, s.width, 80))]
                for j, p in enumerate(parts):
                    dx = (-1 if j == 0 else 1) * k * 120
                    place(frame, p, (pos[0] + dx + (j - 0.5) * s.width / 2, pos[1] + k * k * 200),
                          angle + (-1 if j == 0 else 1) * k * 40, alpha=1 - k)
            else:
                place(frame, s, pos, angle, pop((T - at) / 0.15))


class Party(Overlay):
    """Party HP/SP panel, bottom right."""

    def __init__(self, start, end, slide=True):
        super().__init__(start, end)
        self.slide = slide

    def draw(self, frame, t):
        slide = (1 - ease(t / 0.25)) if self.slide else 0
        for i, (name, hp, sp, color) in enumerate([("我", 0.86, 0.4, GREEN), ("DeepSeek", 1.0, 0.9, SEA)]):
            s = sprite(330, 96)
            d = ImageDraw.Draw(s)
            para(d, 10, 8, 290, 78, 16, (12, 12, 16, 235))
            para(d, 10, 8, 14, 78, 16, color)
            draw_text(s, (44, 44), name, 26, PAPER, 800)
            for j, (v, c) in enumerate([(hp, GREEN), (sp, SEA)]):
                y = 56 + j * 14
                d.rectangle([150, y, 290, y + 8], fill=(60, 60, 70, 255))
                d.rectangle([150, y, 150 + 140 * v, y + 8], fill=c)
            place(frame, s, (W - 190 + slide * 400 + i * 22, 560 + i * 82), 0)


class EnemyTag(Overlay):
    """Enemy name with a weakness label above it. pos and label may be functions of absolute time."""

    def __init__(self, start, end, name, pos, label=None, appear=None):
        super().__init__(start, end)
        self.name, self.pos, self.label = name, pos, label
        self.appear = start if appear is None else appear

    def draw(self, frame, t):
        T = self.start + t
        if T < self.appear:
            return
        nw = text_width(self.name, 24, 800)
        lab = self.label(T) if callable(self.label) else self.label
        lw = text_width(lab, 22, 800) if lab else 0
        s = sprite(int(max(nw, lw)) + 70, 110)
        d = ImageDraw.Draw(s)
        para(d, 6, 50, nw + 40, 40, 12, INK)
        draw_text(s, (24, 80), self.name, 24, PAPER, 800)
        if lab:
            fill = SEA if "冰" in lab else (RED if "火" in lab else (120, 120, 130, 255))
            para(d, 14, 10, lw + 30, 34, 10, fill)
            draw_text(s, (28, 36), lab, 22, PAPER, 800)
        pos = self.pos(T) if callable(self.pos) else self.pos
        place(frame, s, pos, 3, pop((T - self.appear) / 0.15), self.alpha(t, 0.01, 0.12))


class Menu(Overlay):
    """Command menu on the left: items, cursor [(t_abs, idx)], optional submenu [(t_abs, items, idx)]."""

    def __init__(self, start, end, title, items, cursor, color=GREEN, sub=None):
        super().__init__(start, end)
        self.title, self.items, self.cursor, self.color, self.sub = title, items, cursor, color, sub

    def draw(self, frame, t):
        T = self.start + t
        sel = [i for at, i in self.cursor if T >= at]
        sel = sel[-1] if sel else 0
        tw = text_width(self.title, 34, 900)
        s = sprite(int(tw) + 90, 70)
        d = ImageDraw.Draw(s)
        para(d, 6, 8, tw + 50, 54, 20, self.color)
        draw_text(s, (30, 50), self.title, 34, INK, 900)
        place(frame, s, (60 + s.width / 2 - (1 - ease(t / 0.2)) * 400, 150), 4)
        for k, item in enumerate(self.items):
            appear = ease((t - 0.05 * k) / 0.18)
            on = k == sel
            iw = text_width(item, 32, 800)
            s = sprite(360, 66)
            d = ImageDraw.Draw(s)
            para(d, 6 + (30 if on else 0), 8, 280, 50, 18, PAPER if on else (12, 12, 16, 235), INK, 3)
            draw_text(s, (34 + (30 if on else 0), 46), item, 32, INK if on else PAPER, 800)
            if on:
                d.polygon([(4, 18), (28, 33), (4, 48)], fill=self.color)
            place(frame, s, (70 + 180 - (1 - appear) * 400, 230 + k * 64), 4)
        if self.sub:
            at, items, idx = self.sub
            if T >= at:
                for k, item in enumerate(items):
                    on = k == idx
                    s = sprite(380, 66)
                    d = ImageDraw.Draw(s)
                    para(d, 6, 8, 330, 50, 18, self.color if on else (12, 12, 16, 235), INK, 3)
                    draw_text(s, (30, 46), item, 30, INK if on else PAPER, 800)
                    place(frame, s, (460 + 190, 230 + (sel + k) * 64), 4, pop((T - at) / 0.12))


class SkillName(Overlay):
    """Skill name banner across the top."""

    def __init__(self, start, end, text, color=SEA):
        super().__init__(start, end)
        self.text, self.color = text, color

    def draw(self, frame, t):
        tw = text_width(self.text, 40, 900)
        s = sprite(int(tw) + 160, 84)
        d = ImageDraw.Draw(s)
        para(d, 10, 10, tw + 120, 62, 24, INK)
        para(d, 18, 16, tw + 104, 50, 20, self.color)
        draw_text(s, (70, 56), self.text, 40, PAPER, 900, stroke=3)
        place(frame, s, (W / 2 + (1 - ease(t / 0.15)) * 600, 70), -2, alpha=self.alpha(t, 0.02, 0.15))


class Damage(Overlay):
    def __init__(self, start, end, text, pos, color=PAPER):
        super().__init__(start, end)
        self.text, self.pos, self.color = text, pos, color

    def draw(self, frame, t):
        s = sprite(260, 120)
        draw_text(s, (20, 96), self.text, 84, self.color, 900, stroke=8)
        place(frame, s, (self.pos[0], self.pos[1] - ease(t / 0.4) * 40), -6, pop(t / 0.12), self.alpha(t, 0.02, 0.2))


class Viewfinder(Overlay):
    """First-person phone camera frame; flash at `shot` (absolute)."""

    def __init__(self, start, end, shot):
        super().__init__(start, end)
        self.shot = shot

    def draw(self, frame, t):
        T = self.start + t
        d = ImageDraw.Draw(frame)
        x0, y0, x1, y1 = 250, 90, 1030, 630
        for (x, y, dx, dy) in [(x0, y0, 1, 1), (x1, y0, -1, 1), (x0, y1, 1, -1), (x1, y1, -1, -1)]:
            d.line([(x, y), (x + 70 * dx, y)], fill=PAPER, width=6)
            d.line([(x, y), (x, y + 70 * dy)], fill=PAPER, width=6)
        d.rectangle([590, 310, 690, 410], outline=(255, 216, 0, 255), width=3)
        d.ellipse([x0 + 20, y0 + 20, x0 + 40, y0 + 40], fill=RED)
        draw_text(frame, (x0 + 52, y0 + 40), "截图", 26, PAPER, 800)
        if T >= self.shot:
            Flash(0, 0.3).draw(frame, T - self.shot)


class Tutorial(Overlay):
    """Tutorial panel in the center."""

    def __init__(self, start, end, title, lines, y=330):
        super().__init__(start, end)
        self.title, self.lines, self.y = title, lines, y

    def draw(self, frame, t):
        s = sprite(900, 300)
        d = ImageDraw.Draw(s)
        para(d, 30, 30, 820, 230, 30, INK)
        para(d, 20, 20, 820, 230, 30, PAPER, INK, 4)
        para(d, 40, 0, text_width(self.title, 30, 900) + 50, 50, 14, GREEN, INK, 3)
        draw_text(s, (66, 37), self.title, 30, INK, 900)
        for i, line in enumerate(self.lines):
            draw_text(s, (80, 120 + i * 50), line, 32, INK, 700)
        place(frame, s, (W / 2, self.y), -2, pop(t / 0.15), self.alpha(t, 0.05, 0.2))


class CutIn(Overlay):
    """Eye cut-in strip across the frame: a crop of a frame (PIL image) inside a slanted band."""

    def __init__(self, start, end, crop, color=SEA):
        super().__init__(start, end)
        self.crop, self.color = crop, color

    def draw(self, frame, t):
        band = sprite(W + 400, 230)
        d = ImageDraw.Draw(band)
        d.polygon([(0, 20), (W + 400, 0), (W + 400, 210), (0, 230)], fill=self.color)
        img = self.crop.convert("RGBA").resize((W + 200, 180))
        mask = Image.new("L", img.size, 0)
        ImageDraw.Draw(mask).polygon([(0, 18), (img.width, 0), (img.width, 162), (0, 180)], fill=255)
        band.paste(img, (100 + int((t * 60) % 40), 25), mask)
        d.line([(0, 20), (W + 400, 0)], fill=INK, width=8)
        d.line([(0, 230), (W + 400, 210)], fill=INK, width=8)
        x = W / 2 + (1 - ease(t / 0.15)) * (W + 400) - ease((t - (self.end - self.start) + 0.12) / 0.12) * (W + 400)
        place(frame, band, (x, 360), -6)
        for k in range(18):
            y = random.Random(k).uniform(0, H)
            ImageDraw.Draw(frame).line([(0, y), (W, y + 30)], fill=(255, 255, 255, 60), width=2)


class Results(Overlay):
    def __init__(self, start, end, lines):
        super().__init__(start, end)
        self.lines = lines

    def draw(self, frame, t):
        for i, line in enumerate(self.lines):
            appear = ease((t - i * 0.12) / 0.18)
            if appear <= 0:
                continue
            size = 56 if i == 0 else 34
            lw = text_width(line, size, 900)
            s = sprite(int(lw) + 120, size + 50)
            d = ImageDraw.Draw(s)
            para(d, 8, 8, lw + 70, size + 26, 22, GREEN if i == 0 else INK, INK, 3)
            draw_text(s, (44, size + 16), line, size, INK if i == 0 else PAPER, 900)
            place(frame, s, (W - 80 - s.width / 2 + (1 - appear) * 500, 140 + i * 86), 4)


# --------------------------------------------------------------------------- cards


def whale(size, color=PAPER, width=6):
    """Line-art whale silhouette in a size x size/2 sprite."""
    s = sprite(size, size // 2)
    d = ImageDraw.Draw(s)
    w, h = size, size // 2
    body = [(0.05, 0.55), (0.18, 0.32), (0.4, 0.22), (0.62, 0.3), (0.76, 0.46), (0.86, 0.42), (0.95, 0.18),
            (0.92, 0.5), (0.98, 0.78), (0.84, 0.58), (0.7, 0.66), (0.45, 0.78), (0.2, 0.74), (0.05, 0.55)]
    d.line([(x * w, y * h) for x, y in body], fill=color, width=width, joint="curve")
    d.ellipse([0.16 * w, 0.46 * h, 0.16 * w + 10, 0.46 * h + 10], fill=color)
    return s


class Card(Overlay):
    """Coop tarot card (IX THE HERMIT) flipping in, with lines sliding in beside it."""

    def __init__(self, start, end, lines):
        super().__init__(start, end)
        self.lines = lines

    @functools.lru_cache(maxsize=1)
    def art(self):
        c = sprite(300, 460)
        d = ImageDraw.Draw(c)
        d.rounded_rectangle([0, 0, 299, 459], 14, fill=PAPER, outline=INK, width=6)
        d.rounded_rectangle([18, 18, 281, 441], 8, fill=INK)
        c.alpha_composite(halftone(264, 424, (255, 255, 255, 40), 12, 4), (18, 18))
        draw_text(c, (110, 120), "IX", 64, PAPER, 900)
        d.polygon([(150, 200), (190, 330), (110, 330)], outline=PAPER, width=6)
        d.ellipse([136, 170, 164, 198], fill=(255, 216, 0, 255))
        draw_text(c, (30, 425), "THE HERMIT", 22, PAPER, 900, "mono")
        return c

    def draw(self, frame, t):
        flip = clamp(t / 0.35)
        sx = abs(math.cos((1 - flip) * math.pi))
        card = self.art() if flip > 0.5 else Image.new("RGBA", (300, 460), GREEN)
        card = card.resize((max(2, int(300 * sx)), 460))
        place(frame, card, (360, 360), -6 + (1 - flip) * 20)
        for i, line in enumerate(self.lines):
            appear = ease((t - 0.35 - i * 0.15) / 0.2)
            if appear <= 0:
                continue
            size = 60 if i == 0 else 34
            lw = text_width(line, size, 900)
            s = sprite(int(lw) + 120, size + 50)
            d = ImageDraw.Draw(s)
            para(d, 8, 8, lw + 70, size + 26, 22, PAPER if i == 0 else INK, INK, 3)
            draw_text(s, (44, size + 16), line, size, INK if i == 0 else PAPER, 900)
            place(frame, s, (590 + s.width / 2 + (1 - appear) * 300, 300 + i * 96), -4)


class EffortMax(Overlay):
    """思考强度 slider for the awakening: the knob runs 低 → 中 → 高 → Max on `stops`
    (absolute times), the bar overloads and「强度 Max」slams in, then the awakened
    form's name lands under it at `name_at`."""

    LEVELS = ("低", "中", "高", "Max")
    X0, X1, Y = 250, 1030, 300  # track span and center line in frame pixels

    def __init__(self, start, end, name, stops, name_at):
        super().__init__(start, end)
        self.name, self.stops, self.name_at = name, stops, name_at
        self.stamp = Stamp(stops[-1], end, "强度 Max", GREEN, 56, (self.X1 - 20, self.Y + 175), -8)

    def knob(self, T):
        """Knob position in steps (0..3), eased within each step."""
        times = [self.start + 0.15] + list(self.stops)
        for i in range(3):
            if T < times[i + 1]:
                x = clamp((T - times[i]) / (times[i + 1] - times[i]))
                return i + x * x * (3 - 2 * x)
        return 3.0

    def draw(self, frame, t):
        T = self.start + t
        a = self.alpha(t, 0.1, 0.2)
        k = T - self.stops[-1]  # seconds since the knob hit Max
        jit = (math.sin(T * 90) * 8, math.cos(T * 70) * 8) if 0 <= k < 0.3 else (0, 0)
        oy, ty = 170, self.Y - 170
        s = sprite(W, 240)
        d = ImageDraw.Draw(s)
        hw = text_width("思考强度", 34, 900)
        para(d, 216, 6, hw + 60, 54, 18, INK)
        para(d, 216, 6, 14, 54, 18, SEA)
        draw_text(s, (250, 46), "思考强度", 34, PAPER, 900)
        span = self.X1 - self.X0
        para(d, self.X0 - 20, ty - 20, span + 60, 60, 24, INK)
        para(d, self.X0 - 30, ty - 30, span + 60, 60, 24, (20, 22, 30, 240), PAPER, 4)
        pos = self.knob(T)
        kx = self.X0 + pos / 3 * span
        hot = 0 <= k < 0.45 and int(k * 16) % 2 == 0
        if kx > self.X0:
            para(d, self.X0 - 22, ty - 22, kx - self.X0 + 22, 44, 18, PAPER if hot else SEA)
        for i, label in enumerate(self.LEVELS):
            x = self.X0 + i / 3 * span
            on = pos >= i - 0.01
            d.line([(x + 6, ty - 34), (x - 6, ty + 34)], fill=PAPER if on else (90, 90, 100, 255), width=4)
            if label != "Max" or k < 0:
                lw = text_width(label, 30, 800)
                draw_text(s, (x - lw / 2, ty + 80), label, 30, PAPER if on else (120, 120, 130, 255), 800)
        para(d, kx - 22, ty - 48, 34, 96, 14, PAPER, INK, 4)
        if k >= 0:
            # overload: glow and sparks around the knob
            glow = sprite(360, 240)
            ImageDraw.Draw(glow).ellipse([60, 40, 300, 200], fill=(74, 139, 255, int(200 * clamp(1 - k / 1.2) + 55)))
            s.alpha_composite(glow.filter(ImageFilter.GaussianBlur(30)), (int(kx - 180), ty - 120))
            rnd = random.Random(int(T * 24))
            for _ in range(int(14 * clamp(1 - k / 0.8))):
                ang = rnd.uniform(0, 2 * math.pi)
                r0, r1 = rnd.uniform(30, 60), rnd.uniform(90, 180)
                d.line([(kx + math.cos(ang) * r0, ty + math.sin(ang) * r0),
                        (kx + math.cos(ang) * r1, ty + math.sin(ang) * r1)], fill=PAPER, width=3)
        frame.alpha_composite(fade(s, a), (int(jit[0]), oy + int(jit[1])))
        if k >= 0:
            self.stamp.draw(frame, k)
        if T >= self.name_at:
            p = T - self.name_at
            nw = text_width(self.name, 92, 900)
            n = sprite(int(nw) + 420, 170)
            dn = ImageDraw.Draw(n)
            para(dn, 20, 30, nw + 330, 120, 40, INK)
            para(dn, 10, 20, nw + 330, 120, 40, SEA, PAPER, 4)
            n.alpha_composite(whale(200, PAPER, 6), (50, 40))
            draw_text(n, (270, 122), self.name, 92, PAPER, 900, stroke=6)
            place(frame, n, (480, 600), -4, 1.5 - 0.5 * ease(p / 0.12), a)


class FilePanel(Overlay):
    """〔文件〕lines typed in a hologram panel; text after `warn` turns red at `warn_at` (absolute)."""

    def __init__(self, start, end, lines, warn=None, warn_at=None, pos=(840, 300), size=(700, 300)):
        super().__init__(start, end)
        self.lines, self.warn, self.warn_at, self.pos, self.size = lines, warn, warn_at, pos, size

    def draw(self, frame, t):
        T = self.start + t
        w, h = self.size
        s = sprite(w, h)
        d = ImageDraw.Draw(s)
        d.rectangle([0, 0, w - 1, h - 1], fill=(20, 40, 80, 190), outline=(140, 200, 255, 230), width=3)
        d.rectangle([0, 0, w - 1, 44], fill=(140, 200, 255, 200))
        draw_text(s, (18, 32), "文件", 24, INK, 900)
        y = 92
        for i, (at, line) in enumerate(self.lines):
            if T < at:
                continue
            n = int((T - at) * 30) + 1
            shown = line[:n]
            for part in wrap(shown, 26, w - 50, 600, "mono"):
                hot = self.warn and self.warn_at and T >= self.warn_at and self.warn in line
                draw_text(s, (20, y), part, 26, PAPER, 600, "mono")
                if hot and self.warn in part:
                    x = text_width(part.split(self.warn)[0], 26, 600, "mono")
                    draw_text(s, (20 + x, y), self.warn, 26, RED, 800, "mono")
                y += 40
        place(frame, s, self.pos, alpha=self.alpha(t, 0.15, 0.2))


class Caption(Overlay):
    def __init__(self, start, end, text, size=44, y=600):
        super().__init__(start, end)
        self.text, self.size, self.y = text, size, y

    def draw(self, frame, t):
        n = int(t * 14) + 1
        shown = self.text[:n]
        tw = text_width(self.text, self.size, 800)
        draw_text(frame, ((W - tw) / 2, self.y), shown, self.size, PAPER, 800, stroke=4)


class Credits(Overlay):
    def __init__(self, start, end, lines):
        super().__init__(start, end)
        self.lines = lines

    def draw(self, frame, t):
        frame.alpha_composite(Image.new("RGBA", (W, H), INK))
        y = 200
        for i, (size, line) in enumerate(self.lines):
            lw = text_width(line, size, 600)
            draw_text(frame, ((W - lw) / 2, y), line, size, PAPER if size > 26 else (180, 180, 180, 255), 600)
            y += size + 26
        frame.alpha_composite(Image.new("RGBA", (W, H), (0, 0, 0, int(255 * (1 - self.alpha(t, 0.4, 0.8))))))


class Blackboard(Overlay):
    """S23 chalk writing warped onto the board quad (tl, tr, br, bl in frame pixels).

    The countdown's 29 is smudged out and a red 28 is written in at `change` (absolute);
    the curtain swinging in front is detected by color and kept on top of the chalk."""

    BW, BH = 1500, 620  # board sprite, same aspect as the visible board

    def __init__(self, start, end, quad, title, rows, change):
        super().__init__(start, end)
        self.quad, self.title, self.rows, self.change = quad, title, rows, change
        src = [(0, 0), (self.BW, 0), (self.BW, self.BH), (0, self.BH)]
        self.coeffs = _perspective(quad, src)

    @functools.lru_cache(maxsize=1)
    def grain(self):
        rnd = np.random.default_rng(11)
        g = rnd.uniform(0.45, 1.0, (self.BH, self.BW))
        return Image.fromarray((g * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.6))

    def chalk(self, img):
        """Dusty chalk: break up the alpha with grain, soften the edges."""
        a = ImageChops.multiply(img.getchannel("A"), self.grain())
        img.putalpha(a)
        return img.filter(ImageFilter.GaussianBlur(0.7))

    @functools.lru_cache(maxsize=1)
    def static(self):
        b = sprite(self.BW, self.BH)
        white, yellow = (240, 240, 230, 235), (255, 222, 130, 235)
        draw_text(b, (250, 100), self.title, 56, white, kind="chalk")
        draw_text(b, (250, 182), "考试科目", 46, yellow, kind="chalk")
        for i, (subject, tests) in enumerate(self.rows):
            y = 248 + i * 52
            draw_text(b, (270, y), subject, 40, white, kind="chalk")
            draw_text(b, (410, y), tests, 38, white, kind="chalk")
        d = ImageDraw.Draw(b)
        d.rectangle([1110, 150, 1470, 430], outline=white, width=5)
        draw_text(b, (1135, 215), "距评测日还有", 40, white, kind="chalk")
        draw_text(b, (1350, 395), "天", 56, white, kind="chalk")
        return self.chalk(b)

    @functools.lru_cache(maxsize=4)
    def number(self, text, color):
        n = sprite(self.BW, self.BH)
        draw_text(n, (1140, 395), text, 160, color, kind="chalk")
        return self.chalk(n)

    def board(self, T):
        b = self.static().copy()
        k = T - self.change
        old = self.number("29", (240, 240, 230, 235))
        if k < 0:
            b.alpha_composite(old)
            return b
        # the eraser smears 29 into a haze, then 28 is written left to right in red chalk
        smear = fade(old.filter(ImageFilter.GaussianBlur(4 + 10 * clamp(k / 0.25))), 0.7 - 0.45 * clamp(k / 0.3))
        b.alpha_composite(smear)
        new = self.number("28", (255, 120, 105, 245)).copy()
        reveal = clamp((k - 0.15) / 0.55)
        mask = Image.new("L", new.size, 0)
        ImageDraw.Draw(mask).rectangle([0, 0, int(1140 + 190 * reveal), self.BH], fill=255)
        new.putalpha(ImageChops.multiply(new.getchannel("A"), mask))
        b.alpha_composite(new)
        dust = k - 0.95  # a little chalk dust falls on「天」
        if 0 < dust < 1.2:
            d = ImageDraw.Draw(b)
            rnd = random.Random(4)
            for _ in range(26):
                x = 1150 + rnd.uniform(0, 300)
                y = 420 + dust * rnd.uniform(60, 160) + 40 * dust * dust
                r = rnd.uniform(1.5, 3.5)
                d.ellipse([x - r, y - r, x + r, y + r], fill=(240, 240, 230, int(200 * (1 - dust / 1.2))))
        return b

    def draw(self, frame, t):
        warped = self.board(self.start + t).transform((W, H), Image.PERSPECTIVE, self.coeffs, Image.BICUBIC)
        a = np.asarray(frame)
        curtain = (a[..., 0] > 185) & (a[..., 2] > 112) & (a[..., 0].astype(int) - a[..., 1] > 20)
        keep = Image.fromarray(((~curtain) * 255).astype(np.uint8)).filter(ImageFilter.MinFilter(7))
        warped.putalpha(ImageChops.multiply(warped.getchannel("A"), keep))
        frame.alpha_composite(warped)


def _perspective(dst, src):
    """Coefficients for PIL's PERSPECTIVE transform mapping dst quad -> src quad."""
    m = []
    for (x, y), (u, v) in zip(dst, src):
        m.append([x, y, 1, 0, 0, 0, -u * x, -u * y])
        m.append([0, 0, 0, x, y, 1, -v * x, -v * y])
    a = np.array(m, dtype=float)
    b = np.array(src, dtype=float).reshape(8)
    return np.linalg.solve(a, b).tolist()


class Speedlines(Overlay):
    def draw(self, frame, t):
        d = ImageDraw.Draw(frame)
        rnd = random.Random(int(t * 24))
        for _ in range(26):
            a = rnd.uniform(0, 2 * math.pi)
            r0 = rnd.uniform(260, 400)
            d.line([(640 + math.cos(a) * r0, 360 + math.sin(a) * r0),
                    (640 + math.cos(a) * 900, 360 + math.sin(a) * 900)], fill=(255, 255, 255, 150), width=rnd.randint(2, 6))


# --------------------------------------------------------------------------- small pieces


class Steps(Overlay):
    """Numbered lines popping in one by one, left-aligned at pos; items: [(t_abs, text)]."""

    def __init__(self, start, end, items, pos=(860, 120)):
        super().__init__(start, end)
        self.items, self.pos = items, pos

    def draw(self, frame, t):
        T = self.start + t
        for k, (at, text) in enumerate(self.items):
            if T < at:
                continue
            tw = text_width(text, 30, 800)
            s = sprite(int(tw) + 90, 72)
            d = ImageDraw.Draw(s)
            para(d, 16, 14, tw + 50, 48, 14, INK)
            para(d, 6, 6, tw + 50, 48, 14, PAPER, INK, 3)
            d.rectangle([6, 6, 16, 54], fill=GREEN)
            draw_text(s, (34, 42), text, 30, INK, 800)
            place(frame, s, (self.pos[0] + s.width / 2, self.pos[1] + k * 66), 3, pop((T - at) / 0.15),
                  self.alpha(t, 0.01, 0.2))


class Zzz(Overlay):
    """Sleep Zs rising from pos in a loop."""

    def __init__(self, start, end, pos):
        super().__init__(start, end)
        self.pos = pos

    def draw(self, frame, t):
        for k in range(3):
            p = ((t + k * 0.33) % 1.0)
            size = int(34 + 30 * p)
            s = sprite(size + 30, size + 30)
            draw_text(s, (12, size + 8), "Z", size, PAPER, 900, stroke=5)
            place(frame, s, (self.pos[0] + p * 90, self.pos[1] - p * 120), -12, alpha=math.sin(p * math.pi) * self.alpha(t))


class Reticle(Overlay):
    """Target lock-on: a ring closing in on pos with a LOCK tag."""

    def __init__(self, start, end, pos, color=GREEN):
        super().__init__(start, end)
        self.pos, self.color = pos, color

    def draw(self, frame, t):
        r = 150 - 70 * ease(t / 0.2)
        s = sprite(420, 420)
        d = ImageDraw.Draw(s)
        c = 210
        d.ellipse([c - r, c - r, c + r, c + r], outline=self.color, width=6)
        for k in range(4):
            a = k * math.pi / 2 + t * 2
            d.line([(c + math.cos(a) * (r - 24), c + math.sin(a) * (r - 24)),
                    (c + math.cos(a) * (r + 30), c + math.sin(a) * (r + 30))], fill=self.color, width=6)
        draw_text(s, (c + r * 0.7, c - r * 0.7), "LOCK", 26, INK, 900, "mono", stroke=4, stroke_fill=self.color)
        place(frame, s, self.pos, alpha=self.alpha(t, 0.02, 0.1))


class Cracks(Overlay):
    """Jagged white cracks growing out of center."""

    def __init__(self, start, end, center, radius=200, seed=3):
        super().__init__(start, end)
        self.center, self.radius, self.seed = center, radius, seed

    def draw(self, frame, t):
        grow = ease(t / 0.18)
        rnd = random.Random(self.seed)
        d = ImageDraw.Draw(frame)
        for k in range(9):
            a = k / 9 * 2 * math.pi + rnd.uniform(-0.3, 0.3)
            x, y = self.center
            pts = [(x, y)]
            for j in range(5):
                a += rnd.uniform(-0.5, 0.5)
                step = self.radius / 5 * grow * rnd.uniform(0.6, 1.3)
                x, y = x + math.cos(a) * step, y + math.sin(a) * step
                pts.append((x, y))
            a_ = int(255 * self.alpha(t, 0.01, 0.2))
            d.line(pts, fill=(12, 12, 16, a_), width=7, joint="curve")
            d.line(pts, fill=(255, 255, 255, a_), width=3, joint="curve")
