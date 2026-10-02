"""Opening vehicles kit (spec docs/superpowers/specs/2026-10-01-opening-street-design.md §5). Headless Blender 5.2.

  Truck      hero cab-over box truck ("Truck-kun"): Japanese-style 7.6 m, TENSEI badge, DESTINY FREIGHT livery
  Sedan      4.7 m          Hatchback  4.1 m          Taxi  sedan body, MV_TaxiPaint, lit roof sign, checker band
  Van        5.0 m panel van                          SUV   4.6 m

Conventions: metres, front faces -Y, +Z up, origin on the ground at the frontmost point of the front bumper, centred on
the width. L/R are the vehicle's own left/right (L = Blender +X = Unity -X). Every FBX: root empty <Name> -> "Body" (all
static geometry), "Wheel_FL/FR/RL/RR" (origin at the hub, spin axis = Blender X) + locator empties.
Material slot names (MV_*) are the interface; manifest.json carries colours/textures/emission for the Unity side.

Usage (deterministic, re-runnable):
  /opt/anaconda3/bin/python3 tools/opening_vehicle_textures.py build_art/opening/vehicles/textures
  /Applications/Blender.app/Contents/MacOS/Blender -b -P tools/blender/opening_vehicles.py -- build_art/opening/vehicles \
      [--only Truck,Sedan] [--no-preview] [--no-export]
(The Blender script runs the texture script itself if the textures are missing.)
"""
import bpy
import bmesh
import json
import math
import os
import subprocess
import sys
import time
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
GAME = os.path.dirname(os.path.dirname(HERE))
TEX_SCRIPT = os.path.join(GAME, 'tools', 'opening_vehicle_textures.py')
PY3 = '/opt/anaconda3/bin/python3'

# ============================================================================================ materials
# (name, colour, texture, emission, note). MV_Interior must stay LAST: solidified shells clamp inner faces to it.
MAT_DEFS = [
    ('MV_Paint', '#F2F2EE', None, None, 'Truck cab + roof deflector paint (white).'),
    ('MV_Accent', '#D9442E', None, None, 'Truck red accent stripe, tow hooks.'),
    ('MV_Black', '#1E2024', None, None, 'Black trim: grille, gaskets, visor, steps, flares, mirror housings, chassis, tyre wells.'),
    ('MV_Chrome', '#C9CED4', None, None, 'Chrome: bumper, grille surround, air horns, mirror arms, handles, hub caps, lug nuts.'),
    ('MV_Glass', '#FFFFFF', 'T_V_GlassReflect.png', None,
     'Window glass. Colour + opacity come from the texture (RGBA, alpha 0.55-0.95): render transparent so the '
     'interior shows; each pane is UV 0-1.'),
    ('MV_Mirror', '#B9CAD6', None, None, 'Mirror glass (bright cool grey).'),
    ('MV_HeadLamp', '#FFF6E2', None, {'color': '#FFF4D6', 'intensity': 4}, 'Headlamp / fog lamp lenses (on in the cutscene).'),
    ('MV_Signal', '#FF9A1A', None, {'color': '#FF9A1A', 'intensity': 0.6},
     'Amber turn-signal lenses; low glow when idle, raise to ~3 to blink.'),
    ('MV_Marker', '#FFB02E', None, {'color': '#FFA526', 'intensity': 2.5}, 'Amber clearance / side marker lamps (lit).'),
    ('MV_TailLamp', '#C4161C', None, {'color': '#FF2B1F', 'intensity': 2}, 'Red tail lamps (lit); raise to ~5 for brake.'),
    ('MV_LensClear', '#E2E7EA', None, None, 'Clear reverse / plate lamp lenses (unlit).'),
    ('MV_Plate', '#FFFFFF', 'T_V_Plates.png', None, 'Licence plates: 2x4 atlas of 512x256 cells, each plate face UV-maps one cell.'),
    ('MV_Badge', '#FFFFFF', 'T_V_Badge.png', None, 'TENSEI grille badge (chrome letters on a dark plate), UV 0-1.'),
    ('MV_Box', '#FFFFFF', 'T_V_TruckLivery.png', None,
     'Truck box panels. Each side face is UV 0-1 (text reads correctly on both sides); front/roof sample white; '
     'the cab-door DF badges sample the emblem disc of the same texture.'),
    ('MV_BoxFrame', '#B3B8BD', None, None, 'Aluminium: box corner posts, rails, roll-up door, fuel tank, side guards.'),
    ('MV_Tire', '#27282C', None, None, 'Tyre rubber.'),
    ('MV_Rim', '#ECECE6', None, None, 'White-painted steel truck rims.'),
    ('MV_Driver', '#17181B', None, None, 'Dim driver silhouette (keep dark / unlit-ish).'),
    ('MV_CarPaint', '#C9CBCD', None, None, 'Car body paint, neutral light grey: tint per instance in Unity.'),
    ('MV_TaxiPaint', '#F2B705', None, None, 'Taxi yellow body paint.'),
    ('MV_TaxiSign', '#FFFFFF', 'T_V_TaxiSign.png', {'color': '#FFF0B8', 'intensity': 2},
     'Lit TAXI roof sign faces (texture is also the emission map).'),
    ('MV_TaxiChecker', '#FFFFFF', 'T_V_Checker.png', None, 'Taxi checker band; texture tiles in U (wrap = repeat).'),
    ('MV_CarRim', '#A9AFB6', None, None, 'Car alloy wheels.'),
    ('MV_Interior', '#34373C', None, None, 'Cab / car interior: dashboard, seats, inner shell walls.'),
]
MAT_NAMES = [m[0] for m in MAT_DEFS]
MI = {n: i for i, n in enumerate(MAT_NAMES)}


def hex_lin(h):
    h = h.lstrip('#')
    c = [int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)]
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)


def build_materials(tex_dir):
    for name, col, tex, emi, note in MAT_DEFS:
        m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
        lin = hex_lin(col)
        m.diffuse_color = (*lin, 1.0)
        nt = m.node_tree
        bsdf = nt.nodes.get('Principled BSDF')
        bsdf.inputs['Base Color'].default_value = (*lin, 1.0)
        bsdf.inputs['Roughness'].default_value = 0.35 if name in ('MV_Chrome', 'MV_Mirror', 'MV_Glass') else 0.6
        if name == 'MV_Chrome':
            bsdf.inputs['Metallic'].default_value = 0.0
        if tex:
            img = bpy.data.images.load(os.path.join(tex_dir, tex), check_existing=True)
            node = nt.nodes.new('ShaderNodeTexImage')
            node.image = img
            node.interpolation = 'Linear'
            if name == 'MV_TaxiChecker':
                node.interpolation = 'Closest'
            nt.links.new(node.outputs['Color'], bsdf.inputs['Base Color'])
            if name == 'MV_Glass':
                nt.links.new(node.outputs['Alpha'], bsdf.inputs['Alpha'])
                m.surface_render_method = 'BLENDED'
            nt.nodes.active = node
        if emi:
            bsdf.inputs['Emission Color'].default_value = (*hex_lin(emi['color']), 1.0)
            bsdf.inputs['Emission Strength'].default_value = emi['intensity'] * 0.9
            if tex:
                nt.links.new(nt.nodes.active.outputs['Color'], bsdf.inputs['Emission Color'])


# ============================================================================================ math helpers
def T(x, y=None, z=None):
    if y is None:
        return Matrix.Translation(Vector(x))
    return Matrix.Translation((x, y, z))


def RX(d):
    return Matrix.Rotation(math.radians(d), 4, 'X')


def RY(d):
    return Matrix.Rotation(math.radians(d), 4, 'Y')


def RZ(d):
    return Matrix.Rotation(math.radians(d), 4, 'Z')


def SC(x, y, z):
    return Matrix.Diagonal((x, y, z, 1.0))


def M(mx):
    if mx is None:
        return Matrix.Identity(4)
    if isinstance(mx, Matrix):
        return mx
    return Matrix.Translation(Vector(mx))


def frame(o, xa, ya):
    """Matrix with origin o and axes xa, ya, xa x ya (columns)."""
    xa, ya = Vector(xa).normalized(), Vector(ya).normalized()
    za = xa.cross(ya).normalized()
    ya = za.cross(xa)
    m = Matrix.Identity(4)
    for i in range(3):
        m[i][0], m[i][1], m[i][2], m[i][3] = xa[i], ya[i], za[i], o[i]
    return m


def look_frame(p0, p1, up=(0, 0, 1)):
    """Frame at p0 whose local +Z points to p1."""
    d = (Vector(p1) - Vector(p0)).normalized()
    upv = Vector(up)
    if abs(d.dot(upv)) > 0.95:
        upv = Vector((0, 1, 0)) if abs(d.y) < 0.9 else Vector((1, 0, 0))
    xa = upv.cross(d).normalized()
    ya = d.cross(xa)
    m = Matrix.Identity(4)
    for i in range(3):
        m[i][0], m[i][1], m[i][2], m[i][3] = xa[i], ya[i], d[i], p0[i]
    return m


def clamp(v, a, b):
    return max(a, min(b, v))


def rrect2d(w, h, r, n=4, cx=0.0, cy=0.0):
    """CCW rounded rectangle (w x h, corner radius r) as 2D points."""
    r = min(r, w / 2 - 1e-4, h / 2 - 1e-4)
    pts = []
    for (ccx, ccy, a0) in ((w / 2 - r, -h / 2 + r, -90), (w / 2 - r, h / 2 - r, 0), (-w / 2 + r, h / 2 - r, 90),
                           (-w / 2 + r, -h / 2 + r, 180)):
        for i in range(n + 1):
            a = math.radians(a0 + 90 * i / n)
            pts.append((cx + ccx + r * math.cos(a), cy + ccy + r * math.sin(a)))
    return pts


def ellipse2d(rx, ry, n=16, cx=0.0, cy=0.0, a0=0.0):
    return [(cx + rx * math.cos(a0 + 2 * math.pi * i / n), cy + ry * math.sin(a0 + 2 * math.pi * i / n)) for i in range(n)]


