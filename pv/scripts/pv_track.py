"""Build the PV soundtrack from the group mix and export a beat table on the PV timeline.

The only change to the song: after「标题党 不要信」the music tape-stops into silence
(shadow DeepSeek and the choice box) and comes back on the bar line before「深度思考」.
From that point on, PV time = A1 time + OFFSET.

usage: python pv_track.py <pv/music dir> <mix basename, e.g. group-mix-v2>
"""
import pathlib
import sys

import librosa
import numpy as np
import soundfile as sf

MUSIC = pathlib.Path(sys.argv[1])
MIX = sys.argv[2]
SR = 48000
STOP_AT = 78.75     # A1 time: tape-stop starts while「信」rings
STOP_LEN = 0.6      # seconds for the tape to slow to a halt
RESUME_AT = 80.109  # A1 time: bar line before「深度思考」(detected beat)
OFFSET = 3.0        # PV runs this much behind A1 after the resume point


def tape_stop(y, start, length):
    """Decelerate playback linearly from 1x to 0 over `length` seconds."""
    n = int(length * SR)
    k = np.arange(n)
    pos = start * SR + k - k * k / (2 * n)
    i = np.floor(pos).astype(int)
    frac = (pos - i)[:, None]
    out = y[i] * (1 - frac) + y[i + 1] * frac
    out *= np.linspace(1, 0, n)[:, None] ** 0.5
    return out


def main():
    y, sr = sf.read(MUSIC / f"{MIX}.wav", dtype="float32", always_2d=True)
    assert sr == SR, sr
    head = y[: int(STOP_AT * SR)]
    stop = tape_stop(y, STOP_AT, STOP_LEN)
    gap = int(round((RESUME_AT + OFFSET) * SR)) - len(head) - len(stop)
    tail = y[int(RESUME_AT * SR):].copy()
    fade = int(0.01 * SR)
    tail[:fade] *= np.linspace(0, 1, fade)[:, None]
    out = np.concatenate([head, stop, np.zeros((gap, 2), dtype="float32"), tail])
    sf.write(MUSIC / "pv-track.wav", out, SR, subtype="PCM_16")
    print(f"pv-track.wav {len(out) / SR:.2f} s, silence {gap / SR:.2f} s")

    # beat table from the instrumental, mapped onto the PV timeline
    inst, isr = librosa.load(MUSIC / "a1-without-lead-vocal.wav", sr=22050, mono=True)
    env = librosa.onset.onset_strength(y=inst, sr=isr, hop_length=256)
    _, beats = librosa.beat.beat_track(onset_envelope=env, sr=isr, hop_length=256,
                                       start_bpm=158, units="time", tightness=100)
    rows = ["beat,a1,pv"]
    for k, t in enumerate(beats):
        if STOP_AT <= t < RESUME_AT - 0.01:
            continue
        pv = t if t < STOP_AT else t + OFFSET
        rows.append(f"{k},{t:.3f},{pv:.3f}")
    (MUSIC / "pv-beats.csv").write_text("\n".join(rows) + "\n")
    print(f"pv-beats.csv {len(rows) - 1} beats")


if __name__ == "__main__":
    main()
