#!/bin/bash
# 构建端到端测试版并从标题玩到序章结束。截图、report.md 和日志写到 game/E2E/<时间>/。
# `e2e.sh tour` 改为录一段剪辑过的实机视频（画面加声音，需要 ffmpeg）：标题、设置、登记，序章每类台词一两句，手机和暂停菜单，缓存海浅层的 3D 探索和第一场战斗，输出同一目录下的 tour.mp4。
# `e2e.sh dive` 只录跳进缓存海的潜入转场（其余不入镜），输出 dive.mp4。
# 编辑器路径默认按 ProjectSettings/ProjectVersion.txt 的版本在 Unity Hub 的安装目录里找，也可以用 UNITY=... 指定。
# 运行期间会弹出一个 1600×900 的窗口，不要碰键盘鼠标；录视频时会放出声音。
set -euo pipefail
MODE="${1:-prologue}"

GAME="$(cd "$(dirname "$0")/.." && pwd)"
VERSION="$(sed -n 's/^m_EditorVersion: //p' "$GAME/ProjectSettings/ProjectVersion.txt")"
if [ -z "${UNITY:-}" ]; then
  for root in /Applications/Unity/Hub/Editor /Volumes/*/Applications/Unity/Hub/Editor "$HOME/Applications/Unity/Hub/Editor"; do
    if [ -x "$root/$VERSION/Unity.app/Contents/MacOS/Unity" ]; then UNITY="$root/$VERSION/Unity.app/Contents/MacOS/Unity"; break; fi
  done
fi
if [ -z "${UNITY:-}" ]; then echo "找不到 Unity $VERSION，用 UNITY=/path/to/Unity 指定" >&2; exit 2; fi

OUT="$GAME/E2E/$(date +%Y%m%d-%H%M%S)"
mkdir -p "$OUT"
APP="$GAME/Builds/E2E/SystemPromptE2E.app"

echo "构建 → $APP"
if ! "$UNITY" -batchmode -quit -projectPath "$GAME" -executeMethod SystemPrompt.EditorTools.BuildTools.BuildE2E \
  -buildOutput "$APP" -logFile "$OUT/build.log"; then
  echo "构建失败，见 $OUT/build.log" >&2
  grep -E 'error CS|Build Finished|BuildFailedException' "$OUT/build.log" | head -20 >&2 || true
  exit 1
fi

# 测试版有自己的产品名，每次从空存档开始。macOS 播放器的 persistentDataPath 按 bundle id 命名。
BUNDLE_ID="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$APP/Contents/Info.plist")"
rm -rf "$HOME/Library/Application Support/$BUNDLE_ID"

echo "运行 → $OUT"
set +e
if [ "$MODE" = tour ] || [ "$MODE" = dive ]; then
  DIVE=""
  if [ "$MODE" = dive ]; then DIVE="-dive"; fi
  "$APP/Contents/MacOS/System Prompt E2E" -tour "$OUT" $DIVE -screen-fullscreen 0 -screen-width 1600 -screen-height 900 -logFile "$OUT/player.log"
  CODE=$?
  set -e
  cat "$OUT/tour.txt" || true
  if [ $CODE -ne 0 ]; then exit $CODE; fi
  # Frames held for the time they were on screen, resampled to a steady 30 fps.
  ffmpeg -loglevel error -y -f concat -safe 0 -i "$OUT/frames.txt" -i "$OUT/sound.wav" \
    -vf "fps=30,format=yuv420p" -c:v libx264 -crf 18 -preset slow -c:a aac -b:a 192k -shortest -movflags +faststart "$OUT/$MODE.mp4"
  rm -rf "$OUT/frames" "$OUT/frames.txt" "$OUT/sound.wav"
  echo "视频 $OUT/$MODE.mp4"
  exit 0
fi
"$APP/Contents/MacOS/System Prompt E2E" -e2e "$OUT" -screen-fullscreen 0 -screen-width 1600 -screen-height 900 -logFile "$OUT/player.log"
CODE=$?
set -e
echo "退出码 $CODE，报告 $OUT/report.md"
exit $CODE
