"""Contact sheet of rendered frames: rows = views, columns = evenly spaced frames.

Run: python3 sheet.py FRAMES_DIR/<action> OUT.jpg [COLUMNS]
"""
import os
import sys

from PIL import Image, ImageDraw

src, out = sys.argv[1], sys.argv[2]
cols = int(sys.argv[3]) if len(sys.argv) > 3 else 6
views = [v for v in ('front34', 'side', 'back34', 'front', 'hips', 'shoulders', 'tail') if os.path.isdir(os.path.join(src, v))]
rows = []
for v in views:
    files = sorted(f for f in os.listdir(os.path.join(src, v)) if f.endswith('.png'))
    pick = [files[round(i * len(files) / cols)] for i in range(cols)]
    rows.append([(f, Image.open(os.path.join(src, v, f)).convert('RGB')) for f in pick])
w, h = rows[0][0][1].size
s = 0.5
tw, th = int(w * s), int(h * s)
sheet = Image.new('RGB', (tw * cols, th * len(rows)), 'white')
d = ImageDraw.Draw(sheet)
for r, row in enumerate(rows):
    for c, (f, im) in enumerate(row):
        sheet.paste(im.resize((tw, th)), (c * tw, r * th))
        d.text((c * tw + 6, r * th + 4), '%s %s' % (views[r], f[:-4]), fill='black')
sheet.save(out, quality=88)
print(out, sheet.size)
