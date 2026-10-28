"""Headless EEVEE preview helpers copied from the shallow scene, never exported."""
from pathlib import Path
import bpy,math,time,json,sys
from mathutils import Vector
BASE=Path(__file__).resolve().parent

def aim(o,at,target):
    o.location=at;o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler()

def area(name,at,power,size,color,target):
    data=bpy.data.lights.new(name,'AREA');data.energy=power;data.shape='DISK';data.size=size;data.color=color
    o=bpy.data.objects.new(name,data);bpy.data.collections['Reference'].objects.link(o);aim(o,at,target)
    return o

def outline_shells():
    """Preview-only inverted hulls; all source meshes and export budgets stay intact."""
    col=bpy.data.collections.new('Preview_InvertedHull');bpy.data.collections['Reference'].children.link(col)
    mat=bpy.data.materials.new('M_Preview_Ink');mat.use_nodes=True;mat.use_backface_culling=True
    p=mat.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(.001,.003,.009,1)
    p.inputs['Roughness'].default_value=1
    pairs=[];cache={}
    for group in ('Layout','Landmarks','Outer','Drift','Reference'):
        for o in list(bpy.data.collections[group].all_objects):
            if o.type!='MESH' or o.get('role') in ('floor','preview_floor') or o.name.startswith('Ink_'):continue
            if o.data.name not in cache:
                mesh=bpy.data.meshes.new('Ink_'+o.data.name)
                coords=[v.co+v.normal*.012 for v in o.data.vertices]
                mesh.from_pydata(coords,[],[tuple(reversed(p.vertices)) for p in o.data.polygons]);mesh.materials.append(mat);mesh.update()
                cache[o.data.name]=mesh
            shell=bpy.data.objects.new('Ink_'+o.name,cache[o.data.name]);col.objects.link(shell)
            pairs.append((o,shell,group))
    return pairs

def compositor(scene):
    # Screen-space normal edges create a fine black ink line without exporting outline shells.
    tree=bpy.data.node_groups.new('CacheSea_PreviewComposite','CompositorNodeTree')
    scene.compositing_node_group=tree
    tree.interface.new_socket(name='Image',in_out='OUTPUT',socket_type='NodeSocketColor')
    layer=scene.view_layers[0];layer.use_pass_normal=True
    rl=tree.nodes.new('CompositorNodeRLayers')
    edge=tree.nodes.new('CompositorNodeFilter');edge.inputs['Type'].default_value='Sobel'
    tree.links.new(rl.outputs['Normal'],edge.inputs['Image'])
    bw=tree.nodes.new('CompositorNodeRGBToBW');tree.links.new(edge.outputs[0],bw.inputs[0])
    ramp=tree.nodes.new('ShaderNodeValToRGB')
    ramp.color_ramp.elements[0].position=.06;ramp.color_ramp.elements[0].color=(1,1,1,1)
    ramp.color_ramp.elements[1].position=.50;ramp.color_ramp.elements[1].color=(.12,.18,.28,1)
    tree.links.new(bw.outputs[0],ramp.inputs[0])
    mix=tree.nodes.new('ShaderNodeMixRGB');mix.blend_type='MULTIPLY';mix.inputs[0].default_value=.72
    tree.links.new(rl.outputs['Image'],mix.inputs[1]);tree.links.new(ramp.outputs[0],mix.inputs[2])
    glow=tree.nodes.new('CompositorNodeGlare')
    glow.inputs['Type'].default_value='Fog Glow';glow.inputs['Quality'].default_value='Medium';glow.inputs['Threshold'].default_value=1.8
    tree.links.new(mix.outputs[0],glow.inputs['Image'])
    output=tree.nodes.new('NodeGroupOutput');tree.links.new(glow.outputs['Image'],output.inputs['Image'])

def configure(scene):
    try:scene.render.engine='BLENDER_EEVEE_NEXT'
    except TypeError:scene.render.engine='BLENDER_EEVEE'
    scene.render.resolution_x=1600;scene.render.resolution_y=900;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGB'
    scene.view_settings.exposure=.65
    if hasattr(scene,'eevee') and hasattr(scene.eevee,'taa_render_samples'):scene.eevee.taa_render_samples=48
    compositor(scene)
    scene.render.use_freestyle=False
    scene.camera.data.clip_start=.05;scene.camera.data.clip_end=500
    return outline_shells()

def sync_shells(shells,topdown=False):
    bpy.context.view_layer.update()
    for original,shell,group in shells:
        shell.matrix_world=original.matrix_world
        shell.hide_render=original.hide_render or bpy.data.collections[group].hide_render or (topdown and group=='Reference')

def camera_label(camera,body):
    mat=bpy.data.materials.new('M_Preview_Label');mat.use_nodes=True
    p=mat.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(.65,.8,1.,1)
    p.inputs['Emission Color'].default_value=(.65,.8,1.,1);p.inputs['Emission Strength'].default_value=1
    curve=bpy.data.curves.new('Preview_Caption','FONT');curve.body=body;curve.size=.014
    curve.materials.append(mat)
    ob=bpy.data.objects.new('Preview_Caption',curve);bpy.data.collections['Reference'].objects.link(ob)
    ob.parent=camera;ob.location=(-.45,.231,-1);return ob
