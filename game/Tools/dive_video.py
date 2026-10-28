# Renders the dive video, Assets/SystemPrompt/Resources/Art/Transitions/dive.mp4: an endless fall down a
# well of chat bubbles, data panels, code and air bubbles towards a cold white light, the data sea of
# assets/ui/transitions/data_sea.png in 3D. Everything repeats every PERIOD units down the well and the
# camera falls one period per loop, so the last frame runs into the first.
# Three steps: python3 with Pillow draws the textures into a temporary folder, Blender (in the background,
# Eevee) builds the well and renders the frames, ffmpeg adds the glow and encodes them. Run from the
# repository root:
#   python3 game/Tools/dive_video.py [--preview]
# --preview renders every 4th frame at half size into the temporary folder only, for a quick look.
import math
import os
import random
import shutil
import subprocess
import sys
import tempfile

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "game/Assets/SystemPrompt/Resources/Art/Transitions/dive.mp4")
BLENDER = os.environ.get("BLENDER", "/Applications/Blender.app/Contents/MacOS/Blender")
FPS, SECONDS = 30, 8
FRAMES = FPS * SECONDS
PERIOD = 60.0  # well units per loop: 7.5 a second
RADIUS = 5.0
FADE = 30.0  # well units over which things sink into the dark (e^-1)
FOG = (0.004, 0.009, 0.035, 1.0)  # linear, the dark of the well and the background
CARD = (512, 320)


