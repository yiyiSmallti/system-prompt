"""模型与动作展示片的合成：把 Unity 逐镜渲染的无损中间视频（frames/<镜号>.mkv）按 timeline.json 的顺序接起来，
逐帧叠上字幕，配上游戏的标题曲，编码成 1080p60 的 H.264。

字幕走游戏界面的路数（pv/README.md「游戏界面的设计」）：斜切面板、黑白高对比、终端绿 #3FBF7F 点缀，
中文用 Noto Sans SC，英文和数据用 JetBrains Mono。分段处用斜切色块扫过盖住切点。

用法：python3 pv/showcase/compose.py <frames 目录> <输出 mp4>
      python3 pv/showcase/compose.py <frames 目录> <输出目录> --stills 3.5,12,40   只合成这几秒的画面，存成 PNG
"""
import functools
import json
import math
import pathlib
import subprocess
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = pathlib.Path(__file__).resolve().parent
MUSIC = HERE.parent.parent / "game/Assets/SystemPrompt/Resources/Audio/BGM/title.wav"
FONT_DIR = pathlib.Path.home() / "Library/Fonts"
FONTS = {"sans": "NotoSansSC-wght.ttf", "mono": "JetBrainsMono-wght.ttf"}

INK = (12, 12, 16, 255)
PAPER = (246, 246, 240, 255)
GREEN = (63, 191, 127, 255)
WHITE = (255, 255, 255, 255)
DIM = (200, 206, 214, 255)


@functools.lru_cache(maxsize=None)
def font(kind, size, weight=400):
    f = ImageFont.truetype(str(FONT_DIR / FONTS[kind]), size)
    f.set_variation_by_axes([weight])
    return f


def runs(text, kind):
    """等宽字体没有汉字，汉字退回黑体。"""
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


def width(text, size, weight=400, kind="sans"):
    return sum(font(k, size, weight).getlength(r) for r, k in runs(text, kind))


def text(img, xy, s, size, fill, weight=400, kind="sans"):
    """在 xy（左、基线）写字，返回宽度。"""
    d = ImageDraw.Draw(img)
    x, y = xy
    for r, k in runs(s, kind):
        f = font(k, size, weight)
        d.text((x, y), r, font=f, fill=fill, anchor="ls")
        x += f.getlength(r)
    return x - xy[0]


def para(d, x, y, w, h, slant, fill):
    """左下角在 (x, y + h) 的平行四边形，上边往右错 slant。"""
    d.polygon([(x + slant, y), (x + w + slant, y), (x + w, y + h), (x, y + h)], fill=fill)


def ease(x):
    x = min(max(x, 0.0), 1.0)
    return 1 - (1 - x) ** 3


def smooth(a, b, x):
    t = min(max((x - a) / (b - a), 0.0), 1.0)
    return t * t * (3 - 2 * t)


def pop(x):
    """0→1 时略冲过头再回来，用在砸进画面的字上。"""
    x = min(max(x, 0.0), 1.0)
    return 1 + 2.4 * (x - 1) ** 3 + 1.4 * (x - 1) ** 2


def sprite(w, h):
    return Image.new("RGBA", (int(w), int(h)), (0, 0, 0, 0))


def fade(img, alpha):
    if alpha >= 0.999:
        return img
    out = img.copy()
    out.putalpha(img.getchannel("A").point(lambda v: int(v * alpha)))
    return out


# ---------------------------------------------------------------- 字幕

class Overlay:
    """一条字幕：start、end 是全片的秒；draw(frame, t) 里 t 是从 start 起的秒。"""

    fade_in, fade_out = 0.3, 0.25

    def __init__(self, start, end):
        self.start, self.end = start, end

    def alpha(self, t):
        return min(smooth(0, self.fade_in, t), 1 - smooth(self.end - self.start - self.fade_out, self.end - self.start, t))

    def slide(self, t, distance=60):
        """进场时从左边滑进来，出场时往左退一点。"""
        return -distance * (1 - ease(t / self.fade_in)) - distance * 0.5 * smooth(self.end - self.start - self.fade_out, self.end - self.start, t)

    def draw(self, frame, t):
        raise NotImplementedError


