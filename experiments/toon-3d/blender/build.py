"""Build the toon ChatGPT model, render the four turnaround views in EEVEE, save .blend and export .glb.

Runs either as a plain Python script with the `bpy` wheel from PyPI, or inside Blender:
    python build.py --out ../out
    blender -b --python build.py -- --out ../out

Cameras are orthographic and framed like the reference turnaround (1.775 mm per reference pixel,
sole on the measured row), so renders line up pixel for pixel with
assets/characters/turnarounds/chatgpt/1-uniform/*.jpg at 2x.
"""
import argparse
import json
import math
import pathlib
import sys
import time

import bpy
from mathutils import Vector

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import character  # noqa: E402
import toon  # noqa: E402

PX = 0.001775                         # metres per reference pixel
REF_W, REF_H = 454, 1024
# view: (rotation of the character about Z in degrees, camera x offset in metres, sole row in the reference)
VIEWS = {
    "front": (0, 0.0107, 989),
    "left": (-90, 0.064, 980),
    "right": (90, -0.046, 980),
    "back": (180, -0.0036, 980),
}
# close-up for judging the face and hair at PV scale: rotation, framed centre (x, z), framed height, pixels
CLOSEUPS = {"bust": (-30, (0.0, 1.41), 0.66, (1024, 1024))}
LIGHT_DIR = (0.45, 0.75, -0.55)       # sun travels from the upper front-left of the camera
AMBIENT = 0.20

# base, 1st shade, 2nd shade, line (sRGB)
PALETTE = {
    "skin": ("#FFF3EE", "#F5D3CD", "#E2AEAB", "#B9817F"),
    "face": ("#FFF3EE", "#F8DCD6", "#F0C6C1", "#B9817F"),
    "hair": ("#F8F6FE", "#DDD4F4", "#BFB2E6", "#8E7FC8"),
    "shirt": ("#FCFBFF", "#DCD8EE", "#C2BCDC", "#7F7AA2"),
    "skirt": ("#4A4C60", "#343647", "#262735", "#13141B"),
    "stripe": ("#86BDF0", "#6A9EDA", "#5785C2", "#2C4C80"),
    "bow": ("#B6D7FA", "#8DB6EC", "#7499D8", "#4A6EA8"),
    "armband": ("#3E4055", "#2C2E3D", "#20212C", "#101118"),
    "stocking": ("#50464F", "#3A333B", "#2B252C", "#151115"),
    "shoe": ("#7A4B3B", "#5A3428", "#42251C", "#24130E"),
    "sole": ("#3C2C27", "#2C201C", "#201714", "#120C0A"),
    "horn": ("#F4F1FC", "#D6CDEF", "#B9ADDF", "#8475B8"),
    "wing": ("#E3D9F8", "#CBBDEE", "#B3A3E0", "#8E7FC8"),
    "wing_bone": ("#EEE9FA", "#D2C8EE", "#B7AADF", "#8475B8"),
    "tail": ("#FBFAFF", "#DDD4F4", "#C3B7E8", "#8E7FC8"),
    "silver": ("#DCDDE6", "#A9ABBA", "#8A8C9C", "#5B5D6C"),
    "silver_white": ("#F4F4F8", "#D5D3E3", "#BDBAD0", "#8A87A3"),
    "crystal": ("#C9B8F2", "#A08BDF", "#8570C8", "#6C58B0"),
}
TEXTURED = {"face": "face.png", "tail": "tail_scales.png"}
STEPS = {"face": (0.02, 0.10)}        # keep the face mostly lit, as cel-shaded faces usually are
RIM = {"hair": 0.08, "skin": 0.05}


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    p = argparse.ArgumentParser()
    p.add_argument("--out", default=str(HERE.parent / "out"))
    p.add_argument("--textures", default=str(HERE.parent / "textures"))
    p.add_argument("--views", default=",".join(list(VIEWS) + list(CLOSEUPS)))
    p.add_argument("--scale", type=float, default=2.0, help="render size relative to the reference image")
    p.add_argument("--samples", type=int, default=16)
    p.add_argument("--no-render", action="store_true")
    p.add_argument("--no-export", action="store_true")
    return p.parse_args(argv)


def setup_scene(args):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_EEVEE"
    sc.view_settings.view_transform = "Standard"
    sc.view_settings.look = "None"
    sc.render.film_transparent = True
    sc.render.resolution_x = round(REF_W * args.scale)
    sc.render.resolution_y = round(REF_H * args.scale)
    sc.render.image_settings.file_format = "PNG"
    sc.render.image_settings.color_mode = "RGBA"
    sc.eevee.taa_render_samples = args.samples

    world = bpy.data.worlds.new("Ambient")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (AMBIENT, AMBIENT, AMBIENT, 1)
    sc.world = world

    sun = bpy.data.lights.new("Key", "SUN")
    sun.energy = math.pi               # a lit surface facing the sun reads 1.0 through Shader to RGB
    sun.angle = math.radians(0.5)
    ob = bpy.data.objects.new("Key", sun)
    ob.rotation_euler = Vector(LIGHT_DIR).normalized().to_track_quat("-Z", "Y").to_euler()
    sc.collection.objects.link(ob)

    cam = bpy.data.cameras.new("Ortho")
    cam.type = "ORTHO"
    cam.ortho_scale = REF_H * PX
    cam.clip_end = 20
    ob = bpy.data.objects.new("Camera", cam)
    ob.rotation_euler = (math.pi / 2, 0, 0)
    sc.collection.objects.link(ob)
    sc.camera = ob
    return sc


