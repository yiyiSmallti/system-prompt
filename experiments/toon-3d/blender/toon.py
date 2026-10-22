"""Cel-shading materials for EEVEE: Shader to RGB -> constant Color Ramp, plus inverted-hull outlines.

Each toon material keeps an unlinked Principled BSDF (base colour and texture) so the glTF exporter
can be pointed at it, and stores its toon colours as custom properties, which glTF writes as
material extras for three.js / Unity.
"""
import bpy


def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def rgba(hex_color):
    h = hex_color.lstrip("#")
    return tuple(srgb_to_linear(int(h[i:i + 2], 16) / 255) for i in (0, 2, 4)) + (1.0,)


def _clear(nt):
    for n in list(nt.nodes):
        nt.nodes.remove(n)


def toon_material(name, base, shade, shade2, line, image=None, steps=(0.08, 0.42), rim=0.0, alpha=False):
    """Three-tone cel material.

    Lighting (a white Diffuse BSDF through Shader to RGB, so cast shadows count) drives a constant
    Color Ramp: below steps[0] -> shade2 (occluded), below steps[1] -> shade, else base.
    With an image, the ramp holds tints (colour / base) and multiplies the texture; alpha=True cuts
    the surface by the texture's alpha (eye lines and highlights on VRoid faces).
    rim adds a thin lit-side fresnel highlight, as anime hair and skin often have."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    _clear(nt)
    nodes, links = nt.nodes, nt.links

    out = nodes.new("ShaderNodeOutputMaterial")
    out.location = (900, 0)
    diff = nodes.new("ShaderNodeBsdfDiffuse")
    diff.inputs["Color"].default_value = (1, 1, 1, 1)
    diff.location = (-600, 0)
    s2r = nodes.new("ShaderNodeShaderToRGB")
    s2r.location = (-400, 0)
    links.new(diff.outputs[0], s2r.inputs[0])
    ramp = nodes.new("ShaderNodeValToRGB")
    ramp.location = (-200, 0)
    links.new(s2r.outputs["Color"], ramp.inputs["Fac"])
    cr = ramp.color_ramp
    cr.interpolation = "CONSTANT"
    if image is None:
        cols = [rgba(shade2), rgba(shade), rgba(base)]
    else:
        b = rgba(base)
        cols = [tuple(min(1.0, s / max(bb, 1e-4)) for s, bb in zip(rgba(c)[:3], b[:3])) + (1.0,) for c in (shade2, shade)]
        cols.append((1, 1, 1, 1))
    cr.elements[0].position, cr.elements[0].color = 0.0, cols[0]
    cr.elements[1].position, cr.elements[1].color = steps[0], cols[1]
    e = cr.elements.new(steps[1])
    e.color = cols[2]

    color = ramp.outputs["Color"]
    tex = None
    if image is not None:
        tex = nodes.new("ShaderNodeTexImage")
        tex.image = image
        tex.location = (-200, -300)
        mul = nodes.new("ShaderNodeMix")
        mul.data_type = "RGBA"
        mul.blend_type = "MULTIPLY"
        mul.inputs["Factor"].default_value = 1.0
        mul.location = (100, 0)
        links.new(color, mul.inputs["A"])
        links.new(tex.outputs["Color"], mul.inputs["B"])
        color = mul.outputs["Result"]

    if rim > 0:
        # fresnel rim, only where the surface is lit (ramp brightness above shade)
        lw = nodes.new("ShaderNodeLayerWeight")
        lw.inputs["Blend"].default_value = 0.35
        lw.location = (-200, 300)
        rr = nodes.new("ShaderNodeValToRGB")
        rr.color_ramp.interpolation = "CONSTANT"
        rr.color_ramp.elements[1].position = 0.6
        rr.color_ramp.elements[1].color = (rim, rim, rim, 1)
        rr.location = (50, 300)
        links.new(lw.outputs["Facing"], rr.inputs["Fac"])
        lit = nodes.new("ShaderNodeMath")
        lit.operation = "GREATER_THAN"
        lit.inputs[1].default_value = steps[1]
        lit.location = (50, 500)
        bw = nodes.new("ShaderNodeRGBToBW")
        bw.location = (-150, 500)
        links.new(s2r.outputs["Color"], bw.inputs[0])
        links.new(bw.outputs[0], lit.inputs[0])
        rimc = nodes.new("ShaderNodeMix")
        rimc.data_type = "RGBA"
        rimc.blend_type = "MULTIPLY"
        rimc.inputs["Factor"].default_value = 1.0
        rimc.location = (300, 300)
        links.new(rr.outputs["Color"], rimc.inputs["A"])
        links.new(lit.outputs[0], rimc.inputs["B"])
        add = nodes.new("ShaderNodeMix")
        add.data_type = "RGBA"
        add.blend_type = "ADD"
        add.inputs["Factor"].default_value = 1.0
        add.location = (500, 0)
        links.new(color, add.inputs["A"])
        links.new(rimc.outputs["Result"], add.inputs["B"])
        color = add.outputs["Result"]

    emit = nodes.new("ShaderNodeEmission")
    emit.location = (700, 0)
    links.new(color, emit.inputs["Color"])
    surface = emit
    if alpha and tex is not None:
        cut = nodes.new("ShaderNodeMixShader")
        cut.location = (800, 150)
        links.new(tex.outputs["Alpha"], cut.inputs["Fac"])
        links.new(nodes.new("ShaderNodeBsdfTransparent").outputs[0], cut.inputs[1])
        links.new(emit.outputs[0], cut.inputs[2])
        surface = cut
    surface.name = "toon_surface"
    links.new(surface.outputs[0], out.inputs["Surface"])

    # Unlinked PBR fallback, swapped in only while exporting glTF.
    pbr = nodes.new("ShaderNodeBsdfPrincipled")
    pbr.name = "glTF_export"
    pbr.location = (700, -400)
    pbr.inputs["Base Color"].default_value = rgba(base)
    pbr.inputs["Roughness"].default_value = 1.0
    if tex is not None:
        links.new(tex.outputs["Color"], pbr.inputs["Base Color"])

    m["toon_base"] = base
    m["toon_shade"] = shade
    m["toon_shade2"] = shade2
    m["toon_line"] = line
    m["toon_steps"] = list(steps)
    m.diffuse_color = rgba(base)
    return m


def outline_material(base_mat):
    """Back-face-culled flat colour for the inverted hull; it does not cast shadows."""
    m = bpy.data.materials.new(base_mat.name + "_outline")
    m.use_nodes = True
    nt = m.node_tree
    _clear(nt)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    emit = nt.nodes.new("ShaderNodeEmission")
    emit.inputs["Color"].default_value = rgba(base_mat["toon_line"])
    transp = nt.nodes.new("ShaderNodeBsdfTransparent")
    lp = nt.nodes.new("ShaderNodeLightPath")
    mix = nt.nodes.new("ShaderNodeMixShader")
    nt.links.new(lp.outputs["Is Shadow Ray"], mix.inputs["Fac"])
    nt.links.new(emit.outputs[0], mix.inputs[1])
    nt.links.new(transp.outputs[0], mix.inputs[2])
    nt.links.new(mix.outputs[0], out.inputs["Surface"])
    m.use_backface_culling = True
    if hasattr(m, "use_backface_culling_shadow"):
        m.use_backface_culling_shadow = True
    if hasattr(m, "use_transparent_shadow"):
        m.use_transparent_shadow = True
    m.diffuse_color = rgba(base_mat["toon_line"])
    return m


def add_outline(ob, thickness):
    """Inverted hull: a Solidify shell pushed outward along the normals, flipped, with outline materials."""
    base_mats = [s.material for s in ob.material_slots]
    for bm in base_mats:
        ob.data.materials.append(outline_material(bm))
    mod = ob.modifiers.new("Outline", "SOLIDIFY")
    mod.thickness = thickness
    mod.offset = 1.0
    mod.use_flip_normals = True
    mod.use_rim = False
    mod.material_offset = len(base_mats)
    return mod


def swap_for_gltf(materials, export):
    """Link the Principled fallback to the output while exporting; restore the toon surface after."""
    for m in materials:
        nt = m.node_tree
        out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL")
        pbr = nt.nodes.get("glTF_export")
        surface = nt.nodes.get("toon_surface")
        if pbr is None or surface is None:
            continue
        src = pbr if export else surface
        nt.links.new(src.outputs[0], out.inputs["Surface"])
