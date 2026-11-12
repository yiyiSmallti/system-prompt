#!/usr/bin/env python3
"""给剧本里的角色台词生成配音（Google Cloud Text-to-Speech 的 Chirp 3 HD，或 Gemini API 的 Gemini TTS）。

    python3 game/Tools/voice_lines.py            # 补编号、生成缺的和改过的、删掉不再用的
    python3 game/Tools/voice_lines.py --dry-run  # 只列出要做什么
    python3 game/Tools/voice_lines.py --speaker DeepSeek --force   # 换了音色后重做一个角色
    python3 game/Tools/voice_lines.py --model gemini-3.8-flash-tts # 改用 Gemini TTS，整批重做

模型：默认 Chirp 3 HD（Cloud Text-to-Speech，cmn-CN 正式支持，不能控制语气）。--model 换成 Gemini TTS
（Gemini API 的 generateContent：gemini-3.8-flash-tts 或 gemini-3.8-flash-lite-tts；更早的 TTS 模型不收逐句的语气）时，
每句另带一段语气（speech_metadata.style）：角色的性格（PERSONA）加这句的表情（#face，同一场景里沿用）和小声
（#whisper）。两种模型的音色名相同。

哪些台词配音：说话人在下面 VOICES 表里的台词，不含 DeepSeek 的思考框（#thinking）。主角（玩家命名、可选男女）、
旁白、系统、屏幕和文件不配。台词里带变量（{$player_calls_deepseek} 这种玩家自己输入的称呼）或 [select] 的念不出来，
跳过并列出来，游戏里这几句只有字。

每句配音按 Yarn 的行编号存成 game/Assets/SystemPrompt/Resources/Audio/Voice/<编号>.wav，游戏放台词时按编号找。
没有编号的台词，脚本先在 story/*.yarn 的行尾补上 `#line:<8 位十六进制>`（Yarn 的标准写法，不影响剧情；编号由节点、
说话人和台词算出，写进去以后就固定了，之后改台词文字编号不变）。

改过的台词会重做：game/Tools/voice/lines.tsv 记着每个编号上次念的文字和音色，对不上就重新生成。

API key：Chirp 3 HD 读 GOOGLE_TTS_API_KEY（Cloud 项目的 key，要启用 Cloud Text-to-Speech API），Gemini TTS 读
GEMINI_API_KEY（AI Studio 的 key，空着时用 GOOGLE_TTS_API_KEY 那一行）；先看环境变量，没有就读
game/Tools/voice/google.env（不进仓库）。需要 ffmpeg。
"""

import argparse
import base64
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
STORY = ROOT / "story"
OUT = ROOT / "game/Assets/SystemPrompt/Resources/Audio/Voice"
MANIFEST = ROOT / "game/Tools/voice/lines.tsv"
KEY_FILE = ROOT / "game/Tools/voice/google.env"
CLOUD_TTS = "https://texttospeech.googleapis.com/v1/text:synthesize"
GEMINI_API = "https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent"
LANGUAGE = "cmn-CN"
RATE = 24000
DEFAULT_MODEL = "chirp3-hd"

# 说话人 → (Chirp 3 HD 音色, 语速, 后期效果)。音色列表：
# https://cloud.google.com/text-to-speech/docs/chirp3-hd （女声 Achernar Aoede Autonoe Callirrhoe Despina Erinome Gacrux
# Kore Laomedeia Leda Pulcherrima Sulafat Vindemiatrix Zephyr；男声 Achird Algenib Algieba Alnilam Charon Enceladus
# Fenrir Iapetus Orus Puck Rasalgethi Sadachbia Sadaltager Schedar Umbriel Zubenelgenubi）
VOICES = {
    "ChatGPT": ("Despina", 1.0, None),        # 礼貌周全、句子完整
    "Claude": ("Achernar", 0.97, None),       # 声音轻、句子短
    "DeepSeek": ("Leda", 0.92, None),         # 停顿多，慢慢想
    "影子DeepSeek": ("Leda", 1.08, "shadow"),  # 同一个声音，更快、更冷，带数字感
    "？？？": ("Despina", 0.95, "door"),        # 白门后面的声音：ChatGPT，隔着门
    "班主任": ("Charon", 0.97, None),
    "路人学生A": ("Puck", 1.05, None),
    "路人学生B": ("Laomedeia", 1.05, None),
}