class Title(Overlay):
    """开场标题：白色 SYSTEM，歪贴在绿色色块上的黑字 PROMPT，末尾闪烁的下划线光标（同游戏标题）。"""

    def __init__(self, start, end, note):
        super().__init__(start, end)
        self.word = sprite(1100, 220)
        d = ImageDraw.Draw(self.word)
        size = 132
        w1 = width("SYSTEM", size, 800, "mono")
        text(self.word, (20, 160), "SYSTEM", size, WHITE, 800, "mono")
        self.system_w = w1 + 20
        self.block = sprite(width("PROMPT", size, 800, "mono") + 80, 200)
        d2 = ImageDraw.Draw(self.block)
        para(d2, 10, 18, self.block.width - 40, 170, 22, GREEN)
        text(self.block, (34, 160), "PROMPT", size, INK, 800, "mono")
        self.block = self.block.rotate(4, resample=Image.BICUBIC, expand=True)
        self.strip = sprite(980, 150)
        d3 = ImageDraw.Draw(self.strip)
        para(d3, 0, 0, 700, 78, 26, INK)
        text(self.strip, (40, 58), "模型与动作素材一览", 46, WHITE, 700)
        sub = "MODEL & MOTION REEL  ·  6 MODELS  ·  32 MOTIONS  ·  8 PROPS"
        para(d3, 30, 92, width(sub, 24, 600, "mono") + 60, 48, 12, (12, 12, 16, 225))
        text(self.strip, (60, 128), sub, 24, GREEN, 600, "mono")

    def draw(self, frame, t):
        a = self.alpha(t)
        cx = 960
        total = self.system_w + self.block.width - 30
        x0 = cx - total // 2
        y0 = 250
        k = ease(t / 0.45)
        frame.alpha_composite(fade(self.word, a), (int(x0 - 300 * (1 - k)), y0))
        s = pop((t - 0.25) / 0.35)
        if t > 0.25:
            b = self.block.resize((max(1, int(self.block.width * s)), max(1, int(self.block.height * s))), Image.BICUBIC) if abs(s - 1) > 0.01 else self.block
            frame.alpha_composite(fade(b, a), (int(x0 + self.system_w - 10 + (self.block.width - b.width) / 2), int(y0 + 5 + (self.block.height - b.height) / 2)))
            if int(t * 2.4) % 2 == 0:
                d = ImageDraw.Draw(frame)
                cxr = x0 + self.system_w + self.block.width - 10
                d.rectangle([cxr, y0 + 150, cxr + 56, y0 + 166], fill=(255, 255, 255, int(255 * a)))
        if t > 0.6:
            k2 = ease((t - 0.6) / 0.4)
            frame.alpha_composite(fade(self.strip, a * k2), (int(cx - 470 - 80 * (1 - k2)), y0 + 230))


class Name(Overlay):
    """人物或道具脚下的小名牌：白纸条上的名字，底下一行等宽小字。"""

    fade_in = 0.25

    def __init__(self, start, end, note, delay=0.0):
        super().__init__(start + delay, end)
        zh, sub = note["zh"], note.get("sub", "")
        w = max(width(zh, 30, 700), width(sub, 18, 500, "mono")) + 44
        self.img = sprite(w + 20, 92)
        d = ImageDraw.Draw(self.img)
        para(d, 4, 4, w, 44, 10, PAPER)
        text(self.img, (24, 38), zh, 30, INK, 700)
        para(d, 14, 50, width(sub, 18, 500, "mono") + 26, 30, 8, INK)
        text(self.img, (28, 72), sub, 18, GREEN, 500, "mono")
        self.x, self.y = note["x"], note["y"]

    def draw(self, frame, t):
        a = self.alpha(t)
        dy = 18 * (1 - ease(t / self.fade_in))
        frame.alpha_composite(fade(self.img, a), (int(self.x - self.img.width / 2), int(self.y + 8 + dy)))


