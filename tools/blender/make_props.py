"""Procedural low-poly props that the CC0 kits lack (GDD §10: props from kits + custom). Headless Blender.

  Crossbow        hand crossbow (sidekick skill + bandit crossbowmen/archers; UAL pistol clips hold it one-handed)
  Club            nail-studded club for road thugs
  Pendant         the Curator's tell: a dull chronicle-stone pendant (GDD §4.5)
  ChronicleStone  scrying stone that records deeds (GDD §4.3): standing stone + gem eye
  BandageRoll     Provisioner bandage
  Dagger          turncoat/ambusher blade

Material slot names (remapped to palette toon materials in Unity): M_Wood, M_Iron, M_String, M_Stone, M_Gem, M_Cloth, M_Leather.
Usage: blender -b -P make_props.py -- <out_dir> <preview_dir>
"""
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def mat(name, rgb):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.diffuse_color = (*rgb, 1)
    return m


PALETTE = {
    'M_Wood': (0.42, 0.27, 0.15), 'M_Iron': (0.45, 0.47, 0.5), 'M_String': (0.85, 0.82, 0.7),
    'M_Stone': (0.46, 0.5, 0.55), 'M_Gem': (0.25, 0.75, 0.85), 'M_Cloth': (0.92, 0.9, 0.84), 'M_Leather': (0.3, 0.2, 0.13),
}


def add_box(bm, size, center, rot=None):
    m = Matrix.Translation(Vector(center))
    if rot is not None:
        m = m @ rot
    m = m @ Matrix.Diagonal((*size, 1.0))
    bmesh.ops.create_cube(bm, size=1.0, matrix=m)


def add_cyl(bm, r1, r2, depth, center, segments=10, rot=None):
    m = Matrix.Translation(Vector(center))
    if rot is not None:
        m = m @ rot
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segments, radius1=r1, radius2=r2, depth=depth, matrix=m)


def new_object(name, parts):
    """parts: list of (material_name, builder(bm))"""
    me = bpy.data.meshes.new(name)
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    bm = bmesh.new()
    for mi, (mname, build) in enumerate(parts):
        before = set(bm.faces)
        build(bm)
        for f in bm.faces:
            if f not in before:
                f.material_index = mi
        me.materials.append(mat(mname, PALETTE[mname]))
    bm.normal_update()
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = False
    return obj


def crossbow():
    rx = Matrix.Rotation(math.radians(90), 4, 'X')
    return new_object('Crossbow', [
        ('M_Wood', lambda bm: (add_box(bm, (0.045, 0.40, 0.05), (0, 0.10, 0)),                 # stock along +Y
                               add_box(bm, (0.04, 0.09, 0.11), (0, -0.07, -0.06)))),          # grip
        ('M_Iron', lambda bm: (add_box(bm, (0.40, 0.03, 0.025), (0, 0.27, 0.01)),              # prod (bow limbs)
                               add_box(bm, (0.05, 0.05, 0.03), (0, 0.29, 0.01)))),
        ('M_String', lambda bm: add_box(bm, (0.40, 0.006, 0.006), (0, 0.22, 0.02))),
    ])


def club():
    return new_object('Club', [
        ('M_Wood', lambda bm: add_cyl(bm, 0.028, 0.06, 0.72, (0, 0, 0.36), 8)),
        ('M_Iron', lambda bm: [add_box(bm, (0.012, 0.012, 0.05), (math.cos(a) * 0.055, math.sin(a) * 0.055, 0.55 + 0.1 * (i % 2)),
                                        Matrix.Rotation(a, 4, 'Z') @ Matrix.Rotation(math.radians(90), 4, 'Y'))
                               for i, a in enumerate([k * math.pi / 3 for k in range(6)])]),
    ])


def dagger():
    return new_object('Dagger', [
        ('M_Leather', lambda bm: add_cyl(bm, 0.016, 0.016, 0.11, (0, 0, 0.0), 6)),
        ('M_Iron', lambda bm: (add_box(bm, (0.09, 0.02, 0.02), (0, 0, 0.065)),
                               add_cyl(bm, 0.022, 0.003, 0.22, (0, 0, 0.185), 4))),
    ])