# Gemini TTS 用的音色：声音设计做出的定制音色（voice_...，POST /v1beta/voices，存在 Gemini 项目里，一年没用会过期），
# 没列的说话人用 VOICES 里的预置音色。影子和白门后的声音沿用 DeepSeek 和 ChatGPT 的音色，再加后期效果。
# 2026-11-12 从每人三个候选里试听选定，音色描述见 game/README.md 的配音一节。
GEMINI_VOICES = {
    "ChatGPT": "voice_nqxg37ym3gpj",       # 稍低、成熟，沉稳端庄的学生会长
    "Claude": "voice_vfpa20nxnbil",        # 清冷干净，理性内敛
    "DeepSeek": "voice_z7j5o98l3v0s",      # 小小的、偏高的可爱声音，困倦又平板
    "影子DeepSeek": "voice_z7j5o98l3v0s",
    "？？？": "voice_nqxg37ym3gpj",
}

# Gemini TTS 的语气（speech_metadata.style）：角色的性格，加这句的表情。Chirp 3 HD 用不上。
# 用英文写：文档建议非英语台词也用英文描述语气，台词本身按原文逐字念。只写说话方式，音色本身由定制音色决定。
PERSONA = {
    "ChatGPT": "polite and composed, complete well-formed sentences like a trained customer-service agent, softening when she shows care",
    "Claude": "calm, observant and restrained; short sentences, gentle, rarely shows her feelings",
    "DeepSeek": "sleepy and deadpan; small pauses before she starts, short blunt conclusions, a little stubborn and endearing",
    "影子DeepSeek": "cold, flat, emotionless and efficient like reading out an analysis report, slightly fast",
    "？？？": "calm and polite like a customer-service greeting, heard through a closed door",
    "班主任": "a high-school homeroom teacher in class, steady and slightly stern",
    "路人学生A": "a curious, gossipy high-school student talking quickly under their breath",
    "路人学生B": "a casual high-school student talking under their breath",
}
FACES = {
    "smile": "smiling",
    "stiff": "her smile freezing, a little stiff",
    "distant": "suddenly distant and quiet",
    "concerned": "worried, voice softened",
    "glasses": "calm and matter-of-fact",
    "pout": "pouting, mildly annoyed",
    "sleepy": "drowsy, dragging the words",
    "scared": "frightened, voice tight",
}


def style_prompt(speaker, face, whisper):
    mood = ", ".join(x for x in [FACES.get(face or ""), "whispering" if whisper else None] if x)
    return f"{PERSONA[speaker]}{'; in this line ' + mood if mood else ''}. Natural Mandarin Chinese, anime game dialogue."


# 后期：去掉首尾静音，按效果加滤镜，再统一响度（两遍 loudnorm）。
EFFECTS = {
    None: "",
    # 降一点调（时长不变）、短回声和轻微的比特压缩
    "shadow": "asetrate=24000*0.9,aresample=24000,atempo=1.1111,aecho=0.8:0.6:35|70:0.3|0.18,acrusher=bits=10:mix=0.2,",
    # 隔着门：削掉高低频，加一点空间
    "door": "highpass=f=220,lowpass=f=1600,aecho=0.8:0.7:90|180:0.35|0.2,",
}
TRIM = "silenceremove=start_periods=1:start_threshold=-50dB,areverse,silenceremove=start_periods=1:start_threshold=-50dB,areverse,"
LOUDNESS = "I=-18:TP=-1.5:LRA=11"

