"""Export Unity-ready scenes and local kits, then audit every FBX in empty Blender scenes."""
from pathlib import Path
import bpy, json, time, math
from mathutils import Vector, Matrix

BASE=Path(__file__).resolve().parent
SCENE_ROTATION=Matrix.Rotation(math.pi,4,'Z')
SETTINGS=dict(axis_forward='-Z',axis_up='Y',apply_scale_options='FBX_SCALE_UNITS',
              bake_space_transform=True,mesh_smooth_type='FACE',object_types={'MESH','EMPTY'},
              use_selection=True,add_leaf_bones=False,bake_anim=False,use_mesh_modifiers=True,
              path_mode='STRIP',use_custom_props=True)
PROBES=['Lighthouse_Lamp','Beacon_c14_r01_Lamp','Terminal_c23_r17_Mesh',
        'SunkenPhone_c14_r13_Mesh','CacheBlock_c20_r12_Mesh','Gate_c23_r16_Door',
        'Rack_c24_r10_S_L00']

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
                materials={o.name:[s.material.name for s in o.material_slots] for o in meshes},probes=probes)

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
    assert not material_errors,material_errors[:5]
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
                reimported_triangles=triangles,material_slot_checks=len(expected['materials']),material_slot_mismatches=material_errors)

def main():
    start=time.perf_counter();bpy.ops.wm.open_mainfile(filepath=str(BASE/'cache_sea_shallows.blend'))
    assert bpy.context.scene.unit_settings.scale_length==1
    assert all(not o.modifiers for o in bpy.data.objects),'Unapplied modifiers'
    plans=[('cache_sea_shallows.fbx',('Layout','Landmarks','Props'),'CacheSeaShallows'),
           ('cache_sea_shallows_outer.fbx',('Outer',),'CacheSeaShallows_Outer'),
           ('cache_sea_shallows_drift.fbx',('Drift',),'CacheSeaShallows_Drift')]
    expected_scenes={}
    for filename,collections,root_name in plans:
        objects=objects_in(collections)
        expected_scenes[filename]=snapshot(objects,PROBES if filename==plans[0][0] else ())
        export(BASE/'export'/filename,objects,scene=True)
        assert bounds_error(expected_scenes[filename]['bounds'],bounds(objects))<1e-6,'Export staging did not restore world matrices'
    kit_expected={}
    for root in sorted(bpy.data.collections['Kit'].objects,key=lambda o:o.name):
        if root.type!='EMPTY':continue
        location=root.location.copy()
        try:
            root.location=(0,0,0);bpy.context.view_layer.update()
            objects=[root,*root.children]
            kit_expected[root['kit_name']]=snapshot(objects)
            export(BASE/'export/kit'/f'{root["kit_name"]}.fbx',objects,scene=False)
        finally:
            root.location=location;bpy.context.view_layer.update()
    export_seconds=time.perf_counter()-start
    scene_roundtrips={}
    for filename,_,root_name in plans:
        result=audit_import(BASE/'export'/filename,expected_scenes[filename],scene=True)
        result['fbx_metadata']=binary_metadata(BASE/'export'/filename,root_name)
        scene_roundtrips[filename]=result
    kits={}
    for name,expected in kit_expected.items():
        result=audit_import(BASE/'export/kit'/f'{name}.fbx',expected,scene=False)
        kits[name]=dict(max_bounds_error_m=result['max_bounds_error_m'],triangles=result['reimported_triangles'],
                        material_slot_checks=result['material_slot_checks'],material_slot_mismatches=result['material_slot_mismatches'])
    # Unity's clockwise Y rotations: (+Z) -> (+X) -> (-Z) -> (-X).
    facing=[]
    for direction,degrees,expected in [('N',0,(0,0,1)),('E',90,(1,0,0)),('S',180,(0,0,-1)),('W',270,(-1,0,0))]:
        radians=math.radians(degrees);forward=Vector((math.sin(radians),0,math.cos(radians)))
        assert (forward-Vector(expected)).length<1e-6
        facing.append(dict(face=direction,unity_y_degrees=degrees,unity_forward=list(forward)))
    report=dict(revision=2,settings={k:sorted(v) if isinstance(v,set) else v for k,v in SETTINGS.items()},
                scene_pre_rotation_degrees=180,kit_pre_rotation_degrees=0,root_staging_transform='identity',
                scene_unity_mapping='(Bx, Bz, By)',kit_unity_mapping='(-Bx, Bz, -By)',
                blender_roundtrip_rule='Scene meshes reimport after Rz(180); compare both raw rotated expectations and coordinates restored by inverse Rz(180). Kits compare directly.',
                unit_scale_length=1,kit_files=len(kits),scene_roundtrips=scene_roundtrips,kit_roundtrips=kits,
                kit_unity_facing=facing,unity_validation=dict(source='user-provided Unity batchmode measurements in round-two request',
                    version='6000.0.85f1',model_importer='default',prefab_root_local_rotation=[0,0,0],
                    representative_scene_mesh_local_rotation=[0,180,0],representative_scene_meshes=['Lighthouse_Lamp','Terminal_c23_r17_Mesh'],
                    authored_child_directions_preserved=True,instantiation_modes=['Instantiate(prefab)','Instantiate(prefab, zero, identity)'],
                    independently_rerun_this_round=False),
                export_seconds=round(export_seconds,3),total_seconds=round(time.perf_counter()-start,3))
    main_result=scene_roundtrips[plans[0][0]]
    report.update({k:main_result[k] for k in ('bounds_before','bounds_after_restore','max_bounds_error_m','landmarks',
                   'reimported_meshes','reimported_triangles','material_slot_checks','material_slot_mismatches')})
    report['fbx_axis_units']=main_result['fbx_metadata']['axis_units']
    (BASE/'export/roundtrip_report.json').write_text(json.dumps(report,indent=2))
    print('EXPORT_ROUNDTRIP_PASS',json.dumps({k:report[k] for k in ('revision','max_bounds_error_m','kit_files','reimported_triangles','total_seconds')}),flush=True)

if __name__=='__main__':main()
