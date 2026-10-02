"""Measure root-motion ground speed (m/s) of UAL clips from the _RM FBX, for foot-slide-free playback scaling."""
import bpy, sys, json
argv = sys.argv[sys.argv.index('--') + 1:]
out = {}
for f in argv[:-1]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=f)
    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    scn = bpy.context.scene
    for act in bpy.data.actions:
        name = act.name.split('|')[-1]
        arm.animation_data.action = act
        f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
        def root_pos(fr):
            scn.frame_set(fr)
            pb = arm.pose.bones.get('root') or arm.pose.bones[0]
            return (arm.matrix_world @ pb.matrix).translation.copy()
        p0, p1 = root_pos(f0), root_pos(f1)
        dur = (f1 - f0) / scn.render.fps
        d = (p1 - p0)
        d.z = 0
        out[name] = {'speed': round(d.length / dur, 3) if dur > 0 else 0, 'dur': round(dur, 3), 'dx': round(d.x, 3), 'dy': round(d.y, 3)}
json.dump(out, open(argv[-1], 'w'), indent=1)
print('measured', len(out))