class Section(Overlay):
    """分段标题：斜切黑带从左边甩进来，大号绿色编号、中文标题和英文小标。"""

    fade_in, fade_out = 0.35, 0.3

    def __init__(self, start, end, note):
        super().__init__(start, end)
        self.img = sprite(1400, 230)
        d = ImageDraw.Draw(self.img)
        para(d, 0, 30, 1150, 170, 50, INK)
        para(d, 0, 196, 980, 12, 8, GREEN)
        text(self.img, (60, 168), note["no"], 120, GREEN, 800, "mono")
        x = 60 + width(note["no"], 120, 800, "mono") + 36
        text(self.img, (x, 128), note["zh"], 64, WHITE, 800)
        text(self.img, (x + 4, 172), note["en"], 26, GREEN, 600, "mono")

    def draw(self, frame, t):
        frame.alpha_composite(fade(self.img, self.alpha(t)), (int(-40 + 3 * self.slide(t, 120)), 700))


class Card(Overlay):
    """角色转台左边的资料卡。"""

    def __init__(self, start, end, note):
        super().__init__(start, end)
        self.img = sprite(760, 520)
        d = ImageDraw.Draw(self.img)
        name, outfit = note["name"], note["outfit"]
        para(d, 0, 0, 680, 130, 30, INK)
        text(self.img, (44, 98), name, 72, WHITE, 800)
        ow = width(outfit, 40, 700) + 60
        para(d, 30, 140, ow, 64, 16, GREEN)
        text(self.img, (62, 186), outfit, 40, INK, 700)
        para(d, 0, 222, 600, 214, 20, (12, 12, 16, 215))
        rows = [("MODEL", note["model"]), ("HEIGHT", note["height"]), ("RIG", "Mixamo · Humanoid"), ("LOOK", note["look"])]
        for i, (k, v) in enumerate(rows):
            text(self.img, (40, 266 + 36 * i), k, 22, GREEN, 700, "mono")
            text(self.img, (180, 266 + 36 * i), v, 24, WHITE, 500, "mono")
        text(self.img, (40, 266 + 36 * 4 + 12), note["note"], 26, DIM, 500)

    def draw(self, frame, t):
        frame.alpha_composite(fade(self.img, self.alpha(t)), (int(96 + self.slide(t)), 300))


class Motion(Overlay):
    """动作名：左下的大号斜切牌，中文名、动作文件名和长度。"""

    def __init__(self, start, end, note):
        super().__init__(start, end)
        zh, en, meta = note["zh"], note["en"], note.get("meta", "")
        w = max(width(zh, 76, 800) + width(en, 34, 700, "mono") + 120, width(meta, 24, 500) + 80)
        self.img = sprite(w + 60, 200)
        d = ImageDraw.Draw(self.img)
        para(d, 0, 0, w, 116, 28, INK)
        x = 44 + text(self.img, (44, 90), zh, 76, WHITE, 800) + 28
        text(self.img, (x, 88), en, 34, GREEN, 700, "mono")
        para(d, 22, 124, width(meta, 24, 500) + 50, 46, 12, PAPER)
        text(self.img, (46, 157), meta, 24, INK, 500)

    def draw(self, frame, t):
        frame.alpha_composite(fade(self.img, self.alpha(t)), (int(90 + self.slide(t)), 820))


class Page(Overlay):
    """分屏页、对打和道具页左上的标题。"""

    def __init__(self, start, end, note):
        super().__init__(start, end)
        zh, en, sub = note["zh"], note["en"], note.get("sub", "")
        w = width(zh, 46, 800) + width(en, 26, 700, "mono") + 100
        self.img = sprite(max(w, width(sub, 22, 500) + 60) + 50, 130)
        d = ImageDraw.Draw(self.img)
        para(d, 0, 0, w, 70, 18, INK)
        x = 30 + text(self.img, (30, 54), zh, 46, WHITE, 800) + 22
        text(self.img, (x, 52), en, 26, GREEN, 700, "mono")
        if sub:
            para(d, 14, 76, width(sub, 22, 500) + 36, 38, 10, PAPER)
            text(self.img, (32, 104), sub, 22, INK, 500)

    def draw(self, frame, t):
        frame.alpha_composite(fade(self.img, self.alpha(t)), (int(40 + self.slide(t)), 36))


