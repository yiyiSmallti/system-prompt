"""Assemble the per-character group version of 维护时刻 over the A1 instrumental.

Each cover's lead vocal is mapped onto A1's timeline (DTW against A1's lead vocal,
fitted as a constant tempo ratio per cover and an offset per line), cut per line,
stretched to A1's line length with atempo, and placed at A1's time.

usage: python group_mix.py <pv/music dir> <output basename> [per-singer stem dir]
"""
import pathlib
import subprocess
import sys
import tempfile

import librosa
import numpy as np
import soundfile as sf

MUSIC = pathlib.Path(sys.argv[1])
OUT = sys.argv[2]
STEM_DIR = pathlib.Path(sys.argv[3]) if len(sys.argv) > 3 else None
SR = 48000
COVERS = {"G": "gc1", "C": "cc1", "D": "dc2"}
# group pan: -1 left, +1 right
PAN = {"G": -0.3, "C": 0.0, "D": 0.3}
GROUP_GAIN = 0.6
FADE = 0.03
# A1 time where the final chorus ends ("明天学校见"); after this DTW is unreliable
LAST_ANCHOR = 148.5

# (A1 start, A1 end, singers, lyric) -- each character sings the lines that describe her;
# lines about the protagonist, the system or the title hook are sung by all three.
# Boundaries sit just before the first sung word.
SEGMENTS = [
    (3.40, 13.50, "GCD", "System Prompt～ / 叮！你已被指派！"),
    (20.40, 33.39, "G", "樱花飘飘 … 第一 第二 第三 不用紧张"),
    (33.39, 44.30, "C", "邻座推推 小眼镜 / 她说不紧张 (书脊捏弯啦！)"),
    (44.30, 69.40, "D", "主歌 2 + 预副歌"),
    (69.40, 74.55, "GCD", "维护时刻 叮叮叮 潜进缓存海 噗通通"),
    (74.55, 85.95, "D", "噪声体 别乱叫 … 深渊之鲸 甩甩尾"),
    (85.95, 90.50, "GCD", "One More 再来一次！"),
    (116.60, 122.10, "D", "对抗样本！… 找对地方 一下就够了！"),
    (122.10, 123.80, "GCD", "全员倒地！集成！集成！"),
    (123.80, 129.75, "D", "战斗胜利 吃饭饭！"),
    (129.75, 134.75, "GCD", "维护时刻 叮叮叮 潜进缓存海 噗通通"),
    (134.75, 151.60, "D", "不要答案 … 明天学校见！"),
    (151.60, 158.95, "GCD", "System Prompt～ (System Prompt～)"),
    (158.95, 166.00, "G", "距离评测日 还有二十八天！"),
]


def load(name):
    y, sr = sf.read(MUSIC / name, dtype="float32", always_2d=True)
    assert sr == SR, (name, sr)
    return y