def textures(folder):
    from PIL import Image, ImageDraw, ImageFilter, ImageFont
    import numpy as np

    rng = random.Random(3)

    # Cards, a 2x2 atlas: chat bubble, data panel, terminal card, message bubble. Translucent fills, bright
    # outlines, a soft glow around both.
    def card(draw):
        sharp = Image.new("RGBA", CARD, (0, 0, 0, 0))
        draw(ImageDraw.Draw(sharp))
        return Image.alpha_composite(sharp.filter(ImageFilter.GaussianBlur(12)), sharp)

    def bubble(d):
        fill, edge = (50, 120, 235, 110), (170, 225, 255, 255)
        d.polygon([(90, 230), (64, 300), (170, 230)], fill=fill)
        d.rounded_rectangle((16, 16, 496, 236), 48, fill=fill, outline=edge, width=7)
        d.line([(90, 236), (64, 300), (170, 236)], fill=edge, width=7)
        for x in (186, 256, 326):
            d.ellipse((x - 20, 106, x + 20, 146), fill=(235, 245, 255, 255))

    def panel(d):
        d.rounded_rectangle((16, 16, 496, 304), 20, fill=(30, 80, 190, 120), outline=(120, 195, 255, 255), width=5)
        d.rectangle((40, 40, 472, 72), fill=(110, 190, 255, 170))
        for i in range(6):
            d.rectangle((40, 96 + i * 32, 40 + rng.randint(120, 400), 108 + i * 32), fill=(150, 210, 255, 190))

    def terminal(d):
        green = (200, 255, 225, 255)
        d.rounded_rectangle((96, 40, 416, 280), 26, fill=(30, 170, 120, 120), outline=(120, 255, 190, 255), width=6)
        d.line([(150, 110), (200, 160), (150, 210)], fill=green, width=12)
        d.rectangle((220, 198, 300, 212), fill=green)

    def message(d):
        fill, edge = (40, 170, 225, 100), (160, 240, 255, 255)
        d.polygon([(350, 230), (440, 300), (420, 230)], fill=fill)
        d.rounded_rectangle((16, 16, 496, 236), 48, fill=fill, outline=edge, width=7)
        d.line([(350, 236), (440, 300), (420, 236)], fill=edge, width=7)
        for y, w in ((70, 400), (112, 330), (154, 240)):
            d.rectangle((56, y, 56 + w, y + 16), fill=(215, 245, 255, 220))

    atlas = Image.new("RGBA", (CARD[0] * 2, CARD[1] * 2), (0, 0, 0, 0))
    for i, draw in enumerate((bubble, panel, terminal, message)):
        atlas.paste(card(draw), ((i % 2) * CARD[0], (i // 2) * CARD[1]))
    atlas.save(os.path.join(folder, "cards.png"))

    # Code running down the well: a cyan and a green column of glyphs, 16 to a tile.
    font = ImageFont.truetype("/System/Library/Fonts/Menlo.ttc", 46)
    code = Image.new("RGBA", (128, 1024), (0, 0, 0, 0))
    d = ImageDraw.Draw(code)
    for column, color in enumerate(((110, 220, 255), (90, 255, 170))):
        for row in range(16):
            if rng.random() < 0.12:
                continue
            glyph = rng.choice("0123456789ABCDEF" * 3 + "<>{}=;#")
            d.text((column * 64 + 32, row * 64 + 32), glyph, font=font, anchor="mm", fill=color + (rng.randint(140, 255),))
    code.save(os.path.join(folder, "code.png"))

    # The well's lining: dim panels of different sizes, a few lit ones. Cells line up with the tile's edges,
    # so it repeats.
    wall = Image.new("RGB", (1024, 1024), (5, 10, 28))
    d = ImageDraw.Draw(wall)
    for cy in range(16):
        for cx in range(8):
            x0, y0 = cx * 128, cy * 64
            cuts = sorted(rng.sample(range(24, 105, 16), rng.randint(0, 2)))
            for a, b in zip([0] + cuts, cuts + [128]):
                roll = rng.random()
                color = (40, 140, 220) if roll < 0.07 else (30, 160, 115) if roll < 0.1 else (
                    rng.randint(10, 24), rng.randint(24, 52), rng.randint(70, 130))
                d.rectangle((x0 + a + 3, y0 + 3, x0 + b - 3, y0 + 61), fill=color)
    wall.filter(ImageFilter.GaussianBlur(1)).save(os.path.join(folder, "wall.png"))

    # The light at the bottom: a white point in a wide blue halo.
    y, x = np.mgrid[-1:1:512j, -1:1:512j]
    r = np.sqrt(x * x + y * y)
    hot = np.exp(-(r / 0.06) ** 2)
    alpha = np.clip(hot + 0.7 * np.exp(-(r / 0.4) ** 2), 0, 1) * np.clip((1 - r) * 4, 0, 1)
    rgb = np.array([80, 140, 255]) * (1 - hot[..., None]) + 255 * hot[..., None]
    Image.fromarray(np.dstack([rgb, alpha * 255]).astype(np.uint8)).save(os.path.join(folder, "core.png"))


def build(folder, preview):
    import bpy
    import bmesh
    from mathutils import Matrix, Vector

    rng = random.Random(11)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene

    def twist(z):
        # One turn down each period, so the code and the lining spiral and the well still repeats.
        return 2 * math.pi * z / PERIOD

    def nodes(name):
        m = bpy.data.materials.new(name)
        if hasattr(m, "surface_render_method"):
            m.surface_render_method = "DITHERED"
        else:
            m.blend_method = "HASHED"
        tree = m.node_tree
        tree.nodes.clear()
        return m, tree, tree.nodes.new("ShaderNodeOutputMaterial")

    def math_node(tree, op, a, b=None):
        n = tree.nodes.new("ShaderNodeMath")
        n.operation = op
        for i, v in enumerate((a, b)):
            if v is None:
                continue
            if isinstance(v, (int, float)):
                n.inputs[i].default_value = v
            else:
                tree.links.new(v, n.inputs[i])
        return n.outputs[0]

    def glowing(name, color, strength, alpha=None, image=None, sinks=True):
        # Emission that sinks into the fog with distance (colour first, then transparency, so far things
        # never cover the light), with an optional texture and alpha.
        m, tree, out = nodes(name)
        lit = tree.nodes.new("ShaderNodeEmission")
        lit.inputs["Strength"].default_value = strength
        if image is not None:
            tex = tree.nodes.new("ShaderNodeTexImage")
            tex.image = bpy.data.images.load(os.path.join(folder, image))
            tree.links.new(tex.outputs["Color"], lit.inputs["Color"])
            alpha = tex.outputs["Alpha"] if alpha is None else alpha(tree, tex)
        else:
            lit.inputs["Color"].default_value = color
        shader = lit.outputs[0]
        if sinks:
            distance = tree.nodes.new("ShaderNodeCameraData").outputs["View Distance"]
            near = math_node(tree, "EXPONENT", math_node(tree, "MULTIPLY", distance, -1 / FADE))
            fog = tree.nodes.new("ShaderNodeEmission")
            fog.inputs["Color"].default_value = FOG
            mix = tree.nodes.new("ShaderNodeMixShader")
            tree.links.new(near, mix.inputs[0])
            tree.links.new(fog.outputs[0], mix.inputs[1])
            tree.links.new(lit.outputs[0], mix.inputs[2])
            shader = mix.outputs[0]
            visible = math_node(tree, "MINIMUM", math_node(tree, "MULTIPLY", near, 4), 1)
            alpha = visible if alpha is None else math_node(tree, "MULTIPLY", alpha, visible)
        if alpha is not None:
            cut = tree.nodes.new("ShaderNodeMixShader")
            tree.links.new(alpha, cut.inputs[0])
            tree.links.new(tree.nodes.new("ShaderNodeBsdfTransparent").outputs[0], cut.inputs[1])
            tree.links.new(shader, cut.inputs[2])
            shader = cut.outputs[0]
        tree.links.new(shader, out.inputs["Surface"])
        return m

    def mesh_object(name, bm, material):
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        bm.free()
        me.materials.append(material)
        ob = bpy.data.objects.new(name, me)
        scene.collection.objects.link(ob)
        return ob

    def frame_driver(target, path, index, per_loop):
        # Driven by the frame number, so it turns a whole number of times per loop.
        d = target.driver_add(path, index).driver
        d.type = "SCRIPTED"
        d.expression = f"frame*{per_loop / FRAMES:.8f}"

    copies = range(-1, 4)  # one above, for code reaching down past the camera at the start; three below

    # The lining: a cylinder from just behind the camera to past the last copy.
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new()
    around, rows = 64, 60
    top, bottom = 5.0, -PERIOD * len(copies) - 20
    grid = [[bm.verts.new((RADIUS * math.cos(2 * math.pi * i / around), RADIUS * math.sin(2 * math.pi * i / around),
                           top + (bottom - top) * j / rows)) for i in range(around + 1)] for j in range(rows + 1)]
    for j in range(rows):
        for i in range(around):
            face = bm.faces.new((grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]))
            for loop, (a, b) in zip(face.loops, ((i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1))):
                z = top + (bottom - top) * b / rows
                loop[uv].uv = (10 * a / around - 10 * z / PERIOD, 12 * z / PERIOD)
    mesh_object("Lining", bm, glowing("Lining", None, 0.55, image="wall.png"))

    # Cards: bubbles and panels standing out from the lining or floating in the well, leaning towards the
    # middle; half of them turn over once per loop.
    cards = glowing("Cards", None, 2.2, image="cards.png")
    shapes = []
    for k in range(4):
        bm = bmesh.new()
        uv = bm.loops.layers.uv.new()
        verts = [bm.verts.new((x * 0.8, y * 0.5, 0)) for x, y in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
        face = bm.faces.new(verts)
        u0, v0 = (k % 2) * 0.5, 0.5 - (k // 2) * 0.5
        for loop, (x, y) in zip(face.loops, ((0, 0), (1, 0), (1, 1), (0, 1))):
            loop[uv].uv = (u0 + x * 0.5, v0 + y * 0.5)
        me = bpy.data.meshes.new(f"Card{k}")
        bm.to_mesh(me)
        bm.free()
        me.materials.append(cards)
        shapes.append(me)
    for n in range(260):
        r = rng.uniform(3.3, 4.7) if rng.random() < 0.7 else rng.uniform(1.6, 3.3)
        z0 = -rng.uniform(0, PERIOD)
        a = rng.uniform(0, 2 * math.pi) + twist(z0)
        size = rng.uniform(0.5, 1.2)
        tangent = Vector((-math.sin(a), math.cos(a), 0))
        lean = Matrix.Rotation(-rng.uniform(math.radians(20), math.radians(70)), 3, tangent)
        base = lean @ Matrix.Rotation(a + math.pi / 2 + rng.uniform(-0.5, 0.5), 3, "Z")
        shape = rng.choices(shapes, (3, 2, 1, 2))[0]  # few of the green ones
        spin = rng.choice((0, 0, 1, -1)) if r > 2.5 else 0
        axis = rng.randrange(2)
        for c in copies:
            ob = bpy.data.objects.new("Card", shape)
            scene.collection.objects.link(ob)
            ob.location = (r * math.cos(a), r * math.sin(a), z0 - c * PERIOD)
            ob.scale = (size, size, size)
            ob.delta_rotation_euler = base.to_euler("XYZ")  # applied after rotation_euler, so this turns it in place
            if spin:
                frame_driver(ob, "rotation_euler", axis, spin * 2 * math.pi)

    # Code: ribbons of glyphs on the lining, spiralling with it.
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new()
    for n in range(46):
        r = rng.uniform(4.3, 4.9)
        length = rng.uniform(6, 18)
        z0 = -rng.uniform(0, PERIOD)
        a0 = rng.uniform(0, 2 * math.pi)
        half = 0.11 / r
        column = rng.randrange(2) * 0.5
        steps = int(length / 0.5)
        for c in copies:
            rows_ = []
            for s in range(steps + 1):
                z = z0 - s * length / steps
                a = a0 + twist(z)
                rows_.append([bm.verts.new((r * math.cos(a + e), r * math.sin(a + e), z - c * PERIOD)) for e in (-half, half)])
            for s in range(steps):
                face = bm.faces.new((rows_[s][0], rows_[s][1], rows_[s + 1][1], rows_[s + 1][0]))
                v = (s * length / steps) / 3.5, ((s + 1) * length / steps) / 3.5
                for loop, (x, y) in zip(face.loops, ((0, v[0]), (1, v[0]), (1, v[1]), (0, v[1]))):
                    loop[uv].uv = (column + x * 0.5, y)
    mesh_object("Code", bm, glowing("Code", None, 2.5, image="code.png"))

    # Dashed rings of light round the lining, three to a period: the clearest sign of the speed.
    bm = bmesh.new()
    for n in range(3):
        z0 = -PERIOD * (n + rng.random() * 0.6) / 3
        for c in copies:
            for i in range(48):
                a = 2 * math.pi * i / 48 + twist(z0)
                e = 2 * math.pi / 48 * 0.32
                z = z0 - c * PERIOD
                rr = RADIUS * 0.97
                bm.faces.new([bm.verts.new((rr * math.cos(a + s), rr * math.sin(a + s), z + h))
                              for s, h in ((-e, -0.05), (e, -0.05), (e, 0.05), (-e, 0.05))])
    mesh_object("Rings", bm, glowing("Rings", (0.35, 0.8, 1.0, 1.0), 3.0))

    # Air bubbles: bright at the rim, clear in the middle.
    m, tree, out = nodes("Bubbles")
    rim = tree.nodes.new("ShaderNodeLayerWeight")
    rim.inputs["Blend"].default_value = 0.5
    edge = math_node(tree, "POWER", rim.outputs["Facing"], 2.5)
    distance = tree.nodes.new("ShaderNodeCameraData").outputs["View Distance"]
    near = math_node(tree, "EXPONENT", math_node(tree, "MULTIPLY", distance, -1 / FADE))
    lit = tree.nodes.new("ShaderNodeEmission")
    lit.inputs["Color"].default_value = (0.6, 0.85, 1.0, 1.0)
    lit.inputs["Strength"].default_value = 3.0
    cut = tree.nodes.new("ShaderNodeMixShader")
    tree.links.new(math_node(tree, "MULTIPLY", edge, near), cut.inputs[0])
    tree.links.new(tree.nodes.new("ShaderNodeBsdfTransparent").outputs[0], cut.inputs[1])
    tree.links.new(lit.outputs[0], cut.inputs[2])
    tree.links.new(cut.outputs[0], out.inputs["Surface"])
    bm = bmesh.new()
    for n in range(130):
        r = rng.uniform(1.0, 4.8)
        z0 = -rng.uniform(0, PERIOD)
        a = rng.uniform(0, 2 * math.pi)
        size = rng.uniform(0.04, 0.22)
        for c in copies:
            bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=8, radius=size,
                                      matrix=Matrix.Translation((r * math.cos(a), r * math.sin(a), z0 - c * PERIOD)))
    mesh_object("Bubbles", bm, m)

    # Specks: tiny bits of light everywhere.
    bm = bmesh.new()
    for n in range(700):
        r = rng.uniform(0.8, 4.8)
        z0 = -rng.uniform(0, PERIOD)
        a = rng.uniform(0, 2 * math.pi)
        size = rng.uniform(0.015, 0.045)
        for c in copies:
            bmesh.ops.create_icosphere(bm, subdivisions=1, radius=size,
                                       matrix=Matrix.Translation((r * math.cos(a), r * math.sin(a), z0 - c * PERIOD)))
    mesh_object("Specks", bm, glowing("Specks", (0.7, 0.9, 1.0, 1.0), 5.0))

    # The camera falls down the middle one period per loop, looking down; the light rides with it.
    cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
    cam.data.lens = 18
    cam.data.clip_end = 400
    scene.collection.objects.link(cam)
    scene.camera = cam
    frame_driver(cam, "location", 2, -PERIOD)
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new()
    face = bm.faces.new([bm.verts.new((x, y, 0)) for x, y in ((-1, -1), (1, -1), (1, 1), (-1, 1))])
    for loop, xy in zip(face.loops, ((0, 0), (1, 0), (1, 1), (0, 1))):
        loop[uv].uv = xy
    core = mesh_object("Core", bm, glowing("Core", None, 3.0, image="core.png", sinks=False))
    core.parent = cam
    core.location = (0, 0, -115)
    core.scale = (14, 14, 14)

    world = bpy.data.worlds.new("Well")
    world.node_tree.nodes["Background"].inputs["Color"].default_value = FOG
    scene.world = world

    render = scene.render
    try:
        render.engine = "BLENDER_EEVEE_NEXT"
    except TypeError:
        render.engine = "BLENDER_EEVEE"
    scene.eevee.taa_render_samples = 8 if preview else 24
    render.use_motion_blur = True
    if hasattr(render, "motion_blur_shutter"):
        render.motion_blur_shutter = 0.5
    else:
        scene.eevee.motion_blur_shutter = 0.5
    scene.view_settings.view_transform = "Standard"
    render.resolution_x, render.resolution_y = 1920, 1080
    render.resolution_percentage = 50 if preview else 100
    render.fps = FPS
    scene.frame_start, scene.frame_end = 0, FRAMES - 1
    scene.frame_step = 4 if preview else 1
    render.image_settings.file_format = "PNG"
    render.image_settings.color_mode = "RGB"
    render.filepath = os.path.join(folder, "frames", "f_")
    bpy.ops.render.render(animation=True)


def main():
    preview = "--preview" in sys.argv
    folder = tempfile.mkdtemp(prefix="dive_video_")
    textures(folder)
    flags = ["--preview"] if preview else []
    subprocess.run([BLENDER, "-b", "--factory-startup", "--python", os.path.abspath(__file__), "--", folder] + flags, check=True)
    out = os.path.join(folder, "dive.mp4") if preview else OUT
    # The glow: the bright parts blurred and screened back on (in RGB: screening YUV turns it purple).
    glow = "[0]format=gbrp,split[a][b];[b]colorlevels=rimin=0.45:gimin=0.45:bimin=0.45,gblur=sigma=14[g];[a][g]blend=all_mode=screen:all_opacity=0.85,format=yuv420p"
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-framerate", str(FPS / (4 if preview else 1)), "-pattern_type", "glob",
                    "-i", os.path.join(folder, "frames", "f_*.png"), "-filter_complex", glow, "-c:v", "libx264",
                    "-preset", "slow", "-crf", "24", "-movflags", "+faststart", "-an", out], check=True)
    if preview:
        print("preview:", out)
    else:
        shutil.rmtree(folder)
        print("wrote", out)


if __name__ == "__main__":
    try:
        import bpy  # noqa: F401 (only inside Blender)
    except ImportError:
        main()
    else:
        argv = sys.argv[sys.argv.index("--") + 1:]
        build(argv[0], "--preview" in argv)
