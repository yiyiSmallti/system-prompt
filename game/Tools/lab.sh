#!/bin/bash
# 视觉实验室：构建测试版，摆出战斗动作、技能特效、缓存海浅层和深层的场景、走跑动作、机关和噪声体，按游戏时间等间隔拍成胶片（每帧 480×270、一行 4 帧）和整窗静帧，
# 写到 <game>/E2E/lab-<时间>/，index.md 列出每个场景拍了什么、间隔多少、期间的错误和警告。最后一行输出目录。退出码 0 = 选中的场景全部拍到且没有错误日志。
#
#   game/Tools/lab.sh                       全部场景
#   game/Tools/lab.sh battle                只跑一族（battle / sea / deep），也可以写场景 id 或前缀，空格分开：lab.sh battle-shot sea-run
#   game/Tools/lab.sh --no-build sea        不重新构建，用上次的 Builds/E2E/SystemPromptLab.app
#   game/Tools/lab.sh --out /path/dir ...   指定输出目录（目录里旧的 png、index.md、日志会先删掉）
#
# 用时（Apple M4，16 GB）：构建约 2–4 分钟（Library 已有缓存时）；全部场景约 5 分钟，battle 约 1.5 分钟、sea 约 1.5 分钟、deep 约 1.5 分钟，单个战斗场景约 40 秒。
# 新 worktree 第一次跑要先克隆 Library（APFS 克隆，几秒）再完整导入编译，构建约 5–8 分钟。
# 一次调用（构建 + 全部场景）可能超过 10 分钟：在会被 10 分钟超时杀掉的环境里，先 `lab.sh <一个小场景>` 构建，再 `lab.sh --no-build` 跑全部，或者放到后台跑。
#
# 跑哪个工程：当前目录所在 git 检出的 game/（在 worktree 里调用主检出的 lab.sh，就构建和拍摄 worktree 的代码），LAB_GAME=<game 目录> 可以直接指定；都不是时用本脚本所在的检出。
# 目标不是本脚本所在的检出时，缺的或比较旧的实验室文件（E2E/VisualLab.cs 和 .meta）从本检出复制过去；没有 Library 时用 `cp -Rc` 克隆本检出的 Library，
# 缺的不进仓库的文件（Assets/csc.rsp、mcs.rsp、Live2D SDK、UserSettings）也一并补上。
# 同一仓库的所有检出共用一把锁（git common dir 下的 sp-lab.lock），一次只有一个实验室在构建或运行；被占用时最多等 60 分钟，每 30 秒报一次谁占着。e2e.sh 取同一把锁，两边轮流。
# 游戏运行时 HOME 指向一个临时文件夹，读写的存档和设置都在里面，结束后删掉；真正的存档目录不读不写也不删。
# 运行期间会弹出一个 1600×900 的窗口，不要碰键盘鼠标；声音是关的。编辑器路径按 ProjectVersion.txt 在 Unity Hub 的安装目录里找，也可以用 UNITY=... 指定。
set -euo pipefail

SELF_GAME="$(cd "$(dirname "$0")/.." && pwd -P)"
NO_BUILD=0
OUT=""
IDS=()
ONLY=""
while [ $# -gt 0 ]; do
  case "$1" in
    --no-build) NO_BUILD=1 ;;
    --out) shift; OUT="${1:?--out 后面要跟目录}" ;;
    --out=*) OUT="${1#--out=}" ;;
    -h|--help) sed -n '2,20p' "$0"; exit 0 ;;
    -*) echo "不认识的选项 $1" >&2; exit 2 ;;
    *) IDS+=("$1") ;;
  esac
  shift
done

# 目标工程
if [ -n "${LAB_GAME:-}" ]; then
  TARGET="$(cd "$LAB_GAME" && pwd -P)"
elif TOP="$(git rev-parse --show-toplevel 2>/dev/null)" && [ -f "$TOP/game/ProjectSettings/ProjectVersion.txt" ]; then
  TARGET="$(cd "$TOP/game" && pwd -P)"
