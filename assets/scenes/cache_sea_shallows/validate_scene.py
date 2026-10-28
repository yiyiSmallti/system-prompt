"""End-to-end geometry audit of the generated .blend. Produces validation_report.json."""
from pathlib import Path
import bpy, sys, json, math, time, re
import numpy as np
from mathutils import Vector

BASE=Path(__file__).resolve().parent
sys.dont_write_bytecode=True
sys.path.insert(0,str(BASE))
import build_scene as spec

def clipped(vertices,axis,limit,greater):
    if not vertices:return []
    out=[];prev=vertices[-1]
    inside=lambda p:p[axis]>=limit-1e-9 if greater else p[axis]<=limit+1e-9
    for p in vertices:
        a,b=inside(prev),inside(p)
        if a!=b:
            t=(limit-prev[axis])/(p[axis]-prev[axis]);out.append(prev+(p-prev)*t)
        if b:out.append(p)
        prev=p
    return out

def mesh_bounds(o,zmin=None,zmax=None):
    verts=[o.matrix_world @ v.co for v in o.data.vertices]
    if zmin is None:return verts
    points=[]
    for f in o.data.polygons:
        vs=[verts[i] for i in f.vertices]
        vs=clipped(vs,2,zmin,True);vs=clipped(vs,2,zmax,False)
        points.extend(vs)
    return points

