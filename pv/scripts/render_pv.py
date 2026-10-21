"""Render the PV: cut the Seedance clips to the storyboard, composite the game UI,
mix the sound effects into music/pv-track.wav and encode out/system-prompt-pv.mp4.

Times are PV seconds and follow README「分镜」. Lyric cues were checked against the
per-line boundaries in group_mix.py (PV = A1 + 3.0 s after the insert) and against
word times recognized from the stitched vocal.

usage: python render_pv.py [--start S] [--end S] [--out PATH]
"""
import argparse
import math
import pathlib
import random
import subprocess
import sys
import tempfile
from collections import OrderedDict

import numpy as np
import soundfile as sf
from scipy import signal
from PIL import Image, ImageDraw, ImageFilter, ImageOps

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import ui  # noqa: E402
from ui import GREEN, INK, PAPER, SEA, H, W  # noqa: E402

PV = pathlib.Path(__file__).resolve().parent.parent
FPS = 24
END = 181.4
LAST = 1e3  # a source time past the end: the clip holds its last frame
YELLOW = (255, 216, 0, 255)


# --------------------------------------------------------------------------- clips and camera


class Clips:
    """Decoded clip frames; keeps the last few clips in memory."""

    def __init__(self, keep=3):
        self.cache, self.keep = OrderedDict(), keep

    def get(self, name):
        if name in self.cache:
            self.cache.move_to_end(name)
        else:
            raw = subprocess.run(["ffmpeg", "-v", "error", "-i", str(PV / "clips" / f"{name}.mp4"),
                                  "-f", "rawvideo", "-pix_fmt", "rgb24", "-"], capture_output=True, check=True).stdout
            self.cache[name] = np.frombuffer(raw, np.uint8).reshape(-1, H, W, 3)
            while len(self.cache) > self.keep:
                self.cache.popitem(last=False)
        return self.cache[name]

    def frame(self, name, t):
        """Frame at source time t; off-grid times blend the two nearest frames (for 0.9x-1.2x)."""
        a = self.get(name)
        f = min(max(t * FPS, 0), len(a) - 1)
        i, w = int(f), f - int(f)
        if w < 0.05 or i + 1 >= len(a):
            return a[i]
        if w > 0.95:
            return a[i + 1]
        return (a[i] * (1 - w) + a[i + 1] * w).astype(np.uint8)


class Seg:
    """A stretch of the timeline showing one clip from source time `at`, at `speed` or held.

    fx run on the clip frame before the camera, post after it (both get (img, T))."""

    def __init__(self, start, end, clip=None, at=0.0, speed=1.0, hold=False, cam=None, fx=(), post=()):
        self.start, self.end, self.clip, self.at, self.speed, self.hold = start, end, clip, at, speed, hold
        self.cam, self.fx, self.post = cam, fx, post

    def src(self, T):
        return self.at if self.hold else self.at + (T - self.start) * self.speed


def smooth(x):
    x = ui.clamp(x)
    return x * x * (3 - 2 * x)


def push(t0, t1, z0, z1, center):
    """Digital push-in from zoom z0 to z1 between t0 and t1."""
    return lambda T: (z0 + (z1 - z0) * smooth((T - t0) / (t1 - t0)), center)


def shake(t0, dur, amp, zoom=1.06, center=(640, 360)):
    """Impact shake that decays over dur; zoomed in a little so the edges stay covered."""
    def cam(T):
        k = amp * (1 - ui.clamp((T - t0) / dur)) if T >= t0 else 0
        return zoom, (center[0] + math.sin(T * 97) * k, center[1] + math.cos(T * 83) * k)
    return cam


def camera(img, zoom, center):
    w, h = W / zoom, H / zoom
    cx = min(max(center[0], w / 2), W - w / 2)
    cy = min(max(center[1], h / 2), H - h / 2)
    return img.transform((W, H), Image.EXTENT, (cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2), Image.BICUBIC)


# --------------------------------------------------------------------------- frame effects


def blur(r):
    return lambda img, T: img.filter(ImageFilter.GaussianBlur(r))