# ============================================================================================ mesh builder
class MB:
    """Accumulates geometry for one object. Every primitive takes a material name and returns its faces."""

    def __init__(self):
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new('UVMap')

    # ---- bookkeeping
    def _post(self, verts, mat, recalc=True):
        faces = list({f for v in verts for f in v.link_faces})
        faces.sort(key=lambda f: f.index)
        mi = MI[mat]
        for f in faces:
            f.material_index = mi
        if recalc and faces:
            bmesh.ops.recalc_face_normals(self.bm, faces=faces)
        for f in faces:
            f.normal_update()
        uv_box(faces, self.uv)
        return faces

    def set_mat(self, faces, mat):
        for f in faces:
            f.material_index = MI[mat]

    # ---- primitives
    def box(self, mat, size, mx=None):
        m = M(mx) @ SC(*size)
        r = bmesh.ops.create_cube(self.bm, size=1.0, matrix=m)
        return self._post(r['verts'], mat)

    def cyl(self, mat, r, depth, mx=None, segs=16, r2=None, axis='Z', caps=True):
        ax = {'Z': Matrix.Identity(4), 'X': RY(90), 'Y': RX(-90)}[axis]
        res = bmesh.ops.create_cone(self.bm, cap_ends=caps, cap_tris=False, segments=segs, radius1=r,
                                    radius2=r if r2 is None else r2, depth=depth, matrix=M(mx) @ ax)
        return self._post(res['verts'], mat, recalc=caps)

    def cyl_between(self, mat, p0, p1, r, segs=12, r2=None):
        p0, p1 = Vector(p0), Vector(p1)
        L = (p1 - p0).length
        m = look_frame(p0, p1) @ T(0, 0, L / 2)
        return self.cyl(mat, r, L, m, segs, r2)

    def sphere(self, mat, r, mx=None, segs=12, rings=8):
        res = bmesh.ops.create_uvsphere(self.bm, u_segments=segs, v_segments=rings, radius=r, matrix=M(mx))
        return self._post(res['verts'], mat)

    def lathe(self, mat, prof, mx=None, segs=16, axis='Z', phase=0.0, mats=None):
        """Revolve profile [(r, h)] around local Z. Normals: (dh, -dr) side of the walking direction.
        r == 0 at an end collapses to a pole. mats: optional material per profile segment."""
        ax = {'Z': Matrix.Identity(4), 'X': RY(90), 'Y': RX(-90), '-Y': RX(90)}[axis]
        m = M(mx) @ ax
        bm = self.bm
        rings = []
        for (r, h) in prof:
            if r < 1e-6:
                rings.append([bm.verts.new(m @ Vector((0, 0, h)))])
            else:
                ring = []
                for i in range(segs):
                    a = phase + 2 * math.pi * i / segs
                    ring.append(bm.verts.new(m @ Vector((r * math.cos(a), r * math.sin(a), h))))
                rings.append(ring)
        allv = [v for ring in rings for v in ring]
        new_faces = []
        for k in range(len(rings) - 1):
            a, b = rings[k], rings[k + 1]
            for i in range(segs):
                j = (i + 1) % segs
                if len(a) == 1 and len(b) == 1:
                    continue
                if len(a) == 1:
                    f = bm.faces.new((a[0], b[j], b[i]))
                elif len(b) == 1:
                    f = bm.faces.new((a[i], a[j], b[0]))
                else:
                    f = bm.faces.new((a[i], a[j], b[j], b[i]))
                f.material_index = MI[mats[k] if mats else mat]
                new_faces.append(f)
        for f in new_faces:
            f.normal_update()
        uv_box(new_faces, self.uv)
        return new_faces

    def prism(self, mat, poly, z0, z1, mx=None, top_mat=None, bot_mat=None):
        """2D polygon (local XY) extruded along local Z from z0 to z1. Returns dict(faces, top, bottom)."""
        m = M(mx)
        bm = self.bm
        b = [bm.verts.new(m @ Vector((p[0], p[1], z0))) for p in poly]
        t = [bm.verts.new(m @ Vector((p[0], p[1], z1))) for p in poly]
        n = len(poly)
        area = sum(poly[i][0] * poly[(i + 1) % n][1] - poly[(i + 1) % n][0] * poly[i][1] for i in range(n))
        flip = (area < 0) != (z1 < z0)
        faces = []
        top = bm.faces.new(t if not flip else t[::-1])
        bot = bm.faces.new(b[::-1] if not flip else b)
        faces += [top, bot]
        for i in range(n):
            j = (i + 1) % n
            q = (b[i], b[j], t[j], t[i]) if not flip else (b[j], b[i], t[i], t[j])
            faces.append(bm.faces.new(q))
        for f in faces:
            f.material_index = MI[mat]
            f.normal_update()
        if top_mat:
            top.material_index = MI[top_mat]
        if bot_mat:
            bot.material_index = MI[bot_mat]
        uv_box(faces, self.uv)
        return {'faces': faces, 'top': top, 'bottom': bot}

    def ring_prism(self, mat, outer, inner, z0, z1, mx=None):
        """Polygon with a hole (outer/inner same vertex count, both CCW) extruded along local Z."""
        m = M(mx)
        bm = self.bm
        n = len(outer)
        ob = [bm.verts.new(m @ Vector((p[0], p[1], z0))) for p in outer]
        ot = [bm.verts.new(m @ Vector((p[0], p[1], z1))) for p in outer]
        ib = [bm.verts.new(m @ Vector((p[0], p[1], z0))) for p in inner]
        it = [bm.verts.new(m @ Vector((p[0], p[1], z1))) for p in inner]
        faces = []
        for i in range(n):
            j = (i + 1) % n
            faces.append(bm.faces.new((ot[i], ot[j], it[j], it[i])))   # top ring
            faces.append(bm.faces.new((ob[j], ob[i], ib[i], ib[j])))   # bottom ring
            faces.append(bm.faces.new((ob[i], ob[j], ot[j], ot[i])))   # outer wall
            faces.append(bm.faces.new((ib[j], ib[i], it[i], it[j])))   # inner wall
        for f in faces:
            f.material_index = MI[mat]
        bmesh.ops.recalc_face_normals(bm, faces=faces)
        for f in faces:
            f.normal_update()
        uv_box(faces, self.uv)
        return faces

    def loft(self, mat, rings, mx=None, cap0=True, cap1=True, closed=True):
        """Connect rings of 3D local points (equal counts). closed: rings are loops."""
        m = M(mx)
        bm = self.bm
        vr = [[bm.verts.new(m @ Vector(p)) for p in ring] for ring in rings]
        n = len(rings[0])
        faces = []
        for k in range(len(vr) - 1):
            a, b = vr[k], vr[k + 1]
            for i in range(n if closed else n - 1):
                j = (i + 1) % n
                faces.append(bm.faces.new((a[i], a[j], b[j], b[i])))
        if cap0:
            faces.append(bm.faces.new(vr[0][::-1]))
        if cap1:
            faces.append(bm.faces.new(vr[-1]))
        for f in faces:
            f.material_index = MI[mat]
        if cap0 and cap1:
            bmesh.ops.recalc_face_normals(bm, faces=faces)
        for f in faces:
            f.normal_update()
        uv_box(faces, self.uv)
        return faces

    def tube(self, mat, pts, r, mx=None, segs=8, caps=True):
        """Sweep a circle along a polyline (parallel-transport frames, mitred joints)."""
        m = M(mx)
        pts = [Vector(p) for p in pts]
        n = len(pts)
        tans = []
        for i in range(n):
            if i == 0:
                t = pts[1] - pts[0]
            elif i == n - 1:
                t = pts[-1] - pts[-2]
            else:
                t = (pts[i] - pts[i - 1]).normalized() + (pts[i + 1] - pts[i]).normalized()
            tans.append(t.normalized())
        ref = Vector((0, 0, 1)) if abs(tans[0].z) < 0.9 else Vector((1, 0, 0))
        u = tans[0].cross(ref).normalized()
        rings = []
        for i in range(n):
            if i > 0:
                q = tans[i - 1].rotation_difference(tans[i])
                u = (q @ u).normalized()
            v = tans[i].cross(u).normalized()
            ring = []
            for k in range(segs):
                a = 2 * math.pi * k / segs
                ring.append(tuple(pts[i] + (u * math.cos(a) + v * math.sin(a)) * r))
            rings.append(ring)
        return self.loft(mat, rings, m, cap0=caps, cap1=caps)

    def torus(self, mat, R, r, mx=None, segs=24, tsegs=8):
        m = M(mx)
        rings = []
        for i in range(segs + 1):
            a = 2 * math.pi * i / segs
            c, s = math.cos(a), math.sin(a)
            ring = []
            for j in range(tsegs):
                b = 2 * math.pi * j / tsegs
                rr = R + r * math.cos(b)
                ring.append((rr * c, rr * s, r * math.sin(b)))
            rings.append(ring)
        faces = self.loft(mat, rings[:-1] + [rings[0]], m, cap0=False, cap1=False)
        bmesh.ops.remove_doubles(self.bm, verts=list({v for f in faces for v in f.verts}), dist=1e-6)
        faces = [f for f in faces if f.is_valid]
        bmesh.ops.recalc_face_normals(self.bm, faces=faces)
        return faces

    def face(self, mat, pts, normal_hint, uvs=None):
        bm = self.bm
        vs = [bm.verts.new(Vector(p)) for p in pts]
        f = bm.faces.new(vs)
        f.normal_update()
        if f.normal.dot(Vector(normal_hint)) < 0:
            f.normal_flip()
            if uvs is not None:
                pass
        f.material_index = MI[mat]
        if uvs is not None:
            for loop in f.loops:
                idx = vs.index(loop.vert)
                loop[self.uv].uv = uvs[idx]
        else:
            uv_box([f], self.uv)
        return f

    def mirror_x(self, faces):
        """Duplicate faces mirrored across X = 0 (normals fixed)."""
        geom = list({v for f in faces for v in f.verts}) + list({e for f in faces for e in f.edges}) + list(faces)
        res = bmesh.ops.duplicate(self.bm, geom=geom)
        nv = [g for g in res['geom'] if isinstance(g, bmesh.types.BMVert)]
        nf = [g for g in res['geom'] if isinstance(g, bmesh.types.BMFace)]
        for v in nv:
            v.co.x = -v.co.x
        bmesh.ops.reverse_faces(self.bm, faces=nf)
        for f in nf:
            f.normal_update()
        return nf


def uv_box(faces, uvl, scale=0.5):
    for f in faces:
        n = f.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        for loop in f.loops:
            co = loop.vert.co
            if ax == 0:
                loop[uvl].uv = (co.y * scale, co.z * scale)
            elif ax == 1:
                loop[uvl].uv = (co.x * scale, co.z * scale)
            else:
                loop[uvl].uv = (co.x * scale, co.y * scale)


def uv_planar(face, uvl, origin, uax, vax, rect=(0, 0, 1, 1)):
    """Map a face by projection: u = (p-origin).uax (0..1 across uax length), into rect (u0, v0, u1, v1)."""
    o, ua, va = Vector(origin), Vector(uax), Vector(vax)
    lu, lv = ua.length_squared, va.length_squared
    for loop in face.loops:
        d = loop.vert.co - o
        u, v = d.dot(ua) / lu, d.dot(va) / lv
        loop[uvl].uv = (rect[0] + (rect[2] - rect[0]) * u, rect[1] + (rect[3] - rect[1]) * v)


# ============================================================================================ objects
def scene():
    return bpy.context.scene


def mb_to_object(name, mb, loc=(0, 0, 0)):
    me = bpy.data.meshes.new(name)
    mb.bm.normal_update()
    mb.bm.to_mesh(me)
    mb.bm.free()
    for n in MAT_NAMES:
        me.materials.append(bpy.data.materials[n])
    ob = bpy.data.objects.new(name, me)
    scene().collection.objects.link(ob)
    ob.location = loc
    return ob


def apply_modifiers(ob):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = ob.evaluated_get(dg)
    me = bpy.data.meshes.new_from_object(ev, preserve_all_data_layers=True, depsgraph=dg)
    old = ob.data
    ob.modifiers.clear()
    ob.data = me
    bpy.data.meshes.remove(old)
    return ob


def add_bevel(ob, width, segs=1, angle=40.0):
    md = ob.modifiers.new('bevel', 'BEVEL')
    md.width = width
    md.segments = segs
    md.limit_method = 'ANGLE'
    md.angle_limit = math.radians(angle)
    md.use_clamp_overlap = True
    md.harden_normals = False
    return md