def active_rms(y):
    m = y.mean(axis=1)
    n = SR // 50
    frames = m[: len(m) // n * n].reshape(-1, n)
    rms = np.sqrt((frames ** 2).mean(axis=1))
    return np.sqrt((rms[rms > rms.max() * 0.03] ** 2).mean())


def dtw_map(cover_name, ref_feats):
    """Return mapper(a0, a1) -> (c0, c1): an A1 time span to the matching cover span.

    The cover runs at a constant tempo ratio r to A1, so each span is a straight line
    with slope r; its intercept comes from DTW path points where both vocals are
    active. Silent stretches are skipped because DTW wanders there.
    """
    hop, sr = 1024, 22050

    def feats(name):
        y, _ = librosa.load(MUSIC / name, sr=sr, mono=True)
        chroma = librosa.feature.chroma_cqt(y=y, sr=sr, hop_length=hop)
        mfcc = librosa.feature.mfcc(y=y, sr=sr, hop_length=hop, n_mfcc=20)[1:]
        mfcc = (mfcc - mfcc.mean(axis=1, keepdims=True)) / (mfcc.std(axis=1, keepdims=True) + 1e-6)
        rms = librosa.feature.rms(y=y, hop_length=hop)
        act = librosa.amplitude_to_db(rms, ref=np.percentile(rms, 99)) > -30
        return np.vstack([chroma * 2, mfcc * 0.5, act * 3.0])

    ref = ref_feats if ref_feats is not None else feats("a1-lead-vocal.wav")
    q = feats(cover_name)
    _, wp = librosa.sequence.dtw(X=ref, Y=q, metric="cosine", global_constraints=True, band_rad=0.15)
    wp = wp[::-1]
    both = (ref[-1, wp[:, 0]] > 0) & (q[-1, wp[:, 1]] > 0)
    ta = librosa.frames_to_time(wp[both, 0], sr=sr, hop_length=hop)
    tc = librosa.frames_to_time(wp[both, 1], sr=sr, hop_length=hop)

    # pooled slope over the two long sung stretches, each with its own intercept
    xs, ys = [], []
    for lo, hi in ((19, 90), (116, LAST_ANCHOR)):
        k = (ta >= lo) & (ta <= hi)
        x, y = ta[k], tc[k]
        for _ in range(3):
            fit = np.polyfit(x, y, 1)
            keep = np.abs(y - np.polyval(fit, x)) < 0.3
            x, y = x[keep], y[keep]
        xs.append(x - x.mean())
        ys.append(y - y.mean())
    x, y = np.concatenate(xs), np.concatenate(ys)
    r = float((x * y).sum() / (x * x).sum())

    def mapper(a0, a1):
        lo = a0
        if LAST_ANCHOR - a0 < 4:
            # spans after the final chorus extend the last stretch DTW can still follow
            lo = LAST_ANCHOR - 15
        k = (ta >= lo) & (ta <= min(a1, LAST_ANCHOR))
        slope = r
        if min(a1, LAST_ANCHOR) - lo > 8:
            # long spans get their own tempo ratio; covers drift a little between sections
            x, y = ta[k], tc[k]
            for _ in range(3):
                fit = np.polyfit(x, y, 1)
                keep = np.abs(y - np.polyval(fit, x)) < 0.3
                x, y = x[keep], y[keep]
            slope = float(fit[0])
        b = float(np.median(tc[k] - slope * ta[k]))
        return slope * a0 + b, slope * a1 + b

    return mapper, ref, r


def stretch(src, c0, c1, a_len, tmp):
    out = pathlib.Path(tmp) / "seg.wav"
    tempo = (c1 - c0) / a_len
    subprocess.run(
        ["ffmpeg", "-v", "error", "-y", "-ss", f"{c0:.4f}", "-to", f"{c1:.4f}", "-i", str(src),
         "-af", f"atempo={tempo:.6f}", "-ar", str(SR), str(out)],
        check=True,
    )
    y, _ = sf.read(out, dtype="float32", always_2d=True)
    n = int(round(a_len * SR))
    return np.pad(y, ((0, max(0, n - len(y))), (0, 0)))[:n]


def main():
    inst = load("a1-without-lead-vocal.wav")
    a1_rms = active_rms(load("a1-lead-vocal.wav"))
    buses = {key: np.zeros_like(inst) for key in COVERS}
    ref = None
    with tempfile.TemporaryDirectory() as tmp:
        for key, name in COVERS.items():
            src = MUSIC / f"{name}-lead-vocal.wav"
            gain = a1_rms / active_rms(load(src.name))
            mapper, ref, r = dtw_map(src.name, ref)
            print(f"{name}: level gain {20*np.log10(gain):+.1f} dB, tempo ratio {r:.4f}")
            for a0, a1, who, lyric in SEGMENTS:
                if key not in who:
                    continue
                pre, post = (FADE if a0 > FADE else 0.0), FADE
                c0, c1 = mapper(a0 - pre, a1 + post)
                seg = stretch(src, float(c0), float(c1), a1 - a0 + pre + post, tmp)
                ramp = int(FADE * SR)
                env = np.ones(len(seg), dtype="float32")
                env[:ramp] = np.linspace(0, 1, ramp)
                env[-ramp:] = np.linspace(1, 0, ramp)
                seg *= env[:, None] * gain
                if len(who) > 1:
                    p = PAN[key]
                    seg *= GROUP_GAIN * np.array([np.sqrt((1 - p) / 2), np.sqrt((1 + p) / 2)], dtype="float32") * np.sqrt(2)
                start = int(round((a0 - pre) * SR))
                end = min(start + len(seg), len(inst))
                buses[key][start:end] += seg[: end - start]
    bus = sum(buses.values())
    mix = inst + bus
    peak = np.abs(mix).max()
    if peak > 0.98:
        mix *= 0.98 / peak
    print(f"peak before limit {20*np.log10(peak):+.2f} dBFS")
    sf.write(MUSIC / f"{OUT}.wav", mix, SR, subtype="PCM_16")
    sf.write(MUSIC / f"{OUT}-vocals.wav", bus, SR, subtype="PCM_16")
    if STEM_DIR:
        for key, b in buses.items():
            sf.write(STEM_DIR / f"{OUT}-{COVERS[key]}.wav", b, SR, subtype="PCM_16")
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(MUSIC / f"{OUT}.wav"), "-b:a", "256k",
                    str(MUSIC / f"{OUT}.mp3")], check=True)


if __name__ == "__main__":
    main()
