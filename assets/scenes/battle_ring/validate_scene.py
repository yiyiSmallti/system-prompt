"""Audit actual battle meshes: disk exclusion, per-cell bounds and JSON heights."""
from pathlib import Path
import sys,json,math,time
sys.dont_write_bytecode=True
BASE=Path(__file__).resolve().parent
sys.path.insert(0,str(BASE))
import bpy
from mathutils import Vector

def triangle_distance_xy(points):
    # A projected triangle can intersect the disk even when all its vertices are outside.
    a,b,c=[Vector((p.x,p.y-3.4)) for p in points];origin=Vector((0,0))
    def cross(u,v):return u.x*v.y-u.y*v.x
    area=cross(b-a,c-a)
    if abs(area)>1e-9:
        s=[cross(b-a,-a),cross(c-b,-b),cross(a-c,-c)]
        if min(s)>=0 or max(s)<=0:return 0.
    distance=min(a.length,b.length,c.length)
    for u,v in ((a,b),(b,c),(c,a)):
        d=v-u
        t=max(0,min(1,-u.dot(d)/d.length_squared)) if d.length_squared>1e-12 else 0
        distance=min(distance,(u+d*t).length)
    return distance

def main():
    start=time.perf_counter();bpy.ops.wm.open_mainfile(filepath=str(BASE/'battle_ring.blend'))
    bpy.context.view_layer.update()
    blocks=json.loads((BASE/'reference/blocks.json').read_text())['blocks']
    meshes=[o for o in bpy.data.collections['Layout'].all_objects if o.type=='MESH']
    violations=[];height_errors=[];closest=100.;stats=[];integrity=[];checked=set()
    grouped={}
    for o in meshes:grouped.setdefault((o['block_x'],o['block_y']),[]).append(o)
    for b in blocks:
        x,y=b['cell'];cx,cy=b['centre_blender'];objects=grouped.get((x,y),[])
        if not objects:violations.append([x,y,'missing']);continue
        verts=[o.matrix_world@v.co for o in objects for v in o.data.vertices]
        mn=[min(p[i] for p in verts) for i in range(3)];mx=[max(p[i] for p in verts) for i in range(3)]
        if mn[0]<cx-2.0001 or mx[0]>cx+2.0001 or mn[1]<cy-2.0001 or mx[1]>cy+2.0001:violations.append([x,y,'cell bounds',mn,mx])
        error=abs(mx[2]-b['height'])
        if error>.05:height_errors.append([x,y,b['height'],mx[2]])
        stats.append(dict(cell=[x,y],target_height=b['height'],actual_height=mx[2],height_error_m=error,min=mn,max=mx))
        for o in objects:
            vertices=[o.matrix_world@v.co for v in o.data.vertices]
            for p in o.data.polygons:
                d=triangle_distance_xy([vertices[i] for i in p.vertices]);closest=min(closest,d)
                if d<16.5:violations.append([o.name,p.index,'disk',d])
            if o.modifiers:integrity.append([o.name,'modifier'])
            if o.data.name in checked:continue
            checked.add(o.data.name)
            if o.data.uv_layers:integrity.append([o.name,'UV'])
            for p in o.data.polygons:
                if p.area<1e-11 or len(p.vertices)!=3:integrity.append([o.data.name,p.index,'invalid triangle'])
    triangles=sum(len(o.data.polygons) for o in meshes)
    mats=sorted({m.name for o in meshes for m in o.data.materials})
    result=dict(pass_all=not(violations or height_errors or integrity) and triangles<=250000 and len(mats)<=40 and len(grouped)==123,
        block_count=len(grouped),minimum_radius_m=closest,required_radius_m=16.5,
        max_height_error_m=max(x['height_error_m'] for x in stats),blocks=stats,
        violations=violations,height_errors=height_errors,mesh_integrity_issues=integrity,
        triangles=triangles,materials=mats,unique_meshes_checked=len(checked),seconds=round(time.perf_counter()-start,3))
    (BASE/'validation_report.json').write_text(json.dumps(result,indent=2))
    print('BATTLE_VALIDATION',json.dumps({k:result[k] for k in ('pass_all','minimum_radius_m','max_height_error_m','triangles','seconds')}),flush=True)
    assert result['pass_all'],'See validation_report.json'

if __name__=='__main__':main()