def add_boolean(ob, cutter, op='DIFFERENCE'):
    md = ob.modifiers.new('bool_' + cutter.name, 'BOOLEAN')
    md.operation = op
    md.object = cutter
    md.solver = 'EXACT'
    md.material_mode = 'INDEX'
    cutter.hide_render = True
    cutter.hide_viewport = True
    return md


def delete_object(ob):
    me = ob.data
    bpy.data.objects.remove(ob, do_unlink=True)
    if me is not None and me.users == 0:
        bpy.data.meshes.remove(me)


def part(name, build, bevel=None, cutters=None):
    """Build an MB via build(mb) -> object; optional bevel (width, segs, angle) then boolean cutters
    (list of build functions); modifiers applied. Returns the object."""
    mb = MB()
    build(mb)
    ob = mb_to_object(name, mb)
    if bevel:
        add_bevel(ob, *bevel)
    cut_obs = []
    for i, cb in enumerate(cutters or []):
        cmb = MB()
        cb(cmb)
        co = mb_to_object('%s_cut%d' % (name, i), cmb)
        cut_obs.append(co)
        add_boolean(ob, co)
    if bevel or cut_obs:
        apply_modifiers(ob)
    for co in cut_obs:
        delete_object(co)
    return ob


def join_objects(name, objs):
    bm = bmesh.new()
    for o in objs:
        me = o.data.copy()
        me.transform(o.matrix_world)
        bm.from_mesh(me)
        bpy.data.meshes.remove(me)
    for o in objs:
        delete_object(o)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for n in MAT_NAMES:
        me.materials.append(bpy.data.materials[n])
    ob = bpy.data.objects.new(name, me)
    scene().collection.objects.link(ob)
    return ob


def finalize_mesh(ob, smooth_angle=35.0):
    """Triangulate (Blender's beauty fill handles concave n-gons from booleans), drop degenerate faces,
    smooth-by-angle normals, prune unused material slots."""
    me = ob.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.dissolve_degenerate(bm, dist=1e-5, edges=bm.edges[:])
    bmesh.ops.triangulate(bm, faces=bm.faces[:], quad_method='BEAUTY', ngon_method='BEAUTY')
    bad = [f for f in bm.faces if f.calc_area() < 1e-9]
    if bad:
        bmesh.ops.delete(bm, geom=bad, context='FACES')
    loose = [v for v in bm.verts if not v.link_faces]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context='VERTS')
    bm.to_mesh(me)
    bm.free()
    me.shade_smooth()
    me.set_sharp_from_angle(angle=math.radians(smooth_angle))
    # prune unused material slots
    used = sorted({p.material_index for p in me.polygons})
    remap = {old: new for new, old in enumerate(used)}
    mats = [me.materials[i] for i in used]
    idx = [remap[p.material_index] for p in me.polygons]
    me.materials.clear()
    for m in mats:
        me.materials.append(m)
    for p, i in zip(me.polygons, idx):
        p.material_index = i
    me.update()
    return ob


def tri_count(ob):
    me = ob.data
    me.calc_loop_triangles()
    return len(me.loop_triangles)


def empty(name, loc, parent, size=0.12):
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = 'PLAIN_AXES'
    e.empty_display_size = size
    e.location = loc
    scene().collection.objects.link(e)
    e.parent = parent
    return e


# ============================================================================================ shared parts
def tire_profile(W, R, rb=0.245, tread_base=None):
    """Tyre cross-section (r, x) from the inner bead up, across the tread, down to the outer bead
    (lathe normals face out)."""
    tb = R - 0.012 if tread_base is None else tread_base
    hw = W / 2
    return [
        (rb, -hw + 0.015), (rb + 0.025, -hw - 0.004), (rb + 0.085, -hw - 0.010), (R - 0.06, -hw - 0.006),
        (R - 0.025, -hw + 0.004), (tb, -hw + 0.022), (tb, hw - 0.022), (R - 0.025, hw - 0.004),
        (R - 0.06, hw + 0.006), (rb + 0.085, hw + 0.010), (rb + 0.025, hw + 0.004), (rb, hw - 0.015)]


def tire(mb, W, R, x0=0.0, segs=32, blocks=30, rb=0.245, block_h=0.016):
    """Tyre around the X axis centred at x = x0, with two staggered rows of tread blocks."""
    tb = R - block_h + 0.004
    prof = [(r, x + x0) for (r, x) in tire_profile(W, R, rb, tread_base=tb)]
    mb.lathe('MV_Tire', prof, None, segs=segs, axis='X', phase=math.pi / segs)
    hw = W / 2 - 0.022
    bw = hw - 0.012
    rc = tb + block_h / 2 - 0.003
    blen = 2 * math.pi * R / blocks * 0.72
    for row, (xc, off) in enumerate(((x0 + 0.006 + bw / 2, 0.0), (x0 - 0.006 - bw / 2, 0.5))):
        for i in range(blocks):
            a = 360.0 * (i + off) / blocks
            mb.box('MV_Tire', (bw, block_h + 0.003, blen), RX(a) @ T(xc, rc, 0))


def hand_holes(mb, mat, n, r_c, x_face, slope, rx=0.028, ry=0.016, phase=0.0):
    """Dark oval 'hand holes' on a conical rim disc (+X side) at radius r_c, height x_face, dh/dr = -slope."""
    for i in range(n):
        a = phase + 360.0 * i / n
        poly = ellipse2d(rx, ry, 12)
        m = RX(a) @ T(x_face, r_c, 0) @ RZ(math.degrees(math.atan(slope))) @ RY(90)
        mb.prism(mat, poly, -0.003, 0.0015, m)


def lug_nuts(mb, n, pcd, x_base, length, ax_sign, r=0.017, phase=0.0, cap=True):
    for i in range(n):
        a = math.radians(phase + 360.0 * i / n)
        y, z = pcd * math.cos(a), pcd * math.sin(a)
        mx = T(x_base + ax_sign * length / 2, y, z)
        mb.cyl('MV_Chrome', r, length, mx, segs=6, axis='X')
        if cap:
            mb.cyl('MV_Chrome', r * 0.85, 0.012, T(x_base + ax_sign * (length + 0.006), y, z), segs=6, axis='X', r2=r * 0.4)


# ============================================================================================ TRUCK
RAKE = 5.0
TAN_R = math.tan(math.radians(RAKE))
CAB_Z0, CAB_TOP, CAB_BACK, CAB_HW = 0.72, 2.45, 2.10, 1.0
CORNER_R, BACK_R = 0.18, 0.06
FA_Y, RA_Y, WR = 0.95, 5.35, 0.45
FRONT_TRACK, REAR_TRACK = 0.85, 0.83
BOX_Y0, BOX_Y1, BOX_Z0, BOX_Z1, BOX_HW = 2.36, 7.52, 1.10, 3.43, 1.16
# livery canvas (tools/opening_vehicle_textures.py): emblem disc (cx, cy, r) on a 1134x512 canvas
LIV_W, LIV_H, EMBLEM = 1134.0, 512.0, (150.0, 238.0, 96.0)
PLATE_CELLS = {'Truck': 0, 'Sedan': 1, 'Hatchback': 2, 'Taxi': 3, 'Van': 4, 'SUV': 5}


def yf(z):
    """Cab front face Y at height z (5 deg rake)."""
    return 0.16 + (z - CAB_Z0) * TAN_R


def FF(z, x=0.0, out=0.0):
    """Face frame on the cab front at height z: local x = X, local -y = outward normal, local z = up the face."""
    c, s = math.cos(math.radians(RAKE)), math.sin(math.radians(RAKE))
    return T(x, yf(z) - out * c, z + out * s) @ RX(-RAKE)


def FO(z, x=0.0):
    """Face-out frame: polygons drawn in (x, h along the face), extrusion coordinate e = outward."""
    return FF(z, x) @ RX(90)


def plate_rect(cell):
    col, row = cell % 2, cell // 2
    u0, u1 = col * 0.5 + 0.002, col * 0.5 + 0.498
    v1 = 1.0 - row * 0.25 - 0.002
    return (u0, v1 - 0.246, u1, v1)


def cab_section(z, inset):
    hw = CAB_HW - inset
    y0, y1 = yf(z) + inset, CAB_BACK - inset
    rf, rb = max(CORNER_R - inset, 0.035), max(BACK_R - inset, 0.012)
    pts = []

    def arc(cx, cy, r, a0, a1, n):
        for i in range(n + 1):
            a = math.radians(a0 + (a1 - a0) * i / n)
            pts.append((cx + r * math.cos(a), cy + r * math.sin(a), z))
    arc(hw - rf, y0 + rf, rf, -90, 0, 8)
    arc(hw - rb, y1 - rb, rb, 0, 90, 3)
    arc(-hw + rb, y1 - rb, rb, 90, 180, 3)
    arc(-hw + rf, y0 + rf, rf, 180, 270, 8)
    return pts


