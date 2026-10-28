# Makes the transition textures in Assets/SystemPrompt/Resources/Art/Transitions from the generated
# pictures in assets/ui/transitions (see the README there): pictures with a transparent background are
# flattened onto black, everything is saved as JPG, the circuit board scaled to 1024 px, the light streaks
# made to tile sideways (they scroll across the battle and awakening screens). Also draws
# noise.png: R tileable fractal noise (soft dissolves), G white noise (static), B a second fractal noise.
# Needs Pillow and numpy. Run from the repository root:
#   python3 game/Tools/transition_textures.py
import os
import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SOURCE = os.path.join(ROOT, "assets/ui/transitions")
OUT = os.path.join(ROOT, "game/Assets/SystemPrompt/Resources/Art/Transitions")
N = 512


def flat(name):
    a = np.asarray(Image.open(os.path.join(SOURCE, name + ".png"))).astype(np.float32)
    if a.shape[-1] == 4:
        a = a[..., :3] * (a[..., 3:4] / 255.0)
    return Image.fromarray(a.clip(0, 255).astype(np.uint8))


def tile_x(image):
    # Crossfades the picture into a copy shifted by half its width: the edges come from the copy, whose
    # two ends meet, and the copy's own seam in the middle is hidden under the original.
    a = np.asarray(image).astype(np.float32)
    w = a.shape[1]
    t = 1 - np.abs(np.linspace(-1, 1, w))
    t = (t * t * (3 - 2 * t))[None, :, None]
    return Image.fromarray((a * t + np.roll(a, w // 2, axis=1) * (1 - t)).clip(0, 255).astype(np.uint8))


def value_noise(cells, seed):
    g = np.random.default_rng(seed).random((cells, cells))
    x = np.arange(N) * cells / N
    i0 = np.floor(x).astype(int)
    f = x - i0
    f = f * f * (3 - 2 * f)
    i1 = (i0 + 1) % cells  # wraps, so the texture tiles
    rows0 = g[i0][:, i0] * (1 - f)[None, :] + g[i0][:, i1] * f[None, :]
    rows1 = g[i1][:, i0] * (1 - f)[None, :] + g[i1][:, i1] * f[None, :]
    return rows0 * (1 - f)[:, None] + rows1 * f[:, None]


def fbm(seed):
    total, amp = np.zeros((N, N)), 1.0
    for k, cells in enumerate([4, 8, 16, 32, 64, 128]):
        total += value_noise(cells, seed + k) * amp
        amp *= 0.55
    return (total - total.min()) / (total.max() - total.min())


def main():
    os.makedirs(OUT, exist_ok=True)
    for name in ["data_sea", "terminal", "shards", "corrupt"]:
        flat(name).save(os.path.join(OUT, name + ".jpg"), quality=92)
    tile_x(flat("beams")).save(os.path.join(OUT, "beams.jpg"), quality=92)
    flat("circuit").resize((1024, 1024), Image.LANCZOS).save(os.path.join(OUT, "circuit.jpg"), quality=92)
    channels = [fbm(11), np.random.default_rng(7).random((N, N)), fbm(41), np.ones((N, N))]
    rgba = (np.stack(channels, -1) * 255).round().astype(np.uint8)
    Image.fromarray(rgba).save(os.path.join(OUT, "noise.png"))


if __name__ == "__main__":
    main()
