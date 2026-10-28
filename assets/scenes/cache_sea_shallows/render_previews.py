"""Render in headless EEVEE. Optional: -- --only start_lighthouse,topdown,kit_sheet_props."""
from pathlib import Path
import bpy, sys, math, json, time, tempfile
import numpy as np
from mathutils import Vector
from bpy_extras.object_utils import world_to_camera_view

BASE=Path(__file__).resolve().parent

SHOTS={
    'persp_start_lighthouse': ((58,8.7,2.4),(58,55,10.0),21,[(57.3,20,0),(58.55,21.2,0)]),
    'persp_sunken_phone': ((57,23,2.9),(58,40,1.5),22,[(53.6,30,0),(54.4,31,0)]),
    'persp_lighthouse_plaza': ((49,56.5,2.8),(58,75,12.0),13,[(49.7,73,0),(48.7,74.2,0)]),
    'persp_emoji_coral': ((7,18,2.7),(24,24,3),24,[(14,15,0),(16.2,15.4,0)]),
    'persp_deleted_posts': ((6,53,2.6),(17,67,3.2),22,[(9,66,0),(10.2,67,0)]),
    'persp_rack_canyon': ((106,53,2.6),(106,78,7.5),20,[(105.4,65,0),(106.5,67,0)]),
    'persp_maintenance': ((78,46,2.7),(94,33,2),20,[(86,43.5,0),(87.2,42.8,0)]),
    'persp_maintenance_gate': ((94,38,2.65),(94,25,1.8),23,[(92.8,31,0),(95.1,30.7,0)]),
}

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
    for group in ('Layout','Landmarks','Props','Reference','Kit'):
        for o in list(bpy.data.collections[group].all_objects):
            if o.type!='MESH' or o.get('role')=='floor' or o.name.startswith('Ink_'):continue
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

