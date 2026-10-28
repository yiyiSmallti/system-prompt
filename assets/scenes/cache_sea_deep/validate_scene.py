"""End-to-end audit of the delivered meshes, not of placement metadata alone.

Failure modes: missing cells; low or high geometry in reserved cells; a closing
door touching its frame; chat-volume intrusion; debris leaving its cell; uneven
row-label surfaces; incorrect Hash heights; invalid triangles; export pollution.
Every spatial test clips actual triangles to the relevant Z slab and XY box.
"""
from pathlib import Path
import sys,json,math,time
sys.dont_write_bytecode=True
BASE=Path(__file__).resolve().parent
sys.path.insert(0,str(BASE))
import bpy
from mathutils import Vector
import build_scene as spec

def clipped(points,axis,limit,greater):
    if not points:return []
    out=[];prev=points[-1]
    for p in points:
        a=prev[axis]>=limit if greater else prev[axis]<=limit
        b=p[axis]>=limit if greater else p[axis]<=limit
        if a!=b:out.append(prev+(p-prev)*((limit-prev[axis])/(p[axis]-prev[axis])))
        if b:out.append(p)
        prev=p
    return out

def intersect_box(tri,limits):
    result=tri
    for axis,(lo,hi) in enumerate(limits):
        result=clipped(result,axis,lo,True);result=clipped(result,axis,hi,False)
        if len(result)<3:return []
    if sum((result[i]-result[0]).cross(result[i+1]-result[0]).length for i in range(1,len(result)-1))<1e-9:return []
    return result

def world_triangles(o):
    v=[o.matrix_world@p.co for p in o.data.vertices]
    return [[v[i] for i in p.vertices] for p in o.data.polygons]

def points_in_slab(tris,lo,hi):
    points=[]
    for tri in tris:
        p=clipped(clipped(tri,2,lo,True),2,hi,False)
        points.extend(p)
    return points

def mesh_integrity(meshes):
    issues=[];checked=set()
    for o in meshes:
        if o.modifiers:issues.append([o.name,'unapplied modifier'])
        if o.data.name in checked:continue
        checked.add(o.data.name);m=o.data
        if m.uv_layers:issues.append([m.name,'UV layer'])
        seen=set()
        for p in m.polygons:
            if len(p.vertices)!=3:issues.append([m.name,p.index,'non-triangle'])
            if p.area<1e-11:issues.append([m.name,p.index,'zero-area'])
            key=tuple(sorted(tuple(round(v,6) for v in m.vertices[i].co) for i in p.vertices))
            if key in seen:issues.append([m.name,p.index,'duplicate triangle'])
            seen.add(key)
    return issues,len(checked)