def truck_cab():
    """Cab shell: lofted solid -> pockets (arches, step wells, grille/headlamp recesses, door seams, handle pockets)
    -> solidify (hollow, inner walls MV_Interior) -> window holes."""
    levels = [(0.72, 0.0), (0.98, 0.0), (1.30, 0.0), (1.38, 0.0), (2.33, 0.0)]
    for th in (22.5, 45.0, 67.5, 90.0):
        a = math.radians(th)
        levels.append((2.33 + 0.12 * math.sin(a), 0.12 * (1 - math.cos(a))))
    mb = MB()
    rings = [cab_section(z, ins) for z, ins in levels]
    faces = mb.loft('MV_Paint', rings)
    for f in faces:
        c = f.calc_center_median()
        if 1.30 < c.z < 1.38 and f.normal.y < 0.5 and len(f.verts) == 4:
            f.material_index = MI['MV_Accent']
    cab = mb_to_object('Cab', mb)

    cutters = []

    def cutter(name, build):
        cmb = MB()
        build(cmb)
        co = mb_to_object(name, cmb)
        cutters.append(co)
        return co

    # pockets (before hollowing)
    cutter('arch', lambda m: m.cyl('MV_Black', 0.50, 2.6, T(0, FA_Y, 0.45), segs=32, axis='X'))
    for s in (1, -1):
        cutter('stepwell%d' % s, lambda m, s=s: m.box('MV_Black', (0.30, 0.57, 0.40), T(s * 0.95, 1.735, 0.77)))
        cutter('handle%d' % s, lambda m, s=s: m.box('MV_Black', (0.06, 0.17, 0.065), T(s * 1.012, 1.78, 1.215)))
        # door seams: 8 mm wide, 6 mm deep grooves
        def seams(m, s=s):
            fy0, fy1 = yf(1.0) + 0.26, yf(2.30) + 0.26
            L = math.hypot(fy1 - fy0, 1.30)
            m.box('MV_Black', (0.024, 0.008, L + 0.008),
                  T(s * 1.0, (fy0 + fy1) / 2, 1.65) @ RX(-RAKE))
        cutter('seamF%d' % s, seams)
        cutter('seamR%d' % s, lambda m, s=s: m.box('MV_Black', (0.024, 0.008, 1.308), T(s * 1.0, 1.93, 1.65)))
        cutter('seamB%d' % s, lambda m, s=s: m.box('MV_Black', (0.024, 1.93 - yf(1.0) - 0.26, 0.008),
                                                   T(s * 1.0, (1.93 + yf(1.0) + 0.26) / 2, 1.0)))
        cutter('seamT%d' % s, lambda m, s=s: m.box('MV_Black', (0.024, 1.93 - yf(2.30) - 0.26, 0.008),
                                                   T(s * 1.0, (1.93 + yf(2.30) + 0.26) / 2, 2.30)))
        cutter('hlrec%d' % s, lambda m, s=s: m.prism('MV_Black', rrect2d(0.25, 0.19, 0.03), -0.05, 0.05,
                                                    FO(0.87, s * 0.685)))
    cutter('grillerec', lambda m: m.prism('MV_Black', rrect2d(0.92, 0.30, 0.035), -0.05, 0.05, FO(0.945)))
    for s in (1, -1):
        for z in (1.105, 1.16, 1.215):
            cutter('slot%d_%d' % (s, int(z * 1000)),
                   lambda m, s=s, z=z: m.prism('MV_Black', rrect2d(0.24, 0.022, 0.009, 2), -0.012, 0.05,
                                               FO(z, s * 0.585)))
    for co in cutters:
        add_boolean(cab, co)
    sol = cab.modifiers.new('solid', 'SOLIDIFY')
    sol.thickness = 0.04
    sol.offset = -1.0
    sol.use_even_offset = True
    sol.material_offset = 99
    sol.material_offset_rim = 99
    sol.use_rim = True
    # windows (through the hollow shell)
    win = []

    def wcut(name, build):
        cmb = MB()
        build(cmb)
        co = mb_to_object(name, cmb)
        win.append(co)
        return co
    wcut('windshield', lambda m: m.prism('MV_Black', rrect2d(1.80, 0.80, 0.07, 6), -0.15, 0.05, FO(1.85)))
    for s in (1, -1):
        wcut('doorwin%d' % s, lambda m, s=s: m.prism('MV_Black', door_window_poly(), -0.12, 0.12,
                                                    frame((s * 1.0, 0, 0), (0, 1, 0), (0, 0, 1))))
    wcut('safetywin', lambda m: m.prism('MV_Black', rrect2d(0.30, 0.20, 0.04, 4, cx=0.80, cy=1.16), -0.12, 0.12,
                                        frame((-1.0, 0, 0), (0, 1, 0), (0, 0, 1))))
    for co in win:
        add_boolean(cab, co)
    apply_modifiers(cab)
    for co in cutters + win:
        delete_object(co)
    return cab


def door_window_poly(n=4):
    """Door glass outline in (Y, Z): raked front edge, rounded corners."""
    z0, z1, yr = 1.47, 2.22, 1.84
    yb, yt = yf(z0) + 0.36, yf(z1) + 0.36
    r = 0.05
    pts = []
    corners = [((yr - r, z0 + r), -90), ((yr - r, z1 - r), 0), ((yt + r, z1 - r), 90), ((yb + r, z0 + r), 180)]
    for (cx, cy), a0 in corners:
        for i in range(n + 1):
            a = math.radians(a0 + 90 * i / n)
            pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def truck_cab_details(mb):
    """Glass, gaskets, wipers, visor, badge, grille slats, emblem decals, handles, repeaters."""
    uvl = mb.uv
    # --- windshield glass + gasket
    fo = FO(1.85)
    pts = [fo @ Vector((x, h, -0.022)) for (x, h) in rrect2d(1.80, 0.80, 0.07, 6)]
    uvs = [((x + 0.9) / 1.8, (h + 0.4) / 0.8) for (x, h) in rrect2d(1.80, 0.80, 0.07, 6)]
    mb.face('MV_Glass', pts, fo.to_3x3() @ Vector((0, 0, 1)), uvs)
    # gasket ring
    mb.ring_prism('MV_Black', rrect2d(1.89, 0.89, 0.11, 6), rrect2d(1.77, 0.77, 0.055, 6), -0.018, 0.009, FO(1.85))
    # --- door glass + gasket (both sides); safety window on the right (kerb) door
    poly = door_window_poly()
    for s in (1, -1):
        fr = frame((s * 1.0, 0, 0), (0, 1, 0), (0, 0, 1))
        pts = [(s * (1.0 - 0.022), y, z) for (y, z) in poly]
        ys = [p[0] for p in poly]
        zs = [p[1] for p in poly]
        y0, y1, z0, z1 = min(ys), max(ys), min(zs), max(zs)
        uvs = [(((y - y0) if s > 0 else (y1 - y)) / (y1 - y0), (z - z0) / (z1 - z0)) for (y, z) in poly]
        mb.face('MV_Glass', pts, (s, 0, 0), uvs)
        inner_poly = offset_poly(poly, -0.025)
        outer_poly = offset_poly(poly, 0.035)
        mb.ring_prism('MV_Black', outer_poly, inner_poly, s * -0.018, s * 0.008, fr)
    # safety window (right door, low front)
    fr = frame((-1.0, 0, 0), (0, 1, 0), (0, 0, 1))
    sw = rrect2d(0.30, 0.20, 0.04, 4, cx=0.80, cy=1.16)
    mb.face('MV_Glass', [(-0.978, y, z) for (y, z) in sw], (-1, 0, 0),
            [((0.95 - y) / 0.30, (z - 1.06) / 0.20) for (y, z) in sw])
    mb.ring_prism('MV_Black', rrect2d(0.36, 0.26, 0.07, 4, cx=0.80, cy=1.16), rrect2d(0.27, 0.17, 0.025, 4, cx=0.80, cy=1.16),
                  0.018, -0.008, fr)
    # --- wipers (parked along the bottom of the glass)
    for xp, L in ((0.62, 0.62), (-0.20, 0.60)):
        f0 = FF(1.85)
        mb.cyl('MV_Black', 0.022, 0.03, f0 @ T(xp, -0.012, -0.43) @ RX(90), segs=10)
        mb.box('MV_Black', (0.03, 0.014, 0.075), f0 @ T(xp - 0.01, -0.02, -0.395) @ RY(-20))
        mb.box('MV_Black', (L, 0.012, 0.018), f0 @ T(xp - L / 2, -0.012, -0.36) @ RY(-2.5))
        mb.box('MV_Black', (L * 0.95, 0.008, 0.012), f0 @ T(xp - L * 0.52, -0.003, -0.352) @ RY(-2.5))
    # --- sun visor (wedge above the glass) + end plates
    up = Vector((0, math.sin(math.radians(RAKE)), math.cos(math.radians(RAKE))))
    nrm = Vector((0, -math.cos(math.radians(RAKE)), math.sin(math.radians(RAKE))))
    base = Vector((0, yf(2.27), 2.27))
    vf = frame(base, nrm, up)
    prof = [(-0.02, 0.035), (0.175, 0.0), (0.18, 0.03), (0.0, 0.115), (-0.02, 0.115)]
    mb.prism('MV_Black', prof, -0.95, 0.95, vf)
    # --- badge (TENSEI): rounded plate, textured front
    bp = mb.prism('MV_Chrome', rrect2d(0.57, 0.112, 0.026, 4), 0.0, 0.012, FO(1.215))
    bp['top'].material_index = MI['MV_Badge']
    fo = FO(1.215)
    uv_planar(bp['top'], uvl, fo @ Vector((-0.285, -0.056, 0.012)), fo.to_3x3() @ Vector((0.57, 0, 0)),
              fo.to_3x3() @ Vector((0, 0.112, 0)))
    # --- grille slats inside the recess
    for h in (-0.10, -0.05, 0.0, 0.05, 0.10):
        mb.box('MV_Black', (0.89, 0.034, 0.022), FF(0.945) @ T(0, 0.024, h) @ RX(-18))
    # --- DF emblem badges on both doors (sample the livery emblem disc)
    cx, cy, r = EMBLEM
    for s in (1, -1):
        n = 24
        R = 0.105
        poly = [(R * math.cos(2 * math.pi * i / n), R * math.sin(2 * math.pi * i / n)) for i in range(n)]
        fr = frame((s * 1.0, 1.40, 1.13), (0, s, 0), (0, 0, 1))   # local x = viewer's right
        d = mb.prism('MV_Box', poly, 0.0, 0.004, fr)
        for f in d['faces']:
            for loop in f.loops:
                p = fr.inverted() @ loop.vert.co
                a = math.atan2(p.y, p.x)
                k = min(1.0, math.hypot(p.x, p.y) / R) * 0.97
                if f is not d['top']:
                    k = 0.99
                loop[uvl].uv = ((cx + r * k * math.cos(a)) / LIV_W, 1.0 - (cy - r * k * math.sin(a)) / LIV_H)
    # --- smoked rain visors above the door glass
    for s in (1, -1):
        y0, y1 = yf(2.25) + 0.33, 1.87
        mb.box('MV_Black', (0.012, y1 - y0, 0.05), T(s * 1.022, (y0 + y1) / 2, 2.262) @ RY(s * 35))
    # --- side repeaters (amber) just behind the front corners
    for s in (1, -1):
        y = yf(1.10) + 0.20
        mb.box('MV_Chrome', (0.012, 0.085, 0.045), T(s * 1.004, y, 1.10))
        mb.box('MV_Signal', (0.016, 0.07, 0.032), T(s * 1.008, y, 1.10))


def offset_poly(poly, d):
    """Offset a CCW polygon outward by d (miter)."""
    n = len(poly)
    out = []
    for i in range(n):
        p0, p1, p2 = Vector(poly[i - 1]), Vector(poly[i]), Vector(poly[(i + 1) % n])
        e0 = (p1 - p0).normalized()
        e1 = (p2 - p1).normalized()
        n0 = Vector((e0.y, -e0.x))
        n1 = Vector((e1.y, -e1.x))
        nm = (n0 + n1)
        if nm.length < 1e-6:
            nm = n0
        nm.normalize()
        c = max(0.3, nm.dot(n0))
        q = p1 + nm * (d / c)
        out.append((q.x, q.y))
    return out