def main():
    start=time.perf_counter();bpy.ops.wm.open_mainfile(filepath=str(BASE/'cache_sea_shallows.blend'))
    bpy.context.view_layer.update()
    report=json.loads((BASE/'build_report.json').read_text())
    objects={o for n in ('Layout','Landmarks','Props') for o in bpy.data.collections[n].all_objects}
    meshes=[o for o in objects if o.type=='MESH']
    coverage={};miss=[];violations=[];max_wall_intrusion=0.;low_max=0.
    kelp_checks=[];kelp_errors=[]
    for o in meshes:
        for c,r in json.loads(o['allowed_cells']):coverage.setdefault((c,r),set()).add(o['role'])
    for r,row in enumerate(spec.ROWS):
        for c,ch in enumerate(row):
            roles=coverage.get((c,r),set())
            required='wall' if ch=='#' else 'low_wall' if ch=='=' else 'lighthouse' if ch=='L' else 'prop' if ch in 'BFCTXKOYR' else 'plant' if ch=='%' else 'floor'
            okay=(required in roles or ch=='#' and 'wall_special' in roles or ch=='=' and 'phone' in roles)
            if not okay:miss.append([c,r,ch,required,sorted(roles)])
            if ch!='#' and 'floor' not in roles:miss.append([c,r,ch,'floor'])
    # Clip actual triangles to the player-height slab, then audit their projected bounds.
    # This avoids falsely flagging lighthouse balconies which only overhang above 3m.
    for o in meshes:
        if o['role']=='floor':continue
        points=mesh_bounds(o,.15,2.5)
        allowed=json.loads(o['allowed_cells']);cs=[p[0] for p in allowed];rs=[p[1] for p in allowed]
        limits=(min(cs)*4,(max(cs)+1)*4,(spec.H-1-max(rs))*4,(spec.H-min(rs))*4)
        if points:
            mn=[min(v[i] for v in points) for i in (0,1)];mx=[max(v[i] for v in points) for i in (0,1)]
            intrusion=max(limits[0]-mn[0],mx[0]-limits[1],limits[2]-mn[1],mx[1]-limits[3],0.)
            tolerance=.15 if o['role']=='wall' else .00015
            if o['role']=='wall':max_wall_intrusion=max(max_wall_intrusion,intrusion)
            if intrusion>tolerance+.00005:violations.append(dict(name=o.name,role=o['role'],intrusion=intrusion,bounds=[mn,mx],allowed=limits))
        if o['role']=='plant' and o['kit_name']=='KelpEdge':
            c,r=o['cell_c'],o['cell_r'];edge=spec.KELP_EDGES[(c,r)]
            center=spec.cell(c,r)
            # Convex triangles whose clipped vertices lie in this strip cannot enter
            # the two-metre middle opening of any of the other three cell edges.
            strips={'W':(-2,-1,-2,2),'E':(1,2,-2,2),
                    'N':(-2,2,1,2),'S':(-2,2,-2,-1)}
            local=[p-center for p in points]
            assert local,o.name
            footprint_min=[min(p[i] for p in local) for i in (0,1)]
            footprint_max=[max(p[i] for p in local) for i in (0,1)]
            x0,x1,y0,y1=strips[edge]
            outside=[p for p in local if not(x0-1e-4<=p.x<=x1+1e-4 and y0-1e-4<=p.y<=y1+1e-4)]
            passed=not outside and o.get('kelp_edge')==edge
            check=dict(c=c,r=r,edge=edge,rotation_z_degrees=round(math.degrees(o.matrix_world.to_euler().z),4),
                       height_slab_m=[.15,2.5],strip_local_xy_m=[x0,x1,y0,y1],
                       footprint_min_local_xy_m=footprint_min,footprint_max_local_xy_m=footprint_max,
                       other_edge_openings_m=2,passed=passed)
            kelp_checks.append(check)
            if not passed:
                kelp_errors.append(check);violations.append(dict(name=o.name,error='kelp leaves assigned edge strip',edge=edge))
        if o['role']=='plant' and o['kit_name']=='CableHanging':
            z=min(v.z for v in mesh_bounds(o))
            if z<2.5:violations.append(dict(name=o.name,error='cable lower than 2.5m',z=z))
        if o['role'] in ('low_wall','phone'):
            z=max(v.z for v in mesh_bounds(o));low_max=max(low_max,z)
            if z>(1.6 if o['role']=='phone' else 1.3)+.0001:violations.append(dict(name=o.name,error='low wall too tall',z=z))
        if o['role']=='lighthouse':
            for slab,expand in ((2.999,0),(100,2)):
                p=mesh_bounds(o,-100,slab)
                if p:
                    if any(v.x<52-expand-1e-4 or v.x>64+expand+1e-4 or v.y<72-expand-1e-4 or v.y>80+expand+1e-4 for v in p):violations.append(dict(name=o.name,error='lighthouse footprint',slab=slab))
    # Validate each shared mesh once; mesh data is intentionally reused by linked instances.
    zero=[];duplicates=[];ngons=[];modifiers=[];uv=[];checked=set();facing_errors=[]
    for o in meshes:
        if o.modifiers:modifiers.append(o.name)
        facing=re.match(r'Rack_c\d+_r\d+_([SENW])_L\d+',o.name)
        if facing:
            expected={'S':Vector((0,-1,0)),'E':Vector((1,0,0)),'N':Vector((0,1,0)),'W':Vector((-1,0,0))}[facing[1]]
            actual=(o.matrix_world.to_3x3()@o.data.polygons[0].normal).normalized()
            if actual.dot(expected)<.9999:facing_errors.append(o.name)
        m=o.data
        if m.name in checked:continue
        checked.add(m.name)
        if m.uv_layers:uv.append(m.name)
        if any(len(p.vertices)!=3 for p in m.polygons):ngons.append(m.name)
        seen=set()
        for p in m.polygons:
            if p.area<1e-11:zero.append([m.name,p.index,p.area])
            key=tuple(sorted(tuple(round(a,5) for a in m.vertices[i].co) for i in p.vertices))
            if key in seen:duplicates.append([m.name,p.index])
            seen.add(key)
    # Height matches are checked against the exact target, including roof/fan geometry.
    height_errors=[];heights={};interior={};edge={}
    for o in objects:
        if o.type=='EMPTY' and 'target_height' in o:
            actual=max(v.z for child in o.children if child.type=='MESH' for v in mesh_bounds(child))
            target=o['target_height'];error=abs(actual-target)
            if error>.0002:height_errors.append([o.name,target,actual])
            z=spec.zone(o['cell_c'],o['cell_r']);heights.setdefault(z,[]).append(target)
            is_edge=o['cell_c'] in (0,spec.W-1) or o['cell_r'] in (0,spec.H-1)
            (edge if is_edge else interior).setdefault(z,[]).append(target)
    # Cast from the start at eye height to the actual lamp centre. The lamp must be first.
    origin=Vector((58,10,1.65));target=Vector((58,76,33.2));direction=(target-origin).normalized()
    # Reference objects and optional P2 are excluded using an evaluated temporary collection mask.
    for n in ('Kit','Reference','Outer','Drift'):bpy.data.collections[n].hide_viewport=True
    bpy.context.view_layer.update()
    hit,loc,normal,index,obj,matrix=bpy.context.scene.ray_cast(bpy.context.evaluated_depsgraph_get(),origin,direction,distance=(target-origin).length)
    visible=(not hit or (obj is not None and ('Lighthouse' in obj.name) and loc.z>=31.0))
    # A visible lantern/roof hit is expected before the inner emissive cylinder.
    visibility=dict(eye=list(origin),target=list(target),first_hit=obj.name if hit else None,hit_location=list(loc) if hit else None,clear_to_lantern=visible)
    main_triangles=sum(len(o.data.polygons) for o in meshes)
    kelp_checks.sort(key=lambda x:(x['r'],x['c']))
    assert {(x['c'],x['r']) for x in kelp_checks}==set(spec.KELP_EDGES),'Kelp coverage differs from the 12-cell placement table'
    result=dict(pass_all=not(miss or violations or zero or duplicates or ngons or modifiers or height_errors or facing_errors or kelp_errors) and visible and main_triangles<=600000,
        map_cells=spec.W*spec.H,covered_cells=len(coverage),missing=miss,walkability_violations=violations,
        max_wall_intrusion_m=max_wall_intrusion,low_wall_max_z=low_max,zero_area_faces=zero,
        duplicate_triangles=duplicates,ngon_meshes=ngons,unapplied_modifiers=modifiers,uv_dependent_meshes=uv,
        wall_facing_errors=facing_errors,font_file_dependencies=[f.filepath for f in bpy.data.fonts if f.filepath not in ('','<builtin>')],
        image_file_dependencies=[i.filepath for i in bpy.data.images if i.source=='FILE'],
        height_errors=height_errors,rack_height_ranges={k:[min(v),max(v)] for k,v in heights.items()},
        rack_interior_ranges={k:[min(v),max(v)] for k,v in interior.items()},rack_edge_ranges={k:[min(v),max(v)] for k,v in edge.items()},
        lamp_origin=list(bpy.data.objects['Lighthouse_Lamp'].matrix_world.translation),visibility=visibility,
        triangles=main_triangles,main_mesh_objects=len(meshes),unique_main_meshes=len(checked),
        material_count=len(bpy.data.materials),seconds=round(time.perf_counter()-start,3))
    result.update(kelp_checked_cells=len(kelp_checks),kelp_edge_checks=kelp_checks,kelp_edge_violations=kelp_errors)
    (BASE/'validation_report.json').write_text(json.dumps(result,ensure_ascii=False,indent=2))
    print('VALIDATION',json.dumps(result,ensure_ascii=False),flush=True)
    assert result['pass_all'],'See validation_report.json'

if __name__=='__main__':main()