def make_materials(tex_dir):
    mats = {}
    for name, (base, shade, shade2, line) in PALETTE.items():
        img = None
        if name in TEXTURED:
            img = bpy.data.images.load(str(pathlib.Path(tex_dir) / TEXTURED[name]))
        mats[name] = toon.toon_material(name, base, shade, shade2, line, image=img,
                                        steps=STEPS.get(name, (0.10, 0.45)), rim=RIM.get(name, 0.0))
    return mats


def build_character(mats):
    root = bpy.data.objects.new("ChatGPT_Uniform", None)
    bpy.context.scene.collection.objects.link(root)
    parts = character.build_all(mats)
    for ob, outline in parts.items():
        ob.parent = root
        ob["outline_width"] = outline         # exported as node extras for three.js
        ob["cast_shadow"] = int(ob.visible_shadow)
        if ob.name in ("Skirt", "Wings"):
            # single-sided surfaces get real thickness first so the hull has a closed shape to follow
            mod = ob.modifiers.new("Thickness", "SOLIDIFY")
            mod.thickness = 0.005 if ob.name == "Skirt" else 0.003
            mod.offset = -1.0
        toon.add_outline(ob, outline)
    return root, list(parts)


def frame_view(sc, root, view, size):
    """Turn the character and place the orthographic camera for one turnaround view or close-up."""
    cam = sc.camera
    if view in VIEWS:
        rot, cam_x, sole_row = VIEWS[view]
        cam.location = (cam_x, -5.0, (sole_row - REF_H / 2) * PX)
        cam.data.ortho_scale = REF_H * PX
        sc.render.resolution_x, sc.render.resolution_y = size
    else:
        rot, (cx, cz), height, (w, h) = CLOSEUPS[view]
        cam.location = (cx, -5.0, cz)
        cam.data.ortho_scale = height * max(w, h) / h
        sc.render.resolution_x, sc.render.resolution_y = w, h
    root.rotation_euler = (0, 0, math.radians(rot))


def render_views(sc, root, args, out_dir):
    out_dir.mkdir(parents=True, exist_ok=True)
    times = {}
    size = (sc.render.resolution_x, sc.render.resolution_y)
    for view in args.views.split(","):
        frame_view(sc, root, view, size)
        sc.render.filepath = str(out_dir / f"{view}.png")
        t = time.time()
        bpy.ops.render.render(write_still=True)
        times[view] = round(time.time() - t, 1)
        print(f"rendered {view} in {times[view]} s")
    frame_view(sc, root, "front", size)
    return times


def export_glb(path, root, parts, materials):
    for ob in parts:
        for mod in ob.modifiers:
            if mod.name == "Outline":
                mod.show_viewport = mod.show_render = False
    toon.swap_for_gltf(materials, export=True)
    bpy.ops.object.select_all(action="DESELECT")
    root.select_set(True)
    for ob in parts:
        ob.select_set(True)
    bpy.ops.export_scene.gltf(filepath=str(path), export_format="GLB", use_selection=True,
                              export_apply=True, export_extras=True, export_yup=True)
    toon.swap_for_gltf(materials, export=False)
    for ob in parts:
        for mod in ob.modifiers:
            if mod.name == "Outline":
                mod.show_viewport = mod.show_render = True


def stats(parts):
    dg = bpy.context.evaluated_depsgraph_get()
    out = {}
    for ob in parts:
        me = ob.data
        ev = ob.evaluated_get(dg).to_mesh()
        out[ob.name] = {"verts": len(me.vertices), "tris": sum(len(p.vertices) - 2 for p in me.polygons),
                        "tris_with_outline": sum(len(p.vertices) - 2 for p in ev.polygons)}
        ob.evaluated_get(dg).to_mesh_clear()
    return out


def main():
    args = parse_args()
    out = pathlib.Path(args.out).resolve()
    out.mkdir(parents=True, exist_ok=True)
    t0 = time.time()
    sc = setup_scene(args)
    mats = make_materials(args.textures)
    root, parts = build_character(mats)
    info = {"blender": bpy.app.version_string, "build_seconds": round(time.time() - t0, 1), "parts": stats(parts)}
    print(json.dumps(info["parts"], indent=1))

    # the saved file opens on the front view, so F12 in Blender reproduces front.png
    frame_view(sc, root, "front", (sc.render.resolution_x, sc.render.resolution_y))
    bpy.ops.file.pack_all()
    bpy.ops.wm.save_as_mainfile(filepath=str(out / "chatgpt_uniform.blend"), compress=True)
    if not args.no_export:
        export_glb(out / "chatgpt_uniform.glb", root, parts, list(mats.values()))
    if not args.no_render:
        info["render_seconds"] = render_views(sc, root, args, out / "renders" / "blender")
    import gpu
    try:
        info["gpu"] = f"{gpu.platform.vendor_get()} / {gpu.platform.renderer_get()} / {gpu.platform.version_get()}"
    except Exception:
        pass
    (out / "blender_stats.json").write_text(json.dumps(info, indent=1, ensure_ascii=False))


if __name__ == "__main__":
    main()