def truck_chrome_parts(mb):
    """Grille surround, headlamp bezels and lamps, signal wraps, horns, handles, fuel cap."""
    mb.ring_prism('MV_Chrome', rrect2d(1.00, 0.37, 0.06, 4), rrect2d(0.90, 0.28, 0.03, 4), -0.006, 0.022, FO(0.945))
    for s in (1, -1):
        fo = FO(0.87, s * 0.685)
        mb.ring_prism('MV_Black', rrect2d(0.29, 0.23, 0.045, 4), rrect2d(0.235, 0.175, 0.025, 4), -0.004, 0.012, fo)
        for k, dx in enumerate((-0.058, 0.058)):
            # chrome reflector cup + domed lens (inner = high beam, outer = low beam)
            m = fo @ T(dx * s, 0, 0)
            mb.lathe('MV_Chrome', [(0.050, -0.05), (0.052, -0.012), (0.047, -0.008), (0.042, -0.024)], m, segs=20)
            mb.lathe('MV_HeadLamp', [(0.043, -0.026), (0.040, -0.016), (0.030, -0.010), (0.015, -0.007), (0.0, -0.0065)],
                     m, segs=20)
        # LED position strip under the lenses
        mb.box('MV_HeadLamp', (0.20, 0.012, 0.012), FF(0.87, s * 0.685) @ T(0, 0.012, -0.072))
        # amber turn signal wrapping the corner
        rings = []
        for k in range(9):
            a = math.radians(-86 + 78 * k / 8)
            ring = []
            for (rr, z) in ((0.168, 0.785), (0.188, 0.785), (0.188, 0.955), (0.168, 0.955)):
                cxx, cyy = CAB_HW - CORNER_R, yf(z) + CORNER_R
                x = cxx + rr * math.cos(a)
                ring.append((s * x, cyy + rr * math.sin(a), z))
            rings.append(ring)
        f = mb.loft('MV_Signal', rings)
    # --- air horns on the roof (twin trumpets, bells forward)
    for x, L in ((0.12, 0.55), (-0.12, 0.46)):
        back = Vector((x, 0.88, 2.56))
        m = frame(back, (1, 0, 0), (0, 0, 1)) @ RX(90)  # local z -> -Y (forward)
        m = T(back) @ RX(90)
        prof = [(0.0, -0.065), (0.038, -0.065), (0.040, -0.035), (0.038, 0.0), (0.021, 0.014)]
        for i in range(1, 13):
            t = i / 12
            prof.append((0.019 + 0.069 * t ** 3.0, 0.014 + (L - 0.014) * t))
        prof += [(0.093, L - 0.002), (0.093, L + 0.007), (0.080, L + 0.007), (0.060, L - 0.04), (0.034, L - 0.13),
                 (0.0, L - 0.17)]
        mats = ['MV_Chrome'] * (len(prof) - 1)
        mats[-2] = mats[-1] = 'MV_Black'
        mb.lathe('MV_Chrome', prof, m, segs=20, mats=mats)
        # mount brackets
        for y in (0.84, 0.62):
            mb.box('MV_Chrome', (0.032, 0.035, 0.10), T(x, y, 2.50))
    mb.box('MV_Black', (0.42, 0.32, 0.016), T(0, 0.72, 2.455))
    mb.box('MV_Chrome', (0.30, 0.04, 0.025), T(0, 0.84, 2.47))
    mb.tube('MV_Black', [(0.12, 0.945, 2.56), (0.04, 1.00, 2.48), (0.0, 1.10, 2.47)], 0.012, segs=6)
    mb.tube('MV_Black', [(-0.12, 0.945, 2.56), (-0.04, 1.00, 2.48), (0.0, 1.10, 2.47)], 0.012, segs=6)
    # --- door handles (in their pockets)
    for s in (1, -1):
        mb.box('MV_Chrome', (0.024, 0.15, 0.03), T(s * 1.003, 1.78, 1.215))


def truck_bumper(mb):
    """Plan-view C-shaped bumper extruded 0.45-0.72 (chrome)."""
    outer, inner = [], []
    R, hw, yb = 0.24, 1.03, 0.46
    th = 0.17
    for i in range(9):  # +X corner, from side to front
        a = math.radians(0 - 90 * i / 8)
        outer.append((hw - R + R * math.cos(a), R + R * math.sin(a)))
        inner.append((hw - R + (R - th) * math.cos(a), R + (R - th) * math.sin(a)))
    for i in range(9):  # -X corner
        a = math.radians(-90 - 90 * i / 8)
        outer.append((-hw + R + R * math.cos(a), R + R * math.sin(a)))
        inner.append((-hw + R + (R - th) * math.cos(a), R + (R - th) * math.sin(a)))
    poly = [(hw, yb)] + outer + [(-hw, yb), (-hw + th, yb)] + inner[::-1] + [(hw - th, yb)]
    mb.prism('MV_Chrome', poly, 0.45, 0.72)


def truck_bumper_cutters():
    return [
        lambda m: m.box('MV_Black', (0.38, 0.06, 0.19), T(0, -0.012, 0.5875)),     # plate recess (18 mm)
        lambda m: m.prism('MV_Black', rrect2d(0.17, 0.11, 0.04), -0.06, 0.04, T(0.66, 0, 0.585) @ RX(90)),
        lambda m: m.prism('MV_Black', rrect2d(0.17, 0.11, 0.04), -0.06, 0.04, T(-0.66, 0, 0.585) @ RX(90)),
    ]


def truck_front_lower(mb):
    """Plate, fog lamps, step pad, under-spoiler, tow hooks."""
    uvl = mb.uv
    p = mb.prism('MV_Black', rrect2d(0.36, 0.18, 0.012, 3), -0.016, -0.009, T(0, 0, 0.5875) @ RX(90))
    for f in (p['top'], p['bottom']):
        if f.normal.y < -0.5:
            f.material_index = MI['MV_Plate']
            uv_planar(f, uvl, (-0.18, 0, 0.4975), (0.36, 0, 0), (0, 0, 0.18), plate_rect(PLATE_CELLS['Truck']))
    for s in (1, -1):
        m = T(s * 0.66, 0.035, 0.585) @ RX(90)
        mb.lathe('MV_Chrome', [(0.050, -0.03), (0.052, 0.0), (0.046, 0.004), (0.042, -0.01)], m, segs=16)
        mb.lathe('MV_HeadLamp', [(0.043, -0.012), (0.036, -0.003), (0.02, 0.002), (0.0, 0.003)], m, segs=16)
    # centre step pad with ribs
    mb.box('MV_Black', (0.44, 0.16, 0.014), T(0, 0.11, 0.727))
    for k in range(4):
        mb.box('MV_Black', (0.40, 0.012, 0.01), T(0, 0.05 + k * 0.04, 0.737))


def truck_spoiler(mb):
    def ring(z, inset, yfront):
        pts = []
        R = 0.22
        hw = 0.99 - inset
        for i in range(7):
            a = math.radians(0 - 90 * i / 6)
            pts.append((hw - R + R * math.cos(a), yfront + R + R * math.sin(a), z))
        for i in range(7):
            a = math.radians(-90 - 90 * i / 6)
            pts.append((-hw + R + R * math.cos(a), yfront + R + R * math.sin(a), z))
        pts += [(-hw, 0.42, z), (hw, 0.42, z)]
        return pts[::-1]
    mb.loft('MV_Black', [ring(0.33, 0.06, 0.09), ring(0.452, 0.0, 0.035)])
    for s in (1, -1):
        mb.box('MV_Black', (0.10, 0.14, 0.09), T(s * 0.45, 0.15, 0.40))
        mb.torus('MV_Accent', 0.034, 0.011, T(s * 0.45, 0.075, 0.36) @ RY(90), segs=14, tsegs=6)


def truck_sheet_parts(mb):
    """Visor brackets, fender flares, steps, mirrors, grab handles (black, bevelled)."""
    # fender flares
    for s in (1, -1):
        poly = []
        for i in range(13):
            a = math.radians(25 + 125 * i / 12)
            poly.append((FA_Y + 0.575 * math.cos(a), 0.45 + 0.575 * math.sin(a)))
        for i in range(13):
            a = math.radians(150 - 125 * i / 12)
            poly.append((FA_Y + 0.495 * math.cos(a), 0.45 + 0.495 * math.sin(a)))
        fr = frame((0, 0, 0), (0, 1, 0), (0, 0, 1))
        mb.prism('MV_Black', poly, s * 0.985, s * 1.035, fr)
    # steps: upper tread in the step well, lower step on brackets, anti-slip ribs
    for s in (1, -1):
        mb.box('MV_Black', (0.20, 0.53, 0.04), T(s * 0.895, 1.735, 0.72))
        mb.box('MV_Black', (0.23, 0.48, 0.04), T(s * 0.885, 1.735, 0.42))
        for y in (1.52, 1.95):
            mb.box('MV_Black', (0.17, 0.025, 0.30), T(s * 0.89, y, 0.57))
        for k in range(3):
            mb.box('MV_Black', (0.012, 0.44, 0.008), T(s * (0.83 + 0.05 * k), 1.735, 0.744))
            mb.box('MV_Black', (0.012, 0.42, 0.008), T(s * (0.82 + 0.05 * k), 1.735, 0.444))
    # mirror heads (west-coast) + spot mirrors
    for s in (1, -1):
        yaw = 12 if s > 0 else -20
        hm = T(s * 1.31, 0.36, 1.80) @ RZ(yaw)
        mb.box('MV_Black', (0.20, 0.075, 0.42), hm)
        mb.box('MV_Mirror', (0.175, 0.008, 0.39), hm @ T(0, 0.036, 0))
        sm = T(s * 1.30, 0.37, 1.43) @ RZ(yaw + s * 10) @ RX(-100)
        mb.lathe('MV_Black', [(0.0, -0.035), (0.05, -0.03), (0.072, -0.012), (0.074, 0.0), (0.066, 0.004)], sm, segs=16)
        mb.lathe('MV_Mirror', [(0.066, 0.0), (0.05, 0.008), (0.025, 0.013), (0.0, 0.0145)], sm, segs=16)
        # mounting blocks on the cab
        mb.box('MV_Black', (0.03, 0.08, 0.06), T(s * 1.005, yf(2.15) + 0.30, 2.15))
        mb.box('MV_Black', (0.03, 0.08, 0.06), T(s * 1.005, yf(1.45) + 0.30, 1.45))
    # under-view mirror (kerb side, front top corner)
    um = T(-1.02, 0.10, 2.36) @ RX(55)
    mb.box('MV_Black', (0.15, 0.05, 0.12), um)
    mb.box('MV_Mirror', (0.13, 0.006, 0.10), um @ T(0, 0.026, 0))


def truck_tubes(mb):
    """Chrome mirror arms, grab handles, antenna."""
    for s in (1, -1):
        y1, y2 = yf(2.15) + 0.30, yf(1.45) + 0.30
        mb.tube('MV_Chrome', [(s * 1.01, y1, 2.15), (s * 1.12, 0.43, 2.13), (s * 1.25, 0.37, 2.07), (s * 1.30, 0.36, 2.0)],
                0.013, segs=8)
        mb.tube('MV_Chrome', [(s * 1.01, y2, 1.45), (s * 1.15, 0.43, 1.48), (s * 1.27, 0.37, 1.55), (s * 1.30, 0.36, 1.60)],
                0.013, segs=8)
        mb.tube('MV_Chrome', [(s * 1.26, 0.38, 1.535), (s * 1.30, 0.37, 1.47)], 0.009, segs=6)
        # grab handle on the A-pillar side
        yy = lambda z: yf(z) + 0.215
        mb.tube('MV_Black', [(s * 1.0, yy(1.55), 1.55), (s * 1.035, yy(1.58), 1.58), (s * 1.035, yy(2.05), 2.05),
                             (s * 1.0, yy(2.08), 2.08)], 0.014, segs=6)
    mb.tube('MV_Chrome', [(-0.87, 0.46, 2.43), (-0.97, 0.28, 2.48), (-1.02, 0.13, 2.42)], 0.01, segs=6)
    mb.cyl('MV_Black', 0.018, 0.03, T(-0.70, 1.75, 2.465), segs=8)
    mb.cyl_between('MV_Black', (-0.70, 1.75, 2.47), (-0.70, 1.86, 2.84), 0.005, segs=5)


