import re, sys
from PIL import Image, ImageDraw, ImageFont
ROOT = "/Volumes/OpenClawSSD/System prompt"
src = open(f"{ROOT}/game/Assets/SystemPrompt/Scripts/Dungeon/DungeonData.cs", encoding="utf-8").read()
block = src[src.index('["cache_sea_deep"]'):src.index('public static DungeonDef Get')]
rows = re.findall(r'^\s*"([#.=%A-Za-z0-9]+)",\s*$', block[block.index("Map = new[]"):block.index("Zones")], re.M)
H, W = len(rows), len(rows[0])
assert all(len(r) == W for r in rows), [len(r) for r in rows]
zones = []
for m in re.finditer(r'Name = "([^"]+)", Tag = "([^"]+)", Cells = new RectInt\((\d+), (\d+), (\d+), (\d+)\), Style = ZoneStyle\.(\w+)', block):
    n, t, x, y, w, h, s = m.groups(); zones.append((n, t, int(x), int(y), int(w), int(h), s))
look = {"Rift": "#FF5A3C", "Servers": "#FFA733", "RecycleBin": "#8A98B2", "Context": "#9A73FF", "WhiteHall": "#EEF4FF", "Channel": "#3380FF"}
heights = {"Rift": (9, 13), "Servers": (4.6, 5.2), "RecycleBin": (4, 7), "Context": (7, 9), "WhiteHall": (9, 12), "Channel": (5.6, 8.2)}
def zone_at(x, y):
    for z in zones:
        if z[2] <= x < z[2] + z[4] and z[3] <= y < z[3] + z[5]: return z
    return None
def hexc(h, a=255): h = h.lstrip("#"); return tuple(int(h[i:i+2], 16) for i in (0, 2, 4)) + (a,)
def mix(c1, c2, t): return tuple(int(c1[i] * (1 - t) + c2[i] * t) for i in range(3)) + (255,)
F = f"{ROOT}/game/Assets/SystemPrompt/Resources/Fonts/"
fB = lambda s: ImageFont.truetype(F + "NotoSansSC-Bold.ttf", s)
fR = lambda s: ImageFont.truetype(F + "NotoSansSC-Regular.ttf", s)
fM = lambda s: ImageFont.truetype(F + "JetBrainsMono-Bold.ttf", s)
C = 44; ML, MT = 70, 120; LW = 560
img = Image.new("RGBA", (ML + W * C + 40 + LW, MT + H * C + 130), hexc("#0B1020"))
d = ImageDraw.Draw(img, "RGBA")
d.text((ML, 24), "缓存海·深层 俯视布局（游戏地图 cache_sea_deep，1 格 = 4 m）", font=fB(30), fill=hexc("#E8F0FF"))
d.text((ML, 66), f"{W} × {H} 格 = {W*4} m × {H*4} m。Blender：X 向东、Y 向北、Z 向上，地图西南角 = (0, 0)。格 (列 c, 行 r) 的中心 = (4c+2, 4({H-1}-r)+2)", font=fR(19), fill=hexc("#9FB3D9"))
base = hexc("#121A30"); wallc = hexc("#26324F")
for y in range(H):
    for x in range(W):
        ch = rows[y][x]; z = zone_at(x, y); zc = hexc(look[z[6]] if z else look["Channel"])
        x0, y0 = ML + x * C, MT + y * C; box = (x0, y0, x0 + C - 1, y0 + C - 1)
        if ch == "#":
            d.rectangle(box, fill=hexc("#05070E"))
            for k in range(0, 2 * C, 8): d.line((x0 + max(0, k - C), y0 + min(C - 1, k), x0 + min(C - 1, k), y0 + max(0, k - C)), fill=mix(hexc("#05070E"), zc, 0.38), width=1)
        else:
            d.rectangle(box, fill=mix(base, zc, 0.42))
            d.rectangle(box, outline=mix(base, zc, 0.62))
        cx, cy = x0 + C / 2, y0 + C / 2
        if ch == "=":
            d.rectangle((x0 + 3, y0 + 3, x0 + C - 4, y0 + C - 4), fill=hexc("#5C6E99"), outline=hexc("#AFC0E6"))
            for k in range(-C, C, 9): d.line((x0 + 3 + max(0, k), y0 + 3 + max(0, -k), x0 + 3 + min(C - 7, k + C - 7), y0 + 3 + min(C - 7, C - 7 - k)), fill=hexc("#7F92BF"))
        elif ch == "%":
            for dx, dy in ((-8, 6), (0, -6), (8, 5)): d.line((cx + dx, cy + 14, cx + dx + 2, cy + dy - 8), fill=hexc("#3FE0A0"), width=3)
        elif ch == "L":
            d.rectangle(box, fill=hexc("#E8F0FF"))
        elif ch in "SGBFCDMQ" or ch.isdigit():
            spec = {"S": ("#3CE68C", "起"), "G": ("#FFFFFF", "终"), "B": ("#7BFFD2", "信"), "F": ("#FFD25A", "碎"), "C": ("#E0A040", "箱"),
                    "D": ("#F4F7FF", "白门"), "M": ("#FFE08A", "光板"), "Q": ("#FFE08A", "光源")}
            col, lab = spec.get(ch, ("#FF5A64", ch))
            if ch == "X":
                d.rectangle((x0 + 2, cy - 7, x0 + C - 3, cy + 7), fill=hexc(col)); d.rectangle((cx - 7, y0 + 2, cx + 7, y0 + C - 3), fill=hexc(col, 120))
            elif ch.isdigit():
                d.ellipse((cx - 15, cy - 15, cx + 15, cy + 15), fill=hexc("#FF4A5A"), outline=hexc("#FFFFFF")); d.text((cx, cy), ch, font=fM(18), fill=hexc("#FFFFFF"), anchor="mm")
            else:
                d.rounded_rectangle((x0 + 5, y0 + 5, x0 + C - 6, y0 + C - 6), 6, fill=hexc(col, 230))
                d.text((cx, cy), lab, font=fB(14 if len(lab) > 1 else 18), fill=hexc("#0B1020"), anchor="mm")
        elif ch.islower():
            d.text((cx, cy), ch, font=fM(15), fill=hexc("#8090B0"), anchor="mm")
