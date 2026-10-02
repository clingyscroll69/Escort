"""Headless Blender character assembler (GDD §2, §10: one base family, modified via bpy scripts).

For each recipe in recipes.json:
  1. import the Fantasy outfit (Regular-proportion armature, 65 UE-style bones),
  2. graft the head (+eyes, brows) from the UBC Superhero body (head/neck bones share positions exactly),
  3. add hairstyles rigged to the Head bone,
  4. drop unwanted parts, rename materials to stable slot names (M_Outfit, M_SkinHead, M_SkinHands, M_Hair, M_Eyes),
  5. apply per-character proportions (uniform scale),
  6. export FBX for Unity and render front/three-quarter previews for QA.

Usage: blender -b -P build_characters.py -- recipes.json out_dir preview_dir [only_name]
"""
import bpy, bmesh, json, math, os, sys
from mathutils import Vector

Q = '/Users/sapnagoel/Documents/coding/Game/ThirdParty/Quaternius'
OUTFITS = Q + '/Outfits_Fantasy/Modular Character Outfits - Fantasy[Standard]/Exports/FBX (Unity)'
UBC = Q + '/UBC/Universal Base Characters[Standard]'
HAIR = UBC + '/Hairstyles/Rigged to Head Bone/FBX (Unity)'
BODY = {
    'Male': UBC + '/Base Characters/Unity/Superhero_Male_FullBody.fbx',
    'Female': UBC + '/Base Characters/Unity/Superhero_Female_FullBody.fbx',
}
TEX_DIRS = [
    UBC + '/Base Characters/Textures',
    UBC + '/Hairstyles/Textures',
    Q + '/Outfits_Fantasy/Modular Character Outfits - Fantasy[Standard]/Textures/Base',
    Q + '/Outfits_Fantasy/Modular Character Outfits - Fantasy[Standard]/Textures/Peasant',
    Q + '/Outfits_Fantasy/Modular Character Outfits - Fantasy[Standard]/Textures/Ranger',
]


