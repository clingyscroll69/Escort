"""Re-import each opening-vehicle FBX into an empty scene and print hierarchy, pivots, sizes, materials.
Usage: Blender -b -P tools/blender/opening_vehicles_verify.py -- build_art/opening/vehicles/fbx [Truck Sedan ...]"""
import bpy, os, sys
argv = sys.argv[sys.argv.index('--') + 1:]
fbx_dir, names = argv[0], argv[1:]
if not names:
    names = sorted(f[:-4] for f in os.listdir(fbx_dir) if f.endswith('.fbx'))
for n in names:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(fbx_dir, n + '.fbx'))
    roots = [o for o in bpy.context.scene.objects if o.parent is None]
    print('=== %s: roots %s' % (n, [r.name for r in roots]))
    for r in roots:
        print('  root %-8s type %-5s loc %s rot %s scale %s' % (r.name, r.type, tuple(round(v, 3) for v in r.matrix_world.translation),
              tuple(round(v, 3) for v in r.matrix_world.to_euler()), tuple(round(v, 3) for v in r.matrix_world.to_scale())))
        tris = 0
        for c in sorted(r.children_recursive, key=lambda o: o.name):
            line = '    %-10s %-5s world %s' % (c.name, c.type, tuple(round(v, 3) for v in c.matrix_world.translation))
            if c.type == 'MESH':
                c.data.calc_loop_triangles()
                tris += len(c.data.loop_triangles)
                bb = [c.matrix_world @ __import__('mathutils').Vector(b) for b in c.bound_box]
                mn = [round(min(v[i] for v in bb), 3) for i in range(3)]
                mx = [round(max(v[i] for v in bb), 3) for i in range(3)]
                line += ' bbox %s..%s tris %d mats %d' % (mn, mx, len(c.data.loop_triangles), len(c.data.materials))
            print(line)
        print('  total tris', tris)
        mats = sorted({m.name for o in r.children_recursive if o.type == 'MESH' for m in o.data.materials if m})
        print('  materials', mats)
