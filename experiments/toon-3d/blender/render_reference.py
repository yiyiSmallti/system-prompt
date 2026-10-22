"""Control experiment: a professionally made VRoid character through the same toon pipeline.

The procedural ChatGPT model reads as modelling clay. This separates "the model" from "the shading":
it imports VRM1_Constraint_Twist_Sample (pixiv Inc., VRM Public License 1.0; its VRM meta allows
redistribution, modification and corporate commercial use, credit not required), replaces its MToon
materials with the toon materials from toon.py, adds the same inverted-hull outlines and light rig,
poses the arms down through the model's own skeleton, and renders a full-body front view and the
same bust close-up as build.py.

usage: python render_reference.py --vrm <VRM1_Constraint_Twist_Sample.vrm> [--out ../out]
"""
import argparse
import json
import math
import pathlib
import shutil
import struct
import sys
import tempfile
import time

import bpy
from mathutils import Matrix

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import build  # noqa: E402
import toon  # noqa: E402

# view: rotation (deg), framed centre (x, z), framed height (m), pixels
VIEWS = {
    "front": (0, (0.0, 0.84), 1.8176, (908, 2048)),
    "bust": (-30, (0.0, 1.33), 0.55, (1024, 1024)),
}
ARMS_DOWN = {"L": 68, "R": -68}     # degrees about the forward axis
# The sample drives its shoulder, sleeve and twist bones with VRM node constraints, which Blender does
# not evaluate, so they are posed by hand: the arm, its twist bone and the sleeve aim bone turn with
# the upper arm; the shoulder aim bone turns to keep pointing at the elbow.
ARM_BONES = ("J_Bip_{}_UpperArm", "J_Roll_{}_UpperArm", "J_Aim_{}_TopsUpperArm")
SHOULDER_AIM = "J_Aim_{}_Shoulder"
NO_OUTLINE = ("FACE", "EYE")
OUTLINE = 0.0012


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    p = argparse.ArgumentParser()
    p.add_argument("--vrm", required=True)
    p.add_argument("--out", default=str(HERE.parent / "out"))
    p.add_argument("--samples", type=int, default=16)
    return p.parse_args(argv)


def mtoon_params(path):
    """MToon shade and outline colours per material, read straight from the VRM's glTF JSON."""
    data = pathlib.Path(path).read_bytes()
    n = struct.unpack("<I", data[12:16])[0]
    gltf = json.loads(data[20:20 + n])
    out = {}
    for m in gltf["materials"]:
        mt = m.get("extensions", {}).get("VRMC_materials_mtoon", {})
        out[m["name"]] = {"shade": mt.get("shadeColorFactor", [1, 1, 1]),
                          "line": mt.get("outlineColorFactor", [0, 0, 0])}
    return out


def to_hex(linear):
    srgb = [c * 12.92 if c <= 0.0031308 else 1.055 * c ** (1 / 2.4) - 0.055 for c in linear[:3]]
    return "#" + "".join(f"{round(max(0, min(1, c)) * 255):02X}" for c in srgb)


def toonify(objects, params):
    clear = bpy.data.materials.new("no_outline")
    clear.use_nodes = True
    nt = clear.node_tree
    nt.links.new(nt.nodes.new("ShaderNodeBsdfTransparent").outputs[0], nt.nodes["Material Output"].inputs["Surface"])
    for ob in objects:
        if ob.type != "MESH":
            continue
        groups = []
        for slot in ob.material_slots:
            src = slot.material
            img = next((n.image for n in src.node_tree.nodes if n.type == "TEX_IMAGE" and n.image), None)
            group = src.name.rsplit("_", 1)[-1]
            p = params.get(src.name, {"shade": [1, 1, 1], "line": [0, 0, 0]})
            if group in NO_OUTLINE:
                shade = shade2 = "#FFFFFF"            # eyes and face decals stay unlit
            else:
                lin = p["shade"] if min(p["shade"][:3]) < 0.99 else [0.66, 0.62, 0.74]
                shade, shade2 = to_hex(lin), to_hex([c * 0.8 for c in lin])
            steps = (0.02, 0.10) if src.name.startswith("Face_") else (0.10, 0.45)
            slot.material = toon.toon_material(src.name + "_toon", "#FFFFFF", shade, shade2, to_hex(p["line"]),
                                               image=img, steps=steps, alpha=group in NO_OUTLINE)
            groups.append(group)
        toon.add_outline(ob, OUTLINE)
        for k, group in enumerate(groups):
            if group in NO_OUTLINE:
                ob.material_slots[len(groups) + k].material = clear


def pose_arms(objects):
    arm = next(o for o in objects if o.type == "ARMATURE")
    bones = arm.pose.bones
    for side, deg in ARMS_DOWN.items():
        upper = bones[ARM_BONES[0].format(side)]
        aim = bones[SHOULDER_AIM.format(side)]
        rest = {n: bones[n.format(side)].matrix.copy() for n in ARM_BONES + (SHOULDER_AIM,)}
        pivot, elbow, aim_head = upper.head.copy(), upper.tail.copy(), aim.head.copy()
        turn = Matrix.Translation(pivot) @ Matrix.Rotation(math.radians(deg), 4, "Y") @ Matrix.Translation(-pivot)
        e0, e1 = elbow - aim_head, turn @ elbow - aim_head
        aim_deg = math.degrees(math.atan2(e0.x * e1.z - e0.z * e1.x, e0.x * e1.x + e0.z * e1.z)) * -1
        aim_turn = Matrix.Translation(aim_head) @ Matrix.Rotation(math.radians(aim_deg), 4, "Y") @ Matrix.Translation(-aim_head)
        # parents before children; pose matrices are absolute, so each bone lands exactly where intended
        aim.matrix = aim_turn @ rest[SHOULDER_AIM]
        bpy.context.view_layer.update()
        for name in ARM_BONES:
            bones[name.format(side)].matrix = turn @ rest[name]
            bpy.context.view_layer.update()


def main():
    args = parse_args()
    out = pathlib.Path(args.out).resolve() / "renders" / "reference_blender"
    out.mkdir(parents=True, exist_ok=True)
    sc = build.setup_scene(argparse.Namespace(scale=2.0, samples=args.samples))
    with tempfile.TemporaryDirectory() as tmp:
        glb = pathlib.Path(tmp) / "model.glb"           # a .vrm is a glb with VRM extensions
        shutil.copy(args.vrm, glb)
        bpy.ops.import_scene.gltf(filepath=str(glb))
    objects = list(bpy.context.selected_objects)
    root = bpy.data.objects.new("Reference", None)
    sc.collection.objects.link(root)
    for ob in objects:
        if ob.parent is None:
            ob.parent = root
    toonify(objects, mtoon_params(args.vrm))
    pose_arms(objects)

    cam = sc.camera
    for view, (rot, (cx, cz), height, (w, h)) in VIEWS.items():
        root.rotation_euler = (0, 0, math.radians(rot))
        cam.location = (cx, -5.0, cz)
        cam.data.ortho_scale = height * max(w, h) / h
        sc.render.resolution_x, sc.render.resolution_y = w, h
        sc.render.filepath = str(out / f"{view}.png")
        t = time.time()
        bpy.ops.render.render(write_still=True)
        print(f"rendered {view} in {time.time() - t:.1f} s")


if __name__ == "__main__":
    main()
