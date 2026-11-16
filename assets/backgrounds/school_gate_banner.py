# 校门背景（school_gate.png）上那条横幅的字：剧本里写的「共生计划 第一期 欢迎AI特别班入学」，原图生成时只有一条白布。
# 字先排在一张平的画布上，按横幅四角做透视、按布的下垂弯一点，再用正片叠底印到布上，保留布上的褶皱和光影。
#
#   python3 assets/backgrounds/school_gate_banner.py <原图.png> <输出.png>
#
# 原图是 54b4e8e 提交的 school_gate.png（1672×940，LFS sha256 6da593ab…），四角坐标按这张图量的；要重做就先 git lfs 取回那一版。
import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

src, out = sys.argv[1], sys.argv[2]
FONT = "game/Assets/SystemPrompt/Resources/Fonts/NotoSansSC-Black.ttf"
BOLD = "game/Assets/SystemPrompt/Resources/Fonts/NotoSansSC-Bold.ttf"

img = Image.open(src).convert("RGB")
W, H = 1400, 200
canvas = Image.new("RGBA", (W, H), (0, 0, 0, 0))
d = ImageDraw.Draw(canvas)
navy, red = (30, 52, 104, 255), (196, 44, 56, 255)
small = ImageFont.truetype(BOLD, 44)
big = ImageFont.truetype(FONT, 104)
top = "共生计划 · 第一期"
main = "欢迎 AI 特别班入学"
tw = d.textlength(top, font=small)
d.text(((W - tw) / 2, 4), top, font=small, fill=red)
mw = d.textlength(main, font=big)
d.text(((W - mw) / 2, 46), main, font=big, fill=navy)
# 两头各一道红色竖条，像印好的横幅。
for x in (60, W - 60 - 14):
    d.rectangle((x, 30, x + 14, 170), fill=red)
canvas = canvas.filter(ImageFilter.GaussianBlur(0.8))

# 横幅里能写字的部分（原图坐标），左上、右上、右下、左下。
quad = [(600, 252), (1300, 280), (1300, 360), (600, 346)]

def coeffs(dst, src_pts):
    # PIL 的 PERSPECTIVE 要的是从输出坐标映回输入坐标的 8 个系数。
    a = []
    for (x, y), (u, v) in zip(dst, src_pts):
        a.append([x, y, 1, 0, 0, 0, -u * x, -u * y])
        a.append([0, 0, 0, x, y, 1, -v * x, -v * y])
    b = [c for p in src_pts for c in p]
    return np.linalg.solve(np.array(a, float), np.array(b, float))

flat = [(0, 0), (W, 0), (W, H), (0, H)]
warped = canvas.transform(img.size, Image.PERSPECTIVE, tuple(coeffs(quad, flat)), Image.BICUBIC)
# 布中间往下垂：每一列按抛物线往下挪几个像素。
arr = np.array(warped)
sag = np.zeros_like(arr)
x0, x1 = quad[0][0], quad[1][0]
for x in range(x0, x1 + 1):
    t = (x - x0) / (x1 - x0)
    dy = int(round(10 * 4 * t * (1 - t)))
    sag[dy:, x] = arr[:arr.shape[0] - dy, x]
ink = sag[..., :3].astype(float) / 255
alpha = sag[..., 3:4].astype(float) / 255 * 0.92
base = np.array(img).astype(float) / 255
res = base * (1 - alpha) + base * ink * alpha * 1.15
Image.fromarray((np.clip(res, 0, 1) * 255).astype(np.uint8)).save(out)
print("wrote", out)
