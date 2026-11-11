"""Cut the in-game BGM loops out of the Suno instrumentals in game/Music/ (prompts and picks in its README).

Suno holds a steady tempo that creeps up by a beat or two per minute, so the beat grid comes from the audio: a
spectral-flux onset envelope, its autocorrelation near the tempo the prompt asked for, and dynamic programming
for the beats themselves (Ellis 2007, kept tight so it never slips onto the offbeat); the downbeat is the beat
phase where the spectrum changes most. The loop is the pair of bar lines, a multiple of 8 bars and at least the
given length apart, whose 8 beats either side sound most alike in timbre (log-band spectrum) and harmony
(chroma), so jumping back from the end lands where the music could have gone on. The end is nudged by up to
20 ms to where its waveform lines up with the start, and the seam is crossfaded over 30 ms. Suno builds up over
a song, so a loop's last 2 bars come out 1.5–2 dB louder than its first 2; the gain is tilted across the loop
(linear in dB) to bring them level, otherwise every wrap is a step down in volume.

A track marked as having a lead-in also gets <name>_intro.wav, the song from its first sample up to where the
loop starts: the game plays that once and goes on into the loop (the results' fanfare, the awakening theme's
build-up).

Each loop is then played twice in memory and the sample step and spectral flux at the wrap are printed next to
the same measures over the rest of the file: a good seam has an ordinary step and about the flux its start bar
line has in the song.

usage: python3 game/Tools/bgm_loops.py   (from the repository root; needs ffmpeg and numpy)
"""
import pathlib
import subprocess

import numpy as np

ROOT = pathlib.Path(__file__).resolve().parents[2]
MUSIC = ROOT / "game/Music"
OUT = ROOT / "game/Assets/SystemPrompt/Resources/Audio/BGM"
SR = 48000
HOP = 480  # 10 ms analysis frames
NFFT = 2048
SEAM = int(0.03 * SR)

# name: (instrumental in game/Music, tempo the style prompt asked for, shortest loop in seconds[, has a lead-in])
LOOPS = {
    "story": ("story-2a16f8c3.wav", 128, 90),
    "sea": ("sea-b984126f.wav", 80, 60),
    "battle": ("battle-64c1c962.wav", 165, 90),
    "deep": ("deep-f5d66962.wav", 96, 60),
    "awakening": ("awakening-24893ddb.wav", 172, 90, True),
    "results": ("results-8f46a63a.wav", 112, 30, True),
    "shadow": ("shadow-e4bc3700.wav", 72, 60),
}


def read(path):
    pcm = subprocess.run(["ffmpeg", "-v", "error", "-i", str(path), "-ac", "2", "-ar", str(SR), "-f", "f32le", "-"],
                         capture_output=True, check=True).stdout
    return np.frombuffer(pcm, np.float32).reshape(-1, 2)


def write(path, audio):
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-f", "f32le", "-ar", str(SR), "-ac", "2", "-i", "-",
                    "-c:a", "pcm_s16le", str(path)],
                   input=np.ascontiguousarray(audio, np.float32).tobytes(), check=True)


def spectrum(mono):
    """Log-magnitude in 48 log-spaced bands, one row per frame (frame t starts at sample t * HOP)."""
    n = 1 + (len(mono) - NFFT) // HOP
    mag = np.abs(np.fft.rfft(mono[np.arange(NFFT) + HOP * np.arange(n)[:, None]] * np.hanning(NFFT), axis=1))
    edges = np.unique((np.geomspace(30, 16000, 49) * NFFT / SR).astype(int))
    return np.log1p(np.add.reduceat(mag, edges, axis=1)[:, :-1] / np.diff(edges) * 100)


def flux(bands):
    return np.maximum(np.diff(bands, axis=0, prepend=bands[:1]), 0).sum(axis=1)


def beat_grid(bands, bpm):
    """Beat frames: the beat length is the autocorrelation peak within 35% of `bpm`, the beats a tight DP path."""
    env = flux(bands)
    env = np.maximum(env - np.convolve(env, np.ones(50) / 50, mode="same"), 0)  # onsets above the local level
    env /= env.std()
    ac = np.correlate(env, env, mode="full")[len(env) - 1:]
    lo, hi = int(SR / HOP * 60 / bpm / 1.35), int(SR / HOP * 60 / bpm * 1.35)
    k = lo + int(np.argmax(ac[lo:hi]))
    period = k + 0.5 * (ac[k - 1] - ac[k + 1]) / (ac[k - 1] - 2 * ac[k] + ac[k + 1])
    score, back = env.copy(), np.full(len(env), -1)
    lo, hi = int(period * 0.8), int(period * 1.25)
    for t in range(lo, len(env)):
        prev = np.arange(max(0, t - hi), t - lo + 1)
        cand = score[prev] - 1600 * np.log((t - prev) / period) ** 2
        best = int(np.argmax(cand))
        score[t], back[t] = env[t] + cand[best], prev[best]
    t = len(env) - hi + int(np.argmax(score[-hi:]))
    beats = []
    while t >= 0:
        beats.append(t)
        t = back[t]
    return np.array(beats[::-1])


def timbre(bands, beats):
    """Each beat's mean band spectrum, centred on the track's average and normalised."""
    f = np.array([bands[a:b].mean(axis=0) for a, b in zip(beats, beats[1:])])
    f -= f.mean(axis=0)
    return f / np.linalg.norm(f, axis=1, keepdims=True)


