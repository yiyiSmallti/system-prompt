"""Synthesize the PV's 8-bit sound effects into pv/sfx/ (48 kHz mono WAV).

Square waves and bit-crushed noise, to sit with the chiptune song.

usage: python sfx.py <pv dir>
"""
import pathlib
import sys

import numpy as np
import soundfile as sf

SR = 48000
rng = np.random.default_rng(7)


def t_(dur):
    return np.arange(int(dur * SR)) / SR


def square(freq, dur, duty=0.5):
    """Square wave; freq may be a scalar or a per-sample array (for glides)."""
    f = np.broadcast_to(np.asarray(freq, dtype=float), (int(dur * SR),))
    phase = np.cumsum(f) / SR
    return np.where(phase % 1 < duty, 1.0, -1.0)


def env(n, attack=0.004, release=0.05, curve=1.0):
    e = np.ones(n)
    a, r = int(attack * SR), int(release * SR)
    if a:
        e[:a] = np.linspace(0, 1, a)
    if r:
        e[-r:] *= np.linspace(1, 0, r) ** curve
    return e


def decay(n, k):
    return np.exp(-np.arange(n) / SR * k)


def noise(dur):
    return rng.uniform(-1, 1, int(dur * SR))


def crush(x, steps=16, hold=4):
    x = np.round(x * steps) / steps
    return np.repeat(x[::hold], hold)[: len(x)]


def lowpass(x, cutoff):
    a = np.exp(-2 * np.pi * cutoff / SR)
    y = np.empty_like(x)
    acc = 0.0
    for i, v in enumerate(x):
        acc = (1 - a) * v + a * acc
        y[i] = acc
    return y


def notes(seq, duty=0.5):
    """seq: [(freq, dur), ...] played back to back."""
    out = []
    for f, d in seq:
        s = square(f, d, duty)
        out.append(s * env(len(s), 0.002, min(0.02, d / 3)))
    return np.concatenate(out)


def make():
    out = {}
    out["cursor"] = notes([(1568, 0.035)], 0.25) * 0.5
    out["confirm"] = notes([(988, 0.05), (1976, 0.11)], 0.25) * 0.55
    c = notes([(988, 0.07), (1319, 0.38)], 0.5)
    out["coin"] = c * np.concatenate([np.ones(int(0.07 * SR)), decay(len(c) - int(0.07 * SR), 7)]) * 0.45
    arp = [523, 659, 784, 1047, 1319, 1568]
    lv = notes([(f, 0.055) for f in arp] + [(2093, 0.35)], 0.25)
    out["levelup"] = lv * np.concatenate([np.ones(len(lv) - int(0.35 * SR)), decay(int(0.35 * SR), 6)]) * 0.45
    out["key"] = crush(noise(0.012) * decay(int(0.012 * SR), 400), 8, 2) * 0.35
    shot = np.concatenate([noise(0.02) * decay(int(0.02 * SR), 200), np.zeros(int(0.05 * SR)),
                           noise(0.09) * decay(int(0.09 * SR), 45)])
    out["shutter"] = crush(shot, 12, 3) * 0.7
    g = np.concatenate([crush(noise(0.06), 6, 12), square(np.linspace(2400, 300, int(0.08 * SR)), 0.08, 0.3),
                        crush(noise(0.05), 4, 20), square(np.linspace(200, 1800, int(0.06 * SR)), 0.06)])
    out["glitch"] = g * env(len(g), 0.002, 0.02) * 0.45
    drop = square(np.geomspace(220, 45, int(0.28 * SR)), 0.28) * decay(int(0.28 * SR), 9)
    hit = crush(noise(0.28), 10, 6) * decay(int(0.28 * SR), 25)
    out["slam"] = (drop * 0.6 + hit * 0.5) * 0.8
    out["rise"] = square(np.geomspace(330, 1760, int(0.26 * SR)), 0.26, 0.25) * env(int(0.26 * SR), 0.005, 0.04) * 0.4
    sw = lowpass(noise(0.35), 2500) * np.sin(np.linspace(0, np.pi, int(0.35 * SR))) ** 2
    out["whoosh"] = sw / np.abs(sw).max() * 0.5
    # comic 8-bit snore: wobbling low glide in, whistle out
    inhale = square(np.linspace(90, 140, int(0.7 * SR)) + 8 * np.sin(t_(0.7) * 60), 0.7, 0.3)
    exhale = square(np.linspace(1400, 900, int(0.5 * SR)), 0.5, 0.25)
    out["snore"] = np.concatenate([inhale * env(len(inhale), 0.1, 0.15) * 0.4,
                                   np.zeros(int(0.08 * SR)), exhale * env(len(exhale), 0.05, 0.2) * 0.18])
    slap = crush(noise(0.12), 10, 3) * decay(int(0.12 * SR), 40) + square(np.geomspace(160, 60, int(0.12 * SR)), 0.12) * decay(int(0.12 * SR), 30) * 0.5
    out["slap"] = slap * 0.8
    strokes = []
    for d in (0.16, 0.12, 0.2, 0.1):
        s = lowpass(noise(d), 6000) - lowpass(noise(d), 1500) * 0.6
        strokes += [s * np.sin(np.linspace(0, np.pi, len(s))), np.zeros(int(0.05 * SR))]
    ch = np.concatenate(strokes)
    out["chalk"] = ch / np.abs(ch).max() * 0.35
    # underwater bed: brown-ish rumble with slow swell and sparse bubble blips (6 s, loopable)
    w = lowpass(noise(6.0), 180)
    w = w / np.abs(w).max() * (0.6 + 0.4 * np.sin(t_(6.0) * 2 * np.pi / 6.0))
    for _ in range(14):
        at = rng.uniform(0.1, 5.7)
        f0 = rng.uniform(500, 1200)
        b = np.sin(2 * np.pi * np.cumsum(np.linspace(f0, f0 * 1.8, int(0.05 * SR))) / SR) * decay(int(0.05 * SR), 60)
        i = int(at * SR)
        w[i:i + len(b)] += b * 0.25
    fade = int(0.3 * SR)
    w[:fade] *= np.linspace(0, 1, fade)
    w[-fade:] *= np.linspace(1, 0, fade)
    out["water"] = w * 0.5
    beat = np.zeros(int(1.0 * SR))
    for at, k in ((0.0, 1.0), (0.22, 0.7)):
        n = int(0.16 * SR)
        thump = np.sin(2 * np.pi * np.cumsum(np.geomspace(70, 38, n)) / SR) * decay(n, 22)
        i = int(at * SR)
        beat[i:i + n] += thump * k
    out["heartbeat"] = beat * 0.8
    crack = np.concatenate([crush(noise(0.04), 6, 2) * decay(int(0.04 * SR), 80)] * 3)
    out["crack"] = np.concatenate([crack, crush(noise(0.3), 5, 30) * decay(int(0.3 * SR), 10)]) * 0.6
    return out


def main():
    dest = pathlib.Path(sys.argv[1]) / "sfx"
    dest.mkdir(exist_ok=True)
    for name, x in make().items():
        x = np.clip(x, -1, 1).astype(np.float32)
        sf.write(dest / f"{name}.wav", x, SR, subtype="PCM_16")
        print(f"{name}.wav {len(x) / SR:.2f} s")


if __name__ == "__main__":
    main()
