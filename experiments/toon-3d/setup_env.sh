#!/usr/bin/env bash
# One-time environment for the toon-3d experiment, CPU only:
#   - Blender 5.2 as a Python module (the official `bpy` wheel on PyPI, needs Python 3.13) plus Pillow
#   - Mesa EGL/llvmpipe so EEVEE can render without a GPU or display
#   - three.js and playwright-core; Chromium itself comes from $CHROME_PATH
#     (the Claude Code cloud image ships one at /opt/pw-browsers/chromium-1194/chrome-linux/chrome)
set -euo pipefail
HERE=$(cd "$(dirname "$0")" && pwd)
VENV=${TOON3D_VENV:-$HOME/.cache/toon3d/venv}

if command -v apt-get >/dev/null && [ "$(id -u)" = 0 ]; then
  DEBIAN_FRONTEND=noninteractive apt-get install -y -qq libegl1 libegl-mesa0 libgl1-mesa-dri \
    libxkbcommon0 libxi6 libxxf86vm1 libxfixes3 libxrender1 libsm6 libice6 fonts-wqy-zenhei >/dev/null
fi
python3.13 -m venv "$VENV"
"$VENV/bin/pip" install -q --upgrade pip
"$VENV/bin/pip" install -q bpy==5.2.3 pillow==13.0.0
(cd "$HERE/threejs" && npm ci --no-audit --no-fund)
"$VENV/bin/python" -c "import bpy; print('bpy', bpy.app.version_string)"
