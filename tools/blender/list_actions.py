import bpy, sys
f = sys.argv[sys.argv.index('--') + 1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=f)
print('OBJECTS:', [(o.name, o.type) for o in bpy.data.objects])
for a in sorted(bpy.data.actions, key=lambda a: a.name):
    fr = a.frame_range
    print(f'ACT {a.name:45s} frames={fr[0]:.0f}-{fr[1]:.0f} ({(fr[1]-fr[0])/30:.2f}s)')
