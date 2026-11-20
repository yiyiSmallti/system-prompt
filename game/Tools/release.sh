#!/bin/bash
# 构建正式版并打成 zip：`release.sh`（macOS 和 Windows）、`release.sh mac`、`release.sh win`。
# 输出在 game/Builds/Release/：macOS/System Prompt.app、Windows/ 整个文件夹，以及 SystemPrompt-<版本>-<平台>.zip 和各平台的构建日志。
# 正式版不带 SP_E2E，端到端驱动、视觉实验室和录像都不编进去。Windows 需要先在 Unity Hub 里给本版本编辑器加 Windows Build Support (Mono)。
# 编辑器路径按 ProjectSettings/ProjectVersion.txt 的版本找，也可以用 UNITY=... 指定；构建时不能开着这个工程的编辑器，也不能同时跑 e2e.sh 或 lab.sh。
set -euo pipefail
WHICH="${1:-all}"

GAME="$(cd "$(dirname "$0")/.." && pwd)"
VERSION="$(sed -n 's/^m_EditorVersion: //p' "$GAME/ProjectSettings/ProjectVersion.txt")"
if [ -z "${UNITY:-}" ]; then
  for root in /Applications/Unity/Hub/Editor /Volumes/*/Applications/Unity/Hub/Editor "$HOME/Applications/Unity/Hub/Editor"; do
    if [ -x "$root/$VERSION/Unity.app/Contents/MacOS/Unity" ]; then UNITY="$root/$VERSION/Unity.app/Contents/MacOS/Unity"; break; fi
  done
fi
if [ -z "${UNITY:-}" ]; then echo "找不到 Unity $VERSION，用 UNITY=/path/to/Unity 指定" >&2; exit 2; fi

case "$WHICH" in
  all) TARGETS="mac win" ;;
  mac|win) TARGETS="$WHICH" ;;
  *) echo "用法：release.sh [all|mac|win]" >&2; exit 2 ;;
esac

APP_VERSION="$(sed -n 's/^  bundleVersion: //p' "$GAME/ProjectSettings/ProjectSettings.asset")"
OUT="$GAME/Builds/Release"
mkdir -p "$OUT"

for t in $TARGETS; do
  if [ "$t" = mac ]; then METHOD=BuildReleaseMac; DIR="$OUT/macOS"; PLATFORM=macOS
  else METHOD=BuildReleaseWindows; DIR="$OUT/Windows"; PLATFORM=Windows; fi
  LOG="$OUT/build-$PLATFORM.log"
  # 上一次的产物整个删掉，免得旧文件混进新包。
  rm -rf "$DIR"
  echo "构建 $PLATFORM → $DIR"
  if ! "$UNITY" -batchmode -quit -projectPath "$GAME" -executeMethod "SystemPrompt.EditorTools.BuildTools.$METHOD" -logFile "$LOG"; then
    echo "$PLATFORM 构建失败，见 $LOG" >&2
    grep -E 'error CS|正式版构建失败|构建失败|BuildFailedException|another Unity instance' "$LOG" | head -20 >&2 || true
    exit 1
  fi
  grep -E '构建完成' "$LOG" | tail -1 || true
  ZIP="$OUT/SystemPrompt-$APP_VERSION-$PLATFORM.zip"
  rm -f "$ZIP"
  # ditto 保留 .app 里的符号链接和可执行权限。不带扩展属性：macOS 给 Unity 写出的每个文件都加了
  # com.apple.provenance，默认会被打成一个个 ._ 文件，Windows 用户解压后会看到 ._System Prompt.exe 之类的垃圾，
  # 用非 Finder 工具解压的 Mac 用户还会把它们放进 .app。签名嵌在二进制里，不依赖扩展属性。
  ZIPFLAGS=(-c -k --norsrc --noextattr --noqtn --noacl)
  if [ "$t" = mac ]; then ditto "${ZIPFLAGS[@]}" --keepParent "$DIR/System Prompt.app" "$ZIP"
  else ditto "${ZIPFLAGS[@]}" "$DIR" "$ZIP"; fi
  if unzip -Z1 "$ZIP" | grep -qE '(^|/)\._'; then echo "$ZIP 里还有 ._ 文件" >&2; exit 1; fi
  echo "打包 → $ZIP（$(du -h "$ZIP" | cut -f1)）"
done