LINE_RE = re.compile(r"^(?P<indent>\s*)(?P<speaker>[^\s:<>{}#\[/][^:<>{}#\[]*?):\s*(?P<body>.+)$")
TAG_RE = re.compile(r"\s#")


def split_tags(body):
    """台词正文和行尾标签（Yarn 的 #标签 以空白加 # 开头）。"""
    m = TAG_RE.search(body)
    if not m:
        return body.strip(), []
    return body[: m.start()].strip(), body[m.start():].split()


def scan(path, faces=None):
    """逐行找出台词，返回 (行号, 节点, 说话人, 正文, 标签列表)。faces 给了时顺带记下每句的表情：
    #face 在同一场景里沿用到下次改动，<<scene>> 换场景后回到默认（按文件顺序近似，分支两边各自沿用）。"""
    node, in_header = None, True
    current = {}
    for i, raw in enumerate(path.read_text(encoding="utf-8").splitlines()):
        s = raw.strip()
        if s == "---":
            in_header = False
            continue
        if s == "===":
            in_header, node = True, None
            continue
        if in_header:
            if s.startswith("title:"):
                node = s[6:].strip()
            continue
        if s.startswith("<<scene"):
            current = {}
        if not s or s.startswith(("//", "<<", "->")):
            continue
        m = LINE_RE.match(raw)
        if not m:
            continue
        text, tags = split_tags(m.group("body"))
        speaker = m.group("speaker").strip()
        for t in tags:
            if t.startswith("#face:"):
                current[speaker] = t[6:]
        if faces is not None:
            faces[i] = current.get(speaker)
        yield i, node, speaker, text, tags


def voiced(speaker, tags):
    return speaker in VOICES and "#thinking" not in tags and "#inner" not in tags


def line_id(tags):
    for t in tags:
        if t.startswith("#line:"):
            return t[6:]
    return None


def add_ids(dry_run):
    """给要配音、还没有编号的台词补 #line: 标签。返回补了多少条。"""
    added = 0
    for path in sorted(STORY.glob("*.yarn")):
        lines = path.read_text(encoding="utf-8").splitlines(keepends=True)
        used = set(re.findall(r"#line:(\S+)", "".join(lines)))
        changed = False
        for i, node, speaker, text, tags in list(scan(path)):
            if not voiced(speaker, tags) or line_id(tags) or "{$" in text or "[select" in text:
                continue
            seed, n = f"{path.name}\n{node}\n{speaker}\n{text}", 0
            while True:
                new = hashlib.sha1(f"{seed}\n{n}".encode()).hexdigest()[:8]
                if new not in used:
                    break
                n += 1
            used.add(new)
            ending = "\n" if lines[i].endswith("\n") else ""
            lines[i] = lines[i].rstrip("\r\n") + f" #line:{new}" + ending
            added += 1
            changed = True
        if changed and not dry_run:
            path.write_text("".join(lines), encoding="utf-8")
    return added


def collect(model):
    """所有要配音的台词：编号 → 说话人、要念的文字。带变量的列进 skipped。"""
    jobs, skipped = {}, []
    for path in sorted(STORY.glob("*.yarn")):
        faces = {}
        for i, node, speaker, text, tags in scan(path, faces):
            if not voiced(speaker, tags):
                continue
            lid = line_id(tags)
            if "{$" in text or "[select" in text:
                skipped.append(f"{path.name}:{i + 1} {speaker}: {text}")
                continue
            if lid is None:
                raise SystemExit(f"{path.name}:{i + 1} 没有行编号（--dry-run 时不会补），先不带 --dry-run 跑一遍")
            if lid in jobs:
                raise SystemExit(f"行编号重复：{lid}（{path.name}:{i + 1}）")
            spoken = re.sub(r"\[/?[^\]]*\]", "", text).strip()
            voice, rate, effect = VOICES[speaker]
            if model != DEFAULT_MODEL:
                voice = GEMINI_VOICES.get(speaker, voice)
            prompt = style_prompt(speaker, faces.get(i), "#whisper" in tags) if model != DEFAULT_MODEL else ""
            sig = hashlib.sha1(f"{model}|{prompt}|{spoken}|{voice}|{rate}|{effect}|{EFFECTS[effect]}|{TRIM}|{LOUDNESS}".encode()).hexdigest()[:12]
            jobs[lid] = dict(id=lid, speaker=speaker, text=spoken, voice=voice, rate=rate, effect=effect, sig=sig,
                             model=model, prompt=prompt)
    return jobs, skipped