def truck_deflector(mb):
    """White roof fairing from the cab roof up to the box front."""
    ys = [0.95, 1.01, 1.09, 1.19, 1.31, 1.44, 1.57, 1.70, 1.83, 1.95, 2.06, 2.28]
    rings = []
    for y in ys:
        t = clamp((y - 0.95) / (2.08 - 0.95), 0, 1)
        zt = 2.468 + 0.80 * t * t * (3 - 2 * t)
        wb = 0.86
        wt = 0.80 + 0.04 * t
        rc = min(0.10, 0.45 * (zt - 2.45))
        pts = [(wb, y, 2.45)]
        for i in range(5):
            a = math.radians(90 * i / 4)
            pts.append((wt - rc + rc * math.cos(a), y, zt - rc + rc * math.sin(a)))
        for i in range(5):
            a = math.radians(90 + 90 * i / 4)
            pts.append((-wt + rc + rc * math.cos(a), y, zt - rc + rc * math.sin(a)))
        pts.append((-wb, y, 2.45))
        rings.append(pts)
    mb.loft('MV_Paint', rings)


def truck_box_panels(mb):
    """Box body: livery sides (UV 0-1 each, text reads front-to-back on the left, back-to-front on the right)."""
    uvl = mb.uv
    L, H = BOX_Y1 - BOX_Y0, BOX_Z1 - BOX_Z0
    faces = mb.box('MV_Box', (2 * BOX_HW, L, H), T(0, (BOX_Y0 + BOX_Y1) / 2, (BOX_Z0 + BOX_Z1) / 2))
    for f in faces:
        n = f.normal
        if n.x > 0.9:
            uv_planar(f, uvl, (BOX_HW, BOX_Y0, BOX_Z0), (0, L, 0), (0, 0, H))
        elif n.x < -0.9:
            uv_planar(f, uvl, (-BOX_HW, BOX_Y1, BOX_Z0), (0, -L, 0), (0, 0, H))
        else:
            for loop in f.loops:
                loop[uvl].uv = (0.985, 0.985)


def truck_box_frame(mb):
    """Aluminium frame members, roll-up door slats, cross members."""
    for s in (1, -1):
        mb.box('MV_BoxFrame', (0.055, 0.10, 2.41), T(s * 1.1575, 2.335, 2.245))           # front corner posts
        mb.box('MV_BoxFrame', (0.08, 0.14, 2.43), T(s * 1.145, 7.52, 2.235))               # rear corner posts
        mb.box('MV_BoxFrame', (0.035, 5.24, 0.13), T(s * 1.1675, 4.94, 3.385))             # top rails
        mb.box('MV_BoxFrame', (0.045, 5.24, 0.17), T(s * 1.1625, 4.94, 1.155))             # rub rails
        mb.box('MV_BoxFrame', (0.06, 5.24, 0.03), T(s * 1.16, 4.94, 1.085))                # rub rail lip
    mb.box('MV_BoxFrame', (2.37, 0.07, 0.13), T(0, 2.32, 3.385))                           # front header
    mb.box('MV_BoxFrame', (2.37, 0.07, 0.15), T(0, 2.32, 1.145))                           # front bottom rail
    mb.box('MV_BoxFrame', (2.37, 0.10, 0.17), T(0, 7.54, 3.365))                           # rear header
    mb.box('MV_BoxFrame', (2.37, 0.12, 0.16), T(0, 7.54, 1.14))                            # rear sill
    y = 2.50
    while y < 7.45:
        mb.box('MV_BoxFrame', (2.26, 0.055, 0.13), T(0, y, 1.035))                         # floor cross members
        y += 0.35


def truck_door_slats(mb):
    z0, z1, n = 1.24, 3.28, 10
    h = (z1 - z0) / n
    for i in range(n):
        mb.box('MV_BoxFrame', (2.12, 0.03, h - 0.002), T(0, 7.535, z0 + h * (i + 0.5)))


def truck_rear_parts(mb):
    uvl = mb.uv
    for s in (1, -1):
        mb.box('MV_Black', (0.04, 0.04, 2.04), T(s * 1.085, 7.535, 2.26))                  # door tracks
        mb.box('MV_Black', (0.06, 0.03, 0.05), T(s * 0.90, 7.565, 1.31))                   # latches
        mb.box('MV_TailLamp', (0.10, 0.025, 0.06), T(s * 0.98, 7.60, 3.365))               # high rear markers
    mb.box('MV_BoxFrame', (2.12, 0.05, 0.08), T(0, 7.545, 1.27))                           # door bottom bar
    mb.tube('MV_Chrome', [(-0.12, 7.565, 1.30), (-0.12, 7.61, 1.30), (0.12, 7.61, 1.30), (0.12, 7.565, 1.30)], 0.012)
    # under-ride bar + brackets + lamp housings
    mb.box('MV_Black', (2.10, 0.12, 0.16), T(0, 7.46, 0.52))
    for s in (1, -1):
        mb.box('MV_Black', (0.08, 0.08, 0.32), T(s * 0.43, 7.44, 0.76))
        mb.box('MV_Black', (0.44, 0.04, 0.14), T(s * 0.80, 7.535, 0.52))
        for x, mat, r in ((0.96, 'MV_Signal', 0.046), (0.84, 'MV_TailLamp', 0.048), (0.72, 'MV_TailLamp', 0.048),
                          (0.615, 'MV_LensClear', 0.036)):
            m = T(s * x, 7.555, 0.52) @ RX(-90)
            mb.lathe('MV_Chrome', [(r + 0.006, -0.01), (r + 0.008, 0.004), (r + 0.002, 0.007), (r - 0.004, 0.0)], m, segs=14)
            mb.lathe(mat, [(r, 0.0), (r * 0.8, 0.008), (r * 0.4, 0.012), (0.0, 0.013)], m, segs=14)
    # rear plate on a bracket under the sill
    mb.box('MV_Black', (0.40, 0.02, 0.22), T(0, 7.545, 0.79))
    mb.box('MV_Black', (0.05, 0.02, 0.18), T(0, 7.545, 0.97))
    p = mb.prism('MV_Black', rrect2d(0.36, 0.18, 0.012, 3), 0.0, 0.006, T(0, 7.555, 0.79) @ RX(-90))
    for f in (p['top'], p['bottom']):
        if f.normal.y > 0.5:
            f.material_index = MI['MV_Plate']
            uv_planar(f, uvl, (0.18, 7.561, 0.70), (-0.36, 0, 0), (0, 0, 0.18), plate_rect(PLATE_CELLS['Truck']))
    mb.box('MV_LensClear', (0.10, 0.03, 0.025), T(0, 7.565, 0.895))
    # mud flaps
    for s in (1, -1):
        mb.box('MV_Black', (0.52, 0.012, 0.80), T(s * 0.83, 5.96, 0.57))
    # side markers on the rub rails + front clearance lamps
    for s in (1, -1):
        for y in (2.95, 4.25, 6.35, 7.15):
            mb.box('MV_Chrome', (0.012, 0.11, 0.06), T(s * 1.188, y, 1.165))
            mb.box('MV_Marker', (0.018, 0.09, 0.045), T(s * 1.196, y, 1.165))


def truck_marker_lamps(mb):
    for x in (-0.84, -0.42, 0.0, 0.42, 0.84):
        mb.box('MV_Chrome', (0.12, 0.012, 0.075), T(x, 2.279, 3.385))
        mb.box('MV_Marker', (0.10, 0.03, 0.055), T(x, 2.262, 3.385))


def truck_chassis(mb):
    for s in (1, -1):
        mb.box('MV_Black', (0.08, 7.15, 0.24), T(s * 0.43, 3.875, 0.74))                   # frame rails
        mb.box('MV_Black', (0.10, 5.10, 0.11), T(s * 0.43, 4.94, 0.915))                   # body sills
        mb.box('MV_Black', (0.07, 0.95, 0.06), T(s * 0.43, FA_Y, 0.56))                    # front leaf springs
        mb.box('MV_Black', (0.07, 0.70, 0.03), T(s * 0.43, FA_Y, 0.515))
        mb.box('MV_Black', (0.075, 1.30, 0.07), T(s * 0.43, RA_Y, 0.56))                   # rear leaf springs
        mb.box('MV_Black', (0.075, 1.00, 0.035), T(s * 0.43, RA_Y, 0.515))
        mb.cyl('MV_Black', 0.05, 0.22, T(s * 0.72, FA_Y, 0.44), segs=10)                   # kingpins
    for y in (1.85, 3.50, 4.95, 6.30, 7.38):
        mb.box('MV_Black', (0.78, 0.07, 0.16), T(0, y, 0.74))
    mb.box('MV_Black', (0.62, 1.15, 0.45), T(0, 1.15, 0.68))                               # engine
    mb.box('MV_Black', (0.45, 0.60, 0.12), T(0, 1.10, 0.42))
    mb.box('MV_Black', (0.36, 0.55, 0.30), T(0, 2.0, 0.66))                                # gearbox
    mb.cyl_between('MV_Black', (0, 2.28, 0.63), (0, 5.15, 0.47), 0.045, segs=10)           # drive shaft
    mb.cyl('MV_Black', 0.075, 1.36, T(0, RA_Y, 0.45), segs=12, axis='X')                   # rear axle
    mb.sphere('MV_Black', 0.17, T(0, RA_Y, 0.45) @ SC(0.85, 1.0, 0.9), segs=12, rings=8)
    mb.box('MV_Black', (1.44, 0.10, 0.12), T(0, FA_Y, 0.40))                               # front axle beam
    # battery box (right), air tank, muffler + exhaust
    mb.box('MV_Black', (0.42, 0.55, 0.34), T(-0.77, 2.80, 0.63))
    mb.box('MV_Black', (0.43, 0.56, 0.03), T(-0.77, 2.80, 0.775))
    mb.cyl('MV_BoxFrame', 0.11, 0.70, T(-0.72, 3.55, 0.60), segs=14, axis='Y')
    mb.cyl('MV_BoxFrame', 0.13, 0.62, T(-0.70, 4.30, 0.48), segs=14, axis='Y')
    mb.tube('MV_Black', [(-0.25, 1.75, 0.50), (-0.40, 2.35, 0.40), (-0.45, 3.80, 0.40), (-0.62, 3.99, 0.46)], 0.04, segs=8)
    mb.tube('MV_Black', [(-0.70, 4.60, 0.45), (-0.80, 4.72, 0.40), (-0.93, 4.76, 0.33)], 0.035, segs=8)
    # side guards (both sides) with amber reflectors
    for s in (1, -1):
        for z in (0.52, 0.76):
            mb.box('MV_BoxFrame', (0.03, 1.30, 0.08), T(s * 1.06, 4.20, z))
        for y in (3.75, 4.65):
            mb.box('MV_Signal', (0.01, 0.07, 0.04), T(s * 1.078, y, 0.52))
        for y in (3.65, 4.75):
            mb.box('MV_Black', (0.60, 0.04, 0.04), T(s * 0.77, y, 0.76))
            mb.box('MV_Black', (0.03, 0.04, 0.32), T(s * 1.045, y, 0.64))
    # rear fenders over the duals
    for s in (1, -1):
        poly = []
        for i in range(11):
            a = math.radians(32 + 116 * i / 10)
            poly.append((RA_Y + 0.51 * math.cos(a), 0.45 + 0.51 * math.sin(a)))
        for i in range(11):
            a = math.radians(148 - 116 * i / 10)
            poly.append((RA_Y + 0.49 * math.cos(a), 0.45 + 0.49 * math.sin(a)))
        mb.prism('MV_Black', poly, s * 0.56, s * 1.10, frame((0, 0, 0), (0, 1, 0), (0, 0, 1)))
    # spare tyre carrier
    for y in (6.45, 6.95):
        mb.box('MV_Black', (0.92, 0.05, 0.04), T(0, y, 0.37))