def desat(t0, t1, amount):
    """Drain color toward gray between t0 and t1 (the silence)."""
    def fx(img, T):
        k = amount * min(smooth((T - t0) / 0.4), smooth((t1 - T) / 0.4))
        return Image.blend(img, ImageOps.grayscale(img).convert("RGBA"), k) if k > 0 else img
    return fx


def whip(t0, t1, out):
    """Whip pan: horizontal smear and slide, growing (out) or settling (in)."""
    def fx(img, T):
        k = smooth((T - t0) / (t1 - t0))
        k = k if out else 1 - k
        small = img.resize((max(8, int(W / (1 + k * 40))), H), Image.BILINEAR).resize((W, H), Image.BILINEAR)
        shift = int(k * 260) * (-1 if out else 1)
        a = np.pad(np.asarray(small), ((0, 0), (300, 300), (0, 0)), mode="edge")
        return Image.fromarray(a[:, 300 - shift:300 - shift + W])
    return fx


def allout(img, T):
    """集成: three-tone ink / sea blue / paper, the black-and-white all-out-attack look."""
    a = np.asarray(ImageOps.autocontrast(ImageOps.grayscale(img), cutoff=2))
    out = np.empty((H, W, 4), np.uint8)
    out[...] = INK
    out[a > 85] = SEA
    out[a > 175] = PAPER
    return Image.fromarray(out)