def contact_sheets(scene,cam,people,fills,caption,shells,only):
    """Frame every kit and the same 1.75m figure, then compose two 6x3 sheets."""
    report=json.loads((BASE/'build_report.json').read_text())
    kit=bpy.data.collections['Kit'];roots={o['kit_name']:o for o in kit.objects if o.type=='EMPTY'}
    modules=[n for n in report['kit'] if n.startswith(('Rack','Seabed','LowRack','Bubble','EmojiCoral'))]
    props=[n for n in report['kit'] if n not in modules]
    assert len(modules)==len(props)==18,(modules,props)
    pages=[('kit_sheet',modules,'MODULAR KIT / 01'),('kit_sheet_props',props,'PROPS & LANDMARKS / 02')]
    pages=[p for p in pages if only is None or p[0] in only]
    if not pages:return {},{}
    for n in ('Layout','Landmarks','Props','Outer','Drift'):bpy.data.collections[n].hide_render=True
    kit.hide_render=False
    for root in roots.values():
        for child in root.children:child.hide_render=True
    for o in people:o.hide_render=True
    for light in bpy.data.lights:
        if light.type=='POINT':light.energy=0
    for name,power,rotation in [('Sea_Key',2.4,(.45,-.5,-.4)),('Sea_Fill',1.1,(.9,.6,2.5))]:
        ob=bpy.data.objects[name];ob.data.color=(1,1,1);ob.data.energy=power;ob.rotation_euler=rotation
    world=scene.world;bg=world.node_tree.nodes.get('Background')
    for link in list(bg.inputs['Color'].links):world.node_tree.links.remove(link)
    bg.inputs['Color'].default_value=(.012,.020,.036,1);bg.inputs['Strength'].default_value=.7
    scene.view_settings.exposure=1.15
    glare=scene.compositing_node_group.nodes.get('Glare')
    glare.inputs['Threshold'].default_value=3.5;glare.inputs['Strength'].default_value=.35
    cam.data.type='ORTHO';cam.data.sensor_fit='VERTICAL';cam.data.clip_start=.01;cam.data.clip_end=650
    direction=Vector((6,-9,7.5)).normalized()
    orientation=(-direction).to_track_quat('-Z','Y')
    right=orientation@Vector((1,0,0));up=orientation@Vector((0,1,0))
    cam.rotation_euler=orientation.to_euler()
    ref=bpy.data.collections['Reference']
    floor_mesh=bpy.data.meshes.new('Contact_Stage')
    floor_mesh.from_pydata([(-1,-1,0),(1,-1,0),(1,1,0),(-1,1,0)],[],[(0,1,2,3)])
    floor_mesh.materials.append(bpy.data.materials['M_Seabed_Plaza'])
    floor=bpy.data.objects.new('Contact_Stage',floor_mesh);ref.objects.link(floor);floor.location.z=-.06
    captions=[]
    for name in ('Contact_Name','Contact_Dimensions','Contact_Triangles'):
        curve=bpy.data.curves.new(name,'FONT');curve.align_x='CENTER';curve.align_y='CENTER';curve.materials.append(caption)
        ob=bpy.data.objects.new(name,curve);ref.objects.link(ob);ob.parent=cam;captions.append(ob)
    def sync_shells():
        bpy.context.view_layer.update()
        for original,shell,group in shells:
            shell.hide_render=original.hide_render or group not in ('Kit','Reference')
            shell.matrix_world=original.matrix_world
    def image_pixels(path):
        image=bpy.data.images.load(str(path),check_existing=False)
        w,h=image.size;pixels=np.empty(w*h*4,dtype=np.float32);image.pixels.foreach_get(pixels)
        bpy.data.images.remove(image)
        return pixels.reshape(h,w,4)
    def camera_caption(ob,body,y_px,size_px,width,height,scale,align='CENTER',x_px=None):
        ob.hide_render=False;ob.data.body=body;ob.data.size=size_px/height*scale;ob.data.align_x=align
        x=0 if x_px is None else (x_px/width-.5)*(width/height)*scale
        ob.location=(x,(y_px/height-.5)*scale,-1);ob.rotation_euler=(0,0,0)
    page_times={};entries={}
    with tempfile.TemporaryDirectory(prefix='cache-sea-kit-cells-',dir='/private/tmp') as task_tmp:
        task_tmp=Path(task_tmp)
        for page,names,title in pages:
            started=time.perf_counter()
            canvas=np.empty((2400,3200,4),dtype=np.float32);canvas[:]=(.006,.011,.022,1)
            for index,name in enumerate(names):
                root=roots[name];original_location=root.location.copy()
                info=report['kit'][name];mn=Vector(info['min']);mx=Vector(info['max'])
                try:
                    root.location=(-(mn.x+mx.x)/2,-(mn.y+mx.y)/2,-mn.z)
                    for child in root.children:child.hide_render=False
                    bpy.context.view_layer.update()
                    points=[child.matrix_world@Vector(p) for child in root.children for p in child.bound_box]
                    # This figure retains its real 1.75m dimensions in every cell.
                    people[0].hide_render=False
                    people[0].location=right*(max(p.dot(right) for p in points)+.66)
                    bpy.context.view_layer.update()
                    points += [people[0].matrix_world@Vector(p) for p in people[0].bound_box]
                    xs=[p.dot(right) for p in points];ys=[p.dot(up) for p in points];ds=[p.dot(direction) for p in points]
                    width,height=510,730;aspect=width/height
                    scale=max((max(xs)-min(xs))/(.86*aspect),(max(ys)-min(ys))/.74)*1.02
                    target=right*((min(xs)+max(xs))/2)+up*((min(ys)+max(ys))/2-.09*scale)+direction*((min(ds)+max(ds))/2)
                    cam.data.ortho_scale=scale;cam.location=target+direction*max(20,scale*3)
                    scene.render.resolution_x=width;scene.render.resolution_y=height
                    floor.hide_render=False;floor.scale=(max(12,scale*5),max(12,scale*5),1)
                    span=max(info['size']+[1.75])
                    for f in fills:f.hide_render=False;f.data.size=max(3,span*1.6)
                    aim(fills[0],target+Vector((span,-span,span*2)),(0,0,span*.35));fills[0].data.energy=span*span*160
                    fills[0].data.color=(1,.95,.88)
                    aim(fills[1],target+Vector((-span,span,span*1.5)),(0,0,span*.35));fills[1].data.energy=span*span*100
                    fills[1].data.color=(.70,.85,1)
                    camera_caption(captions[0],name,109,25,width,height,scale)
                    dimensions=' x '.join(f'{d:.3f}' for d in info['size'])+' m'
                    camera_caption(captions[1],dimensions,70,19,width,height,scale)
                    camera_caption(captions[2],f'{info["triangles"]:,} tris  |  figure 1.75 m',35,19,width,height,scale)
                    sync_shells()
                    projected=[world_to_camera_view(scene,cam,p) for p in points]
                    framing=dict(min_x=min(p.x for p in projected),max_x=max(p.x for p in projected),
                                 min_y=min(p.y for p in projected),max_y=max(p.y for p in projected))
                    assert framing['min_x']>.04 and framing['max_x']<.96 and framing['min_y']>.20 and framing['max_y']<.98,(name,framing)
                    path=task_tmp/f'{name}.png';scene.render.filepath=str(path)
                    bpy.ops.render.render(write_still=True)
                    row,col=divmod(index,6);x=30+col*526;y=2400-128-row*748-730
                    canvas[y:y+730,x:x+510]=image_pixels(path)
                    entries[name]=dict(sheet=page+'.png',row=row,column=col,size_m=info['size'],triangles=info['triangles'],
                                       figure_height_m=1.75,ortho_scale_m=scale,projected_object_and_figure=framing,
                                       camera_direction=list(direction),cell_pixels=[510,730])
                    print('KIT_CELL_DONE',name,flush=True)
                finally:
                    root.location=original_location
                    for child in root.children:child.hide_render=True
                    people[0].hide_render=True
            # Render the page header at final pixel size; no bitmap font dependency.
            floor.hide_render=True
            for f in fills:f.hide_render=True
            sync_shells()
            scene.render.resolution_x=3200;scene.render.resolution_y=100
            cam.data.ortho_scale=1.;cam.location=(0,0,80);cam.rotation_euler=(0,0,0)
            camera_caption(captions[0],'CACHE SEA / SHALLOWS    '+title,70,34,3200,100,1.,'LEFT',30)
            camera_caption(captions[1],'Each cell framed independently | Same 1.75 m figure | Actual dimensions in metres | Triangle counts exclude the figure',25,21,3200,100,1.,'LEFT',30)
            captions[2].hide_render=True
            header=task_tmp/'header.png';scene.render.filepath=str(header);bpy.ops.render.render(write_still=True)
            canvas[2292:2392]=image_pixels(header)
            sheet=bpy.data.images.new(page,width=3200,height=2400,alpha=True)
            sheet.colorspace_settings.name='sRGB';sheet.pixels.foreach_set(canvas.ravel())
            sheet.filepath_raw=str(BASE/'preview'/f'{page}.png');sheet.file_format='PNG';sheet.save();bpy.data.images.remove(sheet)
            page_times[page]=round(time.perf_counter()-started,3)
            print('PREVIEW_DONE',page,page_times[page],flush=True)
            cam.rotation_euler=orientation.to_euler()
    (BASE/'preview/kit_contact_report.json').write_text(json.dumps(dict(revision=2,sheet_pixels=[3200,2400],
         cells=entries,figure_height_m=1.75,camera_direction=list(direction),pages=list(page_times)),indent=2))
    return page_times,entries

