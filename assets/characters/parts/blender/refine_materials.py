"""Give each kind of surface on a refined whole-character model its own feel, so it stops reading as plastic.

Run: blender -b MODEL-refined.blend -P refine_materials.py -- [OUT.glb]     (saves the .blend in place)
Also imported by refine_whole.py, which calls apply() on the body material.

Tripo writes one value for everything (roughness about 0.73, no metal), and Blender's default specular level
is 0.5, so cloth, skin and hair all catch the same broad highlight. The atlas is thousands of scraps, so the
surfaces are told apart by base colour: gold, cyan gems, skin, hair, white cloth, and dark cloth for the rest.
Per class this writes:
- roughness and metal into a new metallic-roughness map (kept in glTF);
- a softened copy of the baked normal map: Tripo's surface is lumpy like clay, which on skin and hair is most
  of the plastic look, so the normal there is pulled toward the smooth low-poly normal (kept in glTF);
- a flattened copy of the base colour: the generation was not delit, so skin and white cloth carry painted
  shadows; their brightness is pulled toward the lit tone of the class, hue and saturation kept, because the
  game's importer warms the skin itself and a tint here came out orange there (kept in glTF);
- a "surface" map that drives specular level, cloth sheen and skin subsurface (Blender only).
The baked normal map and Tripo's base colour stay in the file as sources, so the script can be re-run after
changing the numbers.
"""
import os
import sys

import bpy
import numpy as np

#            roughness  specular  sheen  subsurface  normal kept  shading removed
FEEL = {
    'white': (0.94,     0.12,     0.35,  0.0,        0.80,        0.50),   # blouse, apron, frills, stockings
    'dark':  (0.88,     0.16,     0.45,  0.0,        0.90,        0.0),    # navy capelet, corset, skirt, boots
    'hair':  (0.70,     0.20,     0.0,   0.0,        0.55,        0.0),    # hair, plaid panel, tail
    'skin':  (0.58,     0.22,     0.0,   0.30,       0.25,        0.90),
    'gem':   (0.18,     0.50,     0.0,   0.0,        1.0,         0.0),
    'gold':  (0.38,     0.50,     0.0,   0.0,        1.0,         0.0),
}
LIT_PERCENTILE = 85      # the brightness a class is flattened toward: this percentile of its own texels
GAIN_RANGE = (0.9, 2.2)  # how far one texel may be darkened or lifted
# Skin in painted shade is also browner than lit skin (the band of thigh under the skirt), and lifted as it is
# comes out orange once the game warms the skin. Texels that needed lifting by more than the first factor take
# the lit skin's own colour, wholly from the second factor on; blush is as bright as lit skin and is left alone.
SKIN_RECOLOUR = (1.15, 1.6)
JPEG_QUALITY = 95
GOLD_METAL = 0.9
SKIN_RADIUS = (0.012, 0.005, 0.003)   # subsurface scatter distance per colour channel, model units


def ramp(x, lo, hi):
    return np.clip((x - lo) / (hi - lo), 0, 1)


def classify(r, g, b):
    """Soft masks (0..1) per surface kind from stored sRGB base colour; they sum to 1."""
    mx, mn = np.maximum(np.maximum(r, g), b), np.minimum(np.minimum(r, g), b)
    # gold: warm with green above blue. In this atlas the thread is pale (about 0.75, 0.63, 0.51) and skin
    # is pink (green within 0.03 of blue), so the green-blue gap is what separates the two.
    gold = ramp(g - b, 0.025, 0.06) * ramp(r - b, 0.06, 0.12) * ramp(r, 0.30, 0.45)
    # gems and glowing trim: bright cyan
    gem = ramp(np.minimum(g, b) - r, 0.30, 0.45) * ramp(g, 0.65, 0.80)
    # skin: red above blue; what is yellow enough to be gold was taken above. Skin in painted shade goes as
    # dark as red 0.45 (the thighs under the skirt), so brightness cannot be what keeps the lines drawn on the
    # face out (mouth, eyelid creases): those are redder, green under 0.72 of red where shaded skin keeps 0.8.
    skin = ramp(r - b, 0.02, 0.05) * ramp(r, 0.30, 0.42) * ramp(g / np.maximum(r, 1e-3), 0.72, 0.80)
    # hair, plaid, tail: clearly blue and not dark
    hair = ramp(b - r, 0.22, 0.36) * ramp(mx, 0.42, 0.58)
    # white cloth: bright, little colour
    white = ramp(mn, 0.55, 0.72) * ramp(0.30 - (mx - mn), 0.0, 0.10)
    out, left = {}, np.ones_like(r)
    for name, m in (('gold', gold), ('gem', gem), ('skin', skin), ('hair', hair), ('white', white)):
        out[name] = m * left
        left = left * (1 - m)
    out['dark'] = left
    return out


