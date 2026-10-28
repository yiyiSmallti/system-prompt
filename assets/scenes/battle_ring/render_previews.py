"""Three battle-camera previews, with real-size party and enemy mannequins."""
from pathlib import Path
import sys,json,time
sys.dont_write_bytecode=True
BASE=Path(__file__).resolve().parent
sys.path.insert(0,str(BASE))
import bpy
from preview_common import *

SHOTS={
    'battle_01_party':((.6,-4.5,1.7),(0,6.4,1.4),19),
    'battle_02_enemy_low':((1.5,3.0,.5),(0,7.,2.6),14),
    'battle_03_victory':((6,-9,7),(0,3.4,2),21),
}

def main():
    start=time.perf_counter();bpy.ops.wm.open_mainfile(filepath=str(BASE/'battle_ring.blend'))
    scene=bpy.context.scene;cam=scene.camera
    fills=[area('Preview_Field_Key',(-7,-5,14),4000,20,(.67,.82,1),(0,3.4,0)),
           area('Preview_Field_Rim',(4,14,12),3000,18,(.36,.75,1),(0,3.4,0))]
    caption=camera_label(cam,'BATTLE RING  |  figures 1.75m / 1.62m')
    from scene_geometry import mannequin_geo,MATS
    MATS.update({m.name:m for m in bpy.data.materials})
    guide=bpy.data.objects.new('Preview_EnemyScale_1.75m',mannequin_geo(1.75).mesh('EnemyScale_175'))
    bpy.data.collections['Reference'].objects.link(guide);guide.location=(-1.2,7.0,0)
    shells=configure(scene)
    args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
    only=set(args[args.index('--only')+1].split(',')) if '--only' in args else None
    report={}
    for name,(at,target,lens) in SHOTS.items():
        if only and name not in only:continue
        cam.data.type='PERSP';cam.data.lens=lens;aim(cam,at,target)
        guide.hide_render=name!='battle_02_enemy_low'
        half=cam.data.sensor_width/(2*lens)
        caption.location=(-half*.94,half*9/16*.87,-1);caption.data.size=half*.031
        caption.data.body=name.upper().replace('_',' ')+'  |  1.75m / 1.62m'
        sync_shells(shells)
        t=time.perf_counter();scene.render.filepath=str(BASE/'preview'/f'{name}.png');bpy.ops.render.render(write_still=True)
        report[name]=dict(seconds=round(time.perf_counter()-t,3),camera=at,target=target,lens_mm=lens,pixels=[1600,900],
            party=[[-2.3,0,1.75],[-.3,-.2,1.62],[1.7,-.4,1.75]],enemy=[0,6.4,1.62])
        print('BATTLE_PREVIEW_DONE',name,flush=True)
    path=BASE/'preview/render_report.json';prior=json.loads(path.read_text()) if path.exists() else {}
    prior.update(report);prior['last_run_seconds']=round(time.perf_counter()-start,3)
    path.write_text(json.dumps(prior,indent=2))
    print('BATTLE_RENDER_PASS',prior['last_run_seconds'],flush=True)

if __name__=='__main__':main()