def truck_fuel_tank(mb):
    mb.box('MV_BoxFrame', (0.46, 0.90, 0.40), T(0.75, 2.97, 0.58))


def truck_tank_details(mb):
    for y in (2.72, 3.22):
        mb.box('MV_Black', (0.48, 0.035, 0.42), T(0.75, y, 0.58))
        mb.box('MV_Black', (0.06, 0.05, 0.10), T(0.49, y, 0.76))
    mb.cyl('MV_Chrome', 0.045, 0.05, T(0.90, 3.30, 0.80), segs=12)
    mb.cyl('MV_Chrome', 0.05, 0.015, T(0.90, 3.30, 0.83), segs=12)
    # battery latches
    for y in (2.65, 2.95):
        mb.box('MV_Chrome', (0.012, 0.05, 0.03), T(-0.985, y, 0.74))


def truck_spare(mb):
    m = T(0, 6.70, 0.50) @ RY(-90)          # tyre axis -> Z
    prof = [(r, x) for (r, x) in tire_profile(0.22, 0.44, 0.24)]
    mb.lathe('MV_Tire', prof, m, segs=24, axis='X')
    mb.cyl('MV_Rim', 0.24, 0.17, T(0, 6.70, 0.50), segs=24)


def truck_interior(mb):
    mb.box('MV_Interior', (1.90, 1.75, 0.04), T(0, 1.20, 1.00))                            # floor
    mb.box('MV_Interior', (0.55, 0.85, 0.24), T(0, 1.40, 1.12))                            # engine hump
    prof = [(0.27, 1.02), (0.27, 1.43), (0.62, 1.415), (0.68, 1.36), (0.66, 1.18), (0.58, 1.02)]
    mb.prism('MV_Interior', prof, -0.95, 0.95, frame((0, 0, 0), (0, 1, 0), (0, 0, 1)))   # dashboard
    mb.box('MV_Interior', (0.42, 0.18, 0.10), T(0.45, 0.62, 1.45))                         # binnacle
    mb.box('MV_Black', (0.30, 0.04, 0.16), T(0, 0.67, 1.25))                               # centre panel
    for x in (0.45, -0.45):                                                                # seats
        mb.box('MV_Interior', (0.40, 0.40, 0.30), T(x, 1.68, 1.17))
        mb.box('MV_Interior', (0.52, 0.50, 0.13), T(x, 1.62, 1.385))
        mb.box('MV_Interior', (0.52, 0.13, 0.62), T(x, 1.90, 1.78) @ RX(-10))
        mb.box('MV_Interior', (0.28, 0.10, 0.17), T(x, 1.97, 2.19) @ RX(-10))
    mb.cyl_between('MV_Black', (0.12, 1.30, 1.24), (0.10, 1.25, 1.55), 0.012, segs=6)       # gear lever
    mb.sphere('MV_Black', 0.03, T(0.10, 1.25, 1.56), segs=8, rings=6)


def truck_wheel_and_driver(mb):
    C = Vector((0.45, 1.25, 1.48))
    wm = T(C) @ RX(-25)
    mb.torus('MV_Black', 0.23, 0.022, wm, segs=24, tsegs=6)
    mb.cyl('MV_Black', 0.07, 0.06, wm, segs=12)
    for a in (90, 210, 330):
        mb.box('MV_Black', (0.20, 0.045, 0.018), wm @ RZ(a) @ T(0.12, 0, 0))
    n = (wm.to_3x3() @ Vector((0, 0, 1))).normalized()
    mb.cyl_between('MV_Black', C - n * 0.03, C - n * 0.45, 0.035, segs=8)
    # driver silhouette
    D = 'MV_Driver'
    xd = 0.45
    mb.box(D, (0.36, 0.30, 0.16), T(xd, 1.68, 1.52))
    mb.box(D, (0.40, 0.24, 0.50), T(xd, 1.77, 1.80) @ RX(-8))
    mb.cyl(D, 0.05, 0.10, T(xd, 1.80, 2.10), segs=8)
    mb.sphere(D, 0.105, T(xd, 1.78, 2.22) @ SC(0.88, 1.0, 1.1), segs=12, rings=8)
    mb.sphere(D, 0.11, T(xd, 1.79, 2.27) @ SC(0.93, 1.0, 0.62), segs=12, rings=6)       # cap crown
    mb.box(D, (0.17, 0.11, 0.014), T(xd, 1.645, 2.285) @ RX(-8))                       # cap brim
    Yw = (wm.to_3x3() @ Vector((0, 1, 0))).normalized()
    X = Vector((1, 0, 0))
    for t, phi in ((1, -20), (-1, 200)):
        H = C + 0.23 * (math.cos(math.radians(phi)) * X + math.sin(math.radians(phi)) * Yw)
        S = Vector((xd + t * 0.19, 1.80, 2.0))
        E = Vector((xd + t * 0.25, 1.55, 1.68))
        mb.sphere(D, 0.065, T(S), segs=10, rings=6)
        mb.cyl_between(D, S, E, 0.05, segs=8)
        mb.sphere(D, 0.048, T(E), segs=8, rings=6)
        mb.cyl_between(D, E, H, 0.042, segs=8)
        mb.sphere(D, 0.046, T(H), segs=8, rings=6)
        K = Vector((xd + t * 0.11, 1.30, 1.53))
        mb.cyl_between(D, (xd + t * 0.10, 1.68, 1.50), K, 0.075, segs=8)
        mb.cyl_between(D, K, (xd + t * 0.11, 1.24, 1.06), 0.06, segs=8)


def truck_wheel(name, loc, rear, side):
    """side = +1 for left (+X). Built for the left, mirrored for the right."""
    mb = MB()
    W, R = 0.235, 0.45
    if not rear:
        tire(mb, W, R, 0.0)
        rim = [(0.258, 0.098), (0.250, 0.106), (0.240, 0.100), (0.232, 0.080), (0.225, 0.056), (0.215, 0.050),
               (0.160, 0.056), (0.105, 0.062), (0.095, 0.070), (0.084, 0.072)]
        mb.lathe('MV_Rim', rim, None, segs=32, axis='X')
        mb.lathe('MV_Chrome', [(0.086, 0.068), (0.084, 0.096), (0.065, 0.115), (0.035, 0.123), (0.0, 0.125)], None,
                 segs=24, axis='X')
        lug_nuts(mb, 6, 0.125, 0.058, 0.026, 1, phase=30)
        hand_holes(mb, 'MV_Black', 6, 0.178, 0.054, 0.109, rx=0.017, ry=0.028)
        mb.prism('MV_Black', ellipse2d(0.245, 0.245, 32), -0.002, 0.0, T(-0.095, 0, 0) @ RY(90))
    else:
        for x0 in (0.13, -0.13):
            tire(mb, W, R, x0)
        rim = [(0.258, 0.228), (0.250, 0.236), (0.240, 0.230), (0.232, 0.19), (0.226, 0.12), (0.222, 0.06),
               (0.215, 0.028), (0.160, 0.032), (0.105, 0.038), (0.096, 0.046), (0.092, 0.11)]
        mb.lathe('MV_Rim', rim, None, segs=32, axis='X')
        mb.lathe('MV_Chrome', [(0.094, 0.105), (0.092, 0.13), (0.070, 0.15), (0.035, 0.158), (0.0, 0.16)], None,
                 segs=24, axis='X')
        lug_nuts(mb, 6, 0.128, 0.034, 0.026, 1, phase=30)
        hand_holes(mb, 'MV_Black', 6, 0.18, 0.0305, 0.073, rx=0.016, ry=0.026)
        mb.prism('MV_Black', ellipse2d(0.245, 0.245, 32), -0.002, 0.0, T(-0.225, 0, 0) @ RY(90))
    if side < 0:
        bm = mb.bm
        for v in bm.verts:
            v.co.x = -v.co.x
        bmesh.ops.reverse_faces(bm, faces=bm.faces[:])
    ob = mb_to_object(name, mb, loc)
    return ob


def build_truck():
    t0 = time.time()
    parts = []
    cab = truck_cab()
    parts.append(cab)
    print('[truck] cab shell %.1fs' % (time.time() - t0))
    parts.append(part('cab_details', truck_cab_details))
    parts.append(part('chrome', truck_chrome_parts, bevel=(0.004, 1, 40)))
    parts.append(part('bumper', truck_bumper, bevel=(0.022, 3, 40), cutters=truck_bumper_cutters()))
    parts.append(part('front_lower', truck_front_lower))
    parts.append(part('spoiler', truck_spoiler, bevel=(0.01, 1, 40)))
    parts.append(part('sheet', truck_sheet_parts, bevel=(0.008, 2, 40)))
    parts.append(part('tubes', truck_tubes))
    parts.append(part('deflector', truck_deflector))
    parts.append(part('box', truck_box_panels))
    parts.append(part('boxframe', truck_box_frame, bevel=(0.008, 1, 40)))
    parts.append(part('slats', truck_door_slats, bevel=(0.012, 2, 40)))
    parts.append(part('rear', truck_rear_parts))
    parts.append(part('markers', truck_marker_lamps, bevel=(0.006, 2, 40)))
    parts.append(part('chassis', truck_chassis, bevel=(0.006, 1, 40)))
    parts.append(part('tank', truck_fuel_tank, bevel=(0.06, 3, 40)))
    parts.append(part('tank_details', truck_tank_details))
    parts.append(part('spare', truck_spare))
    parts.append(part('interior', truck_interior, bevel=(0.03, 2, 40)))
    parts.append(part('driver', truck_wheel_and_driver))
    body = join_objects('Body', parts)
    finalize_mesh(body)
    root = bpy.data.objects.new('Truck', None)
    root.empty_display_type = 'ARROWS'
    scene().collection.objects.link(root)
    body.parent = root
    wheels = [truck_wheel('Wheel_FL', (FRONT_TRACK, FA_Y, WR), False, 1),
              truck_wheel('Wheel_FR', (-FRONT_TRACK, FA_Y, WR), False, -1),
              truck_wheel('Wheel_RL', (REAR_TRACK, RA_Y, WR), True, 1),
              truck_wheel('Wheel_RR', (-REAR_TRACK, RA_Y, WR), True, -1)]
    for w in wheels:
        finalize_mesh(w)
        w.parent = root
    yl = yf(0.87) + 0.002
    locs = {'HL_L': (0.685, yl, 0.87), 'HL_R': (-0.685, yl, 0.87), 'Grille': (0.0, yf(0.945), 0.945),
            'Horn': (0.0, 0.63, 2.56)}
    for n, p in locs.items():
        empty(n, p, root)
    print('[truck] built in %.1fs' % (time.time() - t0))
    return root, list(locs.keys())


# ============================================================================================ export / verify
def collect(root):
    out = [root]
    for c in root.children_recursive:
        out.append(c)
    return out


