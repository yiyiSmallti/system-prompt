"""Build the 123-block battle ring from blocks.json, using shallow rack primitives.

Run with -- --build-only, --export-only, or --render-only to repeat one stage.
The default runs build, geometric validation, FBX roundtrip, and all previews.
"""
from pathlib import Path
import sys,json,math,time
sys.dont_write_bytecode=True
BASE=Path(__file__).resolve().parent
sys.path.insert(0,str(BASE))
import bpy
from mathutils import Vector,Matrix
from scene_geometry import *

BLOCK_DATA=json.loads((BASE/'reference/blocks.json').read_text())
BLOCKS=BLOCK_DATA['blocks']
CENTRE=Vector((0,3.4,0))
DIRS=[('S',0,-1,0.),('E',1,0,math.pi/2),('N',0,1,math.pi),('W',-1,0,math.pi*1.5)]
FACE_CACHE={}

def token(v):
    return ('n' if v<0 else 'p')+f'{abs(v):02d}'

def block_name(x,y):
    return f'BattleRack_x{token(x)}_y{token(y)}'

def setup_materials():
    for name,col,metal in [('Body','#1A2944',.45),('Trim','#0A1222',.3),('Shadow','#050A14',.2)]:material('M_Rack_'+name,col,metal=metal)
    material('M_Rack_LED_Channel','#40F2D9',3.2)
    material('M_Reference','#A9B8CA',metal=.1)

def simple_face(variant):
    g=Geo();g.front(0,1.15,4,2.3,-1.83,'M_Rack_Shadow')
    for x in (-1.93,1.93):g.box((x,-1.92,1.15),(.12,.12,2.3),'M_Rack_Trim',sides=(1,2,4))
    for z in (.08,2.22):g.box((0,-1.92,z),(3.74,.14,.16),'M_Rack_Trim',sides=(1,2,4,5))
    for i in range(4):
        x=-1.4+i*.93
        g.front(x,1.15,.86,1.91,-1.91,'M_Rack_Body',bevel=.025)
        for j in range(3):g.front(x,.57+j*.53,.74,.055,-1.935,'M_Rack_Trim')
        g.front(x-.21,1.85,.055,.055,-1.94,'M_Rack_LED_Channel')
    g.front(0,2.13,3.70,.04,-1.95,'M_Rack_LED_Channel')
    return g

def facade(variant,limit,detail):
    key=(variant,round(limit,7),detail)
    if key in FACE_CACHE:return FACE_CACHE[key]
    g=rack_face(variant) if detail else simple_face(variant)
    if detail:g.transform(Matrix.Translation((0,.10,0)))
    data=g.mesh(f'BattleFacade_{variant}_{detail}')
    if limit<2.29999:
        data=truncated_facade(data,limit)
        for v in data.vertices:
            if v.co.y<-1.997:v.co.y=-1.997
    FACE_CACHE[key]=data
    return data

def polygon_clip(points,normal,limit):
    if not points:return []
    out=[];prev=points[-1];a=prev.dot(normal)-limit
    for p in points:
        b=p.dot(normal)-limit
        if (a>=0)!=(b>=0):out.append(prev+(p-prev)*(a/(a-b)))
        if b>=0:out.append(p)
        prev=p;a=b
    return out

def cut_mesh(mesh,matrix,n,limit,name):
    """Clip complete triangles, not just vertices, against the inner tangent plane."""
    g=Geo()
    for poly in mesh.polygons:
        points=[matrix@mesh.vertices[i].co for i in poly.vertices]
        points=polygon_clip(points,n,limit)
        for normal in (Vector((1,0,0)),Vector((-1,0,0)),Vector((0,1,0)),Vector((0,-1,0))):
            points=polygon_clip(points,normal,-2.)
        if len(points)<3:continue
        for i in range(1,len(points)-1):
            tri=[points[0],points[i],points[i+1]]
            if (tri[1]-tri[0]).cross(tri[2]-tri[0]).length>1e-9:g.face(tri,mesh.materials[poly.material_index].name)
    return g.mesh(name) if g.f else None