def main():
    start=time.perf_counter()
    bpy.ops.wm.open_mainfile(filepath=str(BASE/'cache_sea_shallows.blend'))
    scene=bpy.context.scene
    try:scene.render.engine='BLENDER_EEVEE_NEXT'
    except TypeError:scene.render.engine='BLENDER_EEVEE'
    scene.render.resolution_x=1600;scene.render.resolution_y=900;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGB'
    scene.view_settings.exposure=.3
    scene.render.use_freestyle=False
    if hasattr(scene,'eevee') and hasattr(scene.eevee,'taa_render_samples'):scene.eevee.taa_render_samples=48
    compositor(scene)
    bpy.data.collections['Kit'].hide_render=True
    bpy.data.collections['Outer'].hide_render=False
    bpy.data.collections['Drift'].hide_render=False
    cam=scene.camera;cam.data.clip_end=650;cam.data.clip_start=.08
    people=[bpy.data.objects['Reference_1.75m'],bpy.data.objects['Reference_1.62m']]
    shells=outline_shells()
    fills=[area('Preview_Fill',(58,24,7),1200,14,(.38,.70,1),(58,36,0)),
           area('Preview_Rim',(58,70,18),2200,16,(.30,.65,1),(58,60,0))]
    # A low-energy point inside the glass lantern belongs to Reference only.
    data=bpy.data.lights.new('Lantern_Preview_Light','POINT');data.energy=600;data.color=(.86,.92,1);data.shadow_soft_size=1
    light=bpy.data.objects.new('Lantern_Preview_Light',data);bpy.data.collections['Reference'].objects.link(light);light.location=(58,76,33.2)
    args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
    only=set(args[args.index('--only')+1].split(',')) if '--only' in args else None
    timings={}
    overlays=[]
    caption=bpy.data.materials.new('M_Preview_Caption');caption.use_nodes=True
    p=caption.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(.55,.70,.90,1)
    p.inputs['Emission Color'].default_value=(.55,.70,.90,1);p.inputs['Emission Strength'].default_value=.6
    def label(body,at,size=1.0):
        curve=bpy.data.curves.new('Preview_Label','FONT');curve.body=body;curve.align_x='CENTER';curve.size=size
        ob=bpy.data.objects.new('Preview_Label',curve);bpy.data.collections['Reference'].objects.link(ob);ob.location=at
        curve.materials.append(caption);overlays.append(ob)
        return ob
    def render(name):
        bpy.context.view_layer.update()
        for original,shell,group in shells:
            shell.matrix_world=original.matrix_world
            shell.hide_render=original.hide_render or (group=='Kit' if not name.startswith('kit_') else group not in ('Kit','Reference'))
            if name=='topdown' and group=='Reference':shell.hide_render=True
        t=time.perf_counter();scene.render.filepath=str(BASE/'preview'/f'{name}.png')
        bpy.ops.render.render(write_still=True)
        timings[name]=round(time.perf_counter()-t,3)
        print('PREVIEW_DONE',name,timings[name],flush=True)
    for name,(at,target,lens,locations) in SHOTS.items():
        if only and name not in only and name.removeprefix('persp_') not in only:continue
        cam.data.type='PERSP';cam.data.lens=lens;aim(cam,at,target)
        for o,p in zip(people,locations):o.location=p
        middle=(Vector(at)+Vector(target))*.5
        aim(fills[0],(middle.x,middle.y,7),(middle.x,middle.y,0))
        # White key and cool rim preserve region colours without washing out the dark body.
        fills[0].data.color=(.70,.82,1.)
        aim(fills[1],(target[0],target[1],13),(target[0],target[1],3))
        render(name)
    if not only or 'topdown' in only:
        for o in people:o.hide_render=True
        bpy.data.collections['Outer'].hide_render=True;bpy.data.collections['Drift'].hide_render=True
        cam.data.type='ORTHO';cam.data.ortho_scale=130
        cam.location=(58,46,220);cam.rotation_euler=(0,0,0)
        scene.render.resolution_x=1800;scene.render.resolution_y=1500
        for f in fills:f.hide_render=True
        for c in range(29):label(str(c),(c*4+2,94,44),.90)
        for r in range(23):label(str(r),(-2.2,4*(22-r)+1.5,44),.90)
        label('CACHE SEA / SHALLOWS  |  29 x 23  |  4m GRID',(58,98,44),1.25)
        label('NORTH +Y',(109,96.4,44),.80)
        for body,x,y in [('DELETED POSTS',20,89),('RIDGE',58,89),('RACK CANYON',96,89),('LIGHTHOUSE PLAZA',58,79),('MAIN CHANNEL',56,49),('EMOJI CORAL',20,49),('MAINTENANCE',94,45),('BUBBLE REEF',58,18)]:label(body,(x,y,44),.90)
        label('S',(58,9.4,44),1.5);label('G',(58,65.4,44),1.5)
        render('topdown')
        for o in overlays:o.hide_render=True
    if not only or 'kit_rack_detail' in only:
        for n in ('Layout','Landmarks','Props','Outer','Drift'):bpy.data.collections[n].hide_render=True
        bpy.data.collections['Kit'].hide_render=False
        scene.render.resolution_x=1600;scene.render.resolution_y=1000
        roots=[o for o in bpy.data.collections['Kit'].objects if o.type=='EMPTY']
        for root in roots:
            for child in root.children:child.hide_render=True
        for i in range(6):
            root=bpy.data.objects[f'KIT_RackFacade_V{i+1:02d}'];root.location=((i%3)*5.2,(i//3)*6.4,0)
            for child in root.children:child.hide_render=False
            label(f'V{i+1:02d}',root.location+Vector((0,-2.8,.02)),.4)
        for n,at in [('Seabed_Tile',(2.6,-6,0)),('Seabed_Vent',(7.8,-6,0))]:
            root=bpy.data.objects['KIT_'+n];root.location=at
            for child in root.children:child.hide_render=False
        for o,p in zip(people,[(11.8,-4.4,0),(13,-4.4,0)]):o.hide_render=False;o.location=p
        label('1.75m',(11.8,-5.1,.02),.35);label('1.62m',(13,-5.1,.02),.35)
        cam.data.type='ORTHO';cam.data.ortho_scale=18.5;aim(cam,(5.7,-20,15),(5.7,0.1,1))
        for f in fills:f.hide_render=False
        aim(fills[0],(4,-2,9),(4,1,0));fills[0].data.energy=1700;fills[0].data.size=12
        aim(fills[1],(10,4,8),(7,1,0));fills[1].data.energy=1500;fills[1].data.size=12
        # Use kit visibility rules for this focused sheet too.
        for original,shell,group in shells:
            if group not in ('Kit','Reference'):shell.hide_render=True
        render('kit_rack_detail')
        for o in overlays:o.hide_render=True
    contact_times,contact_entries=contact_sheets(scene,cam,people,fills,caption,shells,only)
    timings.update(contact_times)
    path=BASE/'preview'/'render_report.json'
    prior=json.loads(path.read_text()) if path.exists() else {}
    prior.update(timings);prior['last_run_seconds']=round(time.perf_counter()-start,3)
    if contact_entries:prior['contact_sheet_cells']=len(contact_entries)
    path.write_text(json.dumps(prior,indent=2))
    print('RENDER_TOTAL',prior['last_run_seconds'],flush=True)

if __name__=='__main__':main()