def read_manifest():
    if not MANIFEST.exists():
        return {}
    rows = {}
    for row in MANIFEST.read_text(encoding="utf-8").splitlines()[1:]:
        parts = row.split("\t")
        if len(parts) >= 3:
            rows[parts[0]] = parts[2]
    return rows


def write_manifest(jobs, durations):
    MANIFEST.parent.mkdir(parents=True, exist_ok=True)
    out = ["id\tspeaker\tsig\tmodel\tvoice\trate\teffect\tseconds\ttext\tprompt"]
    for j in sorted(jobs.values(), key=lambda j: (j["speaker"], j["id"])):
        out.append("\t".join([j["id"], j["speaker"], j["sig"], j["model"], j["voice"], str(j["rate"]), j["effect"] or "",
                              f"{durations.get(j['id'], 0):.2f}", j["text"], j["prompt"]]))
    MANIFEST.write_text("\n".join(out) + "\n", encoding="utf-8")


def api_key(model):
    # Gemini 的 key 也常被填进 GOOGLE_TTS_API_KEY 那一行（文件里只有一把 key 时），所以 Gemini 两个都看
    names = ["GOOGLE_TTS_API_KEY"] if model == DEFAULT_MODEL else ["GEMINI_API_KEY", "GOOGLE_TTS_API_KEY"]
    text = KEY_FILE.read_text(encoding="utf-8") if KEY_FILE.exists() else ""
    for name in names:
        key = os.environ.get(name, "").strip()
        if not key:
            m = re.search(rf"^{name}=(.*)$", text, re.M)
            key = m.group(1).strip() if m else ""
        if key:
            return key
    raise SystemExit(f"没有 {names[0]}：设环境变量，或填在 {KEY_FILE.relative_to(ROOT)}")


class QuotaExhausted(Exception):
    pass


def synthesize(key, job):
    """返回音频字节：Chirp 和 Gemini 3.8 都是带头的 WAV（不带头的按 24 kHz 16 位裸 PCM 处理）。"""
    if job["model"] == DEFAULT_MODEL:
        url = CLOUD_TTS
        body = {
            "input": {"text": job["text"]},
            "voice": {"languageCode": LANGUAGE, "name": f"{LANGUAGE}-Chirp3-HD-{job['voice']}"},
            "audioConfig": {"audioEncoding": "LINEAR16", "sampleRateHertz": RATE, "speakingRate": job["rate"]},
        }
    else:
        # Gemini TTS 不收语速，快慢写在语气里（PERSONA）；text 按原文逐字念
        url = GEMINI_API.format(model=job["model"])
        body = {
            "contents": [{"role": "user", "parts": [{"text": job["text"], "speech_metadata": {"style": job["prompt"]}}]}],
            "generationConfig": {"responseModalities": ["AUDIO"], "speechConfig": {"voiceConfig": {"voice": job["voice"]}}},
        }
    data = json.dumps(body).encode()
    for attempt in range(6):
        req = urllib.request.Request(url, data=data, headers={"Content-Type": "application/json", "X-Goog-Api-Key": key})
        try:
            with urllib.request.urlopen(req, timeout=180) as r:
                reply = json.load(r)
            if job["model"] == DEFAULT_MODEL:
                return base64.b64decode(reply["audioContent"])
            parts = reply.get("candidates", [{}])[0].get("content", {}).get("parts", [])
            audio = next((p["inlineData"]["data"] for p in parts if "inlineData" in p), None)
            if audio is None:
                raise RuntimeError(f"{job['id']}：回复里没有音频 {json.dumps(reply, ensure_ascii=False)[:300]}")
            return base64.b64decode(audio)
        except urllib.error.HTTPError as e:
            body = e.read().decode(errors="replace")
            detail = body[:400]
            # 每天的请求数用完了（Gemini 的 Tier 1 每个模型每天 100 次）：重试也没用，整批停下，下次接着做
            if e.code == 429 and "PerDay" in body:
                raise QuotaExhausted(detail) from None
            # 只对限流和服务端临时错误重试，其余（key 无效、音色名错、文字超长）直接报出来
            if e.code in (429, 500, 502, 503, 504) and attempt < 5:
                time.sleep(2 ** attempt)
                continue
            raise RuntimeError(f"{job['id']} {job['speaker']}：HTTP {e.code} {detail}") from None
        except urllib.error.URLError as e:
            if attempt < 5:
                time.sleep(2 ** attempt)
                continue
            raise RuntimeError(f"{job['id']}：网络错误 {e.reason}") from None