def pendant():
    return new_object('Pendant', [
        ('M_Iron', lambda bm: bmesh.ops.create_cone(bm, cap_ends=True, segments=10, radius1=0.035, radius2=0.035, depth=0.012,
                                                    matrix=Matrix.Rotation(math.radians(90), 4, 'X'))),
        ('M_Gem', lambda bm: bmesh.ops.create_icosphere(bm, subdivisions=1, radius=0.022,
                                                        matrix=Matrix.Translation((0, -0.014, 0)) @ Matrix.Diagonal((1, 0.6, 1.25, 1)))),
    ])


def chronicle_stone():
    def stone(bm):
        add_box(bm, (0.62, 0.42, 1.55), (0, 0, 0.9))
        add_box(bm, (0.5, 0.36, 0.25), (0, 0, 1.75), Matrix.Rotation(math.radians(8), 4, 'Y'))
        add_box(bm, (0.9, 0.7, 0.22), (0, 0, 0.11))
    def gem(bm):
        bmesh.ops.create_icosphere(bm, subdivisions=2, radius=0.13, matrix=Matrix.Translation((0, 0.22, 1.25)) @ Matrix.Diagonal((1, 0.55, 1, 1)))
    def runes(bm):
        for i in range(6):
            a = math.radians(-60 + i * 24)
            add_box(bm, (0.035, 0.03, 0.09), (math.sin(a) * 0.22, 0.215, 1.25 + math.cos(a) * 0.22))
    obj = new_object('ChronicleStone', [('M_Stone', stone), ('M_Gem', gem), ('M_Iron', runes)])
    bev = obj.modifiers.new('bevel', 'BEVEL')
    bev.width = 0.03
    bev.segments = 1
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier='bevel')
    return obj


def bandage():
    return new_object('BandageRoll', [('M_Cloth', lambda bm: add_cyl(bm, 0.045, 0.045, 0.07, (0, 0, 0), 10, Matrix.Rotation(math.radians(90), 4, 'Y')))])


def spike_plate():
    def plate(bm):
        add_box(bm, (1.5, 1.5, 0.08), (0, 0, 0.04))
        add_box(bm, (1.6, 0.08, 0.1), (0, 0.78, 0.05))
        add_box(bm, (1.6, 0.08, 0.1), (0, -0.78, 0.05))
    def spikes(bm):
        for ix in range(-2, 3):
            for iy in range(-2, 3):
                add_cyl(bm, 0.045, 0.0, 0.28, (ix * 0.3, iy * 0.3, 0.2), 4)
    return new_object('SpikePlate', [('M_Wood', plate), ('M_Iron', spikes)])


def tripwire():
    def stakes(bm):
        for x in (-2.0, 2.0):
            add_box(bm, (0.09, 0.09, 0.55), (x, 0, 0.27))
    def wire(bm):
        add_box(bm, (4.0, 0.015, 0.015), (0, 0, 0.32))
    return new_object('TripwireStakes', [('M_Wood', stakes), ('M_String', wire)])


def export(obj, out_dir):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    path = os.path.join(out_dir, obj.name + '.fbx')
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH'}, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', mesh_smooth_type='FACE')
    print('[props] exported', path, len(obj.data.polygons), 'faces')


def main():
    argv = sys.argv[sys.argv.index('--') + 1:]
    out_dir, preview_dir = argv[0], argv[1]
    os.makedirs(out_dir, exist_ok=True)
    os.makedirs(preview_dir, exist_ok=True)
    for build in (crossbow, club, dagger, pendant, chronicle_stone, bandage, spike_plate, tripwire):
        reset()
        obj = build()
        export(obj, out_dir)
        # preview
        scn = bpy.context.scene
        scn.render.engine = 'BLENDER_WORKBENCH'
        scn.display.shading.light = 'STUDIO'
        scn.display.shading.color_type = 'MATERIAL'
        scn.render.resolution_x = 400
        scn.render.resolution_y = 400
        cam_data = bpy.data.cameras.new('Cam')
        cam = bpy.data.objects.new('Cam', cam_data)
        scn.collection.objects.link(cam)
        scn.camera = cam
        dims = obj.dimensions
        r = max(dims) * 1.9 + 0.1
        c = Vector(obj.matrix_world.translation) + Vector((0, 0, dims.z * 0.5 if obj.name in ('ChronicleStone', 'Club') else 0))
        cam.location = c + Vector((r * 0.8, r * 0.9, r * 0.5))
        cam.rotation_euler = (c - cam.location).to_track_quat('-Z', 'Y').to_euler()
        scn.render.filepath = os.path.join(preview_dir, obj.name + '.png')
        bpy.ops.render.render(write_still=True)


main()
