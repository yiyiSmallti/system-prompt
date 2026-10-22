#!/usr/bin/env bash
# Rebuild everything from scratch (after setup_env.sh):
#   textures -> procedural model, .blend, .glb and EEVEE renders -> three.js renders
#   -> VRoid control model through both renderers -> comparison sheets
# About 4 minutes on 4 CPU cores without a GPU.
set -euo pipefail
HERE=$(cd "$(dirname "$0")" && pwd)
PY=${TOON3D_VENV:-$HOME/.cache/toon3d/venv}/bin/python
OUT=${1:-$HERE/out}
REF=$HERE/../../assets/characters/turnarounds/chatgpt/1-uniform
VRM=$OUT/cache/VRM1_Constraint_Twist_Sample.vrm
VRM_URL=https://raw.githubusercontent.com/vrm-c/vrm-specification/master/samples/VRM1_Constraint_Twist_Sample/vrm/VRM1_Constraint_Twist_Sample.vrm
VRM_SHA256=12c2b97e95e700783a6a550dc0eee2d7880aeedccef9ae67bc4c5a2f0f2631a2

cd "$HERE"
"$PY" blender/make_textures.py textures
"$PY" blender/build.py --out "$OUT"
node threejs/render.mjs --out "$OUT"

mkdir -p "$OUT/cache"
[ -f "$VRM" ] || curl -fsSL -o "$VRM" "$VRM_URL"
echo "$VRM_SHA256  $VRM" | sha256sum -c -
"$PY" blender/render_reference.py --vrm "$VRM" --out "$OUT"
node threejs/render.mjs --mode reference --glb "$VRM" --out "$OUT"

"$PY" compose.py "$REF" "$OUT"