def main():
    start=time.perf_counter();bpy.ops.wm.open_mainfile(filepath=str(BASE/'cache_sea_deep.blend'))
    bpy.context.view_layer.update()
    groups=('Layout','Landmarks','Outer','Drift')
    meshes=sorted({o for n in groups for o in bpy.data.collections[n].all_objects if o.type=='MESH'},key=lambda o:o.name)
    missing=[];walk=[];reserved=[];context=[];panel=[];low=[];kelp=[];heights=[];row_ends=[];facing=[]
    max_intrusion=0.;max_low=0.;ranges={};edge_ranges={};coverage=set();minimum_bridge=100.
    grid_checks=0
    for o in meshes:
        if 'cell_c' in o and o.get('role') in ('wall','row_end','low_wall','kelp'):coverage.add((o['cell_c'],o['cell_r']))
        tri=world_triangles(o)
        if not tri:continue
        pts=[p for t in tri for p in t]
        mn=[min(p[i] for p in pts) for i in range(3)];mx=[max(p[i] for p in pts) for i in range(3)]
        role=o.get('role','')
        if role in ('wall','row_end','low_wall'):
            c,r=o['cell_c'],o['cell_r'];p=spec.cell(c,r)
            slab=points_in_slab(tri,.15,2.5)
            intr=max([0.]+[max(p.x-2-v.x,v.x-p.x-2,p.y-2-v.y,v.y-p.y-2) for v in slab])
            max_intrusion=max(max_intrusion,intr)
            if role=='low_wall':
                max_low=max(max_low,mx[2])
                if intr>1e-4 or mx[2]>1.3001:low.append([o.name,intr,mx[2]])
        if role=='kelp':
            c,r=o['cell_c'],o['cell_r'];p=spec.cell(c,r);edge=o['kelp_edge']
            points=points_in_slab(tri,.15,2.5);local=[v-p for v in points]
            limits={'W':(-2,-1,-2,2),'E':(1,2,-2,2),'N':(-2,2,1,2),'S':(-2,2,-2,-1)}[edge]
            outside=[v for v in local if not(limits[0]-1e-4<=v.x<=limits[1]+1e-4 and limits[2]-1e-4<=v.y<=limits[3]+1e-4)]
            neighbour={'W':(c-1,r),'E':(c+1,r),'N':(c,r-1),'S':(c,r+1)}[edge]
            kelp.append(dict(c=c,r=r,edge=edge,passed=not outside,strip=limits,clear_middle_m=2,
                adjacent_mark=spec.mark(*neighbour),nearest_wall_note='c6,r16 has no # in its four neighbours; east edge faces the wall beyond the adjacent % cell' if (c,r)==(6,16) else ''))
        if role=='row_end':
            band=points_in_slab(tri,3.2,4.0);axis=0
            thickness=max(p[axis] for p in band)-min(p[axis] for p in band)
            row_ends.append(dict(object=o.name,flatness_error_m=thickness,passed=thickness<1e-4))
        if role=='bridge':
            # Supports may start on rack roofs below six metres; the aisle may not.
            minimum_bridge=min(minimum_bridge,mn[2])
        # Broad phase restricts each mesh to the handful of map cells its bounds touch.
        c0=max(0,math.floor(mn[0]/4));c1=min(spec.W-1,math.floor((mx[0]-1e-6)/4))
        r0=max(0,spec.H-1-math.floor((mx[1]-1e-6)/4));r1=min(spec.H-1,spec.H-1-math.floor(mn[1]/4))
        for r in range(r0,r1+1):
            for c in range(c0,c1+1):
                ch=spec.mark(c,r);z=spec.zone(c,r)
                if ch in '#=':continue
                grid_checks+=1;p=spec.cell(c,r)
                def hit(zlo,zhi,margin=0.):
                    if mx[2]<zlo or mn[2]>zhi:return False
                    bounds=((p.x-2+margin+1e-5,p.x+2-margin-1e-5),(p.y-2+margin+1e-5,p.y+2-margin-1e-5),(zlo,zhi))
                    return any(intersect_box(t,bounds) for t in tri)
                if ch in 'MQD':
                    if hit(0,11.5):reserved.append([o.name,c,r,ch])
                elif role!='kelp' and hit(.15,2.5,.15 if role in ('wall','row_end') else 0):walk.append([o.name,c,r,ch])
                if z=='Context' and hit(3.5,6.5):context.append([o.name,c,r,ch])
                if role=='bridge' and hit(.15,6.0):walk.append([o.name,c,r,'bridge below 6m'])
        if mx[0]>=52 and mn[0]<=64 and mx[1]>=92 and mn[1]<=92.8 and mn[2]<=11.2:
            if any(intersect_box(t,((52.00001,63.99999),(92.00001,92.79999),(0,11.2))) for t in tri):panel.append(o.name)
        if role=='wall' and 'face' in o and '_L' in o.name:
            expected={'S':Vector((0,-1,0)),'E':Vector((1,0,0)),'N':Vector((0,1,0)),'W':Vector((-1,0,0))}[o['face']]
            actual=(o.matrix_world.to_3x3()@o.data.polygons[0].normal).normalized()
            if actual.dot(expected)<.99:facing.append(o.name)
    for r,row in enumerate(spec.ROWS):
        for c,ch in enumerate(row):
            if ch in '#=%' and (c,r) not in coverage:missing.append([c,r,ch])
    for o in bpy.data.collections['Layout'].all_objects:
        if o.type!='EMPTY' or 'target_height' not in o:continue
        c,r=o['cell_c'],o['cell_r'];target=spec.height(c,r)
        actual=max(child.matrix_world.__matmul__(v.co).z for child in o.children if child.type=='MESH' for v in child.data.vertices)
        if abs(actual-target)>.0002:heights.append([o.name,target,actual])
        edge=c in (0,spec.W-1) or r in (0,spec.H-1)
        (edge_ranges if edge else ranges).setdefault(spec.zone(c,r),[]).append(actual)
    integrity,unique=mesh_integrity(meshes)
    triangles={n:sum(len(o.data.polygons) for o in bpy.data.collections[n].all_objects if o.type=='MESH') for n in groups}
    budgets=dict(main=triangles['Layout']+triangles['Landmarks']<=650000,outer=triangles['Outer']<=130000,drift=triangles['Drift']<=10000)
    export_names=[o.name for o in meshes]
    hidden=[n for n in export_names if n.startswith(('Seabed_','Beacon_','Fragment_','DataChest_','Terminal_','ResetConsole_','CacheBlock_','Socket_')) or n.endswith(('_Barrier','_Door'))]
    materials={n:len({m.name for o in bpy.data.collections[n].all_objects if o.type=='MESH' for m in o.data.materials}) for n in groups}
    result=dict(pass_all=not(missing or walk or reserved or context or panel or low or heights or integrity or facing or hidden)
        and all(x['passed'] for x in kelp+row_ends) and all(budgets.values()) and max(materials.values())<=40,
        map_cells=725,solid_or_kelp_cells=len(coverage),required_solid_or_kelp_cells=405,missing_cells=missing,
        walkability_violations=walk,reserved_M_Q_D_violations=reserved,door_panel_violations=panel,
        context_3_5_to_6_5m_violations=context,low_wall_violations=low,max_low_wall_z_m=max_low,
        max_wall_projection_m=max_intrusion,kelp_edge_checks=kelp,row_end_checks=row_ends,
        row_end_flatness_max_m=max(x['flatness_error_m'] for x in row_ends),height_errors=heights,
        rack_interior_ranges_m={k:[min(v),max(v)] for k,v in ranges.items()},
        rack_edge_ranges_m={k:[min(v),max(v)] for k,v in edge_ranges.items()},
        height_rule='interior Low+Hash*(High-Low); map boundary High+2+Hash*4; white door frame is a separate 13m landmark',
        triangle_cell_intersection_checks=grid_checks,mesh_integrity_issues=integrity,wall_facing_errors=facing,
        triangles=triangles,budgets=budgets,material_counts=materials,unexpected_code_hidden_meshes=hidden,
        lighthouse_lamp_blender=list(bpy.data.objects['Lighthouse_Lamp'].matrix_world.translation),
        lighthouse_lamp_unity=[58,33.2,-18],unique_meshes_checked=unique,
        preview_objects_excluded='All floor, people, cameras and lights are in Reference; FBX selects Layout/Landmarks or Outer/Drift only.',
        seconds=round(time.perf_counter()-start,3))
    (BASE/'validation_report.json').write_text(json.dumps(result,ensure_ascii=False,indent=2))
    print('VALIDATION',json.dumps({k:result[k] for k in ('pass_all','triangles','seconds')}),flush=True)
    assert result['pass_all'],'See validation_report.json'

if __name__=='__main__':main()