class Cell(Overlay):
    """分屏每格左下的动作名。"""

    fade_in = 0.25

    def __init__(self, start, end, note):
        cell = note["cell"]
        super().__init__(start + 0.05 * cell, end)
        self.x0, self.y0 = (cell % 3) * 640, (cell // 3) * 540
        zh, en, meta, who = note["zh"], note["en"], note["meta"], note["who"]
        w = max(width(zh, 40, 800) + 40, width(en, 24, 700, "mono") + 40)
        self.img = sprite(560, 190)
        d = ImageDraw.Draw(self.img)
        para(d, 0, 0, w, 58, 14, INK)
        text(self.img, (22, 45), zh, 40, WHITE, 800)
        para(d, 10, 62, width(en, 24, 700, "mono") + 34, 38, 10, GREEN)
        text(self.img, (26, 90), en, 24, INK, 700, "mono")
        para(d, 4, 106, max(width(meta, 20, 500, "mono"), width(who, 20, 500)) + 40, 66, 10, (12, 12, 16, 205))
        text(self.img, (20, 132), meta, 20, WHITE, 500, "mono")
        text(self.img, (20, 162), who, 20, DIM, 500)

    def draw(self, frame, t):
        frame.alpha_composite(fade(self.img, self.alpha(t)), (int(self.x0 + 28 + self.slide(t, 30)), self.y0 + 330))


class Grid(Overlay):
    """分屏的格线。"""

    fade_in = fade_out = 0.01

    def draw(self, frame, t):
        d = ImageDraw.Draw(frame)
        for x in (640, 1280):
            d.polygon([(x - 4, 0), (x + 4, 0), (x + 4, 1080), (x - 4, 1080)], fill=INK)
        d.rectangle([0, 536, 1920, 544], fill=INK)
        d.rectangle([0, 538, 1920, 541], fill=(63, 191, 127, 160))


class Callout(Overlay):
    """对打时头顶的招式名：歪斜的黑框白字，绿边，砸进来。"""

    fade_in, fade_out = 0.1, 0.2

    def __init__(self, start, end, note):
        super().__init__(start, end)
        s = note["text"]
        w = width(s, 54, 900) + 70
        img = sprite(w + 40, 110)
        d = ImageDraw.Draw(img)
        para(d, 6, 10, w, 86, 18, GREEN)
        para(d, 0, 4, w, 86, 18, INK)
        text(img, (36, 70), s, 54, WHITE, 900)
        self.img = img.rotate(6 if note.get("side") == "left" else -6, resample=Image.BICUBIC, expand=True)
        self.x, self.y = note["x"], note["y"]

    def draw(self, frame, t):
        s = pop(t / 0.16)
        img = self.img if abs(s - 1) < 0.01 else self.img.resize((max(1, int(self.img.width * s)), max(1, int(self.img.height * s))), Image.BICUBIC)
        y = self.y - img.height - 6 * t
        frame.alpha_composite(fade(img, self.alpha(t)), (int(self.x - img.width / 2), int(y)))


class Credits(Overlay):
    """片尾：标题字标和署名。"""

    fade_in, fade_out = 0.6, 0.01

    def __init__(self, start, end, note):
        super().__init__(start, end)
        self.img = sprite(1920, 1080)
        d = ImageDraw.Draw(self.img)
        d.rectangle([0, 0, 1920, 1080], fill=(8, 8, 14, 120))
        # 署名压在欢呼的人物身上，中间再垫一条更深的带子。
        # 上下边各用 140 像素渐变过渡，免得天幕上切出一道横线。
        for y in range(130, 1040):
            k = smooth(130, 270, y) * (1 - smooth(900, 1040, y))
            d.line([(0, y), (1920, y)], fill=(8, 8, 14, int(120 + 75 * k)))
        size = 96
        w1 = width("SYSTEM", size, 800, "mono")
        w2 = width("PROMPT", size, 800, "mono")
        x0 = 960 - (w1 + w2 + 60) / 2
        text(self.img, (x0, 380), "SYSTEM", size, WHITE, 800, "mono")
        para(d, x0 + w1 + 20, 288, w2 + 40, 116, 16, GREEN)
        text(self.img, (x0 + w1 + 40, 380), "PROMPT", size, INK, 800, "mono")
        lines = [
            ("角色原创", "ZipZipPipe（胖次狐）"),
            ("原设定稿", "lmy414/ai-chan-sheets · CC BY-NC-SA 4.0"),
            ("3D 模型", "Tripo 生成 · Mixamo 骨骼"),
            ("动作", "Tripo（母作）· Mixamo（衍生作《无敌究极后桥背摔》）"),
            ("渲染", "Unity 6 · 写实光照 · 1080p60"),
        ]
        for i, (k, v) in enumerate(lines):
            y = 520 + 58 * i
            kw = width(k, 28, 700)
            text(self.img, (900 - kw, y), k, 28, GREEN, 700)
            text(self.img, (940, y), v, 28, WHITE, 500)
        s = "非商业同人作品"
        text(self.img, (960 - width(s, 24, 500) / 2, 860), s, 24, DIM, 500)

    def draw(self, frame, t):
        frame.alpha_composite(fade(self.img, self.alpha(t)), (0, 0))


class Hud(Overlay):
    """右上角的小标，开场标题和片尾时不显示。"""

    def __init__(self, start, end):
        super().__init__(start, end)
        self.img = sprite(560, 60)
        d = ImageDraw.Draw(self.img)
        d.ellipse([8, 20, 24, 36], fill=GREEN)
        text(self.img, (38, 37), "SYSTEM PROMPT", 22, WHITE, 700, "mono")
        text(self.img, (38 + width("SYSTEM PROMPT ", 22, 700, "mono"), 37), "· MODEL REEL", 22, (220, 226, 232, 200), 500, "mono")

    def draw(self, frame, t):
        frame.alpha_composite(fade(self.img, 0.85 * self.alpha(t)), (1920 - self.img.width - 10, 26))


class Wipe(Overlay):
    """分段处的转场：一组斜切色块从左扫过，正好在切点盖满画面。"""

    def __init__(self, cut):
        super().__init__(cut - 0.3, cut + 0.3)

    def draw(self, frame, t):
        p = t / 0.6
        d = ImageDraw.Draw(frame)
        slant = 420
        colours = [GREEN, INK, PAPER, INK]
        for i, c in enumerate(colours):
            lag = 0.07 * i
            lead = ease((p - lag) / 0.5) * (1920 + slant * 2) - slant
            tail = ease((p - 0.5 - lag * 0.6) / 0.5) * (1920 + slant * 2) - slant
            if lead <= tail:
                continue
            d.polygon([(tail + slant, 0), (lead + slant, 0), (lead, 1080), (tail, 1080)], fill=c)


def build(timeline):
    fps = timeline["fps"]
    overlays, cuts, t0 = [], [], 0.0
    shots = timeline["shots"]
    total = sum(s["frames"] for s in shots) / fps
    for shot in shots:
        dur = shot["frames"] / fps
        notes = shot["notes"]
        names = sorted([n for n in notes if n["kind"] == "name"], key=lambda n: n["x"])
        if any(n["kind"] == "cell" for n in notes):
            overlays.append(Grid(t0, t0 + dur))
        for n in notes:
            a, b = t0 + n["start"], t0 + min(n["end"], dur)
            k = n["kind"]
            if k == "title":
                overlays.append(Title(a, b, n))
            elif k == "name":
                overlays.append(Name(a, b, n, 0.07 * names.index(n)))
            elif k == "section":
                overlays.append(Section(a + 0.25, b, n))
                if t0 > 0:
                    cuts.append(t0)
            elif k == "card":
                overlays.append(Card(a, b, n))
            elif k == "motion":
                overlays.append(Motion(a, b, n))
            elif k == "page":
                overlays.append(Page(a, b, n))
            elif k == "cell":
                overlays.append(Cell(a, b, n))
            elif k == "callout":
                overlays.append(Callout(a, b, n))
            elif k == "credits":
                overlays.append(Credits(a, b, n))
                cuts.append(t0)
            else:
                raise ValueError(f"{shot['id']} 里有不认识的字幕 {k}")
        t0 += dur
    first = shots[0]["frames"] / fps
    credits = next(o.start for o in overlays if isinstance(o, Credits))
    overlays.insert(0, Hud(first, credits))
    overlays += [Wipe(c) for c in cuts]
    return overlays, total


def main():
    frames_dir, out = pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2])
    stills = [float(x) for x in sys.argv[sys.argv.index("--stills") + 1].split(",")] if "--stills" in sys.argv else None
    timeline = json.loads((frames_dir / "timeline.json").read_text())
    fps, W, H = timeline["fps"], timeline["width"], timeline["height"]
    overlays, total = build(timeline)
    missing = [s["id"] for s in timeline["shots"] if not (frames_dir / f"{s['id']}.mkv").exists()]
    if missing:
        sys.exit("还没渲染的镜头：" + ", ".join(missing))
    listing = frames_dir / "concat.txt"
    listing.write_text("".join(f"file '{s['id']}.mkv'\n" for s in timeline["shots"]))
    out.parent.mkdir(parents=True, exist_ok=True)
    count = sum(s["frames"] for s in timeline["shots"])

    decode = subprocess.Popen(["ffmpeg", "-v", "error", "-f", "concat", "-safe", "0", "-i", str(listing),
                               "-f", "rawvideo", "-pix_fmt", "rgb24", "-"], stdout=subprocess.PIPE)
    fade_out = 2.5
    if stills is not None:
        out.mkdir(parents=True, exist_ok=True)
        wanted = {round(t * fps): t for t in stills}
        last = max(wanted)
        for i in range(last + 1):
            raw = decode.stdout.read(W * H * 3)
            if i not in wanted:
                continue
            t = i / fps
            frame = Image.frombuffer("RGB", (W, H), raw, "raw", "RGB", 0, 1).convert("RGBA")
            for o in overlays:
                if o.start <= t < o.end:
                    o.draw(frame, t - o.start)
            frame.convert("RGB").save(out / f"{t:07.2f}.png")
        decode.kill()
        print(f"{len(wanted)} 张 → {out}")
        return
    encode = subprocess.Popen([
        "ffmpeg", "-v", "error", "-y",
        "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{W}x{H}", "-r", str(fps), "-i", "-",
        "-i", str(MUSIC),
        # 帧上不标色域和传递特性的话，ffmpeg 会拿帧上的 unknown 盖掉下面的 -color_primaries/-color_trc，播放器就可能猜错、发灰。
        "-filter_complex", f"[0:v]scale=out_color_matrix=bt709:out_range=tv,format=yuv420p,"
                           f"setparams=color_primaries=bt709:color_trc=bt709:colorspace=bt709:range=tv[v];"
                           f"[1:a]atrim=0:{total:.3f},afade=t=in:d=0.3,afade=t=out:st={total - fade_out:.3f}:d={fade_out}[a]",
        "-map", "[v]", "-map", "[a]",
        "-c:v", "libx264", "-preset", "slow", "-crf", "15", "-tune", "animation", "-profile:v", "high",
        "-x264-params", f"keyint={fps * 2}",
        "-color_primaries", "bt709", "-color_trc", "bt709", "-colorspace", "bt709", "-color_range", "tv",
        "-c:a", "aac", "-b:a", "256k", "-movflags", "+faststart", str(out)], stdin=subprocess.PIPE)

    size = W * H * 3
    for i in range(count):
        raw = decode.stdout.read(size)
        if len(raw) < size:
            sys.exit(f"中间视频只解出 {i} 帧，应有 {count} 帧")
        t = i / fps
        frame = Image.frombuffer("RGB", (W, H), raw, "raw", "RGB", 0, 1).convert("RGBA")
        for o in overlays:
            if o.start <= t < o.end:
                o.draw(frame, t - o.start)
        rgb = np.asarray(frame.convert("RGB"))
        # 开头从黑里亮起来，结尾暗下去。
        k = min(smooth(0, 0.5, t), 1 - smooth(total - 0.9, total - 0.05, t))
        if k < 1:
            rgb = (rgb.astype(np.float32) * k).astype(np.uint8)
        encode.stdin.write(rgb.tobytes())
        if i % (fps * 10) == 0:
            print(f"{t:6.1f}/{total:.1f} 秒", flush=True)
    encode.stdin.close()
    if encode.wait() != 0 or decode.wait() != 0:
        sys.exit("ffmpeg 失败")
    print(f"→ {out}")


if __name__ == "__main__":
    main()