def chroma(mono, beats):
    """Each beat's energy per pitch class from 60 Hz to 4 kHz, normalised."""
    size = 8192
    freqs = np.fft.rfftfreq(size, 1 / SR)
    keep = (freqs > 60) & (freqs < 4000)
    pitch = (np.round(12 * np.log2(freqs[keep] / 440)) % 12).astype(int)
    out = []
    for a, b in zip(beats * HOP, beats[1:] * HOP):
        starts = range(a, max(a + 1, b - size), size // 2)
        mag = np.mean([np.abs(np.fft.rfft(np.pad(mono[s:s + size], (0, max(0, s + size - len(mono)))) * np.hanning(size)))
                       for s in starts], axis=0)[keep]
        c = np.bincount(pitch, mag ** 2, minlength=12)
        out.append(c / (np.linalg.norm(c) + 1e-9))
    return np.array(out)


def best_loop(beats, tim, chro, phase, shortest):
    """The downbeat pair (i, j), a multiple of 32 beats and at least `shortest` seconds apart, whose 8 beats either side match best."""
    seconds = beats * HOP / SR
    n = len(tim)
    pairs = [(i, i + length) for length in range(32, n, 32) for i in range(8 + (phase - 8) % 4, n - length - 8, 4)
             if seconds[i + length] - seconds[i] >= shortest]
    score = lambda p: (float(np.mean([tim[p[0] + k] @ tim[p[1] + k] for k in range(-8, 8)])),
                       float(np.mean([chro[p[0] + k] @ chro[p[1] + k] for k in range(-8, 8)])))
    i, j = max(pairs, key=lambda p: sum(score(p)))
    return i, j, score((i, j))


def align(mono, a, b):
    """`b` moved by up to 20 ms to where the waveform around it correlates best with the one around `a`."""
    reach, before, after = int(0.02 * SR), 2048, 4096
    ref = mono[a - before:a + after]
    corr = np.correlate(mono[b - before - reach:b + after + reach], ref, mode="valid")
    return b - reach + int(np.argmax(corr))


def peak_flux(fl, s):
    """The strongest flux among frames starting within NFFT samples of sample s."""
    return fl[max(0, (s - NFFT) // HOP):(s + NFFT) // HOP + 1].max()


def seam_report(loop, bar_lines, source_start):
    """Sample step and spectral flux at the wrap of the loop played twice, against the rest of the file."""
    twice = np.concatenate([loop, loop])
    n = len(loop)
    steps = np.abs(np.diff(twice, axis=0)).max(axis=1)
    step = steps[n - 1]
    fl = flux(spectrum(twice.mean(axis=1)))
    wrap = peak_flux(fl, n)
    bars = np.median([peak_flux(fl, s) for s in bar_lines])
    return (f"step {step:.4f} (file p{(steps < step).mean() * 100:.0f}, p99 {np.percentile(steps, 99):.4f}), "
            f"flux {wrap:.1f} = {wrap / bars:.2f}× the median at its other bar lines (file p{(fl < wrap).mean() * 100:.0f}; "
            f"{source_start:.1f} at the same bar line in the song)")


def main():
    for name, (source, bpm, shortest, *lead_in) in LOOPS.items():
        audio = read(MUSIC / source)
        mono = audio.mean(axis=1)
        bands = spectrum(mono)
        beats = beat_grid(bands, bpm)
        tim, chro = timbre(bands, beats), chroma(mono, beats)
        change = np.r_[0, 1 - np.sum(tim[1:] * tim[:-1], axis=1)]
        phase = int(np.argmax([change[p::4].mean() for p in range(4)]))
        i, j, (t_score, h_score) = best_loop(beats, tim, chro, phase, shortest)
        a = beats[i] * HOP
        b = align(mono, a, beats[j] * HOP)
        level = lambda x: 10 * np.log10(np.mean(x ** 2))
        tilt = level(audio[a:beats[i + 8] * HOP]) - level(audio[beats[j - 8] * HOP:b])
        loop = audio[a:b] * 10 ** (tilt * np.arange(b - a)[:, None] / (b - a) / 20)
        t = np.linspace(0, np.pi / 2, SEAM)[:, None]
        follow = audio[b:b + SEAM] * 10 ** (tilt / 20)  # what follows the end, at the end's gain, fades into the start
        loop[:SEAM] = loop[:SEAM] * np.sin(t) + follow * np.cos(t)
        write(OUT / f"{name}.wav", loop)
        if lead_in and lead_in[0]:
            write(OUT / f"{name}_intro.wav", audio[:a])
        tempo = 60 * SR / HOP * (len(beats) - 1) / (beats[-1] - beats[0])
        print(f"{name}: {source} at {tempo:.1f} BPM, loop {a / SR:.2f}–{b / SR:.2f} s ({(j - i) // 4} bars, "
              f"timbre {t_score:.3f}, harmony {h_score:.3f}), {len(loop) / SR:.1f} s, tilted {tilt:+.1f} dB")
        print("  seam:", seam_report(read(OUT / f"{name}.wav"), [beats[k] * HOP - a for k in range(i + 4, j, 4)],
                                     peak_flux(flux(spectrum(mono)), a)))


if __name__ == "__main__":
    main()