else
  TARGET="$SELF_GAME"
fi
if [ ! -f "$TARGET/ProjectSettings/ProjectVersion.txt" ]; then echo "$TARGET 不是 Unity 工程" >&2; exit 2; fi

VERSION="$(sed -n 's/^m_EditorVersion: //p' "$TARGET/ProjectSettings/ProjectVersion.txt")"
if [ -z "${UNITY:-}" ]; then
  for root in /Applications/Unity/Hub/Editor /Volumes/*/Applications/Unity/Hub/Editor "$HOME/Applications/Unity/Hub/Editor"; do
    if [ -x "$root/$VERSION/Unity.app/Contents/MacOS/Unity" ]; then UNITY="$root/$VERSION/Unity.app/Contents/MacOS/Unity"; break; fi
  done
fi
if [ -z "${UNITY:-}" ]; then echo "找不到 Unity $VERSION，用 UNITY=/path/to/Unity 指定" >&2; exit 2; fi

APP="$TARGET/Builds/E2E/SystemPromptLab.app"
if [ -z "$OUT" ]; then OUT="$TARGET/E2E/lab-$(date +%Y%m%d-%H%M%S)"; fi
mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd -P)"
rm -f "$OUT"/*.png "$OUT/index.md" "$OUT/player.log" "$OUT/build.log"

# 锁：同一仓库所有检出共用。目录里的 pid 是持有者，进程不在了就算失效。
COMMON="$(git -C "$TARGET" rev-parse --path-format=absolute --git-common-dir 2>/dev/null || true)"
if [ -z "$COMMON" ]; then COMMON="$SELF_GAME/Temp"; mkdir -p "$COMMON"; fi
LOCK="$COMMON/sp-lab.lock"
SANDBOX=""
PLAYER_PID=""
OWN_LOCK=0
cleanup() {
  if [ -n "$PLAYER_PID" ] && kill -0 "$PLAYER_PID" 2>/dev/null; then kill "$PLAYER_PID" 2>/dev/null || true; fi
  if [ -n "$SANDBOX" ] && [ -d "$SANDBOX" ]; then rm -rf "$SANDBOX"; fi
  if [ "$OWN_LOCK" = 1 ] && [ "$(cat "$LOCK/pid" 2>/dev/null)" = "$$" ]; then rm -rf "$LOCK"; fi
}
trap cleanup EXIT
trap 'exit 130' INT TERM HUP

WAITED=0
NEXT_REPORT=0
while ! mkdir "$LOCK" 2>/dev/null; do
  HOLDER="$(cat "$LOCK/pid" 2>/dev/null || true)"
  if [ -n "$HOLDER" ] && ! ps -p "$HOLDER" >/dev/null 2>&1; then
    echo "锁的持有者 $HOLDER 已经不在了，清掉失效的锁"
    rm -rf "$LOCK"
    continue
  fi
  if [ -z "$HOLDER" ] && [ -n "$(find "$LOCK" -maxdepth 0 -mmin +1 2>/dev/null)" ]; then
    echo "锁里没有持有者（超过 1 分钟），清掉"
    rm -rf "$LOCK"
    continue
  fi
  if [ "$WAITED" -ge "$NEXT_REPORT" ]; then
    echo "等实验室的锁（已等 ${WAITED} 秒）：$(cat "$LOCK/who" 2>/dev/null || echo "pid $HOLDER")"
    NEXT_REPORT=$((NEXT_REPORT + 30))
  fi
  if [ "$WAITED" -ge 3600 ]; then echo "等了 60 分钟锁还被占着，放弃" >&2; exit 4; fi
  sleep 2
  WAITED=$((WAITED + 2))
done
OWN_LOCK=1
echo "$$" > "$LOCK/pid"
echo "pid $$，$(date '+%H:%M:%S') 起，工程 $TARGET" > "$LOCK/who"

# 目标是别的检出时，补齐实验室文件和不进仓库的东西
if [ "$TARGET" != "$SELF_GAME" ]; then
  for f in Assets/SystemPrompt/E2E/VisualLab.cs Assets/SystemPrompt/E2E/VisualLab.cs.meta; do
    if [ -f "$SELF_GAME/$f" ] && { [ ! -f "$TARGET/$f" ] || [ "$SELF_GAME/$f" -nt "$TARGET/$f" ]; } && ! cmp -s "$SELF_GAME/$f" "$TARGET/$f"; then
      echo "复制 $f → $TARGET"
      mkdir -p "$(dirname "$TARGET/$f")"
      cp -p "$SELF_GAME/$f" "$TARGET/$f"
    fi
  done
  if [ ! -d "$TARGET/Library" ] && [ -d "$SELF_GAME/Library" ]; then
    echo "克隆 Library（cp -Rc）"
    T0=$SECONDS
    cp -Rc "$SELF_GAME/Library" "$TARGET/Library"
    echo "Library 克隆用了 $((SECONDS - T0)) 秒"
  fi
  for f in Assets/csc.rsp Assets/csc.rsp.meta Assets/mcs.rsp Assets/mcs.rsp.meta Assets/Live2D Assets/Live2D.meta UserSettings; do
    if [ -e "$SELF_GAME/$f" ] && [ ! -e "$TARGET/$f" ]; then
      echo "补上 $f"
      cp -Rc "$SELF_GAME/$f" "$TARGET/$f"
    fi
  done
fi

if [ "$NO_BUILD" = 0 ]; then
  echo "构建 → $APP"
  T0=$SECONDS
  if ! "$UNITY" -batchmode -quit -projectPath "$TARGET" -executeMethod SystemPrompt.EditorTools.BuildTools.BuildE2E \
    -buildOutput "$APP" -logFile "$OUT/build.log"; then
    echo "构建失败（$((SECONDS - T0)) 秒），见 $OUT/build.log" >&2
    grep -E 'error CS' "$OUT/build.log" | sort -u | head -40 >&2 || true
    grep -E 'BuildFailedException|another Unity instance|Multiple Unity instances' "$OUT/build.log" | head -5 >&2 || true
    exit 1
  fi
  echo "构建用了 $((SECONDS - T0)) 秒"
elif [ ! -x "$APP/Contents/MacOS/System Prompt E2E" ]; then
  echo "--no-build 但没有 $APP，先不带 --no-build 跑一次" >&2
  exit 2
fi

# 游戏在临时 HOME 里运行：persistentDataPath（存档、设置）和偏好都落在里面
SANDBOX="$(mktemp -d "${TMPDIR:-/tmp}/sp-lab.XXXXXX")"
SANDBOX="$(cd "$SANDBOX" && pwd -P)"
ARGS=(-lab "$OUT" -screen-fullscreen 0 -screen-width 1600 -screen-height 900 -logFile "$OUT/player.log")
if [ ${#IDS[@]} -gt 0 ]; then
  ONLY="$(IFS=,; echo "${IDS[*]}")"
  ARGS+=(-labOnly "$ONLY")
fi
echo "运行${ONLY:+（$ONLY）} → $OUT"
T0=$SECONDS
set +e
HOME="$SANDBOX" CFFIXED_USER_HOME="$SANDBOX" SP_LAB_HOME="$SANDBOX" "$APP/Contents/MacOS/System Prompt E2E" "${ARGS[@]}" >/dev/null &
PLAYER_PID=$!
wait "$PLAYER_PID"
CODE=$?
PLAYER_PID=""
set -e
echo "运行用了 $((SECONDS - T0)) 秒，退出码 $CODE（0 = 全部拍到且没有错误），见 index.md"
grep -E '^- (结果|各组用时)' "$OUT/index.md" 2>/dev/null || true
echo "$OUT"
exit $CODE
