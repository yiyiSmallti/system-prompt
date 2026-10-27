"""Fix the Tripo PBR maps on the cloth and the hair material, in place.

Run: blender -b MODEL.blend -P materials.py
Tripo marked the capelet, corset, neck bow and garters metallic all over (0.9-1.0), so the navy cloth renders
as dark metal; stockings and boot leather came out glossy. For each cloth part a new metallic-roughness map is
written: metal only where the base colour is gold, cloth roughness raised to a floor per part, gold kept
semi-glossy. The maps stay ordinary glTF textures, so the fix survives FBX/GLB export. Cloth also gets a
little sheen (Blender only). Hair: matte, weaker specular and normal map, so it stops reading as plastic.
Tail: the same, plus some subsurface light, so it reads as soft rather than a glossy tube. Running it again
leaves the maps it already rewrote alone.
"""
import bpy
import numpy as np

# cloth roughness floor per part (gold trim keeps 0.25-0.45)
CLOTH = {'capelet': 0.85, 'corset': 0.80, 'neck-bow': 0.60, 'garter-L': 0.65, 'garter-R': 0.65, 'boots': 0.50,
         'fin-ear-L': 0.60, 'fin-ear-R': 0.60, 'stockings': 0.75, 'blouse': 0.75, 'skirt': 0.85, 'apron': 0.95,
         'glove-L': 0.75, 'glove-R': 0.75, 'headband': 0.75}
SHEEN = {'capelet': 0.25, 'corset': 0.25, 'skirt': 0.25, 'stockings': 0.4, 'blouse': 0.2, 'apron': 0.2,
         'glove-L': 0.2, 'glove-R': 0.2, 'neck-bow': 0.15, 'headband': 0.2}


def pixels(img):
    a = np.empty(img.size[0] * img.size[1] * 4, dtype=np.float32)
    img.pixels.foreach_get(a)
    return a.reshape(-1, 4)


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


done = {}
for name, floor in CLOTH.items():
    o = bpy.data.objects.get(name)
    if not o:
        continue
    mat = o.material_slots[0].material
    b = bsdf(mat)
    if 'Sheen Weight' in b.inputs:
        b.inputs['Sheen Weight'].default_value = SHEEN.get(name, 0.2)
        b.inputs['Sheen Roughness'].default_value = 0.5
    if mat.name in done:
        continue
    base_node = tex_into(mat, 'Base Color')
    rm_node = tex_into(mat, 'Metallic') or tex_into(mat, 'Roughness')
    if not base_node or not rm_node:
        print('skip', name, 'no textures')
        continue
    base, rm = base_node.image, rm_node.image
    if rm.name.endswith('_fixed'):
        continue
    if not rm.size[0] or not base.size[0]:
        raise SystemExit('texture not loaded: %s / %s (relative path?)' % (base.filepath, rm.filepath))
    if tuple(base.size) != tuple(rm.size):
        base = base.copy()
        base.scale(*rm.size)
    bc = pixels(base)
    r, g, bl = bc[:, 0], bc[:, 1], bc[:, 2]
    # gold: red and green clearly above blue, and bright enough (stored sRGB values)
    gold = np.clip((np.minimum(r, g) - bl - 0.04) / 0.12, 0, 1) * np.clip((r - 0.30) / 0.15, 0, 1)
    px = pixels(rm)
    old_metal, old_rough = px[:, 2].copy(), px[:, 1].copy()
    px[:, 2] = gold * np.maximum(old_metal, 0.85)
    px[:, 1] = gold * np.clip(old_rough, 0.25, 0.45) + (1 - gold) * np.maximum(old_rough, floor)
    new = bpy.data.images.new(rm.name + '_fixed', rm.size[0], rm.size[1], alpha=True, float_buffer=False)
    new.colorspace_settings.name = rm.colorspace_settings.name
    new.pixels.foreach_set(px.ravel())
    new.pack()
    rm_node.image = new
    # Roughness may have been overridden with a constant earlier; feed it from the map again
    sep = next((n for n in mat.node_tree.nodes if n.type == 'SEPARATE_COLOR'), None)
    if sep and not b.inputs['Roughness'].links:
        mat.node_tree.links.new(sep.outputs['Green'], b.inputs['Roughness'])
    done[mat.name] = name
    print('cloth %-10s metal %.2f -> %.2f, roughness %.2f -> %.2f, gold %.1f%%' % (
        name, old_metal.mean(), px[:, 2].mean(), old_rough.mean(), px[:, 1].mean(), 100 * (gold > 0.5).mean()), flush=True)

# hair: matte with a soft sheen instead of a wet plastic highlight
hm = bpy.data.objects['hair'].material_slots[0].material
hb = bsdf(hm)
for l in list(hb.inputs['Roughness'].links):
    hm.node_tree.links.remove(l)
hb.inputs['Roughness'].default_value = 0.62
hb.inputs['Specular IOR Level'].default_value = 0.25
hb.inputs['Sheen Weight'].default_value = 0.35
hb.inputs['Sheen Roughness'].default_value = 0.35
hb.inputs['Sheen Tint'].default_value = (0.75, 0.9, 1.0, 1)
for n in hm.node_tree.nodes:
    if n.type == 'NORMAL_MAP':
        n.inputs['Strength'].default_value = 0.5
print('hair matte: roughness 0.62, specular 0.25, sheen 0.35, normal 0.5')

# tail: soft and a little translucent; the glow stays the assembly JSON's emissive, the roughness matches its `mat`
tm = bpy.data.objects['tail-glow'].material_slots[0].material
tb = bsdf(tm)
for l in list(tb.inputs['Roughness'].links):
    tm.node_tree.links.remove(l)
tb.inputs['Roughness'].default_value = 0.65
tb.inputs['Specular IOR Level'].default_value = 0.3
tb.inputs['Subsurface Weight'].default_value = 0.35
tb.inputs['Subsurface Radius'].default_value = (0.25, 0.55, 1.0)
tb.inputs['Subsurface Scale'].default_value = 0.02
tb.inputs['Sheen Weight'].default_value = 0.4
tb.inputs['Sheen Roughness'].default_value = 0.4
tb.inputs['Sheen Tint'].default_value = (0.6, 0.85, 1.0, 1)
for n in tm.node_tree.nodes:
    if n.type == 'NORMAL_MAP':
        n.inputs['Strength'].default_value = 0.4
print('tail soft: roughness 0.65, specular 0.3, subsurface 0.35, sheen 0.4, normal 0.4')
bpy.ops.wm.save_mainfile()
