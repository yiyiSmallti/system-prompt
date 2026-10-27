"""Export the rigged, animated DeepSeek: 2K textures as files, an FBX for Unity, a GLB for the browser, and the .blend.

Run: blender -b ANIMATED.blend -P export.py -- OUT_DIR NAME
Writes OUT_DIR/textures/*.png, OUT_DIR/NAME.fbx (textures embedded, one take per action),
OUT_DIR/NAME.glb (JPEG textures, one animation per action) and OUT_DIR/NAME.blend (textures relative).
"""
import os
import re
import sys

import bpy

OUT, NAME = sys.argv[sys.argv.index('--') + 1:]
TEX = os.path.join(OUT, 'textures')
os.makedirs(TEX, exist_ok=True)

# 4K packed textures -> 2K files next to the .blend
for img in list(bpy.data.images):
    if img.type != 'IMAGE' or not img.users:
        continue
    if max(img.size) > 2048:
        img.scale(2048, 2048)
    base = re.sub(r'[^A-Za-z0-9_-]+', '_', os.path.splitext(img.name)[0]).strip('_')
    path = os.path.join(TEX, base + '.png')
    img.file_format = 'PNG'
    img.save(filepath=path)
    new = bpy.data.images.load(path)
    new.colorspace_settings.name = img.colorspace_settings.name
    new.alpha_mode = img.alpha_mode
    img.user_remap(new)
    bpy.data.images.remove(img)
    new.name = base

arm = bpy.data.objects['DeepSeek']
arm.animation_data.action = None
for pb in arm.pose.bones:   # export in the rest (T) pose
    pb.matrix_basis.identity()

bpy.ops.export_scene.fbx(
    filepath=os.path.join(OUT, NAME + '.fbx'), object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False,
    use_armature_deform_only=False, bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0, path_mode='COPY', embed_textures=True,
    apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', primary_bone_axis='Y',
    secondary_bone_axis='X', armature_nodetype='NULL', mesh_smooth_type='FACE')
bpy.ops.export_scene.gltf(
    filepath=os.path.join(OUT, NAME + '.glb'), export_format='GLB', export_animations=True,
    export_animation_mode='ACTIONS', export_image_format='JPEG', export_image_quality=88, export_skins=True,
    export_force_sampling=True, export_frame_step=1)
bpy.ops.file.make_paths_relative()
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, NAME + '.blend'), compress=True)
for f in sorted(os.listdir(OUT)):
    p = os.path.join(OUT, f)
    if os.path.isfile(p):
        print('%-40s %7.1f MB' % (f, os.path.getsize(p) / 1e6))