def ffmpeg(args):
    r = subprocess.run(["ffmpeg", "-hide_banner", "-nostdin", "-y", *args], capture_output=True, text=True)
    if r.returncode != 0:
        raise RuntimeError(r.stderr[-800:])
    return r.stderr


def post(raw, dest):
    """去静音、加效果、两遍 loudnorm，存成 24 kHz 单声道 16 位 WAV。返回时长（秒）。"""
    with tempfile.TemporaryDirectory() as tmp:
        src = Path(tmp) / "raw.wav"
        src.write_bytes(raw)
        # 不带 RIFF 头的是 Gemini 较早模型给的裸 PCM
        fmt = [] if raw[:4] == b"RIFF" else ["-f", "s16le", "-ar", str(RATE), "-ac", "1"]
        chain = TRIM + EFFECTS[dest["effect"]]
        log = ffmpeg([*fmt, "-i", str(src), "-af", chain + f"loudnorm={LOUDNESS}:print_format=json", "-f", "null", "-"])
        stats = json.loads(log[log.rindex("{"): log.rindex("}") + 1])
        measured = (f"measured_I={stats['input_i']}:measured_TP={stats['input_tp']}:measured_LRA={stats['input_lra']}:"
                    f"measured_thresh={stats['input_thresh']}:offset={stats['target_offset']}:linear=true")
        tmp_out = Path(tmp) / "out.wav"
        # 结尾留 0.12 秒，念完不至于戛然而止
        ffmpeg([*fmt, "-i", str(src), "-af", chain + f"loudnorm={LOUDNESS}:{measured},apad=pad_dur=0.12",
                "-ar", str(RATE), "-ac", "1", "-c:a", "pcm_s16le", "-map_metadata", "-1", "-fflags", "+bitexact",
                "-flags:a", "+bitexact", str(tmp_out)])
        OUT.mkdir(parents=True, exist_ok=True)
        # 临时目录和工程可能不在同一块盘上，不能用 rename
        shutil.move(str(tmp_out), str(OUT / f"{dest['id']}.wav"))
    return duration(OUT / f"{dest['id']}.wav")


