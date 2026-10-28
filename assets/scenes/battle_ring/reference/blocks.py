# The ring of rack stacks round the battle field, as BattleStage.BuildRacks lays it out
# (game/Assets/SystemPrompt/Scripts/Dungeon/BattleStage.cs). Hash is DungeonWorld.Hash in float32.
import json, math, sys
import numpy as np

def hash2(x, y):
    a = np.float32(x) * np.float32(127.1) + np.float32(y) * np.float32(311.7)
    n = np.float32(math.sin(float(a))) * np.float32(43758.5453)
    return float(n - np.floor(n))

CELL, CENTRE_Z = 4.0, 3.4  # BattleStage.Centre = (0, 0, 3.4) in Unity, i.e. Blender (0, 3.4)
blocks = []
for y in range(-9, 10):
    for x in range(-9, 10):
        d = math.hypot(x, y)
        h = hash2(x + 40, y + 40)
        if d < 4.2 or d > 9.5 or h < 0.3 + (d - 4.2) * 0.05:
            continue
        height = 4 + hash2(y + 40, x + 40) * (5 + (d - 4.2) * 2.2)
        blocks.append({"cell": [x, y], "centre_blender": [x * CELL, CENTRE_Z + y * CELL], "height": round(height, 3)})
out = {"cell_size": CELL, "field_centre_blender": [0.0, CENTRE_Z],
       "note": "Blender X = Unity x, Blender Y = Unity z. Each block is a solid 4 x 4 m stack of racks of the given height.",
       "blocks": blocks}
json.dump(out, open(sys.argv[1] if len(sys.argv) > 1 else "blocks.json", "w"), indent=1)
print(len(blocks), "blocks; heights", min(b["height"] for b in blocks), "-", max(b["height"] for b in blocks))