def log(*a):
    print('[build]', *a, flush=True)


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_fbx(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    return [o for o in bpy.data.objects if o not in before]


def first(objs, typ):
    return next(o for o in objs if o.type == typ)


def rebind(mesh, arm):
    mw = mesh.matrix_world.copy()
    mesh.parent = arm
    mesh.matrix_world = mw
    found = False
    for m in mesh.modifiers:
        if m.type == 'ARMATURE':
            m.object = arm
            found = True
    if not found:
        m = mesh.modifiers.new('Armature', 'ARMATURE')
        m.object = arm


def cut_below(mesh, zcut, keep_groups=None, min_weight=0.5):
    """Keep only the head: vertices above zcut (world) whose skin weights are dominated by keep_groups.
    A pure height cut leaves the Superhero trapezius (higher clavicles than the Regular outfit) poking through."""
    me = mesh.data
    mw = mesh.matrix_world
    gi = {g.index: g.name for g in mesh.vertex_groups}
    bm = bmesh.new()
    bm.from_mesh(me)
    deform = bm.verts.layers.deform.active
    kill = []
    for v in bm.verts:
        z = (mw @ v.co).z
        if z < zcut:
            kill.append(v)
            continue
        if keep_groups and deform is not None:
            w = sum(wt for idx, wt in v[deform].items() if gi.get(idx) in keep_groups)
            if w < min_weight:
                kill.append(v)
    bmesh.ops.delete(bm, geom=kill, context='VERTS')
    bm.to_mesh(me)
    bm.free()
    me.update()


def cut_above(mesh, zmax, ymin=None):
    """Delete vertices above zmax (world). If ymin is set, only those with y >= ymin (front part)."""
    me = mesh.data
    mw = mesh.matrix_world
    bm = bmesh.new()
    bm.from_mesh(me)
    kill = []
    for v in bm.verts:
        w = mw @ v.co
        if w.z > zmax and (ymin is None or w.y >= ymin):
            kill.append(v)
    bmesh.ops.delete(bm, geom=kill, context='VERTS')
    bm.to_mesh(me)
    bm.free()
    me.update()


def retarget_faces(rule):
    """Move faces of one material slot to a new material when their centre is within |x| <= x_abs_max."""
    for o in bpy.data.objects:
        if o.type != 'MESH' or not o.name.startswith(rule['mesh']):
            continue
        names = [s.material.name if s.material else '' for s in o.material_slots]
        if rule['material'] not in names:
            continue
        src = names.index(rule['material'])
        mat = bpy.data.materials.get(rule['to']) or bpy.data.materials.new(rule['to'])
        if rule['to'] not in names:
            o.data.materials.append(mat)
            names.append(rule['to'])
        dst = names.index(rule['to'])
        mw = o.matrix_world
        n = 0
        for poly in o.data.polygons:
            if poly.material_index != src:
                continue
            c = mw @ poly.center
            if abs(c.x) <= rule['x_abs_max'] and c.z >= rule.get('z_min', -1e9):
                poly.material_index = dst
                n += 1
        log(f"retarget {o.name}: {n} faces {rule['material']} -> {rule['to']}")


def mirror_part(part, arm):
    """Duplicate a one-sided accessory to the other side (x -> -x, _r <-> _l vertex groups)."""
    src = next((o for o in bpy.data.objects if o.type == 'MESH' and o.name.startswith(part)), None)
    if src is None:
        log('mirror: missing', part)
        return
    dup = src.copy()
    dup.data = src.data.copy()
    dup.name = src.name + '_Mirror'
    bpy.context.scene.collection.objects.link(dup)
    me = dup.data
    inv = dup.matrix_world.inverted()
    mw = dup.matrix_world
    for v in me.vertices:
        w = mw @ v.co
        w.x = -w.x
        v.co = inv @ w
    for poly in me.polygons:
        poly.flip()
    for g in dup.vertex_groups:
        if g.name.endswith('_r'):
            g.name = g.name[:-2] + '_TMP_l'
        elif g.name.endswith('_l'):
            g.name = g.name[:-2] + '_r'
    for g in dup.vertex_groups:
        if g.name.endswith('_TMP_l'):
            g.name = g.name.replace('_TMP_l', '_l')
    me.update()
    rebind(dup, arm)
    log('mirrored', part)


def find_image(name):
    base = os.path.splitext(name)[0]
    for d in TEX_DIRS:
        for ext in ('.png', '.jpg'):
            p = os.path.join(d, base + ext)
            if os.path.exists(p):
                return p
    return None


def fix_images():
    for img in bpy.data.images:
        if img.filepath and os.path.exists(bpy.path.abspath(img.filepath)):
            continue
        p = find_image(img.name.split('.png')[0] + '.png')
        if p:
            img.filepath = p
            img.reload()


def rename_materials(objs):
    for o in objs:
        if o.type != 'MESH':
            continue
        for slot in o.material_slots:
            m = slot.material
            if m is None:
                continue
            n = m.name
            if n.startswith('MI_Superhero'):
                target = 'M_SkinHead'
            elif n.startswith('MI_Regular'):
                target = 'M_SkinHands'
            elif n.startswith('MI_Hair'):
                target = 'M_Hair'
            elif n.startswith('MI_Eyes'):
                target = 'M_Eyes'
            elif n.startswith('MI_Peasant') or n.startswith('MI_Ranger'):
                target = 'M_Outfit'
            else:
                target = n
            if n != target:
                existing = bpy.data.materials.get(target)
                if existing is not None and existing is not m:
                    slot.material = existing
                else:
                    m.name = target


def build(name, r, out_dir):
    reset()
    outfit_objs = import_fbx(os.path.join(OUTFITS, 'Outfits', r['outfit'] + '.fbx'))
    arm = first(outfit_objs, 'ARMATURE')
    arm.name = 'Armature'
    for extra in r.get('add_parts', []):
        objs = import_fbx(os.path.join(OUTFITS, 'Modular Parts', extra + '.fbx'))
        for o in objs:
            if o.type == 'MESH':
                rebind(o, arm)
        for o in objs:
            if o.type == 'ARMATURE':
                bpy.data.objects.remove(o, do_unlink=True)
    for o in list(bpy.data.objects):
        if o.type == 'MESH' and any(o.name.startswith(d) for d in r.get('drop', [])):
            bpy.data.objects.remove(o, do_unlink=True)
    for prefix, rule in r.get('trim', {}).items():
        for o in list(bpy.data.objects):
            if o.type == 'MESH' and o.name.startswith(prefix):
                cut_above(o, rule['z_max'], rule.get('y_min'))

    # Head graft from the Superhero body of the same sex.
    body_objs = import_fbx(BODY[r['sex']])
    body_arm = first(body_objs, 'ARMATURE')
    zcut = r.get('neck_cut', 1.38 if r['sex'] == 'Male' else 1.34)
    for o in body_objs:
        if o.type != 'MESH':
            continue
        if o.name.startswith('Eyebrows') and r.get('brows') == 'none':
            bpy.data.objects.remove(o, do_unlink=True)
            continue
        if o.name.startswith('SuperHero') or o.name.startswith('Superhero'):
            cut_below(o, zcut, keep_groups={'Head', 'neck_01'}, min_weight=r.get('neck_weight', 0.5))
            o.name = 'Face'  # must not collide with the 'Head' bone (Humanoid mapping needs unique names)
        rebind(o, arm)
    bpy.data.objects.remove(body_arm, do_unlink=True)

    for h in r.get('hair', []):
        objs = import_fbx(os.path.join(HAIR, h + '.fbx'))
        for o in objs:
            if o.type == 'MESH':
                rebind(o, arm)
        for o in objs:
            if o.type == 'ARMATURE':
                bpy.data.objects.remove(o, do_unlink=True)

    fix_images()
    rename_materials(bpy.data.objects)
    for rule in r.get('retarget_faces', []):
        retarget_faces(rule)
    for part in r.get('mirror_parts', []):
        mirror_part(part, arm)

    # Proportions (uniform scale) and facing: the kit imports facing +Y; rotate 180 deg so the exported bind pose
    # faces Unity +Z (the Agent's forward). Both are baked into bones and meshes.
    s = r.get('scale', 1.0)
    bpy.ops.object.select_all(action='DESELECT')
    arm.scale = (arm.scale[0] * s, arm.scale[1] * s, arm.scale[2] * s)
    arm.rotation_euler.z += math.pi
    for o in bpy.data.objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, name + '.fbx')
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=False, object_types={'ARMATURE', 'MESH'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
        use_armature_deform_only=False, add_leaf_bones=False, bake_anim=False, path_mode='STRIP',
        mesh_smooth_type='FACE', use_mesh_modifiers=False)
    tris = sum(len(o.data.polygons) for o in bpy.data.objects if o.type == 'MESH')
    log(f'exported {name}: {path} meshes={[o.name for o in bpy.data.objects if o.type == "MESH"]} faces={tris}')
    return arm