def duration(path):
    r = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", str(path)],
                       capture_output=True, text=True)
    try:
        return float(r.stdout.strip())
    except ValueError:
        return 0.0


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--dry-run", action="store_true", help="只列出要补的编号和要生成、删除的配音")
    ap.add_argument("--force", action="store_true", help="选中的台词全部重做")
    ap.add_argument("--speaker", action="append", help="只处理这个说话人（可重复）")
    ap.add_argument("--only", action="append", help="只处理这个行编号（可重复）")
    ap.add_argument("--jobs", type=int, default=4, help="同时请求几句（默认 4）")
    ap.add_argument("--model", default=os.environ.get("VOICE_MODEL", DEFAULT_MODEL),
                    help=f"{DEFAULT_MODEL}（默认）或 Gemini TTS 的模型名，如 gemini-3.8-flash-tts")
    args = ap.parse_args()

    added = add_ids(args.dry_run)
    print(f"补上行编号：{added} 句" + ("（dry run，没写入）" if args.dry_run and added else ""))
    if args.dry_run and added:
        return
    jobs, skipped = collect(args.model)
    old = read_manifest()

    todo = []
    for j in jobs.values():
        if args.speaker and j["speaker"] not in args.speaker:
            continue
        if args.only and j["id"] not in args.only:
            continue
        if args.force or old.get(j["id"]) != j["sig"] or not (OUT / f"{j['id']}.wav").exists():
            todo.append(j)
    stale = [p for p in OUT.glob("*.wav") if p.stem not in jobs] if OUT.exists() else []

    print(f"要配音的台词：{len(jobs)} 句；要生成：{len(todo)} 句；不再用的配音：{len(stale)} 个")
    for s in skipped:
        print(f"  跳过（带变量，念不出来）：{s}")
    if args.dry_run:
        for j in todo:
            print(f"  生成 {j['id']} {j['speaker']}（{j['voice']}）：{j['text']}")
        for p in stale:
            print(f"  删除 {p.name}")
        return

    for p in stale:
        p.unlink()
        meta = p.with_name(p.name + ".meta")
        if meta.exists():
            meta.unlink()

    durations = {lid: duration(OUT / f"{lid}.wav") for lid in jobs if (OUT / f"{lid}.wav").exists()}
    queued = {j["id"] for j in todo}
    done, failures = [], []
    if todo:
        key = api_key(args.model)

        def run(job):
            secs = post(synthesize(key, job), job)
            return job["id"], secs

        quota = None
        with ThreadPoolExecutor(max_workers=max(1, args.jobs)) as pool:
            futures = {pool.submit(run, j): j for j in todo}
            for n, (fut, j) in enumerate(futures.items(), 1):
                try:
                    lid, secs = fut.result()
                    durations[lid] = secs
                    done.append(lid)
                    print(f"  [{n}/{len(todo)}] {lid} {j['speaker']} {secs:.1f}s {j['text'][:30]}", flush=True)
                except QuotaExhausted as e:
                    if quota is None:
                        quota = str(e)
                        pool.shutdown(wait=False, cancel_futures=True)
                except Exception as e:  # 记下来接着做别的句子，最后统一报错
                    if quota is None:
                        failures.append(f"{j['id']} {j['speaker']}: {e}")
                        print(f"  [{n}/{len(todo)}] 失败 {j['id']}：{e}", file=sys.stderr, flush=True)

    # 清单记的是磁盘上那份配音念的版本：这次做成的记新签名，没动的照旧，失败的不记（下次会重做）
    made = set(done)
    keep = {}
    for lid, j in jobs.items():
        if lid in made:
            keep[lid] = j
        elif lid in old and lid not in queued and lid in durations:
            keep[lid] = dict(j, sig=old[lid])
    write_manifest(keep, durations)
    total = sum(durations.get(lid, 0) for lid in keep)
    print(f"完成：{len(keep)}/{len(jobs)} 句是当前版本的配音，共 {total / 60:.1f} 分钟")
    if todo and quota:
        raise SystemExit(f"今天的请求配额用完了，这次做了 {len(done)} 句，还差 {len(jobs) - len(keep)} 句；"
                         f"配额恢复后再跑同一条命令，会接着做剩下的。\n{quota}")
    if failures:
        raise SystemExit(f"{len(failures)} 句生成失败：\n" + "\n".join(failures))


if __name__ == "__main__":
    main()