def export_fbx(root, path):
    bpy.ops.object.select_all(action='DESELECT')
    for o in collect(root):
        o.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH', 'EMPTY'}, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             mesh_smooth_type='FACE', use_mesh_modifiers=True, add_leaf_bones=False)


def model_tris(root):
    return sum(tri_count(o) for o in collect(root) if o.type == 'MESH')


def model_materials(root):
    names = set()
    for o in collect(root):
        if o.type == 'MESH':
            names.update(m.name for m in o.data.materials if m)
    return sorted(names)


# ============================================================================================ previews
def setup_render_common(res=(1280, 720)):
    scn = scene()
    scn.render.resolution_x, scn.render.resolution_y = res
    scn.render.resolution_percentage = 100
    scn.render.film_transparent = False
    scn.view_settings.view_transform = 'Standard'
    if scn.world is None:
        scn.world = bpy.data.worlds.new('World')
    return scn


def camera(name, loc, target, fov_v=None, ortho=None, lens=None):
    cd = bpy.data.cameras.new(name)
    cam = bpy.data.objects.new(name, cd)
    scene().collection.objects.link(cam)
    cam.location = loc
    d = Vector(target) - Vector(loc)
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    cd.clip_start = 0.05
    cd.clip_end = 500
    if ortho:
        cd.type = 'ORTHO'
        cd.ortho_scale = ortho
    elif fov_v:
        cd.sensor_fit = 'VERTICAL'
        cd.angle_y = math.radians(fov_v)
    elif lens:
        cd.lens = lens
    return cam


def render_workbench(path, cam, res=(1280, 720)):
    scn = setup_render_common(res)
    scn.render.engine = 'BLENDER_WORKBENCH'
    sh = scn.display.shading
    sh.light = 'STUDIO'
    sh.color_type = 'TEXTURE'
    sh.show_cavity = True
    sh.cavity_type = 'BOTH'
    sh.show_object_outline = True
    sh.object_outline_color = (0.05, 0.05, 0.06)
    sh.show_backface_culling = True
    sh.show_shadows = False
    scn.world.color = (0.62, 0.66, 0.70)
    scn.display.render_aa = '8'
    scn.camera = cam
    scn.render.filepath = path
    bpy.ops.render.render(write_still=True)


def eevee_env(sun_dir=(0.6, 0.6, 0.33), sun_strength=3.5, world=(0.70, 0.76, 0.80), ground=True):
    scn = scene()
    if scn.world is None:
        scn.world = bpy.data.worlds.new('World')
    w = scn.world
    if w.node_tree is None:
        w.use_nodes = True
    nt = w.node_tree
    bg = nt.nodes.get('Background')
    bg.inputs['Color'].default_value = (*world, 1.0)
    bg.inputs['Strength'].default_value = 1.0
    sun = bpy.data.objects.get('PreviewSun')
    if sun is None:
        ld = bpy.data.lights.new('PreviewSun', 'SUN')
        sun = bpy.data.objects.new('PreviewSun', ld)
        scn.collection.objects.link(sun)
    sun.data.energy = sun_strength
    sun.data.color = (1.0, 0.92, 0.82)
    sun.data.angle = math.radians(2)
    d = Vector(sun_dir).normalized()
    sun.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    if ground and bpy.data.objects.get('PreviewGround') is None:
        me = bpy.data.meshes.new('PreviewGround')
        bm = bmesh.new()
        bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=80)
        bm.to_mesh(me)
        bm.free()
        g = bpy.data.objects.new('PreviewGround', me)
        scn.collection.objects.link(g)
        gm = bpy.data.materials.new('PreviewAsphalt')
        gm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (*hex_lin('#3A3F47'), 1)
        gm.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = 0.9
        me.materials.append(gm)


def render_eevee(path, cam, res=(1280, 720), samples=32):
    scn = setup_render_common(res)
    scn.render.engine = 'BLENDER_EEVEE'
    scn.eevee.taa_render_samples = samples
    scn.camera = cam
    scn.render.filepath = path
    bpy.ops.render.render(write_still=True)


def clear_preview_objects():
    for o in list(bpy.data.objects):
        if o.name.startswith('PrevCam'):
            bpy.data.objects.remove(o, do_unlink=True)


def truck_previews(prev_dir):
    out = []
    shots = [
        ('Truck_34_front_left', 'wb', dict(loc=(6.3, -5.0, 2.7), target=(0.0, 2.9, 1.45), lens=32)),
        ('Truck_side_left', 'wb', dict(loc=(14.0, 3.8, 1.75), target=(0.0, 3.8, 1.75), ortho=8.6)),
        ('Truck_rear_34_right', 'wb', dict(loc=(-6.0, 12.8, 2.9), target=(0.0, 4.4, 1.5), lens=32)),
        ('Truck_front_close_wb', 'wb', dict(loc=(1.3, -2.2, 1.35), target=(0.0, 0.3, 1.2), lens=24)),
        ('Truck_ingame_20m', 'ev', dict(loc=(0.0, -20.0, 1.56), target=(0.0, 0.0, 1.56), fov_v=60)),
        ('Truck_ingame_2m', 'ev', dict(loc=(0.0, -2.0, 1.56), target=(0.0, 0.0, 1.56), fov_v=60)),
        ('Truck_ingame_1p6m', 'ev', dict(loc=(0.0, -1.6, 1.56), target=(0.0, 0.0, 1.56), fov_v=60)),
        ('Truck_approach_35m', 'ev', dict(loc=(-3.5, -35.0, 1.56), target=(0.0, 0.0, 1.4), fov_v=60)),
        ('Truck_34_eevee', 'ev', dict(loc=(6.3, -5.0, 2.7), target=(0.0, 2.9, 1.45), lens=32)),
    ]
    eevee_env(sun_dir=(0.67, 0.67, 0.33))
    for name, eng, kw in shots:
        cam = camera('PrevCam_' + name, kw['loc'], kw['target'], kw.get('fov_v'), kw.get('ortho'), kw.get('lens'))
        path = os.path.join(prev_dir, name + '.png')
        ground = bpy.data.objects.get('PreviewGround')
        if ground:
            ground.hide_render = (eng == 'wb')
        if eng == 'wb':
            render_workbench(path, cam)
        else:
            render_eevee(path, cam)
        out.append(path)
        print('[preview]', path)
    clear_preview_objects()
    return out


# ============================================================================================ manifest
DRAFT_COPY = [
    {'where': 'Truck box sides (T_V_TruckLivery.png)', 'text': 'DESTINY FREIGHT'},
    {'where': 'Truck box sides (T_V_TruckLivery.png)', 'text': 'Always on time.'},
    {'where': 'Truck box sides, lower rear (T_V_TruckLivery.png)', 'text': 'No. 4649'},
    {'where': 'Truck box emblem + cab-door badges', 'text': 'DF'},
    {'where': 'Truck grille badge (T_V_Badge.png)', 'text': 'TENSEI'},
    {'where': 'Licence plates (T_V_Plates.png)', 'text': 'CITY / TK 4649 (truck), KN 3021 (sedan), MR 1188 (hatchback), '
                                                         'CITY TAXI / TX 7012 (taxi), VN 5530 (van), SV 9047 (SUV); '
                                                         'spares HB 2290, SD 6408'},
    {'where': 'Taxi roof sign (T_V_TaxiSign.png)', 'text': 'TAXI'},
]


def write_manifest(out_dir, models):
    path = os.path.join(out_dir, 'manifest.json')
    data = {}
    if os.path.exists(path):
        with open(path) as f:
            data = json.load(f)
    data.setdefault('models', {})
    data['models'].update(models)
    used = set()
    for mdl in data['models'].values():
        used.update(mdl.get('materials', []))
    mats = {}
    for name, col, tex, emi, note in MAT_DEFS:
        if name not in used:
            continue
        e = {'color': col, 'texture': tex, 'emission': emi, 'note': note}
        if name == 'MV_Glass':
            e['transparent'] = True
        if name == 'MV_TaxiChecker':
            e['wrap'] = 'repeat'
        mats[name] = e
    data['materials'] = mats
    data['draft_copy'] = DRAFT_COPY
    data['conventions'] = ('Front faces -Y in Blender (+Z in Unity). Root origin = ground, centre of width, frontmost '
                           'point of the front bumper. L/R = vehicle\'s own left/right (L = Blender +X = Unity -X). '
                           'Wheels: origin at hub centre, spin axis = Blender X (Unity X). Locator empties carry the root '
                           'orientation (forward = vehicle forward). Exported per spec §5 settings.')
    with open(path, 'w') as f:
        json.dump(data, f, indent=2)
    print('[manifest] wrote', path)


# ============================================================================================ main
def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def ensure_textures(tex_dir):
    need = ['T_V_TruckLivery.png', 'T_V_GlassReflect.png', 'T_V_Plates.png', 'T_V_Badge.png', 'T_V_TaxiSign.png',
            'T_V_Checker.png']
    if all(os.path.exists(os.path.join(tex_dir, n)) for n in need):
        return
    subprocess.check_call([PY3, TEX_SCRIPT, tex_dir])


BUILDERS = {}


def main():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    out_dir = os.path.abspath(argv[0]) if argv and not argv[0].startswith('--') else os.path.join(
        GAME, 'build_art', 'opening', 'vehicles')
    only = None
    if '--only' in argv:
        only = argv[argv.index('--only') + 1].split(',')
    do_preview = '--no-preview' not in argv
    do_export = '--no-export' not in argv
    fbx_dir, tex_dir, prev_dir = (os.path.join(out_dir, d) for d in ('fbx', 'textures', 'previews'))
    for d in (fbx_dir, tex_dir, prev_dir):
        os.makedirs(d, exist_ok=True)
    ensure_textures(tex_dir)
    order = ['Truck', 'Sedan', 'Hatchback', 'Taxi', 'Van', 'SUV']
    for name in order:
        if only and name not in only:
            continue
        if name not in BUILDERS:
            continue
        reset()
        build_materials(tex_dir)
        root, locators, notes, preview_fn = BUILDERS[name]()
        tris = model_tris(root)
        print('[%s] tris = %d' % (name, tris))
        for o in collect(root):
            if o.type == 'MESH':
                print('   %-10s tris %6d  loc %s' % (o.name, tri_count(o), tuple(round(v, 3) for v in o.location)))
        if do_export:
            export_fbx(root, os.path.join(fbx_dir, name + '.fbx'))
            write_manifest(out_dir, {name: {'file': 'fbx/%s.fbx' % name, 'tris': tris, 'locators': locators,
                                            'materials': model_materials(root), 'notes': notes}})
        if do_preview:
            preview_fn(prev_dir)


def _truck():
    root, locs = build_truck()
    notes = ('Hero cab-over box truck, 7.6 m. Children: Body (static), Wheel_FL/FR (front singles), Wheel_RL/RR (rear '
             'duals, one object per side). Locators: HL_L/HL_R headlamp lens centres, Grille centre, Horn between the '
             'roof air horns. Interior (dash, wheel, seats, driver) is visible through MV_Glass, which must render '
             'transparent. Nose-dive: pitch Body about the rear axle (y = 5.35 m) or the root.')
    return root, ['HL_L', 'HL_R', 'Grille', 'Horn'], notes, truck_previews


BUILDERS['Truck'] = _truck

if __name__ == '__main__':
    main()
