#!/bin/bash
# 渲染模型与动作展示片：先把 game/ 里的角色模型、动作、道具和三渲二着色器，以及衍生作《无敌究极后桥背摔》
# PV 工程里的 Mixamo 格斗动作同步进 unity/，再用 Unity 逐镜渲染成无损中间视频（frames/），最后由 compose.py
# 叠字幕、配乐，编码成 out/model-showcase.mp4。
# 用法：pv/showcase/render.sh [镜号,镜号]，给镜号只重渲这几镜（其余用 frames/ 里已有的）。
#       STILLS=1 pv/showcase/render.sh [镜号]：每镜只出几张预览图到 stills/，用来看构图。
#       COMPOSE=0 pv/showcase/render.sh 镜号：只渲不合成。内存紧时一个 Unity 进程连渲太多镜会被系统杀掉，分几批渲。
# 编辑器按 unity/ProjectSettings/ProjectVersion.txt 的版本在 Unity Hub 的安装目录里找，也可以用 UNITY=... 指定；
# 衍生作的位置用 SUPLEX=... 指定。第一次运行要导入模型，十分钟左右。
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
GAME="$(cd "$HERE/../../game" && pwd)"
SUPLEX="${SUPLEX:-$(cd "$HERE/../../.." && pwd)/Projects/system-prompt-ultimate-suplex}"
PROJECT="$HERE/unity"
VERSION="$(sed -n 's/^m_EditorVersion: //p' "$PROJECT/ProjectSettings/ProjectVersion.txt")"
if [ -z "${UNITY:-}" ]; then
  for root in /Applications/Unity/Hub/Editor /Volumes/*/Applications/Unity/Hub/Editor "$HOME/Applications/Unity/Hub/Editor"; do
    if [ -x "$root/$VERSION/Unity.app/Contents/MacOS/Unity" ]; then UNITY="$root/$VERSION/Unity.app/Contents/MacOS/Unity"; break; fi
  done
fi
if [ -z "${UNITY:-}" ]; then echo "找不到 Unity $VERSION，用 UNITY=/path/to/Unity 指定" >&2; exit 2; fi

# 同步素材。母作的导入设置（ModelAssetImporter）原样带过来，模型的修整和贴图校色和游戏里一致。
SRC="$GAME/Assets/SystemPrompt"
DST="$PROJECT/Assets/SystemPrompt"
mkdir -p "$DST/Editor" "$DST/Resources/Shaders" "$DST/Resources/Models/Motions"
rsync -a "$SRC/Editor/ModelAssetImporter.cs" "$SRC/Editor/ModelAssetImporter.cs.meta" "$DST/Editor/"
rsync -a --delete "$SRC/Resources/Models/Characters" "$SRC/Resources/Models/Props" "$DST/Resources/Models/"
rsync -a "$SRC/Resources/Models/Motions/" "$DST/Resources/Models/Motions/"
for f in Toon.shader SeaCommon.cginc; do rsync -a "$SRC/Resources/Shaders/$f" "$SRC/Resources/Shaders/$f.meta" "$DST/Resources/Shaders/"; done
# 衍生作的动作分在两个 Unity 工程里（格斗本体 game/ 和 PV 的 pv/unity/），只取母作没有的。
FOUND=0
for dir in "$SUPLEX/game" "$SUPLEX/pv/unity"; do
  for f in "$dir"/Assets/SystemPrompt/Resources/Models/Motions/*.fbx; do
    [ -e "$f" ] || continue
    name="$(basename "$f")"
    FOUND=1
    [ -e "$SRC/Resources/Models/Motions/$name" ] || rsync -a "$f" "$f.meta" "$DST/Resources/Models/Motions/"
  done
done
if [ "$FOUND" = 0 ]; then echo "找不到衍生作的动作（$SUPLEX），用 SUPLEX=... 指定" >&2; exit 2; fi

LOG="$HERE/frames/unity.log"
mkdir -p "$HERE/frames"
FFMPEG="$(command -v ffmpeg)" || { echo "找不到 ffmpeg" >&2; exit 2; }
ARGS=(-out "$HERE/frames" -ffmpeg "$FFMPEG")
[ -n "${1:-}" ] && ARGS+=(-shots "$1")
[ -n "${STILLS:-}" ] && ARGS=(-out "$HERE/stills" -stills ${1:+-shots "$1"})
if ! "$UNITY" -batchmode -quit -projectPath "$PROJECT" -executeMethod Showcase.Reel.Run "${ARGS[@]}" -logFile "$LOG"; then
  echo "渲染失败，见 $LOG" >&2
  grep -E 'error CS|Exception|找不到' "$LOG" | head -20 >&2 || true
  exit 1
fi
[ -n "${STILLS:-}" ] && exit 0
[ "${COMPOSE:-1}" = 0 ] && exit 0
python3 "$HERE/compose.py" "$HERE/frames" "$HERE/out/model-showcase.mp4"
