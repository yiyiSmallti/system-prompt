"""Render the full deep scene; -- --only context,white_hall,topdown is supported."""
from pathlib import Path
import sys,json,time,math
sys.dont_write_bytecode=True
BASE=Path(__file__).resolve().parent
sys.path.insert(0,str(BASE))
import bpy
from mathutils import Vector
from bpy_extras.object_utils import world_to_camera_view
from preview_common import *
import build_scene as spec

SHOTS={
    'persp_start_rift':((58,6.2,2.25),(56.4,17,4.2),19,[(57.5,15.2,0),(55.1,15.1,0)]),
    'persp_servers_aisle':((38,38,2.35),(77,38,2.8),23,[(47,37.2,0),(48.6,38.5,0)]),
    'persp_servers_bridges':((38,54,2.7),(72,54,6.1),21,[(47,53.2,0),(48.5,54.5,0)]),
    'persp_recycle_files':((6,57,2.35),(16,48,1.3),21,[(17,49,0),(18.7,48.3,0)]),
    'persp_context':((86,46,2.4),(109,46,4.5),20,[(94,45.4,0),(95.5,46.7,0)]),
    'persp_white_hall':((58,71,2.45),(58,94,7.2),22,[(56.8,83.5,0),(58.5,84,0)]),
    'persp_outer_lighthouse':((58,-47,2.7),(58,-18,14.0),16,[(54.8,-25,0),(56.5,-24.5,0)]),
}