TEX_OUT = '/Users/sapnagoel/Documents/coding/Game/Escort/Assets/_Game/Art/Characters/Textures'


def use_game_textures(name):
    """Preview with the recoloured game textures (tools/recolor_textures.py output) when present."""
    slots = {'M_Outfit': f'T_{name}_Outfit.png', 'M_SkinHead': f'T_{name}_Head.png', 'M_SkinHands': f'T_{name}_Hands.png',
             'M_Hair': f'T_{name}_Hair.png'}
    for mat_name, tex in slots.items():
        m = bpy.data.materials.get(mat_name)
        p = os.path.join(TEX_OUT, tex)
        if m is None or not os.path.exists(p) or not m.use_nodes:
            continue
        img = bpy.data.images.load(p, check_existing=True)
        for n in m.node_tree.nodes:
            if n.type == 'TEX_IMAGE' and n.image and 'Normal' not in n.image.name and 'Roughness' not in n.image.name:
                n.image = img
    um = bpy.data.materials.get('M_Undershirt')
    if um is not None:
        um.diffuse_color = (0.08, 0.09, 0.14, 1)


def preview(name, preview_dir):
    use_game_textures(name)
    scn = bpy.context.scene
    scn.render.engine = 'BLENDER_WORKBENCH'
    scn.display.shading.light = 'STUDIO'
    scn.display.shading.color_type = 'TEXTURE'
    scn.display.shading.show_cavity = True
    scn.render.resolution_x = 640
    scn.render.resolution_y = 900
    scn.render.film_transparent = False
    world = bpy.data.worlds.new('W') if not scn.world else scn.world
    scn.world = world
    cam_data = bpy.data.cameras.new('Cam')
    cam_data.lens = 85
    cam = bpy.data.objects.new('Cam', cam_data)
    scn.collection.objects.link(cam)
    scn.camera = cam
    os.makedirs(preview_dir, exist_ok=True)
    # Blender Z-up after FBX import; character faces -Y.
    target = Vector((0, 0, 0.95))
    shots = {'front': (0, -7.0, 1.2), 'three_q': (-4.6, -5.2, 1.6), 'back': (2.5, 6.5, 1.5), 'head': (-0.45, -1.5, 1.65)}
    for label, pos in shots.items():
        cam.location = Vector(pos)
        tgt = Vector((0, 0, 1.58)) if label == 'head' else target
        cam.rotation_euler = (tgt - cam.location).to_track_quat('-Z', 'Y').to_euler()
        cam_data.lens = 85 if label != 'head' else 70
        scn.render.filepath = os.path.join(preview_dir, f'{name}_{label}.png')
        bpy.ops.render.render(write_still=True)


def main():
    argv = sys.argv[sys.argv.index('--') + 1:]
    recipes = json.load(open(argv[0]))
    out_dir, preview_dir = argv[1], argv[2]
    only = argv[3] if len(argv) > 3 else None
    for name, r in recipes.items():
        if (name.startswith('_') and name != only) or (only and name != only):
            continue
        build(name, r, out_dir)
        preview(name, preview_dir)


main()
