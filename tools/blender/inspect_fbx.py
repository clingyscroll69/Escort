# Headless Blender: inspect one or more FBX files. Usage: blender -b -P inspect_fbx.py -- file1.fbx [file2.fbx ...]
import bpy, sys
files = sys.argv[sys.argv.index('--') + 1:]
for f in files:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=f)
    print('\n=====', f.split('/')[-1])
    for o in bpy.data.objects:
        dims = tuple(round(d, 3) for d in o.dimensions)
        extra = ''
        if o.type == 'MESH':
            extra = f' verts={len(o.data.vertices)} mats={[m.name for m in o.data.materials]} vgroups={len(o.vertex_groups)}'
        if o.type == 'ARMATURE':
            extra = f' bones={len(o.data.bones)}'
        print(f'  {o.type:9s} {o.name:40s} parent={o.parent.name if o.parent else None} dims={dims} scale={tuple(round(s,3) for s in o.scale)}{extra}')
    arm = next((o for o in bpy.data.objects if o.type == 'ARMATURE'), None)
    if arm:
        print('  BONES:', [b.name for b in arm.data.bones][:80])
    acts = [a.name for a in bpy.data.actions]
    print(f'  ACTIONS ({len(acts)}):', acts)
    for m in bpy.data.materials:
        texs = []
        if m.use_nodes:
            for n in m.node_tree.nodes:
                if n.type == 'TEX_IMAGE' and n.image: texs.append(n.image.name)
        print(f'  MAT {m.name}: {texs}')