def main():
    start=time.perf_counter();bpy.ops.wm.open_mainfile(filepath=str(BASE/'cache_sea_deep.blend'))
    scene=bpy.context.scene;cam=scene.camera;shells=configure(scene)
    people=[bpy.data.objects['Reference_1.75m'],bpy.data.objects['Reference_1.62m']]
    fills=[area('Preview_Local_Key',(58,34,9),2600,12,(.72,.84,1),(58,38,0)),
           area('Preview_Local_Rim',(58,44,12),3400,15,(.40,.66,1),(58,38,1))]
    caption=camera_label(cam,'CACHE SEA / DEEP  |  4m cells  |  figures 1.75m / 1.62m')
    data=bpy.data.lights.new('Preview_Lantern_Core','POINT');data.energy=1100;data.color=(.85,.92,1);data.shadow_soft_size=1
    ob=bpy.data.objects.new('Preview_Lantern_Core',data);bpy.data.collections['Reference'].objects.link(ob);ob.location=(58,-18,33.2)
    args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
    only=set(args[args.index('--only')+1].split(',')) if '--only' in args else None
    report={}
    for name,(at,target,lens,positions) in SHOTS.items():
        if only and name not in only and name.removeprefix('persp_') not in only:continue
        for n in ('Outer','Drift'):bpy.data.collections[n].hide_render=False
        cam.data.type='PERSP';cam.data.lens=lens;aim(cam,at,target)
        for o,p in zip(people,positions):o.hide_render=False;o.location=p
        middle=(Vector(at)+Vector(target))*.5
        aim(fills[0],(middle.x,middle.y,9),(middle.x,middle.y,1));fills[0].data.energy=3800 if 'white' in name else 2600
        aim(fills[1],(target[0],target[1],13),(target[0],target[1],2))
        caption.data.body=name.removeprefix('persp_').upper().replace('_',' ')+'  |  1.75m / 1.62m'
        half=cam.data.sensor_width/(2*lens)
        caption.location=(-half*.94,half*9/16*.87,-1);caption.data.size=half*.031
        sync_shells(shells)
        figure_framing=[]
        for o in people:
            projected=[world_to_camera_view(scene,cam,o.matrix_world.__matmul__(Vector(v))) for v in o.bound_box]
            figure_framing.append(dict(name=o.name,min_x=min(p.x for p in projected),max_x=max(p.x for p in projected),
                min_y=min(p.y for p in projected),max_y=max(p.y for p in projected)))
        assert all(0<=f['min_x']<f['max_x']<=1 and 0<=f['min_y']<f['max_y']<=1 for f in figure_framing),(name,figure_framing)
        t=time.perf_counter();scene.render.filepath=str(BASE/'preview'/f'{name}.png');bpy.ops.render.render(write_still=True)
        report[name]=dict(seconds=round(time.perf_counter()-t,3),camera=at,target=target,lens_mm=lens,figures=positions,pixels=[1600,900],figure_framing=figure_framing)
        print('PREVIEW_DONE',name,flush=True)
    if not only or 'topdown' in only:
        caption.hide_render=True
        for p in people:p.hide_render=True
        for n in ('Outer','Drift','Landmarks'):bpy.data.collections[n].hide_render=True
        for light in fills:light.hide_render=True
        cam.data.type='ORTHO';cam.data.ortho_scale=132;cam.location=(58,50,230);cam.rotation_euler=(0,0,0)
        scene.render.resolution_x=1800;scene.render.resolution_y=1600
        ref=bpy.data.collections['Reference']
        def label(body,at,size=1.0):
            curve=bpy.data.curves.new('Map_Label','FONT');curve.body=body;curve.size=size;curve.align_x='CENTER'
            curve.materials.append(bpy.data.materials['M_Preview_Label'])
            ob=bpy.data.objects.new('Map_Label',curve);ref.objects.link(ob);ob.location=at
        for c in range(spec.W):label(str(c),(c*4+2,101.5,44),.83)
        for r in range(spec.H):label(str(r),(-2.2,4*(24-r)+1.5,44),.83)
        label('CACHE SEA / DEEP   |   29 x 25   |   NORTH +Y',(58,106,44),1.15)
        for z in spec.ZONES:
            x,y,w,h=z['rect'];label(z['style'].upper(),(4*x+2*w,4*(25-y)-1.5,44),.85)
            x0,x1=4*x,4*(x+w);y0,y1=4*(25-y-h),4*(25-y)
            vertices=[];faces=[];half=.045
            for a,b in [((x0,y0),(x1,y0)),((x1,y0),(x1,y1)),((x1,y1),(x0,y1)),((x0,y1),(x0,y0))]:
                dx,dy=b[0]-a[0],b[1]-a[1];length=math.hypot(dx,dy);nx,ny=-dy/length*half,dx/length*half
                i=len(vertices);vertices.extend([(a[0]+nx,a[1]+ny,44),(b[0]+nx,b[1]+ny,44),(b[0]-nx,b[1]-ny,44),(a[0]-nx,a[1]-ny,44)])
                faces.append((i,i+1,i+2,i+3))
            mesh=bpy.data.meshes.new('Preview_ZoneOutline');mesh.from_pydata(vertices,[],faces);mesh.materials.append(bpy.data.materials['M_Rack_LED_'+z['style']])
            ob=bpy.data.objects.new('Preview_ZoneOutline_'+z['style'],mesh);ref.objects.link(ob)
        for r,row in enumerate(spec.ROWS):
            for c,ch in enumerate(row):
                if ch in 'MQDSG':
                    p=spec.cell(c,r);label(ch,(p.x,p.y-.35,44),1.1)
        sync_shells(shells,True)
        t=time.perf_counter();scene.render.filepath=str(BASE/'preview/topdown.png');bpy.ops.render.render(write_still=True)
        report['topdown']=dict(seconds=round(time.perf_counter()-t,3),pixels=[1800,1600],north='+Y',map_bounds=[0,116,0,100])
    path=BASE/'preview/render_report.json';prior=json.loads(path.read_text()) if path.exists() else {}
    prior.update(report);prior['last_run_seconds']=round(time.perf_counter()-start,3)
    path.write_text(json.dumps(prior,indent=2))
    print('RENDER_PASS',prior['last_run_seconds'],flush=True)

if __name__=='__main__':main()
