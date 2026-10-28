"""Export Unity-ready scenes and local kits, then audit every FBX in empty Blender scenes."""
from pathlib import Path
import bpy, json, time, math
from collections import Counter
from mathutils import Vector, Matrix

BASE=Path(__file__).resolve().parent
SCENE_ROTATION=Matrix.Rotation(math.pi,4,'Z')
SETTINGS=dict(axis_forward='-Z',axis_up='Y',apply_scale_options='FBX_SCALE_UNITS',
              bake_space_transform=True,mesh_smooth_type='FACE',object_types={'MESH','EMPTY'},
              use_selection=True,add_leaf_bones=False,bake_anim=False,use_mesh_modifiers=True,
              path_mode='STRIP',use_custom_props=True)
PROBES=[]

def objects_in(names):
    return sorted({o for n in names for o in bpy.data.collections[n].all_objects
                   if o.type in ('MESH','EMPTY')},key=lambda o:o.name)

def bounds(objects,transform=None):
    transform=Matrix.Identity(4) if transform is None else transform
    pts=[transform @ (o.matrix_world @ Vector(v)) for o in objects if o.type=='MESH' for v in o.bound_box]
    return dict(min=[min(p[i] for p in pts) for i in range(3)],
                max=[max(p[i] for p in pts) for i in range(3)])

def bounds_error(expected,actual):
    return max(abs(expected[k][i]-actual[k][i]) for k in ('min','max') for i in range(3))

def bbox_center(o,transform=None):
    b=bounds([o],transform)
    return [(a+b)/2 for a,b in zip(b['min'],b['max'])]

def export(path,objects,scene=False):
    # Flatten only the export staging and keep the root identity. Unity's default
    # importer needs a world-Z half-turn on scene meshes; local kits omit it.
    roots=[o for o in objects if o.type=='EMPTY' and o.parent is None]
    assert len(roots)==1,[o.name for o in roots]
    root=roots[0]
    assert max(abs(root.matrix_world[i][j]-(1 if i==j else 0)) for i in range(4) for j in range(4))<1e-6,root.name
    selected=[root]+[o for o in objects if o.type=='MESH']
    restore=[(o,o.parent,o.matrix_world.copy(),o.get('assembly_parent')) for o in selected if o!=root]
    rotation=SCENE_ROTATION if scene else Matrix.Identity(4)
    try:
        for o,parent,matrix,_ in restore:
            o['assembly_parent']=parent.name if parent else ''
            o.parent=root
            o.matrix_world=rotation @ matrix
        bpy.context.view_layer.update()
        bpy.ops.object.select_all(action='DESELECT')
        for o in selected:o.hide_set(False);o.select_set(True)
        bpy.context.view_layer.objects.active=root
        bpy.ops.export_scene.fbx(filepath=str(path),**SETTINGS)
    finally:
        for o,parent,matrix,old_property in restore:
            o.parent=parent;o.matrix_world=matrix
            if old_property is None:
                if 'assembly_parent' in o:del o['assembly_parent']
            else:o['assembly_parent']=old_property
        bpy.context.view_layer.update()

def snapshot(objects,probe_names=()):
    meshes=[o for o in objects if o.type=='MESH']
    probes={}
    for n in probe_names:
        ob=bpy.data.objects[n]
        probes[n]=dict(origin=list(ob.matrix_world.translation),bounds_center=bbox_center(ob))
        if n.startswith('Rack_'):
            probes[n]['front_normal']=list((ob.matrix_world.to_3x3() @ ob.data.polygons[0].normal).normalized())
    return dict(bounds=bounds(objects),rotated_bounds=bounds(objects,SCENE_ROTATION),
                triangles=sum(len(o.data.polygons) for o in meshes),mesh_count=len(meshes),
                materials={o.name:[s.material.name for s in o.material_slots] for o in meshes},
                material_face_counts={o.name:dict(Counter(o.data.materials[p.material_index].name for p in o.data.polygons)) for o in meshes},probes=probes)