# the lighthouse stands outside the map, 6 cells south of the bottom edge (LighthouseCell = (14, 29))
px = ML + 14.5 * C; py = MT + H * C + 30
d.polygon([(px, py + 18), (px - 9, py), (px + 9, py)], fill=hexc("#E8F0FF"))
d.text((px + 16, py + 2), "灯塔：在地图南边外，格 (14, 29)，Blender (58, −18)", font=fB(15), fill=hexc("#E8F0FF"))
# zone outlines and names
for n, t, x, y, w, h, s in zones:
    col = hexc(look[s])
    d.rectangle((ML + x * C + 1, MT + y * C + 1, ML + (x + w) * C - 2, MT + (y + h) * C - 2), outline=col, width=3)
    lo, hi = heights[s]
    lab = f"{n}  {t}"; sub = f"机柜高 {lo:g}–{hi:g} m"
    tx, ty = ML + x * C + 8, MT + y * C + 6
    tw = max(d.textlength(lab, font=fB(16)), d.textlength(sub, font=fR(13))) + 12
    d.rectangle((tx - 4, ty - 2, tx + tw, ty + 40), fill=hexc("#0B1020", 215))
    d.text((tx, ty), lab, font=fB(16), fill=col); d.text((tx, ty + 21), sub, font=fR(13), fill=hexc("#C8D4F0"))
# notes
d.text((ML + 14 * C + C / 2, MT + 1 * C - 6), "白门（游戏代码生成，镜子演出）", font=fB(13), fill=hexc("#0B1020"), anchor="mm")
# column/row indices and world coords
for x in range(W): d.text((ML + x * C + C / 2, MT - 12), str(x), font=fM(13), fill=hexc("#6F80A8"), anchor="mm")
for y in range(H): d.text((ML - 14, MT + y * C + C / 2), str(y), font=fM(13), fill=hexc("#6F80A8"), anchor="rm")
d.text((ML, MT + H * C + 10), "(0, 0) m  西南角", font=fR(15), fill=hexc("#9FB3D9"))
d.text((ML + W * C, MT - 30), f"({W*4}, {H*4}) m  东北角", font=fR(15), fill=hexc("#9FB3D9"), anchor="rb")
# scale bar
sx, sy = ML, MT + H * C + 62
for i in range(5):
    d.rectangle((sx + i * C, sy, sx + (i + 1) * C, sy + 10), fill=hexc("#E8F0FF" if i % 2 == 0 else "#445577"))
d.text((sx, sy + 16), "0", font=fM(14), fill=hexc("#C8D4F0")); d.text((sx + 5 * C, sy + 16), "20 m", font=fM(14), fill=hexc("#C8D4F0"), anchor="ma")
# north arrow
nx, ny = ML + W * C - 30, MT + H * C + 60
d.polygon([(nx, ny - 34), (nx - 12, ny), (nx + 12, ny)], fill=hexc("#E8F0FF")); d.text((nx, ny + 4), "N", font=fM(18), fill=hexc("#E8F0FF"), anchor="ma")
# legend
gx, gy = ML + W * C + 40, MT
d.text((gx, gy), "图例（地图字符）", font=fB(22), fill=hexc("#E8F0FF")); gy += 44
items = [
    ("#", "机柜墙（黑色斜线格）：整格实心，高度按区域，外圈更高"),
    ("=", "矮墙 1.3 m：挡脚不挡视线（回收站里是文件堆）"),
    ("%", "海草：只是长在那里，不挡路"),
    (".", "海床：可走（游戏代码画，不建模）"),
    ("S", "起点"), ("G", "出口（白门旁，光柱）"), ("B", "信标"),
    ("F", "剧情碎片"), ("C", "宝箱"),
    ("D", "封住的白门：游戏代码生成，留空"), ("M", "光板：解谜用，游戏代码生成，留空"),
    ("Q", "光源：游戏代码生成，留空"),
    ("1-6", "噪声体（游戏生成，不建模）"), ("a-z", "巡逻点（不建模）"),
]
for k, v in items:
    d.text((gx, gy), k, font=fM(18), fill=hexc("#FFD25A")); d.text((gx + 60, gy + 1), v, font=fR(17), fill=hexc("#C8D4F0")); gy += 30
gy += 16
d.text((gx, gy), "区域（RectInt 列, 行, 宽, 高）", font=fB(20), fill=hexc("#E8F0FF")); gy += 36
for n, t, x, y, w, h, s in zones:
    d.rectangle((gx, gy + 4, gx + 18, gy + 22), fill=hexc(look[s]))
    d.text((gx + 28, gy), f"{n}  ({x},{y},{w},{h})  {s}", font=fR(16), fill=hexc("#C8D4F0")); gy += 28
d.text((gx, gy + 6), "其余格子 = 主航道风格 Channel（地光 #3380FF）", font=fR(15), fill=hexc("#9FB3D9"))
out = sys.argv[1] if len(sys.argv) > 1 else __file__.replace(".py", ".png")
img.convert("RGB").save(out, optimize=True)
print(out, img.size, W, H, len(zones))
