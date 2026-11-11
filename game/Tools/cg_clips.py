"""Make the in-game cutscene pictures out of the generated clips in game/Cutscenes/ (prompts in its README).

Each clip is cut where its last good frame is, and its last second is stretched to two so the motion eases to
a stop on the frame the game then holds. A clip may also open at half speed (the shadow turning to light takes
the model one second, too quick to follow). The clip keeps its own frame rate: at full speed its frames pass
through untouched, and in the slowed parts neighbouring frames are blended. The picture is fitted to
1920x1080 (the models give 1920x1088 or 1922x1078: the first is cropped, anything else scaled). The sound
track is dropped. The still the game shows when the story is being skipped is the clip's last frame.

Writes game/Assets/SystemPrompt/Resources/Art/CG/<id>_motion.mp4 and <id>.png.

usage: python3 game/Tools/cg_clips.py [id ...]   (from the repository root; needs ffmpeg)
"""
import pathlib
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
SOURCE = ROOT / "game/Cutscenes"
OUT = ROOT / "game/Assets/SystemPrompt/Resources/Art/CG"
EASE = 1.0  # seconds of the clip's end that are slowed down

# id: (seconds of the clip that are used, None for all of it; seconds at its start played at half speed)
CLIPS = {
    "dive": (None, 0),
    "abyss_whale": (None, 1.2),  # the shadow breaking up into light and streaming into her chest
    "council_window": (None, 0),
}


def probe(path, entries, frames=False):
    out = subprocess.run(["ffprobe", "-v", "error", "-select_streams", "v:0"] + (["-count_frames"] if frames else []) +
                         ["-show_entries", "stream=" + entries, "-of", "csv=p=0", str(path)],
                         capture_output=True, text=True, check=True).stdout
    return out.strip().split(",")


def build(name, cut, slow):
    source = SOURCE / f"{name}.mp4"
    width, height, rate, length = probe(source, "width,height,r_frame_rate,duration")
    end = cut or float(length)
    knee = end - EASE
    fit = "crop=1920:1080" if (width, height) == ("1920", "1088") else "scale=1920:1080:flags=lanczos"
    # The first `slow` seconds take twice as long. From there the clip runs as it is up to the knee, and from the
    # knee source time s lands at knee + 2·EASE·(1 − √((end − s)/EASE)), which starts at the clip's own speed and
    # reaches a standstill 2·EASE later.
    pts = f"if(lt(T,{slow}),2*T,{slow}+if(lt(T,{knee}),T,{knee}+{2 * EASE}*(1-sqrt(max(0,({end}-T)/{EASE})))))/TB"
    motion = OUT / f"{name}_motion.mp4"
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(source), "-t", str(end), "-an",
                    "-vf", f"{fit},setpts='{pts}',framerate=fps={rate}",
                    "-t", f"{slow + knee + 2 * EASE:.3f}", "-c:v", "libx264", "-preset", "slow", "-crf", "24", "-pix_fmt", "yuv420p",
                    "-movflags", "+faststart", str(motion)], check=True)
    frames = int(probe(motion, "nb_read_frames", frames=True)[0])
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(motion), "-vf", f"select=eq(n\\,{frames - 1})",
                    "-frames:v", "1", str(OUT / f"{name}.png")], check=True)
    played = float(probe(motion, "duration")[0])
    print(f"{name}: {width}x{height} at {rate}, {end:.2f} s of the clip -> {played:.2f} s, {motion.stat().st_size / 1e6:.1f} MB")


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    unknown = [name for name in sys.argv[1:] if name not in CLIPS]
    if unknown:
        sys.exit(f"no such clip: {', '.join(unknown)} (have {', '.join(CLIPS)})")
    for name in sys.argv[1:] or CLIPS:
        build(name, *CLIPS[name])