def build():
    start=time.perf_counter();bpy.ops.wm.read_factory_settings(use_empty=True)
    MATS.clear();COLS.clear();FACE_CACHE.clear();CLIPPED.clear()
    for name in ('Layout','Landmarks','Outer','Drift','Reference'):COLS[name]=collection(name)
    inner_col=collection('Ring_Racks',COLS['Layout'])
    root=empty('BattleRing',COLS['Layout']);setup_materials()
    scene=bpy.context.scene;scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1.;scene.unit_settings.length_unit='METERS'
    occupied={tuple(b['cell']) for b in BLOCKS};height_at={tuple(b['cell']):b['height'] for b in BLOCKS}
    fan=fan_geo().mesh('BattleFan_Linked')
    cuts=[];full_count=0;simple_count=0
    for b in BLOCKS:
        x,y=b['cell'];px,py=b['centre_blender'];ht=b['height'];name=block_name(x,y)
        group=empty(name,inner_col,(px,py,0),root)
        group['block_x']=x;group['block_y']=y;group['target_height']=ht
        radial=Vector((px,py-3.4,0));distance=radial.length;n=radial.normalized()
        # One millimetre outside the required disk absorbs float32/FBX roundoff.
        limit=16.501-distance
        needs_cut=math.hypot(max(0,abs(px)-2),max(0,abs(py-3.4)-2))<16.5
        if needs_cut:cuts.append([x,y])
        group['inner_corner_cut']=needs_cut
        def put(suffix,data,at=(0,0,0),angle=0.,scale=1.):
            matrix=Matrix.Translation(Vector(at))@Matrix.Rotation(angle,4,'Z')@Matrix.Diagonal((scale,scale,1,1))
            if needs_cut:
                clipped=cut_mesh(data,matrix,n,limit,name+'_'+suffix+'_Cut')
                if clipped is None:return None
                ob=obj_mesh(name+'_'+suffix,clipped,inner_col,parent=group)
                if suffix=='Fan':
                    # Keep the rotor's own pivot rather than its parent block origin.
                    for v in ob.data.vertices:v.co-=Vector(at)
                    ob.location=at
            else:
                ob=obj_mesh(name+'_'+suffix,data,inner_col,at,group)
                ob.rotation_euler.z=angle;ob.scale=(scale,scale,1)
            ob['block_x']=x;ob['block_y']=y;ob['role']='battle_rack'
            return ob
        layers=math.ceil(ht/2.3)
        for label,dx,dy,angle in DIRS:
            if (x+dx,y+dy) in occupied:
                lower=height_at[(x+dx,y+dy)]
                if lower<ht-.01:
                    g=Geo();bottom=lower
                    # Exposed height differences are exterior faces; close them too.
                    upper_base=(layers-1)*2.3 if ht>9 else ht
                    bands=[(bottom,min(ht,upper_base),1.),(max(bottom,upper_base),ht,.90)] if ht>9 else [(bottom,ht,1.)]
                    for z0,z1,scale in bands:
                        if z1<=z0:continue
                        g.front(0,(z0+z1)/2,4*scale,z1-z0,-1.995*scale,'M_Rack_Body')
                        for xx in (-1.92*scale,1.92*scale):g.front(xx,(z0+z1)/2,.065,z1-z0,-1.997*scale,'M_Rack_Trim')
                        g.front(0,z1-.02,min(3.72*scale,3.72),.025,-1.998*scale,'M_Rack_LED_Channel')
                    put(f'{label}_Step',g.mesh(name+'_'+label+'_Step'),angle=angle)
                continue
            detail=Vector((dx,dy,0)).dot(-n)>-.05
            for layer in range(layers):
                remain=min(2.3,ht-layer*2.3);variant=(x*13+y*7+layer)%6
                scale=.90 if layer==layers-1 and ht>9 else 1.
                put(f'{label}_L{layer:02d}',facade(variant,remain,detail),(0,0,layer*2.3),angle,scale)
                if detail:full_count+=1
                else:simple_count+=1
        g=Geo();scale=.90 if ht>9 else 1.;a=2*scale
        g.face([(-a,-a,0),(a,-a,0),(a,a,0),(-a,a,0)],'M_Rack_Body')
        for xx in (-1.55*scale,1.55*scale):g.box((xx,0,-.025),(.04,3.45*scale,.05),'M_Rack_Trim',sides=(1,2,3,4,5))
        put('Top',g.mesh(name+'_Roof'),(0,0,ht))
        if ht>9:
            g=Geo();z=min((layers-1)*2.3,ht-.031)
            # A real shoulder below the recessed top tier keeps the skyline closed.
            for yy in (-1.9,1.9):g.box((0,yy,z),(4,.20,.06),'M_Rack_Trim',sides=(5,))
            for xx in (-1.9,1.9):g.box((xx,0,z),(.20,3.6,.06),'M_Rack_Trim',sides=(5,))
            put('Shoulder',g.mesh(name+'_Shoulder'))
        put('Fan',fan,(n.x*.65,n.y*.65,ht-.10))
        if needs_cut:
            square=[Vector((-2,-2,0)),Vector((2,-2,0)),Vector((2,2,0)),Vector((-2,2,0))]
            polygon=polygon_clip(square,n,limit)
            edge=[p for p in polygon if abs(p.dot(n)-limit)<1e-5]
            assert len(edge)==2,(name,edge)
            tangent=Vector((n.y,-n.x,0));edge.sort(key=lambda p:p.dot(tangent))
            centre=(edge[0]+edge[1])/2;width=(edge[1]-edge[0]).length
            angle=math.atan2(-n.x,n.y)
            for layer in range(layers):
                remain=min(2.3,ht-layer*2.3)
                data=facade((x+y+layer)%6,remain,True)
                g=Geo();m=Matrix.Translation(centre+Vector((0,0,layer*2.3)))@Matrix.Rotation(angle,4,'Z')@Matrix.Translation((0,2.,0))@Matrix.Diagonal((max(.05,width-.08)/4,1,1,1))
                for p in data.polygons:g.face([m@data.vertices[i].co for i in p.vertices],data.materials[p.material_index].name)
                ob=put(f'InnerChamfer_L{layer:02d}',g.mesh(name+f'_Chamfer{layer}'))
    setup_reference()
    scene.camera.location=(.6,-4.5,1.7);scene.camera.data.lens=19
    scene.camera.rotation_euler=(Vector((0,6.4,1.4))-scene.camera.location).to_track_quat('-Z','Y').to_euler()
    if 'kit_coordinate_mapping' in scene:del scene['kit_coordinate_mapping']
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D':
                area.spaces.active.region_3d.view_location=(0,3.4,0)
                area.spaces.active.region_3d.view_distance=105
    # Three real-scale party figures and one enemy; no character enters the FBX.
    people=[bpy.data.objects['Reference_1.75m'],bpy.data.objects['Reference_1.62m']]
    people[0].location=(-2.3,0,0);people[1].location=(-.3,-.2,0)
    obj_mesh('Reference_Party_1.75m',mannequin_geo(1.75).mesh('PartyFigure'),COLS['Reference'],(1.7,-.4,0))
    obj_mesh('Reference_Enemy_1.62m',mannequin_geo(1.62).mesh('EnemyFigure'),COLS['Reference'],(0,6.4,0))
    g=Geo();g.box((0,3.4,-.10),(100,100,.08),'M_Rack_Body',sides=(5,))
    for x in range(-48,49,4):g.box((x,3.4,-.045),(.012,96,.003),'M_Rack_LED_Channel',sides=(5,))
    for y in range(-48,49,4):g.box((0,y+3.4,-.045),(96,.012,.003),'M_Rack_LED_Channel',sides=(5,))
    floor=obj_mesh('Preview_Stage',g.mesh('Battle_Preview_Stage'),COLS['Reference']);floor['role']='preview_floor'
    bpy.context.view_layer.update()
    meshes=[o for o in COLS['Layout'].all_objects if o.type=='MESH']
    triangles=sum(len(o.data.polygons) for o in meshes)
    report=dict(block_count=len(BLOCKS),triangles=triangles,budget=250000,inner_corner_cuts=cuts,
        field_centre_blender=[0,3.4],exclusion_radius_m=16.5,cut_radius_m=16.501,full_detail_layers=full_count,simple_outer_layers=simple_count,
        material_count=len({m.name for o in meshes for m in o.data.materials}),
        materials=[dict(name=m.name,color=m['base_srgb'],emission=m['emission_strength']) for m in MATS.values()],
        unique_meshes=len({o.data.name for o in meshes}),mesh_instances=len(meshes),
        build_seconds=round(time.perf_counter()-start,3),blender_version=bpy.app.version_string)
    report['major_components_triangles']={
        'fans':sum(len(o.data.polygons) for o in meshes if o.name.endswith('_Fan')),
        'tops_and_shoulders':sum(len(o.data.polygons) for o in meshes if o.name.endswith(('_Top','_Shoulder'))),
        'inner_chamfers':sum(len(o.data.polygons) for o in meshes if '_InnerChamfer_' in o.name),
        'height_difference_closures':sum(len(o.data.polygons) for o in meshes if o.name.endswith('_Step')),
    }
    (BASE/'build_report.json').write_text(json.dumps(report,indent=2))
    assert triangles<=250000,triangles
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(BASE/'battle_ring.blend'),check_existing=False)
    print('BATTLE_BUILD_PASS',json.dumps(report),flush=True)

def main():
    args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
    if '--render-only' in args:
        import render_previews
        render_previews.main();return
    if '--export-only' in args:
        import export_fbx
        export_fbx.main();return
    build()
    import validate_scene
    validate_scene.main()
    if '--build-only' not in args:
        import export_fbx
        export_fbx.main()
        import render_previews
        render_previews.main()

if __name__=='__main__':main()