def fresh_import(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system='METRIC';bpy.context.scene.unit_settings.scale_length=1
    bpy.ops.import_scene.fbx(filepath=str(path),use_custom_normals=True)
    bpy.context.view_layer.update()

def binary_metadata(path,root_name):
    from io_scene_fbx import parse_fbx
    tree,version=parse_fbx.parse(str(path))
    global_settings=next(e for e in tree.elems if e.id==b'GlobalSettings')
    properties=next(e for e in global_settings.elems if e.id==b'Properties70')
    axes={e.props[0].decode():e.props[-1] for e in properties.elems
          if e.props[0] in (b'UnitScaleFactor',b'UpAxis',b'UpAxisSign',b'FrontAxis',b'FrontAxisSign',b'CoordAxis',b'CoordAxisSign')}
    objects=next(e for e in tree.elems if e.id==b'Objects')
    root=next(e for e in objects.elems if e.id==b'Model' and e.props[1].decode().split('\x00')[0]==root_name)
    props=next(e for e in root.elems if e.id==b'Properties70')
    transform={e.props[0].decode():list(e.props[-3:]) for e in props.elems
               if e.props[0] in (b'Lcl Translation',b'Lcl Rotation',b'Lcl Scaling')}
    assert max(abs(x) for x in transform.get('Lcl Rotation',[0,0,0]))<1e-6,transform
    assert max(abs(x) for x in transform.get('Lcl Translation',[0,0,0]))<1e-6,transform
    assert max(abs(x-1) for x in transform.get('Lcl Scaling',[1,1,1]))<1e-6,transform
    effective={'Lcl Translation':[0,0,0],'Lcl Rotation':[0,0,0],'Lcl Scaling':[1,1,1]}
    effective.update(transform)
    return dict(version=version,axis_units=axes,root_model_transform=effective,
                explicit_root_model_properties=transform,omitted_transform_properties_use_fbx_defaults=True)

def audit_import(path,expected,scene):
    fresh_import(path)
    objects=list(bpy.context.scene.objects)
    undo=SCENE_ROTATION.inverted() if scene else Matrix.Identity(4)
    raw=bounds(objects);restored=bounds(objects,undo)
    raw_expected=expected['rotated_bounds'] if scene else expected['bounds']
    raw_error=bounds_error(raw_expected,raw)
    error=bounds_error(expected['bounds'],restored)
    assert raw_error<.001 and error<.001,(path.name,raw_error,error)
    meshes=[o for o in objects if o.type=='MESH']
    triangles=sum(len(o.data.polygons) for o in meshes)
    assert triangles==expected['triangles'],(path.name,triangles,expected['triangles'])
    assert len(meshes)==expected['mesh_count'],path.name
    material_errors=[]
    for name,slots in expected['materials'].items():
        actual=[s.material.name for s in bpy.data.objects[name].material_slots]
        if actual!=slots:material_errors.append(dict(object=name,expected=slots,actual=actual))
    face_errors=[]
    for name,counts in expected['material_face_counts'].items():
        mesh=bpy.data.objects[name].data
        actual=dict(Counter(mesh.materials[p.material_index].name for p in mesh.polygons))
        if actual!=counts:face_errors.append(dict(object=name,expected=counts,actual=actual))
    assert not material_errors,material_errors[:5]
    assert not face_errors,face_errors[:5]
    landmarks={}
    for name,probe in expected['probes'].items():
        ob=bpy.data.objects[name]
        original=Vector(probe['origin']);raw_origin=ob.matrix_world.translation.copy()
        restored_origin=undo @ raw_origin
        origin_error=(restored_origin-original).length
        raw_probe_expected=SCENE_ROTATION @ original if scene else original
        assert (raw_origin-raw_probe_expected).length<.001,(name,'raw origin')
        assert origin_error<.001,(name,'restored origin')
        center=bbox_center(ob,undo);center_error=(Vector(center)-Vector(probe['bounds_center'])).length
        assert center_error<.001,(name,'bounds center',center_error)
        landmarks[name]=dict(blender_authored=list(original),blender_expected_imported=list(raw_probe_expected),
                             blender_imported=list(raw_origin),blender_restored=list(restored_origin),error_m=origin_error,
                             authored_bounds_center=probe['bounds_center'],restored_bounds_center=center,bounds_center_error_m=center_error,
                             unity_expected=[original.x,original.z,original.y],
                             unity_expected_bounds_center=[probe['bounds_center'][0],probe['bounds_center'][2],probe['bounds_center'][1]])
        if 'front_normal' in probe:
            normal=(undo.to_3x3() @ ob.matrix_world.to_3x3() @ ob.data.polygons[0].normal).normalized()
            assert normal.dot(Vector(probe['front_normal']))>.9999,(name,'normal')
            landmarks[name]['restored_front_normal']=list(normal)
    return dict(bounds_before=expected['bounds'],expected_rotated_import_bounds=raw_expected,
                raw_imported_bounds=raw,bounds_after_restore=restored,max_bounds_error_m=error,
                raw_bounds_error_m=raw_error,landmarks=landmarks,reimported_meshes=len(meshes),
                reimported_triangles=triangles,material_slot_checks=len(expected['materials']),material_slot_mismatches=material_errors,material_face_count_mismatches=face_errors,material_face_count_checks=len(expected['material_face_counts']))

BLEND_FILE='battle_ring.blend'
PROBES=['BattleRack_xn03_yn09_Top', 'BattleRack_xp00_yn09_Top']
PLANS=[('battle_ring.fbx', ('Layout',), 'BattleRing')]

def main():
    start=time.perf_counter()
    bpy.ops.wm.open_mainfile(filepath=str(BASE/BLEND_FILE))
    assert bpy.context.scene.unit_settings.scale_length==1
    assert all(not o.modifiers for o in bpy.data.objects)
    expected={}
    for filename,collections,root_name in PLANS:
        objects=objects_in(collections)
        expected[filename]=snapshot(objects,PROBES if filename==PLANS[0][0] else ())
        used={m.name for o in objects if o.type=='MESH' for m in o.data.materials}
        assert len(used)<=40,(filename,len(used))
        expected[filename]['material_count']=len(used)
        export(BASE/'export'/filename,objects,scene=True)
        assert bounds_error(expected[filename]['bounds'],bounds(objects))<1e-5
    export_seconds=time.perf_counter()-start
    results={}
    for filename,_,root_name in PLANS:
        results[filename]=audit_import(BASE/'export'/filename,expected[filename],scene=True)
        results[filename]['fbx_metadata']=binary_metadata(BASE/'export'/filename,root_name)
        results[filename]['material_count']=expected[filename]['material_count']
        assert all(o.type in ('MESH','EMPTY') for o in bpy.context.scene.objects)
        root=bpy.data.objects[root_name]
        assert all(o.parent==root for o in bpy.context.scene.objects if o.type=='MESH')
    report=dict(pass_all=True,settings={k:sorted(v) if isinstance(v,set) else v for k,v in SETTINGS.items()},
        scene_pre_rotation_degrees=180,unity_mapping='(Bx,Bz,By)',root_transform='identity',
        rule='Compare raw Blender reimport to Rz(180) authored coordinates, then restore and compare bounds, each probe origin, centre, counts and material slots.',
        scene_roundtrips=results,export_seconds=round(export_seconds,3),total_seconds=round(time.perf_counter()-start,3),
        verification_scope='Blender FBX roundtrip and FBX root/axis metadata. Unity import was not run in this task.')
    (BASE/'export/roundtrip_report.json').write_text(json.dumps(report,indent=2))
    print('EXPORT_ROUNDTRIP_PASS',json.dumps({k:report[k] for k in ('pass_all','total_seconds')}),flush=True)

if __name__=='__main__':main()