def bsdf(mat):
    return next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')


def tex_into(mat, socket_name):
    """The image texture node feeding a BSDF input, directly or through Separate Color."""
    s = bsdf(mat).inputs[socket_name]
    if not s.links:
        return None
    n = s.links[0].from_node
    while n.type != 'TEX_IMAGE' and n.inputs and n.inputs[0].links:
        n = n.inputs[0].links[0].from_node
    return n if n.type == 'TEX_IMAGE' else None


def pixels(img):
    a = np.empty(img.size[0] * img.size[1] * 4, dtype=np.float32)
    img.pixels.foreach_get(a)
    return a.reshape(-1, 4)


def store(name, size, px):
    """A packed non-colour image built from float pixels."""
    img = bpy.data.images.new(name, size[0], size[1], alpha=True, float_buffer=False)
    img.colorspace_settings.name = 'Non-Color'
    img.pixels.foreach_set(px.ravel())
    img.pack()
    return img, None


def luminance(r, g, b):
    return 0.299 * r + 0.587 * g + 0.114 * b


def flatten_base(src, path):
    """Copy of the base colour with painted shading removed per class, saved as JPEG at `path` and packed."""
    px = pixels(src)
    n = len(px)
    sample = px[::max(1, n // 2000000)]
    sc = classify(sample[:, 0], sample[:, 1], sample[:, 2])
    sl = luminance(sample[:, 0], sample[:, 1], sample[:, 2])
    lit = {k: float(np.percentile(sl[sc[k] > 0.8], LIT_PERCENTILE)) for k in FEEL if FEEL[k][5] and (sc[k] > 0.8).any()}
    bright = (sc['skin'] > 0.8) & (sl >= lit.get('skin', 2.0))
    skin_rgb = sample[bright][:, :3].mean(0) if bright.any() else None
    for a in range(0, n, 4000000):   # in slices: an 8K map is 67 million texels
        c = px[a:a + 4000000]
        cls = classify(c[:, 0], c[:, 1], c[:, 2])
        lum = np.maximum(luminance(c[:, 0], c[:, 1], c[:, 2]), 0.05)
        gain = np.ones_like(lum)
        for k, target in lit.items():
            gain += cls[k] * (np.clip((target / lum) ** FEEL[k][5], *GAIN_RANGE) - 1)
        rgb = c[:, :3] * gain[:, None]
        if skin_rgb is not None:
            was_dark = cls['skin'] * ramp(lit['skin'] / lum, *SKIN_RECOLOUR)
            plain = skin_rgb[None, :] * (luminance(rgb[:, 0], rgb[:, 1], rgb[:, 2]) / luminance(*skin_rgb))[:, None]
            rgb += was_dark[:, None] * (plain - rgb)
        c[:, :3] = np.clip(rgb, 0, 1)
    tmp = bpy.data.images.new('flatten_tmp', src.size[0], src.size[1], alpha=False)
    tmp.pixels.foreach_set(px.ravel())
    tmp.filepath_raw, tmp.file_format = path, 'JPEG'
    tmp.save(quality=JPEG_QUALITY)
    bpy.data.images.remove(tmp)
    img = bpy.data.images.load(path)
    img.pack()
    print('FLATTEN: lit tone ' + ', '.join('%s %.2f' % kv for kv in lit.items()))
    return img


def swap(nodes, old, new, name):
    """Point the image nodes showing `old` at `new`, and give `new` the name, dropping an unused earlier one."""
    for n in nodes:
        if n.type == 'TEX_IMAGE' and n.image == old:
            n.image = new
    prev = bpy.data.images.get(name)
    if prev and prev != new and not prev.users:
        bpy.data.images.remove(prev)
    new.name = name


def apply(mat, name, tex_dir):
    """Rewrite mat's maps by surface kind and wire the Blender-only surface map. JPEGs go to tex_dir."""
    nt = mat.node_tree
    b = bsdf(mat)
    base_node, rm_node = tex_into(mat, 'Base Color'), tex_into(mat, 'Roughness')
    normal_node = b.inputs['Normal'].links[0].from_node.inputs['Color'].links[0].from_node
    # sources: what Tripo and the bake produced, kept in the file under a fake user
    base_src = bpy.data.images.get(mat.get('base_source', '')) or base_node.image
    normal_src = bpy.data.images.get(mat.get('normal_source', '')) or normal_node.image
    for key, img in (('base_source', base_src), ('normal_source', normal_src)):
        mat[key] = img.name
        img.use_fake_user = True
    size = tuple(rm_node.image.size)
    small = base_src.copy()
    small.scale(*size)
    bc = pixels(small)
    bpy.data.images.remove(small)
    cls = classify(bc[:, 0], bc[:, 1], bc[:, 2])

    def blend(col):
        return sum(cls[k] * FEEL[k][col] for k in FEEL)

    px = pixels(rm_node.image).copy()
    px[:, 1] = blend(0)
    px[:, 2] = cls['gold'] * GOLD_METAL
    rm, _ = store(name + '_rm_tmp', size, px)
    swap(nt.nodes, rm_node.image, rm, name + '_rm_fixed')

    if tuple(normal_src.size) == size:
        npx = pixels(normal_src).copy()
        keep = blend(4)
        npx[:, 0] = 0.5 + (npx[:, 0] - 0.5) * keep
        npx[:, 1] = 0.5 + (npx[:, 1] - 0.5) * keep
        soft, _ = store(name + '_normal_tmp', size, npx)
        swap(nt.nodes, normal_node.image, soft, name + '_normal_soft')
    else:
        print('normal map is %s, surface map %s: left as it is' % (tuple(normal_src.size), size))

    os.makedirs(tex_dir, exist_ok=True)
    flat = flatten_base(base_src, os.path.join(tex_dir, name + '_basecolor_flat.jpg'))
    swap(nt.nodes, base_node.image, flat, name + '_basecolor_flat')

    # surface map: R specular level, G sheen, B subsurface
    sp = np.ones_like(px)
    sp[:, 0], sp[:, 1], sp[:, 2] = blend(1), blend(2), blend(3)
    surf, _ = store(name + '_surface_tmp', size, sp)
    node = nt.nodes.get('Surface') or nt.nodes.new('ShaderNodeTexImage')
    node.name = node.label = 'Surface'
    old_surf = node.image
    node.image = surf
    if old_surf and not old_surf.users:
        bpy.data.images.remove(old_surf)
    surf.name = name + '_surface'
    sep = nt.nodes.get('SurfaceSplit') or nt.nodes.new('ShaderNodeSeparateColor')
    sep.name = 'SurfaceSplit'
    nt.links.new(node.outputs['Color'], sep.inputs['Color'])
    nt.links.new(sep.outputs['Red'], b.inputs['Specular IOR Level'])
    nt.links.new(sep.outputs['Green'], b.inputs['Sheen Weight'])
    nt.links.new(sep.outputs['Blue'], b.inputs['Subsurface Weight'])
    b.inputs['Sheen Roughness'].default_value = 0.5
    b.inputs['Subsurface Radius'].default_value = SKIN_RADIUS
    b.inputs['Subsurface Scale'].default_value = 1.0
    print('SURFACES: ' + ', '.join('%s %.1f%%' % (k, 100 * float(cls[k].mean())) for k in FEEL))


def apply_hair(mat):
    """A material that is hair all over (the grafted ahoge): constants instead of maps."""
    b = bsdf(mat)
    b.inputs['Roughness'].default_value = FEEL['hair'][0]
    b.inputs['Specular IOR Level'].default_value = FEEL['hair'][1]
    b.inputs['Metallic'].default_value = 0.0


if __name__ == '__main__':
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    name = os.path.basename(bpy.data.filepath).replace('-refined.blend', '')
    apply(bpy.data.materials['DeepSeekBattle'], name, os.path.join(os.path.dirname(bpy.data.filepath), 'textures'))
    if 'DeepSeekAhoge' in bpy.data.materials:
        apply_hair(bpy.data.materials['DeepSeekAhoge'])
    bpy.ops.wm.save_mainfile()
    if argv:
        bpy.ops.object.select_all(action='DESELECT')
        ob = bpy.data.objects['DeepSeekBattle']
        ob.select_set(True)
        bpy.context.view_layer.objects.active = ob
        bpy.ops.export_scene.gltf(filepath=argv[0], export_format='GLB', use_selection=True)