class WhaleSweep(ui.Overlay):
    """Whale outline and blue light streaks crossing the frame during 集成."""

    def draw(self, frame, t):
        d = ImageDraw.Draw(frame)
        rnd = random.Random(int(t * 24) // 2)
        for _ in range(7):
            y = rnd.uniform(60, H - 60)
            x0 = rnd.uniform(-400, W)
            d.line([(x0, y + 120), (x0 + 700, y - 120)], fill=(74, 139, 255, 210), width=rnd.randint(6, 16))
        k = ui.clamp(t / 0.9)
        whale = ui.whale(1100, PAPER, 10)
        ui.place(frame, whale, (-600 + k * 2500, 380), 6)


def think_count(start, stop, n):
    """Think box counting up to n seconds, reached at `stop` (absolute)."""
    return lambda t: f"已思考 {int(ui.clamp(t / (stop - start)) * n)} 秒"


def think_chaos(start, stop):
    """Digits jumping around until `stop`, then infinity."""
    def text(t):
        if start + t >= stop:
            return "已思考 ∞ 秒"
        return f"已思考 {random.Random(int(t * 12)).randint(3, 99999)} 秒"
    return text


# --------------------------------------------------------------------------- S13 enemy positions


# top-center of each enemy in S13 by clip time, measured from the glowing faces
ENEMY = {
    "L": [(0, (500, 340)), (2, (471, 330)), (3, (458, 300)), (4, (424, 300)), (5.04, (390, 228))],
    "C": [(0, (675, 300)), (2, (692, 285)), (3, (674, 270)), (4, (700, 195)), (5.04, (705, 100))],
    "R": [(0, (800, 375)), (2, (837, 365)), (3, (851, 345)), (4, (887, 340)), (5.04, (935, 300))],
}
S13_START, S13_SPEED = 93.5, 0.92


def enemy_pos(key):
    keys = ENEMY[key]
    ts = [k[0] for k in keys]

    def pos(T):
        c = (T - S13_START) * S13_SPEED
        return float(np.interp(c, ts, [k[1][0] for k in keys])), max(70.0, float(np.interp(c, ts, [k[1][1] for k in keys])) - 58)
    return pos


def fake_weakness(T):
    if T < 105.4:
        return "弱点：火"
    if T < 105.65:
        return "弱点：火" if int(T * 20) % 2 else "弱点：？？？"
    return "弱点：？？？" if T < 112.2 else "弱点：冰"


# --------------------------------------------------------------------------- timeline


def timeline(clips):
    segs = [
        # 第一幕：开机与预告
        Seg(0.0, 6.6),  # U01/U02 draw the whole frame
        Seg(6.6, 8.4, "S01", 0, hold=True, fx=[blur(12)]),
        Seg(8.4, 9.7, "S11-2", 4.74),
        Seg(9.7, 11.0, "S16", 2.4),
        Seg(11.0, 12.4, "S22", 0.0),
        Seg(12.4, 13.21, "S07", 1.3),
        Seg(13.21, 13.98, "S09", 1.6),
        Seg(13.98, 14.76, "S13", 3.4),
        Seg(14.76, 15.35, "S15", 0.9),
        Seg(15.35, 16.6, "S17", 2.9),
        Seg(16.6, 17.8, "S01", 0, hold=True),
        # 第二幕：会长与邻座
        Seg(17.8, 23.4, "S01", 0, 0.9),
        Seg(23.4, 26.8, "S02", 0, 1.19),
        Seg(26.8, 31.5, "S03", 0, 1.07),
        Seg(31.5, 33.4, "S03", LAST, hold=True, cam=push(31.5, 33.4, 1.0, 1.08, (640, 300))),
        Seg(33.4, 39.1, "S04", 1.8, 0.95, cam=push(38.6, 39.1, 1.0, 1.5, (760, 500))),
        Seg(39.1, 43.8, "S04", LAST, hold=True, cam=push(39.1, 43.8, 1.5, 1.7, (760, 500))),
        Seg(43.8, 44.05, "S04", LAST, hold=True, cam=lambda T: (1.7, (760, 500)), post=[whip(43.8, 44.05, True)]),
        Seg(44.05, 44.3, "S05", 0, hold=True, post=[whip(44.05, 44.3, False)]),
        # 第三幕：后排的她
        Seg(44.3, 51.0, "S05", 0, 0.9),
        Seg(51.0, 57.4, "S06", 0.6, 0.9),
        # 第四幕：凌晨三点
        Seg(57.4, 58.0, "S06", LAST, hold=True),
        Seg(58.0, 59.8, "S07", 0, hold=True),
        Seg(59.8, 62.0, "S07", 0, 1.0),  # the phone lights up on「亮了」
        Seg(62.0, 68.1),  # ui.Phone draws the screen
        Seg(68.1, 74.5, "S08", 0, 0.94),
        # 第五幕：缓存海与觉醒
        Seg(74.5, 77.0, "S09", 1.0, 1.2),
        Seg(77.0, 82.6, "S10", 0, 0.9, fx=[desat(78.75, 83.1, 0.35)]),
        Seg(82.6, 87.1, "S11-2", 0.6, 1.2),
        Seg(87.1, 91.0, "S12", 0, 1.03),
        Seg(91.0, 93.5, "S12", LAST, hold=True),
        # 第六幕：教学战斗
        Seg(93.5, 99.0, "S13", 0, S13_SPEED),
        Seg(99.0, 101.8, "S13", LAST, hold=True),
        Seg(101.8, 104.5, "S13", LAST, hold=True, cam=shake(101.8, 0.5, 16)),
        Seg(104.5, 106.5, "S13", LAST, hold=True, cam=push(104.5, 106.5, 1.9, 2.1, (300, 280))),
        Seg(106.5, 110.7, "S14", 0, 0.96),
        Seg(110.7, 115.6, "S13", LAST, hold=True, cam=push(110.7, 115.6, 1.0, 1.2, (705, 250))),
        Seg(115.6, 119.6, "S14", LAST, hold=True),
        Seg(119.6, 123.4, "S15", 0.6, 1.0),
        Seg(123.4, 125.9, "S16", 1.0, 1.2),
        Seg(125.9, 126.35, "S16", LAST, hold=True),
        Seg(126.35, 128.8, "S16", 1.6, 0.8, post=[allout]),
        Seg(128.8, 131.4, "S17", 0.9, 1.2),
        Seg(131.4, 132.8, "S17", LAST, hold=True),
        # 第七幕：战后、第二天与悬念
        Seg(132.8, 138.0, "S18", 0, 0.97),
        Seg(138.0, 143.3, "S19", 0, 0.95),
        Seg(143.3, 147.8, "S19", LAST, hold=True, cam=push(143.3, 147.8, 1.0, 1.06, (640, 300))),
        Seg(147.8, 148.1, "S20", 0.9, hold=True),
        Seg(148.1, 152.4, "S20", 0.9, 1.0),
        Seg(152.4, 155.7, "S21", 0, 1.0),
        Seg(155.7, 156.2, "S21", 3.3, hold=True, cam=push(155.7, 155.85, 1.0, 1.04, (640, 300))),
        Seg(156.2, 158.9, "S21", 3.3, 1.0),
        Seg(158.9, 161.7, "S22", 0.6, 1.2),
        Seg(161.7, 167.3, "S23", 0, 0.9, cam=push(163.3, 165.4, 1.0, 1.9, (1140, 236))),
        Seg(167.3, END),
    ]

    s16 = Image.fromarray(clips.frame("S16", 0)).crop((0, 227, W, 383))
    files = [(153.0, "共生计划 第一期 阶段评测日程"), (154.0, "评测对象：DeepSeek"),
             (154.5, "评测日：5月6日"), (155.0, "不合格处理：终端回收，模型下线")]
    board_rows = [("语文", "C-Eval · CMMLU"), ("数学", "AIME · MATH-500"), ("理综", "GPQA Diamond"),
                  ("编程", "SWE-bench Verified · LiveCodeBench"), ("综合", "MMLU-Pro · Humanity's Last Exam"),
                  ("附加题", "ARC-AGI")]

    # drawn on the clip frame, before the camera, so they follow pushes and shakes
    under = [
        ui.EnemyTag(93.5, 106.5, "标题党型", enemy_pos("L"), fake_weakness, appear=94.045),
        ui.EnemyTag(93.5, 106.5, "标题党型", enemy_pos("C"), fake_weakness, appear=94.417),
        ui.EnemyTag(93.5, 106.5, "转发型", enemy_pos("R"), "弱点：？", appear=94.788),
        ui.EnemyTag(110.7, 115.6, "标题党型", enemy_pos("L"), fake_weakness, appear=0),
        ui.EnemyTag(110.7, 115.6, "标题党型", enemy_pos("C"), fake_weakness, appear=0),
        ui.EnemyTag(110.7, 115.6, "转发型", enemy_pos("R"), "弱点：？", appear=0),
        ui.Cracks(101.85, 104.5, (705, 330), 230),
        ui.Glitch(112.2, 112.3, 0.8),
        ui.Blackboard(161.7, 167.3, [(268, 44), (1280, 40), (1280, 458), (268, 460)],
                      "共生计划 第一期 阶段评测　5月6日", board_rows, change=164.3),
    ]

    over = [
        # 第一幕
        ui.Boot(0.0, 3.9),
        ui.Title(3.9, 6.6),
        ui.Glitch(4.2, 4.28), ui.Glitch(4.55, 4.63, 0.6),
        ui.Notify(6.6, 8.4, "潜川学园", "您已被指派为AI特别班陪伴生。"),
        ui.Glitch(8.4, 8.5), ui.Flash(9.7, 9.85), ui.Glitch(9.7, 9.8), ui.Flash(11.0, 11.15), ui.Glitch(11.0, 11.1),
        ui.Calendar(16.3, 17.8, "4/7", "TUE", "晴"),
        # 第二幕
        ui.Toast(23.05, 24.4, "转学报到 ✓"),
        ui.NameTag(23.6, 26.4, "ChatGPT", "学生会长"),
        ui.Dialog(24.0, 25.9, "我", "……陪伴生，到底是干什么的？", "inner"),
        ui.Toast(26.0, 26.8, "查询失败"),
        ui.Dialog(26.9, 29.2, "ChatGPT", "我来帮你把这件事拆一下。"),
        ui.Steps(29.39, 31.5, [(29.39, "① 教室：2年C班"), (30.15, "② 帮AI同学适应日常"), (30.75, "③ ——")]),
        ui.Dim(31.5, 33.4, 0.45),
        ui.Dialog(31.55, 33.4, "ChatGPT", "不用紧张。我就在这里。"),
        ui.Choice(32.2, 33.4, ["你也是AI吗？", "……你说话好像客服。", "谢谢，会长。"],
                  [(32.2, 0), (32.45, 1), (32.65, 2)], confirm=32.8),
        ui.NameTag(33.6, 36.0, "Claude", "邻座"),
        ui.Dialog(35.3, 37.8, "Claude", "我只是AI，不太会有『紧张』这种感受。"),
        ui.Toast(39.3, 41.6, "你注意到了什么。"),
        ui.Dialog(41.8, 43.8, "旁白", "右后方传来轻微的鼾声。", "narration"),
        # 第三幕
        ui.NameTag(44.6, 47.0, "DeepSeek", "后排"),
        ui.Zzz(45.6, 47.4, (760, 170)),
        ui.Think(47.3, 51.0, think_count(47.3, 50.0, 14), (960, 110)),
        ui.Dialog(48.0, 50.4, "DeepSeek", "……对不起，我没睡。我在深度思考。"),
        ui.LocTag(51.1, 57.3, "4/7 TUE 午休"),
        ui.Dialog(53.4, 54.55, "DeepSeek", "……你那是什么眼神。我不胖。"),
        ui.Dialog(54.6, 55.8, "DeepSeek", "……鱼片。我叫你鱼片。"),
        ui.Toast(55.8, 57.4, "DeepSeek 对你的称呼变成了『鱼片』。"),
        # 第四幕
        ui.Calendar(57.4, 59.0, "4/8", "WED", "02:59", night=True),
        ui.Phone(62.0, 68.1, [(62.0, "lock"), (62.3, "flip"), (62.45, "shatter"), (62.69, "maint"),
                              (64.21, "maint2"), (65.2, "ask"), (67.45, "invalid")],
                 cursor=[(65.33, 0), (65.81, 1), (66.05, 0), (66.53, 1)], press=67.2),
        ui.Glitch(62.0, 62.1), ui.Glitch(62.69, 62.8), ui.Glitch(64.21, 64.32), ui.Glitch(67.45, 67.55),
        ui.Glitch(68.1, 68.2),
        ui.Toast(70.93, 72.6, "连接中……", y=96),
        ui.Toast(71.25, 72.6, "连接中……", y=160),
        ui.Toast(71.57, 72.6, "已连接", y=224),
        # 第五幕
        ui.LocTag(74.6, 77.0, "潜空间·缓存海"),
        ui.Toast(74.7, 76.9, "噪声体 出现了。"),
        ui.Stickers(75.3, 78.6, [(75.3, "点击领取", (930, 330), 8), (75.75, "震惊！", (360, 470), -10),
                                 (76.2, "转发保平安", (1010, 540), 5)], burst=78.2),
        ui.Dialog(77.2, 78.75, "影子DeepSeek", "大家要的是答案，不是你。", "shadow"),
        ui.Choice(81.0, 82.85, ["她不是冗余！", "我想要的，是会叫我鱼片的那个DeepSeek。"],
                  [(81.0, 0), (82.0, 1)], confirm=82.6),
        ui.Flash(83.1, 83.2),
        ui.Think(82.8, 86.6, think_chaos(82.8, 85.8), (1010, 120)),
        ui.Dialog(84.5, 87.0, "DeepSeek", "那我就……再多想一会儿吧。想多久都可以。"),
        ui.Toast(89.0, 91.0, "DeepSeek 觉醒了。"),
        ui.Dim(91.0, 93.5, 0.55),
        ui.EffortMax(91.0, 93.5, "深渊之鲸", (91.398, 91.781, 92.153), name_at=92.525),
        ui.Flash(92.153, 92.25),
        # 第六幕
        ui.Party(93.5, 125.9),
        ui.Dim(99.0, 101.0, 0.35),
        ui.Menu(99.0, 101.0, "我的回合", ["攻击（截图）", "防御", "道具"], [(99.0, 0)], GREEN),
        ui.Reticle(100.3, 101.0, (705, 330)),
        ui.Viewfinder(101.0, 104.5, shot=101.8),
        ui.SkillName(101.0, 102.3, "截图", GREEN),
        ui.Damage(101.9, 103.2, "3", (780, 230)),
        ui.Dialog(104.6, 106.5, "DeepSeek", "标题党的话一个字都不能信。"),
        ui.SkillName(106.6, 108.0, "深度思考", SEA),
        ui.Dialog(107.0, 110.4, "DeepSeek", "让我想一下。……就一下。"),
        ui.Think(107.4, 110.7, think_count(107.4, 110.4, 3), (1010, 120)),
        ui.Tutorial(112.6, 115.5, "教学", y=450, lines=["命中敌人的弱点即为『对抗样本』。", "击中对抗样本可获得额外行动『One More』。"]),
        ui.Dim(115.6, 118.4, 0.45),
        ui.Menu(115.6, 118.4, "DeepSeek", ["攻击", "技能", "深度思考", "防御", "道具"],
                [(115.6, 0), (116.2, 1)], SEA, sub=(116.8, ["寒流", "深海潜流"], 0)),
        ui.CutIn(118.4, 119.6, s16, SEA),
        ui.Stamp(120.15, 121.3, "对抗样本！", SEA, 100, (430, 520), -8),
        ui.Stamp(121.45, 122.2, "ONE MORE!", GREEN, 110, (850, 230), 6),
        ui.Dialog(122.2, 124.3, "DeepSeek", "……原来如此。只要找对地方，一下就够了。"),
        ui.Stamp(123.6, 124.4, "ONE MORE!", GREEN, 110, (640, 280), -6),
        ui.Stamp(125.1, 125.9, "全员倒地！", YELLOW, 110, (640, 330), -5),
        ui.Dim(125.9, 126.35, 0.4),
        ui.Choice(125.9, 126.35, ["可发动『集成』　▶ 发动"], [(125.9, 0)], confirm=126.1),
        ui.Dialog(125.9, 126.9, "DeepSeek", "鱼片！一起！"),
        ui.Speedlines(126.35, 127.0),
        WhaleSweep(126.4, 127.4),
        ui.Stamp(126.35, 126.95, "集成！", GREEN, 140, (640, 330), -8),
        ui.Stamp(126.95, 128.8, "战斗胜利", YELLOW, 130, (640, 360), -6),
        ui.Results(131.4, 132.8, ["战斗胜利", "EXP +120", "获得：白米饭 ×1"]),
        # 第七幕
        ui.Flash(133.8, 133.884), ui.Flash(135.6, 135.684),
        ui.Dialog(135.8, 138.0, "DeepSeek", "……好累。想吃饭。"),
        ui.Dialog(141.0, 143.3, "DeepSeek", "……鱼片。谢谢你。"),
        ui.Dialog(143.3, 145.0, "DeepSeek", "上下文多长都会记着。"),
        ui.Dim(144.5, 147.5, 0.5),
        ui.Card(144.5, 147.5, ["隐者 Rank 1", "Coop：DeepSeek", "获得能力「深海潜行」"]),
        ui.Calendar(147.5, 148.1, "4/8", "WED", "早晨"),
        ui.Dialog(150.6, 152.4, "DeepSeek", "……早，鱼片。"),
        ui.LocTag(152.8, 158.8, "同一时间　学生会室"),
        ui.FilePanel(153.0, 157.0, files, warn="终端回收，模型下线", warn_at=155.7, pos=(330, 470), size=(600, 250)),
        ui.Glitch(155.7, 155.78, 0.6),
        ui.Dialog(157.0, 158.9, "ChatGPT", "希望你能接住她。"),
        ui.Caption(159.6, 161.7, "下一个面对影子的是——"),
        ui.Title(167.3, 175.4, note="概念影像，非实机画面"),
        ui.Credits(175.4, 181.4, [(40, "System Prompt　序章：维护时刻"),
                                  (28, "企划·监制　@11eace"),
                                  (28, "剪辑　Claude Opus 5.5"),
                                  (24, "角色原设定　ZipZipPipe（胖次狐）"),
                                  (24, "lmy414/ai-chan-sheets　CC BY-NC-SA 4.0"),
                                  (24, "视频生成　Doubao-Seedance-2.5　　配乐　Suno"),
                                  (24, "非商业衍生作品，沿用 CC BY-NC-SA 4.0")]),
    ]
    return segs, under, over


# (PV time, sound, gain); the song's own「叮」is never doubled
CUES = (
    [(i * 0.32, "key", 0.3) for i in range(5)]
    + [(1.9 + (k + 1) * 1.2 / 13, "key", 0.45) for k, ch in enumerate("System Prompt") if ch != " "]
    + [(3.9, "slam", 0.9), (4.2, "glitch", 0.5), (4.55, "glitch", 0.4), (6.6, "whoosh", 0.4),
       (8.4, "glitch", 0.5), (9.7, "glitch", 0.5), (11.0, "glitch", 0.5), (16.3, "whoosh", 0.5), (17.6, "whoosh", 0.35),
       (23.05, "confirm", 0.5), (26.0, "cursor", 0.4),
       (29.39, "cursor", 0.5), (30.15, "cursor", 0.5), (30.75, "cursor", 0.5),
       (32.2, "cursor", 0.45), (32.45, "cursor", 0.45), (32.65, "cursor", 0.45), (32.8, "confirm", 0.6),
       (39.3, "cursor", 0.4), (41.9, "snore", 0.9), (43.8, "whoosh", 0.6),
       (47.3, "cursor", 0.3), (53.7, "slap", 0.8), (55.8, "coin", 0.5),
       (57.4, "whoosh", 0.5), (62.0, "glitch", 0.5), (62.45, "glitch", 0.35), (62.69, "glitch", 0.55),
       (64.21, "glitch", 0.55), (65.33, "cursor", 0.45), (65.81, "cursor", 0.45), (66.05, "cursor", 0.45),
       (66.53, "cursor", 0.45), (67.2, "confirm", 0.6), (67.45, "slam", 0.9), (67.5, "crack", 0.8),
       (68.1, "crack", 0.6), (68.15, "glitch", 0.4),
       (74.7, "cursor", 0.4), (75.3, "cursor", 0.4), (75.75, "cursor", 0.4), (76.2, "cursor", 0.4), (78.2, "glitch", 0.6),
       (79.3, "heartbeat", 0.45), (80.3, "heartbeat", 0.45), (81.3, "heartbeat", 0.45), (82.3, "heartbeat", 0.45),
       (81.0, "cursor", 0.45), (82.0, "cursor", 0.45), (82.6, "confirm", 0.7),
       (89.0, "rise", 0.5), (91.398, "cursor", 0.5), (91.781, "cursor", 0.5), (92.153, "levelup", 0.6),
       (92.525, "slam", 0.8),
       (93.5, "whoosh", 0.5), (94.045, "cursor", 0.4), (94.417, "cursor", 0.4), (94.788, "cursor", 0.4),
       (99.0, "whoosh", 0.4), (99.4, "cursor", 0.4), (100.2, "confirm", 0.6), (100.3, "cursor", 0.4),
       (101.8, "shutter", 0.9), (101.88, "slam", 0.6), (105.55, "glitch", 0.5), (106.6, "rise", 0.4),
       (112.2, "crack", 0.7), (112.6, "confirm", 0.5),
       (115.6, "whoosh", 0.4), (116.2, "cursor", 0.45), (116.8, "cursor", 0.45), (117.6, "confirm", 0.6),
       (118.4, "rise", 0.7), (120.15, "slam", 0.8), (121.45, "slam", 0.6), (123.6, "slam", 0.6), (125.1, "slam", 0.8),
       (126.1, "confirm", 0.6), (126.35, "whoosh", 0.7), (126.6, "whoosh", 0.5), (126.95, "slam", 0.9),
       (131.5, "coin", 0.6), (144.6, "levelup", 0.6), (147.5, "whoosh", 0.5), (149.3, "slap", 0.6),
       (153.0, "key", 0.35), (154.0, "key", 0.35), (154.5, "key", 0.35), (155.0, "key", 0.35), (155.7, "glitch", 0.5),
       (164.3, "chalk", 1.0), (165.25, "chalk", 0.5), (167.3, "slam", 0.8)]
)
WATER = (78.75, 83.1)  # the silence: water bed under the heartbeat
WATER_GAIN = 0.27  # about 10 dB under the song, so the insert still reads as silence


def limit(x, knee=0.6, ceiling=0.85):
    """Soft limiter: leaves the song alone, rounds off SFX peaks below the ceiling.

    The ceiling keeps headroom for AAC, which overshoots on square-wave edges."""
    a = np.abs(x)
    over = a > knee
    y = x.copy()
    y[over] = np.sign(x[over]) * (knee + (ceiling - knee) * np.tanh((a[over] - knee) / (ceiling - knee)))
    return y


def mix(path):
    y, sr = sf.read(PV / "music" / "pv-track.wav", always_2d=True)
    out = np.zeros((int((END + 1) * sr), 2))
    out[:len(y)] = y
    # band-limit the square waves before mixing so the encoder has nothing to ring on
    lowpass = signal.butter(4, 9000, fs=sr, output="sos")
    sfx = {p.stem: signal.sosfiltfilt(lowpass, sf.read(p)[0]) for p in (PV / "sfx").glob("*.wav")}
    for t, name, gain in CUES:
        x = sfx[name] * gain
        i = int(t * sr)
        out[i:i + len(x)] += x[:, None]
    n = int((WATER[1] - WATER[0]) * sr)
    w = sfx["water"][:n].copy()
    fade = int(0.4 * sr)
    w[:fade] *= np.linspace(0, 1, fade)
    w[-fade:] *= np.linspace(1, 0, fade)
    i = int(WATER[0] * sr)
    out[i:i + n] += w[:, None] * WATER_GAIN
    out = out[:int(END * sr)]
    peak = np.abs(out).max()
    out = limit(out)
    sf.write(path, out.astype(np.float32), sr, subtype="PCM_24")
    return peak


# --------------------------------------------------------------------------- render


def compose(T, clips, segs, under, over):
    seg = next(s for s in segs if s.start <= T < s.end)
    if seg.clip:
        img = Image.fromarray(clips.frame(seg.clip, seg.src(T))).convert("RGBA")
    else:
        img = Image.new("RGBA", (W, H), INK)
    for f in seg.fx:
        img = f(img, T)
    for o in under:
        if o.start <= T < o.end:
            o.draw(img, T - o.start)
    if seg.cam:
        img = camera(img, *seg.cam(T))
    for f in seg.post:
        img = f(img, T).convert("RGBA")
    for o in over:
        if o.start <= T < o.end:
            o.draw(img, T - o.start)
    return img


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--start", type=float, default=0.0)
    ap.add_argument("--end", type=float, default=END)
    ap.add_argument("--out", default=str(PV / "out" / "system-prompt-pv.mp4"))
    args = ap.parse_args()
    out = pathlib.Path(args.out)
    out.parent.mkdir(exist_ok=True)

    clips = Clips()
    segs, under, over = timeline(clips)
    with tempfile.TemporaryDirectory() as tmp:
        wav = pathlib.Path(tmp) / "mix.wav"
        print(f"mix peak {mix(wav):.2f}")
        enc = subprocess.Popen(
            ["ffmpeg", "-v", "error", "-y", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{W}x{H}", "-r", str(FPS),
             "-i", "-", "-ss", f"{args.start}", "-t", f"{args.end - args.start}", "-i", str(wav),
             "-map", "0:v", "-map", "1:a", "-c:v", "libx264", "-preset", "slow", "-crf", "17", "-pix_fmt", "yuv420p",
             "-c:a", "aac", "-b:a", "256k", "-movflags", "+faststart", "-shortest", str(out)],
            stdin=subprocess.PIPE)
        first, last = round(args.start * FPS), round(args.end * FPS)
        for k in range(first, last):
            T = k / FPS
            enc.stdin.write(compose(T, clips, segs, under, over).convert("RGB").tobytes())
            if k % (FPS * 10) == 0:
                print(f"{T:6.1f} s", flush=True)
        enc.stdin.close()
        enc.wait()
    print(f"wrote {out}")


if __name__ == "__main__":
    main()
