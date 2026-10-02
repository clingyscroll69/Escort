import bpy, sys
files = sys.argv[sys.argv.index('--') + 1:]
rigs = []
for f in files:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=f)
    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    rigs.append({b.name: (arm.matrix_world @ b.head_local).copy() for b in arm.data.bones})
a, b = rigs
for name in ['root','pelvis','spine_01','spine_03','neck_01','Head','clavicle_l','upperarm_l','lowerarm_l','hand_l','thigh_l','calf_l','foot_l','ball_l']:
    pa, pb = a[name], b[name]
    print(f'{name:12s} A={tuple(round(x,3) for x in pa)} B={tuple(round(x,3) for x in pb)} d={(pa-pb).length:.3f}')
