"""Generate PV shots with the Ark video generation API (Doubao-Seedance-2.5).

Shot prompts, durations and reference images are read from pv/README.md
(「Seedance 提示词」), so the README stays the single source of truth.
Tasks are logged in clips/tasks.json; finished videos land in clips/S11.mp4,
retakes in clips/S11-2.mp4 and so on.

The API key is read from ~/.config/ark.env (ARK_API_KEY=... or the bare key)
and is never printed.

usage:
  python seedance.py dry S11 [S12 ...]     show what would be sent, no charge
  python seedance.py submit S11 [S12 ...]  create tasks (charged)
  python seedance.py wait                  poll pending tasks and download results
"""
import base64
import json
import pathlib
import re
import sys
import time
import urllib.error
import urllib.request

PV = pathlib.Path(__file__).resolve().parent.parent
CLIPS = PV / "clips"
LOG = CLIPS / "tasks.json"
API = "https://ark.cn-beijing.volces.com/api/v3/contents/generations/tasks"
MODEL = "doubao-seedance-2-5-260628"
PRICE = (0.070, 0.077)  # yuan per 1k tokens


def api_key():
    lines = [l.strip() for l in (pathlib.Path.home() / ".config/ark.env").read_text().splitlines()]
    lines = [l for l in lines if l and not l.startswith("#")]
    for line in lines:
        if line.startswith("ARK_API_KEY="):
            return line.split("=", 1)[1].strip().strip('"').strip("'")
    if len(lines) == 1 and "=" not in lines[0]:
        return lines[0]
    sys.exit("~/.config/ark.env has no ARK_API_KEY")


def shots():
    text = (PV / "README.md").read_text()
    suffix = re.search(r"^> (日系二次元.+)$", text, re.M).group(1)
    out = {}
    pat = r"^\*\*(S\d+) ([^*]+)\*\*（(\d+) 秒(?:，(.+?))?）：(.+)$"
    for sid, name, sec, refs, prompt in re.findall(pat, text, re.M):
        out[sid] = {
            "name": name,
            "duration": int(sec),
            "refs": re.findall(r"`([^`]+)`", refs or ""),
            "prompt": f"{prompt}{suffix}",
        }
    return out


def body(shot):
    content = [{"type": "text", "text": shot["prompt"]}]
    for ref in shot["refs"]:
        data = base64.b64encode((PV / "refs" / ref).read_bytes()).decode()
        content.append({"type": "image_url", "image_url": {"url": f"data:image/jpeg;base64,{data}"},
                        "role": "reference_image"})
    return {"model": MODEL, "content": content, "resolution": "720p", "ratio": "16:9",
            "duration": shot["duration"], "generate_audio": False, "watermark": False}


def call(method, url, key, payload=None):
    req = urllib.request.Request(url, method=method, data=json.dumps(payload).encode() if payload else None,
                                 headers={"Content-Type": "application/json", "Authorization": f"Bearer {key}"})
    try:
        with urllib.request.urlopen(req, timeout=120) as resp:
            return json.loads(resp.read().decode())
    except urllib.error.HTTPError as e:
        sys.exit(f"HTTP {e.code}: {e.read().decode(errors='replace')[:500]}")


def load_log():
    return json.loads(LOG.read_text()) if LOG.exists() else []


def save_log(log):
    LOG.write_text(json.dumps(log, ensure_ascii=False, indent=1) + "\n")


def cost(tokens):
    return f"{tokens * PRICE[0] / 1000:.2f}–{tokens * PRICE[1] / 1000:.2f} 元"


def main():
    cmd, ids = sys.argv[1], sys.argv[2:]
    all_shots = shots()
    CLIPS.mkdir(exist_ok=True)
    if cmd == "dry":
        for sid in ids:
            s = all_shots[sid]
            size = len(json.dumps(body(s)))
            print(f"{sid} {s['name']} {s['duration']} s refs={s['refs']} body={size / 1e6:.2f} MB")
            print(f"  {s['prompt']}")
        return
    key = api_key()
    log = load_log()
    if cmd == "submit":
        for sid in ids:
            s = all_shots[sid]
            task = call("POST", API, key, body(s))
            take = 1 + sum(1 for r in log if r["shot"] == sid and r["status"] not in ("failed", "expired"))
            log.append({"shot": sid, "take": take, "task": task["id"], "duration": s["duration"],
                        "status": "queued", "submitted": time.strftime("%Y-%m-%d %H:%M:%S")})
            save_log(log)
            print(f"{sid} take {take}: task {task['id']}")
    elif cmd == "wait":
        while True:
            pending = [r for r in log if r["status"] in ("queued", "running")]
            for r in pending:
                t = call("GET", f"{API}/{r['task']}", key)
                r["status"] = t["status"]
                if t["status"] == "succeeded":
                    name = r["shot"] if r["take"] == 1 else f"{r['shot']}-{r['take']}"
                    dest = CLIPS / f"{name}.mp4"
                    urllib.request.urlretrieve(t["content"]["video_url"], dest)
                    r["file"] = dest.name
                    r["tokens"] = t.get("usage", {}).get("completion_tokens")
                    print(f"{name}: saved, {r['tokens']} tokens ≈ {cost(r['tokens'] or 0)}")
                elif t["status"] in ("failed", "expired"):
                    # keep only the code: the message can contain the account ID
                    r["error"] = (t.get("error") or {}).get("code")
                    print(f"{r['shot']} take {r['take']}: {t['status']} {r['error']}")
                save_log(log)
            if not any(r["status"] in ("queued", "running") for r in log):
                break
            time.sleep(15)
        done = [r for r in log if r.get("tokens")]
        total = sum(r["tokens"] for r in done)
        print(f"total {len(done)} clips, {total} tokens ≈ {cost(total)}")
    else:
        sys.exit(__doc__)


if __name__ == "__main__":
    main()
