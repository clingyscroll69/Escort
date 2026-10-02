"""Opening street kit, part C (street furniture + signals) and D (pigeon). Headless Blender 5.2.
Spec: docs/superpowers/specs/2026-10-01-opening-street-design.md §5. Geometry helpers are shared with opening_phone.py.

Every asset is one FBX: a root empty named after the asset (origin at the base on the ground, front facing -Y) with
a mesh child "<Asset>_Mesh" (+ separate children for parts Unity animates or toggles) and locator empties.
Textures come from tools/opening_street_textures.py (run automatically if missing).

Usage: Blender -b -P tools/blender/opening_street.py -- <out_dir> [--only Name,Name] [--no-export] [--no-preview]
<out_dir> = build_art/opening/street (fbx/, textures/, previews/ and manifest.json).
"""
import bpy, bmesh, math, os, sys, subprocess
import numpy as np
from mathutils import Vector, Matrix

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import opening_phone as kit                                         # noqa: E402
from opening_phone import Builder, revolve, sweep, rrect_outline, log, make_empty, hex_lin   # noqa: E402

TOOLS = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TEX_DIR = None

# ----------------------------------------------------------------------------------------------------------------
# materials: colour (sRGB hex), texture (file in textures/), emission (authored at the "on" state; Unity dims them)
MS = {
    'MS_Pole': {'color': '#2F3A36', 'note': 'poles, arms, brackets (dark green-grey)'},
    'MS_PoleDark': {'color': '#26292D', 'note': 'base plates, clamps, bollards, dark street furniture'},
    'MS_Metal': {'color': '#9AA0A6', 'note': 'galvanised / stainless steel: bolts, bike rack, chains, rims'},
    'MS_SignalHousing': {'color': '#1B1C1E', 'note': 'signal and ped head housings, visors, button faceplate'},
    'MS_Backplate': {'color': '#141516', 'note': 'signal backplates'},
    'MS_SignalYellow': {'color': '#F2C230', 'note': 'retroreflective backplate border'},
    'MS_LensRed': {'color': '#E8321F', 'emission': {'color': '#FF3A22', 'intensity': 3.0},
                   'note': 'signal red lens (emission authored on; Unity dims when off)'},
    'MS_LensAmber': {'color': '#F2A21E', 'emission': {'color': '#FFB028', 'intensity': 3.0},
                     'note': 'signal amber lens'},
    'MS_LensGreen': {'color': '#19D99A', 'emission': {'color': '#2BFFB8', 'intensity': 3.0},
                     'note': 'signal green lens'},
    'MS_PedHand': {'color': '#FF7A1A', 'texture': 'T_PedHand.png', 'emission': {'color': '#FFFFFF', 'intensity': 2.5},
                   'note': 'orange raised-hand LED icon (emission = texture x intensity); 0.5 mm behind MS_PedWalk'},
    'MS_PedWalk': {'color': '#F2F5EE', 'texture': 'T_PedWalk.png', 'emission': {'color': '#FFFFFF', 'intensity': 2.5},
                   'note': 'white walking-person LED icon; same panel as MS_PedHand, show one at a time'},
    'MS_PedPanelOff': {'color': '#121314', 'note': 'dark ped head panel; countdown digits drawn by Unity'},
    'MS_StreetBlade': {'color': '#0D6B3E', 'texture': 'T_StreetNames.png',
                       'note': 'street name blades: top half MAPLE AVE, bottom half 3RD ST'},
    'MS_SignBack': {'color': '#B9BDC1', 'note': 'aluminium sign backs and edges'},
    'MS_ButtonBox': {'color': '#E8B820', 'note': 'push-button housing yellow'},
    'MS_PushSign': {'color': '#F3F3EE', 'texture': 'T_PushButtonSign.png', 'note': 'PUSH BUTTON FOR WALK SIGNAL'},
    'MS_LampHead': {'color': '#5E6469', 'note': 'LED cobra head housing'},
    'MS_LampLens': {'color': '#FFF4D6', 'emission': {'color': '#FFF4D6', 'intensity': 4.0},
                    'note': 'street lamp LED lens / shelter light strip (still lit at 7:52)'},
    'MS_Banner': {'color': '#F2E6C9', 'texture': 'T_BannerSpringMarket.png', 'note': 'lamp-post banner'},
    'MS_BannerHem': {'color': '#E9DCBF', 'note': 'banner pole pockets'},
    'MS_ShelterFrame': {'color': '#3B4247', 'note': 'bus shelter frame, posts, lightbox body'},
    'MS_ShelterRoof': {'color': '#6B7176', 'note': 'bus shelter roof top'},
    'MS_ShelterGlass': {'color': '#2F4250', 'texture': 'T_GlassReflection.png',
                        'note': 'dark tinted reflective glass (opaque, reflection baked in the texture)'},
    'MS_AdPanel': {'color': '#3A2A80', 'texture': 'T_AdHeroSummoner.png', 'emission': {'color': '#FFFFFF', 'intensity': 1.6},
                   'note': 'back-lit ad: HERO SUMMONER (emission = texture x intensity)'},
    'MS_Wood': {'color': '#A0693A', 'note': 'bench slats, A-frame frame'},
    'MS_BusSign': {'color': '#1E4FA3', 'texture': 'T_BusStopSign.png', 'note': 'bus stop sign 12 · 47'},
    'MS_NoParking': {'color': '#F4F4EF', 'texture': 'T_NoParking.png', 'note': 'NO PARKING sign'},
    'MS_Concrete': {'color': '#B5B0A6', 'note': 'concrete: planter, footings, curb inlet'},
    'MS_TimetableFace': {'color': '#ECEDEA', 'note': 'timetable case face'},
    'MS_HydrantRed': {'color': '#C2392B', 'note': 'fire hydrant body'},
    'MS_HydrantCap': {'color': '#E8B820', 'note': 'fire hydrant bonnet / cap yellow'},
    'MS_TrashGreen': {'color': '#2F5D45', 'note': 'trash can slats (dark green)'},
    'MS_NewsBoxBlue': {'color': '#2459A8', 'note': 'DAILY NEWS box body'},
    'MS_NewsBoxYellow': {'color': '#E3B21C', 'note': 'CITY WEEKLY box body'},
    'MS_NewsFront': {'color': '#ECE8DD', 'texture': 'T_NewsBoxFronts.png',
                     'note': 'news box door fronts: left half DAILY NEWS, right half CITY WEEKLY'},
    'MS_Reflective': {'color': '#F2F2EE', 'note': 'retroreflective bollard band'},
    'MS_MeterHead': {'color': '#4A5560', 'note': 'parking meter head'},
    'MS_MeterScreen': {'color': '#2B3A33', 'note': 'parking meter LCD'},
    'MS_SolarPanel': {'color': '#1F2A44', 'note': 'parking meter solar panel'},
    'MS_BikeFrame': {'color': '#4F7D7A', 'note': 'bicycle frame (teal)'},
    'MS_Tire': {'color': '#1E1F21', 'note': 'tyres, grips'},
    'MS_BikeSeat': {'color': '#2A2A2C', 'note': 'saddle, pedals'},
    'MS_CastIron': {'color': '#3A3B3D', 'note': 'tree grate, drain grate, manhole frame'},
    'MS_Manhole': {'color': '#5A5958', 'texture': 'T_ManholeCover.png', 'note': 'manhole cover pattern'},
    'MS_Mulch': {'color': '#4A3727', 'note': 'mulch under the tree grate'},
    'MS_Shadow': {'color': '#101112', 'note': 'dark insides of drains'},
    'MS_Soil': {'color': '#3F3125', 'note': 'planter soil'},
    'MS_Shrub': {'color': '#4E7A3A', 'note': 'shrub foliage'},
    'MS_ShrubDark': {'color': '#3C6330', 'note': 'shrub foliage, darker'},
    'MS_Flower': {'color': '#E58AA8', 'note': 'small flowers in the planter'},
    'MS_DrainMarker': {'color': '#2D6E8E', 'texture': 'T_DrainMarker.png', 'note': 'curb drain plaque'},
    'MS_Chalkboard': {'color': '#2E3532', 'texture': 'T_AFrameChalk.png', 'note': 'cafe chalkboard FRESH COFFEE'},
    'MS_CafeMetal': {'color': '#2F6B4F', 'note': 'bistro table and chair frames (cafe green)'},
    'MS_CafeTop': {'color': '#E8E2D6', 'note': 'bistro table top'},
    'MS_PigeonBody': {'color': '#8A8F99', 'note': 'pigeon blue-grey body, head, wings'},
    'MS_PigeonNeck': {'color': '#5E8C7E', 'texture': 'T_PigeonNeck.png', 'note': 'iridescent green/purple neck'},
    'MS_PigeonBar': {'color': '#2E3035', 'note': 'pigeon wing bars, primaries, tail band'},
    'MS_PigeonFeet': {'color': '#D98C8C', 'note': 'pink feet'},
    'MS_PigeonEye': {'color': '#1C1A18', 'note': 'dark eye'},
    'MS_PigeonBeak': {'color': '#2B2B2B', 'note': 'beak'},
    'MS_PigeonCere': {'color': '#E9E6DF', 'note': 'white cere at the beak base'},
}
STREET_MATS = MS
kit.MATS.update(MS)


def smat(name):
    """Material with base colour, optional image texture (TEX_DIR) and optional emission."""
    m = bpy.data.materials.get(name)
    if m is not None:
        return m
    spec = kit.MATS[name]
    lin = hex_lin(spec['color'])
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*lin, 1.0)
    nt = m.node_tree
    bsdf = nt.nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value = (*lin, 1.0)
    bsdf.inputs['Roughness'].default_value = 0.6
    if spec.get('texture'):
        img = bpy.data.images.load(os.path.join(TEX_DIR, spec['texture']), check_existing=True)
        tn = nt.nodes.new('ShaderNodeTexImage')
        tn.image = img
        nt.links.new(tn.outputs['Color'], bsdf.inputs['Base Color'])
        nt.nodes.active = tn
    if spec.get('emission'):
        e = spec['emission']
        bsdf.inputs['Emission Color'].default_value = (*hex_lin(e['color']), 1.0)
        bsdf.inputs['Emission Strength'].default_value = float(e['intensity'])
    return m


kit.material = smat          # to_object() in the shared helpers resolves materials through this


class UVB(Builder):
    """Builder with optional explicit per-face UVs (others get a box projection, 1 UV unit = 2 m)."""

    def __init__(self):
        super().__init__()
        self.uvs = []

    def f(self, idx, mat, smooth=True, uv=None):
        super().f(idx, mat, smooth)
        self.uvs.append(uv)

    def extend(self, other, matrix=None):
        off = len(self.verts)
        for p in other.verts:
            self.verts.append(tuple(matrix @ Vector(p)) if matrix is not None else p)
        for fc, m, sm, uv in zip(other.faces, other.mats, other.smooth, getattr(other, 'uvs', [None] * len(other.faces))):
            self.faces.append(tuple(i + off for i in fc))
            self.mats.append(m)
            self.smooth.append(sm)
            self.uvs.append(uv)


def to_obj(B, name, sharp_angle=35.0, recalc=True):
    """Mesh object from a UVB: materials, UVs, merged duplicates, outward normals, triangulated, smooth by angle."""
    me = bpy.data.meshes.new(name)
    me.from_pydata(B.verts, [], B.faces)
    names = []
    for m in B.mats:
        if m not in names:
            names.append(m)
    for n in names:
        me.materials.append(smat(n))
    idx = {n: i for i, n in enumerate(names)}
    uvl = me.uv_layers.new(name='UVMap')
    co = [v.co for v in me.vertices]
    uvs = getattr(B, 'uvs', [None] * len(B.faces))
    for p, m, uv in zip(me.polygons, B.mats, uvs):
        p.material_index = idx[m]
        if uv is not None:
            for li, (u, v) in zip(p.loop_indices, uv):
                uvl.data[li].uv = (u, v)
        else:
            n = p.normal
            ax = max(range(3), key=lambda k: abs(n[k]))
            a, b = [k for k in range(3) if k != ax]
            for li in p.loop_indices:
                c = co[me.loops[li].vertex_index]
                uvl.data[li].uv = (c[a] / 2.0, c[b] / 2.0)
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    if recalc:
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bmesh.ops.triangulate(bm, faces=bm.faces, quad_method='BEAUTY', ngon_method='BEAUTY')
    bm.to_mesh(me)
    bm.free()
    me.set_sharp_from_angle(angle=math.radians(sharp_angle))
    return obj


# ----------------------------------------------------------------------------------------------------------------
# primitives (all positions in metres; boxes are bevelled so hard edges catch the toon light)
def bbox(B, size, center, mat, bevel=0.01, rot=None, cap_mats=None):
    """Bevelled box: rounded-rect outline in XY (bevel radius, 2 segments) swept along Z with chamfered caps."""
    sx, sy, sz = size
    bv = min(bevel, sx / 2 - 1e-4, sy / 2 - 1e-4, sz / 2 - 1e-4)
    sub = UVB()
    ol = rrect_outline(sx / 2, sy / 2, max(bv, 1e-4), 2)
    prof = [(bv, -sz / 2), (0.0, -sz / 2 + bv), (0.0, sz / 2 - bv), (bv, sz / 2)] if bv > 1e-4 else \
        [(0.0, -sz / 2), (0.0, sz / 2)]
    top, bot = (cap_mats or (mat, mat))
    sweep(sub, ol, prof, (0, 0, 0), (1, 0, 0), (0, 1, 0), (0, 0, 1), lambda k, i: mat, cap_first=bot, cap_last=top)
    sub.uvs = [None] * len(sub.faces)
    M = Matrix.Translation(Vector(center)) @ (rot.to_4x4() if rot is not None else Matrix.Identity(4))
    B.extend(sub, M)


def cyl(B, r, h, base, axis=(0, 0, 1), mat='MS_Pole', segs=16, bevel=0.0, r_top=None, cap=True):
    rt = r if r_top is None else r_top
    if bevel > 0:
        prof = [(0, 0), (r - bevel, 0), (r, bevel), (rt, h - bevel), (rt - bevel, h), (0, h)]
    else:
        prof = [(0, 0), (r, 0), (rt, h), (0, h)]
    if not cap:
        prof = prof[1:-1]
    sub = UVB()
    revolve(sub, prof, segs, Vector(base), Vector(axis), mat)
    sub.uvs = [None] * len(sub.faces)
    B.extend(sub)


def rev(B, prof, segs, base, axis, mats):
    sub = UVB()
    revolve(sub, prof, segs, Vector(base), Vector(axis), mats)
    sub.uvs = [None] * len(sub.faces)
    B.extend(sub)


def pipe(B, pts, r, mat, segs=12, caps=True):
    """Tube along a polyline with parallel-transport frames (rounded joints come from the polyline density)."""
    pts = [Vector(p) for p in pts]
    tans = []
    for i in range(len(pts)):
        a = pts[max(i - 1, 0)]
        b = pts[min(i + 1, len(pts) - 1)]
        tans.append((b - a).normalized())
    ref = Vector((0, 0, 1)) if abs(tans[0].z) < 0.9 else Vector((1, 0, 0))
    n = tans[0].cross(ref).normalized()
    rings = []
    for i, p in enumerate(pts):
        if i > 0:
            q = tans[i - 1].rotation_difference(tans[i])
            n = (q @ n).normalized()
        b = tans[i].cross(n).normalized()
        rings.append([B.v(p + (n * math.cos(2 * math.pi * k / segs) + b * math.sin(2 * math.pi * k / segs)) * r)
                      for k in range(segs)])
    for i in range(len(rings) - 1):
        for k in range(segs):
            k2 = (k + 1) % segs
            B.f((rings[i][k], rings[i][k2], rings[i + 1][k2], rings[i + 1][k]), mat)
    if caps:
        B.f(tuple(reversed(rings[0])), mat)
        B.f(tuple(rings[-1]), mat)


def prism_x(B, pts_yz, x0, x1, mat):
    """Closed prism: a (y, z) polygon extruded from x0 to x1."""
    a = [B.v((x0, y, z)) for (y, z) in pts_yz]
    b = [B.v((x1, y, z)) for (y, z) in pts_yz]
    B.f(tuple(a), mat)
    B.f(tuple(reversed(b)), mat)
    n = len(pts_yz)
    for i in range(n):
        j = (i + 1) % n
        B.f((a[i], b[i], b[j], a[j]), mat)


def quad(B, corners, mat, uv=((0, 0), (1, 0), (1, 1), (0, 1))):
    """Textured quad from 4 corners (bottom-left, bottom-right, top-right, top-left as seen from its front)."""
    B.f(tuple(B.v(c) for c in corners), mat, uv=list(uv))


def plate(B, w, h, t, center, normal_axis, mat_front, mat_back, uv_front=((0, 0), (1, 0), (1, 1), (0, 1)),
          uv_back=None, edge_mat='MS_SignBack', r=0.0, segs=4):
    """Flat sign plate w x h x t. normal_axis: '-Y', '+Y', '-X', '+X' (direction its front faces). Rounded corners
    (radius r) via a fan; front/back faces get UVs mapped over the plate's bounds."""
    c = Vector(center)
    if normal_axis in ('-Y', '+Y'):
        s = -1 if normal_axis == '-Y' else 1
        right = Vector((1, 0, 0)) if s < 0 else Vector((-1, 0, 0))
        nrm = Vector((0, s, 0))
    else:
        s = -1 if normal_axis == '-X' else 1
        right = Vector((0, -1, 0)) if s < 0 else Vector((0, 1, 0))
        nrm = Vector((s, 0, 0))
    up = Vector((0, 0, 1))
    ol = rrect_outline(w / 2, h / 2, r, segs) if r > 0 else [(w / 2, -h / 2, 0, 0), (w / 2, h / 2, 0, 0),
                                                               (-w / 2, h / 2, 0, 0), (-w / 2, -h / 2, 0, 0)]
    # outline is CCW seen from the front when u = right, v = up
    pts2 = [(u, v) for (u, v, _, _) in ol]
    front = [B.v(c + nrm * (t / 2) + right * u + up * v) for (u, v) in pts2]
    back = [B.v(c - nrm * (t / 2) + right * u + up * v) for (u, v) in pts2]

    def uvmap(rect, u, v, mirror=False):
        u0, v0, u1, v1 = rect
        fu = (u + w / 2) / w
        fv = (v + h / 2) / h
        if mirror:
            fu = 1 - fu
        return (u0 + (u1 - u0) * fu, v0 + (v1 - v0) * fv)

    rf = (uv_front[0][0], uv_front[0][1], uv_front[2][0], uv_front[2][1])
    B.f(tuple(front), mat_front, uv=[uvmap(rf, u, v) for (u, v) in pts2])
    if uv_back is not None:
        rb = (uv_back[0][0], uv_back[0][1], uv_back[2][0], uv_back[2][1])
        B.f(tuple(reversed(back)), mat_back, uv=[uvmap(rb, u, v, mirror=True) for (u, v) in reversed(pts2)])
    else:
        B.f(tuple(reversed(back)), mat_back)
    n = len(pts2)
    for i in range(n):
        j = (i + 1) % n
        B.f((front[i], back[i], back[j], front[j]), edge_mat)


# ----------------------------------------------------------------------------------------------------------------
# assembly / export / previews
def assemble(name, parts, locators=()):
    """parts: list of (child name, UVB, (x, y, z) origin). locators: (name, position, parent child or None)."""
    root = make_empty(name, size=0.25)
    objs = {}
    for cname, B, origin in parts:
        o = to_obj(B, cname)
        if origin is not None and any(abs(x) > 0 for x in origin):
            o.data.transform(Matrix.Translation(-Vector(origin)))
            o.location = Vector(origin)
        o.parent = root
        objs[cname] = o
    for lname, pos, par in locators:
        p = objs[par] if par else root
        e = make_empty(lname, Vector(pos) - (p.location if par else Vector()), p, 0.08)
        objs[lname] = e
    return root, objs


def tris_of(root):
    return sum(kit.tri_count(o) for o in kit.descendants(root) if o.type == 'MESH')


def export(root, out_dir):
    path = os.path.join(out_dir, 'fbx', root.name + '.fbx')
    kit.export_fbx(path, kit.descendants(root))
    return 'fbx/' + root.name + '.fbx'


def preview(root, out_dir, label='threequarter', direction=(0.75, -1.0, 0.55), res=(720, 720), lens=50, margin=1.25,
            target=None):
    objs = [o for o in kit.descendants(root) if o.type == 'MESH']
    bpy.context.view_layer.update()
    pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    lo = Vector((min(p[i] for p in pts) for i in range(3)))
    hi = Vector((max(p[i] for p in pts) for i in range(3)))
    c = (lo + hi) / 2 if target is None else Vector(target)
    rad = (hi - lo).length / 2
    fov = 2 * math.atan(36 / (2 * lens))
    dist = rad * margin / math.sin(fov / 2)
    cam = kit.camera('Cam_' + label, c + Vector(direction).normalized() * dist, c, lens=lens)
    cam.data.clip_end = dist * 4 + 50
    path = os.path.join(out_dir, 'previews', f'{root.name}_{label}.png')
    kit.render(path, res)
    bpy.data.objects.remove(cam, do_unlink=True)
    return path


def setup_preview_scene():
    scn = kit.setup_render((720, 720))
    scn.display.shading.color_type = 'TEXTURE'
    return scn


def ground(size=30.0):
    """Preview-only ground plane (not exported)."""
    B = UVB()
    s = size / 2
    quad(B, [(-s, -s, -0.001), (s, -s, -0.001), (s, s, -0.001), (-s, s, -0.001)], 'MS_PreviewGround')
    return to_obj(B, 'PV_Ground')


kit.MATS['MS_PreviewGround'] = {'color': '#B9B4AA', 'note': 'preview only'}


# ----------------------------------------------------------------------------------------------------------------
# C1-C3 signals
def visor(B, center, radius, length, mat, a0=-50.0, a1=230.0, t=0.006, segs=14, droop=0.03):
    """Tunnel visor: an arc shell (open at the bottom) from the lens plane forward along -Y."""
    cx, cy, cz = center
    outer, inner = [], []
    for k in range(segs + 1):
        a = math.radians(a0 + (a1 - a0) * k / segs)
        dx, dz = math.cos(a), math.sin(a)
        for ring, rr in ((outer, radius + t), (inner, radius)):
            ring.append((B.v((cx + dx * rr, cy, cz + dz * rr)),
                         B.v((cx + dx * rr, cy - length, cz + dz * rr - droop * (1 + dz) / 2))))
    for k in range(segs):
        o0, o1 = outer[k], outer[k + 1]
        i0, i1 = inner[k], inner[k + 1]
        B.f((o0[0], o1[0], o1[1], o0[1]), mat)            # outside
        B.f((i0[1], i1[1], i1[0], i0[0]), mat)            # inside
        B.f((o0[1], o1[1], i1[1], i0[1]), mat)            # front rim
    for end in (0, segs):
        o, i = outer[end], inner[end]
        B.f((o[0], o[1], i[1], i[0]), mat)


def signal_head(B, top, lenses=('MS_LensRed', 'MS_LensAmber', 'MS_LensGreen'), section=0.36, depth=0.26):
    """3-section vehicle head (12 in lenses) hanging from `top` (x, y, z of the housing top centre), facing -Y.
    Returns the centre of the middle lens."""
    x, y, ztop = top
    gap = 0.006
    centers = []
    for k, lm in enumerate(lenses):
        zc = ztop - section / 2 - k * (section + gap)
        centers.append(zc)
        bbox(B, (section, depth, section), (x, y, zc), 'MS_SignalHousing', bevel=0.018)
        yf = y - depth / 2
        rev(B, [(0.0, 0.010), (0.08, 0.008), (0.148, -0.001), (0.152, -0.008)], 24, (x, yf, zc), (0, -1, 0), lm)
        rev(B, [(0.150, -0.004), (0.166, 0.004), (0.172, 0.014), (0.176, -0.004)], 24, (x, yf, zc), (0, -1, 0),
            'MS_SignalHousing')
        visor(B, (x, yf - 0.012, zc), 0.172, 0.24, 'MS_SignalHousing')
    # backplate with retroreflective yellow border, 0.66 x 1.38 m
    hb = len(lenses) * (section + gap) - gap
    zc = ztop - hb / 2
    bw, bh = section + 0.30, hb + 0.30
    yb = y + depth / 2 + 0.012
    plate(B, bw, bh, 0.012, (x, yb, zc), '-Y', 'MS_Backplate', 'MS_Backplate', edge_mat='MS_Backplate', r=0.06, segs=4)
    outer = rrect_outline(bw / 2, bh / 2, 0.06, 4)
    inner = rrect_outline(bw / 2 - 0.05, bh / 2 - 0.05, 0.035, 4)
    yy = yb - 0.006 - 0.0015
    ov = [B.v((x + u, yy, zc + w)) for (u, w, _, _) in outer]
    iv = [B.v((x + u, yy, zc + w)) for (u, w, _, _) in inner]
    for i in range(len(ov)):
        j = (i + 1) % len(ov)
        B.f((ov[i], ov[j], iv[j], iv[i]), 'MS_SignalYellow')
    # top cap + hanger stub
    bbox(B, (0.12, 0.12, 0.05), (x, y, ztop + 0.025), 'MS_SignalHousing', bevel=0.01)
    return Vector((x, y - depth / 2, centers[1]))


def arm_z(xa):
    """Height of the mast arm's axis at x (arm runs from the pole toward -X with a gentle rise)."""
    return 6.0 + 0.28 * max(0.0, (-xa - 0.2)) / 6.3


def traffic_signal_mast():
    B = UVB()
    # foundation plate, anchor bolts, pole with flange and cap
    bbox(B, (0.48, 0.48, 0.035), (0, 0, 0.0175), 'MS_PoleDark', bevel=0.006)
    for sx in (-1, 1):
        for sy in (-1, 1):
            cyl(B, 0.016, 0.11, (sx * 0.19, sy * 0.19, 0.03), mat='MS_Metal', segs=8)
            cyl(B, 0.03, 0.03, (sx * 0.19, sy * 0.19, 0.035), mat='MS_Metal', segs=6)
    rev(B, [(0.0, 0.035), (0.215, 0.035), (0.215, 0.07), (0.18, 0.11), (0.168, 0.16), (0.166, 0.35), (0.118, 6.72),
            (0.104, 6.80), (0.07, 6.845), (0.0, 6.86)], 20, (0, 0, 0), (0, 0, 1), 'MS_Pole')
    bbox(B, (0.10, 0.03, 0.22), (0, 0.168, 0.62), 'MS_PoleDark', bevel=0.008)          # handhole cover (back)
    # mast arm (6.5 m) on a clamp plate, tie rod above
    bbox(B, (0.14, 0.34, 0.46), (-0.15, 0, 6.0), 'MS_Pole', bevel=0.012)
    a0 = Vector((-0.2, 0, arm_z(-0.2)))
    a1 = Vector((-6.5, 0, arm_z(-6.5)))
    L = (a1 - a0).length
    rev(B, [(0.0, 0.0), (0.105, 0.0), (0.105, 0.06), (0.06, L - 0.03), (0.045, L), (0.0, L + 0.004)], 18, a0,
        (a1 - a0).normalized(), 'MS_Pole')
    pipe(B, [(-0.10, 0, 6.70), (-3.0, 0, arm_z(-3.0) + 0.07)], 0.018, 'MS_Pole', segs=8)
    bbox(B, (0.10, 0.22, 0.10), (-0.13, 0, 6.70), 'MS_Pole', bevel=0.01)
    bbox(B, (0.12, 0.16, 0.16), (-3.0, 0, arm_z(-3.0)), 'MS_Pole', bevel=0.01)
    # two hanging heads
    locs = []
    for xh in (-3.7, -6.05):
        zt = arm_z(xh) - 0.16
        bbox(B, (0.14, 0.2, 0.18), (xh, 0, arm_z(xh)), 'MS_Pole', bevel=0.012)
        cyl(B, 0.026, 0.14, (xh, 0, zt), mat='MS_Pole', segs=10)
        locs.append(signal_head(B, (xh, 0.0, zt)))
    # pole-mounted head facing -Y, on two bracket pipes
    yh = -0.40
    locs.append(signal_head(B, (0.0, yh, 4.05)))
    for zb in (3.92, 3.06):
        pipe(B, [(0, -0.10, zb), (0, yh + 0.14, zb)], 0.024, 'MS_Pole', segs=10)
        rev(B, [(0.155, -0.03), (0.155, 0.03)], 16, (0, 0, zb), (0, 0, 1), 'MS_Pole')
    # street name blade under the arm: MAPLE AVE (front), aluminium back
    xb = -1.85
    zb = arm_z(xb) - 0.16 - 0.225
    plate(B, 1.8, 0.45, 0.012, (xb, 0.0, zb), '-Y', 'MS_StreetBlade', 'MS_SignBack',
          uv_front=((0, 0.5), (1, 0.5), (1, 1), (0, 1)), r=0.03, segs=3)
    for dx in (-0.6, 0.6):
        bbox(B, (0.05, 0.05, 0.2), (xb + dx, 0.03, zb + 0.24), 'MS_Pole', bevel=0.008)
        bbox(B, (0.07, 0.24, 0.07), (xb + dx, 0.0, arm_z(xb + dx)), 'MS_Pole', bevel=0.01)
    root, objs = assemble('TrafficSignalMast', [('TrafficSignalMast_Mesh', B, None)],
                          [('HeadA', locs[0], None), ('HeadB', locs[1], None), ('HeadPole', locs[2], None)])
    notes = ('6.8 m pole (front -Y), 6.5 m mast arm at 6.0 m toward -X, two 3-section heads on the arm (HeadA 3.7 m, '
             'HeadB 6.05 m from the pole) and one on the pole (HeadPole); locators sit on the amber lens. Lenses are '
             'separate faces: MS_LensRed/Amber/Green (emission authored on; drive per state). Street name blade '
             'MAPLE AVE under the arm (UV: top half of T_StreetNames).')
    return root, notes, []


PED_POLE_OFFSET = 0.26     # mounting pole axis behind the ped head (Blender +Y)


def ped_head(B, z0=0.0, y0=0.0):
    """LED pedestrian head 0.46 x 0.46 m, origin at its bottom centre; returns the countdown centre."""
    W, H, D = 0.46, 0.46, 0.24
    zc = z0 + H / 2
    bbox(B, (W, D, H), (0, y0, zc), 'MS_SignalHousing', bevel=0.02)
    yf = y0 - D / 2
    # recessed face: dark panel, two overlapping icon faces on the left half, countdown window on the right
    plate(B, 0.42, 0.42, 0.004, (0, yf - 0.002, zc), '-Y', 'MS_PedPanelOff', 'MS_PedPanelOff',
          edge_mat='MS_SignalHousing', r=0.012, segs=2)
    s = 0.19
    for dz, mat in ((-0.0045, 'MS_PedHand'), (-0.0050, 'MS_PedWalk')):
        quad(B, [(-0.205, yf + dz, zc - s / 2), (-0.205 + s, yf + dz, zc - s / 2), (-0.205 + s, yf + dz, zc + s / 2),
                 (-0.205, yf + dz, zc + s / 2)], mat)
    cd = Vector((0.105, yf - 0.0045, zc))
    # thin divider between the halves and a frame around the countdown window
    bbox(B, (0.008, 0.006, 0.40), (0.0, yf - 0.006, zc), 'MS_SignalHousing', bevel=0.002)
    # hood: top and side plates leaning forward
    bbox(B, (W + 0.01, 0.17, 0.012), (0, yf - 0.085, z0 + H + 0.004), 'MS_SignalHousing', bevel=0.004,
         rot=Matrix.Rotation(math.radians(-8), 3, 'X'))
    for sx in (-1, 1):
        x0 = sx * (W / 2 + 0.004)
        prism_x(B, [(yf, z0 + 0.02), (yf - 0.17, z0 + H - 0.02), (yf - 0.17, z0 + H + 0.01), (yf, z0 + H + 0.01)],
                x0, x0 + sx * 0.012, 'MS_SignalHousing')
    # clamp brackets to a pole PED_POLE_OFFSET behind
    for zb in (z0 + 0.08, z0 + H - 0.08):
        bbox(B, (0.06, PED_POLE_OFFSET - D / 2 - 0.05, 0.03), (0, y0 + D / 2 + (PED_POLE_OFFSET - D / 2 - 0.05) / 2, zb),
             'MS_PoleDark', bevel=0.006)
        rev(B, [(0.060, -0.025), (0.072, -0.025), (0.072, 0.025), (0.060, 0.025)], 16, (0, y0 + PED_POLE_OFFSET, zb),
            (0, 0, 1), 'MS_PoleDark')
        cyl(B, 0.01, 0.03, (0.045, y0 + PED_POLE_OFFSET - 0.06, zb), axis=(1, 0, 0), mat='MS_Metal', segs=6)
    return cd


def ped_signal():
    B = UVB()
    cd = ped_head(B)
    root, objs = assemble('PedSignal', [('PedSignal_Mesh', B, None)], [('Countdown', cd, None)])
    notes = ('LED ped head 0.46 m, origin at the housing bottom centre; clamp brackets for a pole 0.26 m behind '
             '(Blender +Y = Unity -Z). Left half: MS_PedHand and MS_PedWalk on the same square (Walk 0.5 mm in '
             'front of Hand) - show one at a time. Right half MS_PedPanelOff; empty Countdown at its centre, facing '
             'out of the panel (Unity +Z), 0.19 m square window.')
    return root, notes, []


def push_button(B, z, y_face):
    """Accessible push-button box (yellow body, black face, round silver button with a raised arrow)."""
    bbox(B, (0.12, 0.075, 0.18), (0, y_face + 0.0375, z), 'MS_ButtonBox', bevel=0.012)
    bbox(B, (0.098, 0.006, 0.156), (0, y_face - 0.002, z), 'MS_SignalHousing', bevel=0.003)
    rev(B, [(0.0, 0.012), (0.022, 0.011), (0.027, 0.006), (0.028, -0.004)], 20, (0, y_face - 0.005, z - 0.02),
        (0, -1, 0), 'MS_Metal')
    # raised tactile arrow on the button (points -X = toward the crossing this button serves)
    arrow = [(-0.017, 0.0), (-0.004, 0.010), (-0.004, 0.004), (0.016, 0.004), (0.016, -0.004), (-0.004, -0.004),
             (-0.004, -0.010)]
    yb = y_face - 0.017
    top = [B.v((ax, yb - 0.003, z - 0.02 + az)) for ax, az in arrow]
    bot = [B.v((ax, yb + 0.003, z - 0.02 + az)) for ax, az in arrow]
    B.f(tuple(top), 'MS_SignalHousing')
    B.f(tuple(reversed(bot)), 'MS_SignalHousing')
    for i in range(len(arrow)):
        j = (i + 1) % len(arrow)
        B.f((top[i], bot[i], bot[j], top[j]), 'MS_SignalHousing')
    for k in range(5):                                        # speaker holes above the button
        cyl(B, 0.0035, 0.004, (-0.024 + k * 0.012, y_face - 0.006, z + 0.045), axis=(0, -1, 0),
            mat='MS_PoleDark', segs=6)


def ped_pole():
    B = UVB()
    R = 0.057
    rev(B, [(0.0, 0.0), (0.13, 0.0), (0.13, 0.022), (0.085, 0.05), (0.066, 0.085), (R, 0.11), (R, 3.17), (0.05, 3.195),
            (0.03, 3.205), (0.0, 3.21)], 16, (0, 0, 0), (0, 0, 1), 'MS_Pole')
    for k in range(4):
        a = math.radians(45 + 90 * k)
        cyl(B, 0.012, 0.035, (math.cos(a) * 0.105, math.sin(a) * 0.105, 0.02), mat='MS_Metal', segs=6)
    push_button(B, 1.0, -R - 0.075)
    # PUSH BUTTON FOR WALK SIGNAL sign (9 x 12 in) above the button, on two band clamps
    plate(B, 0.23, 0.305, 0.004, (0, -R - 0.012, 1.34), '-Y', 'MS_PushSign', 'MS_SignBack', r=0.012, segs=2)
    for zb in (1.25, 1.43):
        rev(B, [(R, -0.012), (R + 0.006, -0.012), (R + 0.006, 0.012), (R, 0.012)], 16, (0, 0, zb), (0, 0, 1),
            'MS_Metal')
    H = UVB()
    z0 = 2.6 - 0.23
    cd = ped_head(H, z0=z0, y0=-PED_POLE_OFFSET)
    root, objs = assemble('PedPole', [('PedPole_Mesh', B, None), ('PedHead', H, (0, -PED_POLE_OFFSET, z0))],
                          [('Countdown', cd, 'PedHead')])
    notes = ('3.2 m pole; PedHead (separate child, same layout as PedSignal) centred at 2.6 m facing -Y, its '
             'Countdown empty as child; push-button box at 1.0 m (yellow body, black face, silver button with a raised '
             'arrow pointing Unity +X, i.e. the crossing to the viewer\'s left) with the PUSH BUTTON sign above.')
    return root, notes, ['PUSH BUTTON FOR WALK SIGNAL']


# ----------------------------------------------------------------------------------------------------------------
# C4 street lamp, C6 bus shelter + bus stop sign, C7 sign posts
def street_lamp():
    B = UVB()
    bbox(B, (0.40, 0.40, 0.03), (0, 0, 0.015), 'MS_PoleDark', bevel=0.006)
    for sx in (-1, 1):
        for sy in (-1, 1):
            cyl(B, 0.014, 0.09, (sx * 0.15, sy * 0.15, 0.03), mat='MS_Metal', segs=8)
            cyl(B, 0.026, 0.026, (sx * 0.15, sy * 0.15, 0.03), mat='MS_Metal', segs=6)
    # decorative base shroud + tapered pole + cap
    rev(B, [(0.0, 0.03), (0.20, 0.03), (0.20, 0.10), (0.17, 0.16), (0.155, 0.45), (0.13, 0.52), (0.12, 0.56)], 20,
        (0, 0, 0), (0, 0, 1), 'MS_PoleDark')
    rev(B, [(0.115, 0.5), (0.11, 0.56), (0.066, 8.42), (0.05, 8.48), (0.0, 8.5)], 16, (0, 0, 0), (0, 0, 1), 'MS_Pole')
    # curved davit arm toward the street (-Y)
    pts = []
    for k in range(13):
        t = k / 12
        a = t * math.pi / 2
        pts.append((0.0, -0.55 * (1 - math.cos(a)), 8.05 + 0.55 * math.sin(a)))
    pts += [(0.0, -0.55 - 0.45 * t, 8.6 + 0.02 * t) for t in (0.5, 1.0)] + [(0.0, -1.55, 8.64), (0.0, -2.05, 8.66)]
    pipe(B, pts, 0.045, 'MS_Pole', segs=12)
    rev(B, [(0.0, 0.0), (0.075, 0.0), (0.075, 0.12), (0.05, 0.16), (0.0, 0.17)], 12, (0, 0, 7.98), (0, 0, 1), 'MS_Pole')
    # LED cobra head: rounded plan, domed top, flat lens underneath, slight upward tilt
    head = UVB()
    ol = rrect_outline(0.17, 0.40, 0.13, 6)
    prof = [(0.03, -0.035), (0.0, -0.02), (0.0, 0.01), (0.02, 0.045), (0.06, 0.07), (0.12, 0.085), (0.17, 0.09)]
    sweep(head, ol, prof, (0, 0, 0), (1, 0, 0), (0, 1, 0), (0, 0, 1), lambda k, i: 'MS_LampHead',
          cap_first='MS_LampHead', cap_last='MS_LampHead')
    head.uvs = [None] * len(head.faces)
    lens = rrect_outline(0.12, 0.27, 0.08, 6)
    lv = [head.v((u, w - 0.03, -0.0365)) for (u, w, _, _) in lens]
    head.f(tuple(reversed(lv)), 'MS_LampLens')
    hc = Vector((0.0, -2.40, 8.66))
    M = Matrix.Translation(hc) @ Matrix.Rotation(math.radians(-4), 4, 'X')
    B.extend(head, M)
    light = M @ Vector((0, -0.03, -0.04))
    # banner brackets toward the street with ball finials, banner 0.6 x 1.2 m (faces +-X, both sides printed)
    for zb in (4.15, 5.47):
        pipe(B, [(0, -0.05, zb), (0, -0.80, zb)], 0.018, 'MS_Pole', segs=8)
        rev(B, [(0.0, -0.03), (0.03, -0.015), (0.03, 0.015), (0.0, 0.035)], 10, (0, -0.83, zb), (0, -1, 0), 'MS_Pole')
        rev(B, [(0.07, -0.025), (0.08, -0.025), (0.08, 0.025), (0.07, 0.025)], 14, (0, 0, zb), (0, 0, 1), 'MS_Pole')
        cyl(B, 0.026, 0.62, (0, -0.11, zb), axis=(0, -1, 0), mat='MS_BannerHem', segs=10)
    plate(B, 0.60, 1.26, 0.006, (0.0, -0.42, 4.81), '-X', 'MS_Banner', 'MS_Banner',
          uv_back=((0, 0), (1, 0), (1, 1), (0, 1)), edge_mat='MS_BannerHem')
    root, objs = assemble('StreetLamp', [('StreetLamp_Mesh', B, None)], [('LampLight', light, None)])
    notes = ('8.5 m pole on a bolted base with shroud, curved arm reaching 2.4 m toward -Y (street), LED cobra head '
             'with emissive MS_LampLens underneath (LampLight locator at the lens, pointing down). Banner 0.6 x 1.2 m on '
             'two bracket arms toward the street, printed both sides (faces Unity +-X).')
    return root, notes, ['SPRING MARKET · SAT (lamp banner)']


def bus_shelter():
    B = UVB()
    yb = 0.65
    for xp in (-1.95, 0.0, 1.95):
        bbox(B, (0.08, 0.08, 2.40), (xp, yb, 1.20), 'MS_ShelterFrame', bevel=0.008)
        bbox(B, (0.16, 0.16, 0.012), (xp, yb, 0.006), 'MS_ShelterFrame', bevel=0.003)
    bbox(B, (0.06, 0.06, 2.36), (1.95, -0.27, 1.18), 'MS_ShelterFrame', bevel=0.006)
    # roof: fascia box, inset top panel, cantilever beams, front light strip
    bbox(B, (4.22, 1.80, 0.12), (0, -0.07, 2.46), 'MS_ShelterFrame', bevel=0.02)
    bbox(B, (4.10, 1.68, 0.03), (0, -0.07, 2.535), 'MS_ShelterRoof', bevel=0.012)
    for xp in (-1.95, 0.0, 1.95):
        bbox(B, (0.07, 1.55, 0.10), (xp, -0.12, 2.35), 'MS_ShelterFrame', bevel=0.01)
    bbox(B, (3.6, 0.05, 0.02), (0, -0.88, 2.395), 'MS_LampLens', bevel=0.004)
    # back glass (two panels) with rails
    for x0, x1 in ((-1.91, -0.04), (0.04, 1.91)):
        plate(B, x1 - x0, 1.98, 0.012, ((x0 + x1) / 2, yb, 1.215), '-Y', 'MS_ShelterGlass', 'MS_ShelterGlass',
              uv_back=((0, 0), (1, 0), (1, 1), (0, 1)), edge_mat='MS_ShelterFrame')
    for zr in (0.19, 2.24):
        bbox(B, (3.9, 0.05, 0.05), (0, yb, zr), 'MS_ShelterFrame', bevel=0.006)
    # side glass at +X
    plate(B, 0.86, 1.98, 0.012, (1.95, 0.19, 1.215), '+X', 'MS_ShelterGlass', 'MS_ShelterGlass',
          uv_back=((0, 0), (1, 0), (1, 1), (0, 1)), edge_mat='MS_ShelterFrame')
    for zr in (0.19, 2.24):
        bbox(B, (0.05, 0.92, 0.05), (1.95, 0.19, zr), 'MS_ShelterFrame', bevel=0.006)
    # ad lightbox at -X, printed both faces (+-X), emissive
    bbox(B, (0.15, 1.30, 1.92), (-1.95, 0.04, 1.20), 'MS_ShelterFrame', bevel=0.02)
    plate(B, 1.17, 1.72, 0.004, (-1.95 - 0.074, 0.04, 1.20), '-X', 'MS_AdPanel', 'MS_ShelterFrame',
          edge_mat='MS_ShelterFrame')
    plate(B, 1.17, 1.72, 0.004, (-1.95 + 0.074, 0.04, 1.20), '+X', 'MS_AdPanel', 'MS_ShelterFrame',
          edge_mat='MS_ShelterFrame')
    for yl in (-0.55, 0.62):
        bbox(B, (0.05, 0.05, 0.25), (-1.95, yl, 0.125), 'MS_ShelterFrame', bevel=0.006)
    # bench along the back: three slats on two brackets, middle armrest
    for k in range(3):
        bbox(B, (2.0, 0.095, 0.03), (0.0, 0.30 + k * 0.105, 0.46), 'MS_Wood', bevel=0.008)
    for xb in (-0.85, 0.85):
        bbox(B, (0.05, 0.40, 0.04), (xb, 0.42, 0.43), 'MS_ShelterFrame', bevel=0.006)
        bbox(B, (0.05, 0.04, 0.45), (xb, 0.60, 0.22), 'MS_ShelterFrame', bevel=0.006)
    bbox(B, (0.04, 0.32, 0.03), (0.0, 0.42, 0.64), 'MS_ShelterFrame', bevel=0.008)
    bbox(B, (0.04, 0.03, 0.17), (0.0, 0.28, 0.555), 'MS_ShelterFrame', bevel=0.006)
    root, objs = assemble('BusShelter', [('BusShelter_Mesh', B, None)], [('AdCenter', (-1.95, 0.04, 1.2), None)])
    notes = ('4.2 x 1.8 x 2.55 m glass shelter, open front toward -Y (street), back glass + side glass at +X '
             '(MS_ShelterGlass, opaque, reflection texture), roof with an emissive light strip (MS_LampLens) along the '
             'front edge, slatted bench, back-lit ad lightbox at -X printed on both faces (MS_AdPanel, emissive; '
             'AdCenter locator).')
    return root, notes, ['HERO SUMMONER', "Will today be the day you're chosen?", 'DAILY SUMMON READY']


def bus_stop_sign():
    B = UVB()
    rev(B, [(0.0, -0.02), (0.11, -0.02), (0.11, 0.02), (0.06, 0.04), (0.0, 0.045)], 14, (0, 0, 0), (0, 0, 1),
        'MS_Concrete')
    rev(B, [(0.0, 0.0), (0.038, 0.0), (0.038, 2.90), (0.03, 2.93), (0.0, 2.94)], 12, (0, 0, 0), (0, 0, 1), 'MS_Pole')
    plate(B, 0.30, 0.60, 0.006, (0, -0.048, 2.55), '-Y', 'MS_BusSign', 'MS_BusSign',
          uv_back=((0, 0), (1, 0), (1, 1), (0, 1)), r=0.02, segs=2)
    for zb in (2.33, 2.77):
        bbox(B, (0.06, 0.08, 0.035), (0, -0.03, zb), 'MS_Metal', bevel=0.006)
    # timetable case at 1.5 m
    bbox(B, (0.30, 0.05, 0.46), (0, -0.065, 1.45), 'MS_PoleDark', bevel=0.012)
    plate(B, 0.25, 0.40, 0.003, (0, -0.091, 1.45), '-Y', 'MS_TimetableFace', 'MS_TimetableFace',
          edge_mat='MS_PoleDark')
    for k in range(9):
        bbox(B, (0.19 if k % 3 else 0.12, 0.002, 0.008), (0, -0.094, 1.6 - k * 0.032), 'MS_PoleDark', bevel=0.0)
    root, objs = assemble('BusStopSign', [('BusStopSign_Mesh', B, None)])
    notes = '2.9 m pole, 0.3 x 0.6 m BUS STOP plate (routes 12 · 47) printed both sides facing -Y/+Y, timetable case.'
    return root, notes, ['BUS STOP · 12 · 47 · STOP 3417']


def sign_post():
    B = UVB()
    rev(B, [(0.0, 0.0), (0.04, 0.0), (0.04, 2.86), (0.045, 2.87), (0.045, 2.90), (0.0, 2.90)], 12, (0, 0, 0),
        (0, 0, 1), 'MS_Pole')
    rev(B, [(0.0, -0.02), (0.10, -0.02), (0.10, 0.02), (0.05, 0.035), (0.0, 0.04)], 12, (0, 0, 0), (0, 0, 1),
        'MS_Concrete')
    bbox(B, (0.07, 0.03, 0.05), (0, 0, 2.925), 'MS_PoleDark', bevel=0.006)      # clamp gripping the lower blade
    # two blades, crossed: MAPLE AVE along X (faces +-Y), 3RD ST along Y (faces +-X), printed both sides
    plate(B, 0.91, 0.23, 0.006, (0.0, 0.0, 3.03), '-Y', 'MS_StreetBlade', 'MS_StreetBlade',
          uv_front=((0, 0.5), (1, 0.5), (1, 1), (0, 1)), uv_back=((0, 0.5), (1, 0.5), (1, 1), (0, 1)), r=0.02, segs=2)
    plate(B, 0.91, 0.23, 0.006, (0.0, 0.0, 3.30), '-X', 'MS_StreetBlade', 'MS_StreetBlade',
          uv_front=((0, 0), (1, 0), (1, 0.5), (0, 0.5)), uv_back=((0, 0), (1, 0), (1, 0.5), (0, 0.5)), r=0.02, segs=2)
    bbox(B, (0.10, 0.10, 0.06), (0, 0, 3.165), 'MS_PoleDark', bevel=0.01)       # cross bracket between the blades
    bbox(B, (0.03, 0.07, 0.04), (0, 0, 3.42), 'MS_PoleDark', bevel=0.006)       # top clip on the upper blade
    root, objs = assemble('SignPost', [('SignPost_Mesh', B, None)])
    notes = ('Corner post 3.3 m with two crossed street-name blades: MAPLE AVE along Blender X (faces -Y/+Y), 3RD ST '
             'along Blender Y (faces -X/+X), both printed on both sides.')
    return root, notes, ['MAPLE AVE (300 block)', '3RD ST (100 block)']


def no_parking_sign():
    B = UVB()
    # galvanised U-channel post (front lip + web) 2.5 m
    bbox(B, (0.05, 0.012, 2.5), (0, 0.0, 1.25), 'MS_Metal', bevel=0.003)
    for sx in (-1, 1):
        bbox(B, (0.008, 0.035, 2.5), (sx * 0.021, 0.02, 1.25), 'MS_Metal', bevel=0.002)
    plate(B, 0.305, 0.457, 0.003, (0, -0.012, 2.18), '-Y', 'MS_NoParking', 'MS_SignBack', r=0.015, segs=2)
    for zb in (2.0, 2.36):
        cyl(B, 0.008, 0.012, (0, -0.0135, zb), axis=(0, -1, 0), mat='MS_Metal', segs=6)
    root, objs = assemble('NoParkingSign', [('NoParkingSign_Mesh', B, None)])
    return root, '2.5 m U-channel post with a 12 x 18 in NO PARKING sign facing -Y.', ['NO PARKING']


# ----------------------------------------------------------------------------------------------------------------
# C5 small street furniture
def ring_bolts(B, n, r, z, rb=0.012, h=0.02, mat='MS_Metal', a0=0.0):
    for k in range(n):
        a = a0 + 2 * math.pi * k / n
        cyl(B, rb, h, (math.cos(a) * r, math.sin(a) * r, z), mat=mat, segs=6)


def fire_hydrant():
    B = UVB()
    rev(B, [(0.0, 0.0), (0.17, 0.0), (0.17, 0.035), (0.14, 0.05), (0.122, 0.07), (0.118, 0.10), (0.118, 0.52),
            (0.132, 0.535), (0.132, 0.575), (0.122, 0.59)], 20, (0, 0, 0), (0, 0, 1), 'MS_HydrantRed')
    ring_bolts(B, 6, 0.152, 0.035, mat='MS_HydrantRed', rb=0.014)
    ring_bolts(B, 6, 0.128, 0.575, mat='MS_HydrantRed', rb=0.011, h=0.018)
    rev(B, [(0.125, 0.59), (0.12, 0.635), (0.10, 0.69), (0.065, 0.735), (0.035, 0.75), (0.0, 0.752)], 20, (0, 0, 0),
        (0, 0, 1), 'MS_HydrantCap')
    cyl(B, 0.032, 0.045, (0, 0, 0.745), mat='MS_HydrantCap', segs=5)
    for sx in (-1, 1):                                    # 2.5 in hose nozzles with caps and chains
        cyl(B, 0.045, 0.10, (sx * 0.10, 0, 0.42), axis=(sx, 0, 0), mat='MS_HydrantRed', segs=12)
        cyl(B, 0.056, 0.04, (sx * 0.20, 0, 0.42), axis=(sx, 0, 0), mat='MS_HydrantRed', segs=6, bevel=0.006)
        pipe(B, [(sx * 0.22, -0.03, 0.39), (sx * 0.20, -0.06, 0.33), (sx * 0.15, -0.07, 0.31), (sx * 0.11, -0.09, 0.33)],
             0.005, 'MS_Metal', segs=5)
    cyl(B, 0.065, 0.10, (0, -0.10, 0.36), axis=(0, -1, 0), mat='MS_HydrantRed', segs=14)   # 4.5 in pumper nozzle
    cyl(B, 0.08, 0.05, (0, -0.20, 0.36), axis=(0, -1, 0), mat='MS_HydrantRed', segs=6, bevel=0.008)
    cyl(B, 0.022, 0.03, (0, -0.25, 0.36), axis=(0, -1, 0), mat='MS_HydrantRed', segs=5)
    root, objs = assemble('FireHydrant', [('FireHydrant_Mesh', B, None)])
    return root, 'Classic hydrant 0.75 m: red body, yellow bonnet, pumper nozzle facing -Y, chained side caps.', []


def trash_can():
    B = UVB()
    for k in range(4):
        a = math.radians(45 + 90 * k)
        bbox(B, (0.06, 0.06, 0.06), (math.cos(a) * 0.24, math.sin(a) * 0.24, 0.03), 'MS_PoleDark', bevel=0.01)
    rev(B, [(0.0, 0.06), (0.262, 0.06), (0.262, 0.86), (0.0, 0.86)], 24, (0, 0, 0), (0, 0, 1), 'MS_PoleDark')  # liner
    for z0, z1 in ((0.06, 0.13), (0.80, 0.87)):
        rev(B, [(0.27, z0), (0.30, z0), (0.305, z0 + 0.01), (0.305, z1 - 0.01), (0.30, z1), (0.27, z1)], 24,
            (0, 0, 0), (0, 0, 1), 'MS_PoleDark')
    n = 22
    for k in range(n):
        a = 2 * math.pi * k / n
        bbox(B, (0.062, 0.022, 0.70), (math.cos(a) * 0.285, math.sin(a) * 0.285, 0.465), 'MS_TrashGreen',
             bevel=0.006, rot=Matrix.Rotation(a + math.pi / 2, 3, 'Z'))
    rev(B, [(0.0, 0.87), (0.31, 0.87), (0.31, 0.905), (0.29, 0.95), (0.24, 1.00), (0.16, 1.04), (0.07, 1.06),
            (0.0, 1.065)], 24, (0, 0, 0), (0, 0, 1), 'MS_PoleDark')
    # dome lid opening facing -Y with a little hood
    bbox(B, (0.26, 0.03, 0.085), (0, -0.258, 0.968), 'MS_Shadow', bevel=0.012,
         rot=Matrix.Rotation(math.radians(-42), 3, 'X'))
    root, objs = assemble('TrashCan', [('TrashCan_Mesh', B, None)])
    return root, 'Municipal bin 0.6 m dia x 1.07 m: steel bands, 22 dark-green slats, dome lid with a -Y opening.', []


def news_box(name, body_mat, u0, u1):
    B = UVB()
    bbox(B, (0.38, 0.32, 0.03), (0, 0.02, 0.015), 'MS_PoleDark', bevel=0.006)
    bbox(B, (0.10, 0.10, 0.32), (0, 0.02, 0.18), body_mat, bevel=0.01)
    bbox(B, (0.50, 0.42, 0.60), (0, 0.0, 0.64), body_mat, bevel=0.02)
    bbox(B, (0.52, 0.45, 0.04), (0, 0.0, 0.955), body_mat, bevel=0.012,
         rot=Matrix.Rotation(math.radians(6), 3, 'X'))                  # sloped lid
    plate(B, 0.46, 0.46, 0.006, (0, -0.212, 0.64), '-Y', 'MS_NewsFront', body_mat,
          uv_front=((u0, 0), (u1, 0), (u1, 1), (u0, 1)), edge_mat=body_mat)
    bbox(B, (0.14, 0.09, 0.11), (0.15, -0.20, 0.885), 'MS_Metal', bevel=0.012)     # coin mechanism
    bbox(B, (0.05, 0.006, 0.008), (0.15, -0.247, 0.90), 'MS_Shadow', bevel=0.0)
    cyl(B, 0.012, 0.012, (0.18, -0.245, 0.865), axis=(0, -1, 0), mat='MS_PoleDark', segs=8)
    root, objs = assemble(name, [(name + '_Mesh', B, None)])
    return root


def news_box_daily():
    root = news_box('NewsBox_DailyNews', 'MS_NewsBoxBlue', 0.0, 0.5)
    return root, 'Newspaper vending box, blue; front door texture = left half of T_NewsBoxFronts.', \
        ['DAILY NEWS', 'The Daily News', '50¢']


def news_box_weekly():
    root = news_box('NewsBox_CityWeekly', 'MS_NewsBoxYellow', 0.5, 1.0)
    return root, 'Newspaper vending box, yellow; front door texture = right half of T_NewsBoxFronts.', \
        ['CITY WEEKLY', 'City Weekly', 'FREE']


def bollard():
    B = UVB()
    rev(B, [(0.0, 0.0), (0.115, 0.0), (0.115, 0.03), (0.095, 0.055), (0.088, 0.08), (0.088, 0.72)], 20, (0, 0, 0),
        (0, 0, 1), 'MS_PoleDark')
    rev(B, [(0.088, 0.72), (0.0895, 0.722), (0.0895, 0.798), (0.088, 0.80)], 20, (0, 0, 0), (0, 0, 1), 'MS_Reflective')
    rev(B, [(0.088, 0.80), (0.088, 0.86), (0.075, 0.915), (0.045, 0.945), (0.0, 0.955)], 20, (0, 0, 0), (0, 0, 1),
        'MS_PoleDark')
    root, objs = assemble('Bollard', [('Bollard_Mesh', B, None)])
    return root, 'Steel bollard 0.95 m with a retroreflective band (MS_Reflective) at 0.72-0.80 m.', []


def bench():
    B = UVB()
    for xs in (-0.82, 0.0, 0.82):
        end = abs(xs) > 0.1
        bbox(B, (0.05, 0.05, 0.43), (xs, -0.17, 0.215), 'MS_PoleDark', bevel=0.008)     # front leg
        bbox(B, (0.05, 0.05, 0.85), (xs, 0.21, 0.40), 'MS_PoleDark', bevel=0.008,
             rot=Matrix.Rotation(math.radians(-12), 3, 'X'))                             # back leg / back support
        bbox(B, (0.05, 0.46, 0.05), (xs, 0.02, 0.42), 'MS_PoleDark', bevel=0.008,
             rot=Matrix.Rotation(math.radians(-4), 3, 'X'))                              # seat bearer
        for yy in (-0.17, 0.17):
            bbox(B, (0.12, 0.08, 0.012), (xs, yy if yy < 0 else 0.13, 0.006), 'MS_PoleDark', bevel=0.003)
        if end:
            pipe(B, [(xs, 0.18, 0.66), (xs, -0.05, 0.64), (xs, -0.19, 0.62), (xs, -0.20, 0.44)], 0.022, 'MS_PoleDark',
                 segs=8)
    for k in range(4):                                                                   # seat slats
        bbox(B, (1.80, 0.085, 0.032), (0, -0.17 + k * 0.105, 0.455 - k * 0.006), 'MS_Wood', bevel=0.008)
    for k in range(3):                                                                   # back slats
        z = 0.56 + k * 0.11
        bbox(B, (1.80, 0.032, 0.085), (0, 0.235 + (z - 0.56) * 0.21, z), 'MS_Wood', bevel=0.008,
             rot=Matrix.Rotation(math.radians(-12), 3, 'X'))
    root, objs = assemble('Bench', [('Bench_Mesh', B, None)])
    return root, 'Park bench 1.8 m: cast frames (3) with armrests, 4 seat + 3 back wood slats; faces -Y.', []


def parking_meter():
    B = UVB()
    rev(B, [(0.0, 0.0), (0.08, 0.0), (0.08, 0.02), (0.045, 0.04), (0.04, 0.06), (0.04, 1.02), (0.0, 1.02)], 12,
        (0, 0, 0), (0, 0, 1), 'MS_Pole')
    bbox(B, (0.10, 0.10, 0.06), (0, 0, 1.03), 'MS_Pole', bevel=0.01)
    bbox(B, (0.22, 0.17, 0.30), (0, 0, 1.21), 'MS_MeterHead', bevel=0.03)
    rev(B, [(0.0, -0.085), (0.11, -0.085), (0.11, 0.085), (0.0, 0.085)], 16, (0, 0, 1.36), (0, 1, 0), 'MS_MeterHead')
    plate(B, 0.13, 0.065, 0.006, (0, -0.087, 1.29), '-Y', 'MS_MeterScreen', 'MS_MeterHead', edge_mat='MS_PoleDark',
          r=0.01, segs=2)
    bbox(B, (0.035, 0.01, 0.006), (-0.045, -0.088, 1.20), 'MS_Shadow', bevel=0.0)      # coin slot
    bbox(B, (0.06, 0.01, 0.008), (0.035, -0.088, 1.16), 'MS_Shadow', bevel=0.0)        # card slot
    for dx in (-0.03, 0.03):
        cyl(B, 0.011, 0.008, (dx, -0.085, 1.235), axis=(0, -1, 0), mat='MS_Metal', segs=10)
    bbox(B, (0.17, 0.12, 0.012), (0, -0.01, 1.468), 'MS_MeterHead', bevel=0.004,
         cap_mats=('MS_SolarPanel', 'MS_MeterHead'), rot=Matrix.Rotation(math.radians(-22), 3, 'X'))
    root, objs = assemble('ParkingMeter', [('ParkingMeter_Mesh', B, None)])
    return root, 'Single-space smart meter 1.5 m: post, head with LCD, coin/card slots, buttons, solar fin.', []


def torus(B, R, r, centre, axis, mat, segs=32, tube=8):
    prof = [(R + r * math.cos(2 * math.pi * k / tube), r * math.sin(2 * math.pi * k / tube)) for k in range(tube + 1)]
    sub = UVB()
    revolve(sub, prof, segs, Vector(centre), Vector(axis), mat)
    sub.uvs = [None] * len(sub.faces)
    B.extend(sub)


def wheel(B, cx, cz, y=0.0):
    c = (cx, y, cz)
    torus(B, 0.318, 0.019, c, (0, 1, 0), 'MS_Tire', segs=32, tube=8)
    torus(B, 0.297, 0.008, c, (0, 1, 0), 'MS_Metal', segs=28, tube=5)
    cyl(B, 0.022, 0.09, (cx, y - 0.045, cz), axis=(0, 1, 0), mat='MS_Metal', segs=10)
    for k in range(16):
        a = 2 * math.pi * k / 16
        side = 0.03 if k % 2 else -0.03
        pipe(B, [(cx, y + side, cz), (cx + math.cos(a) * 0.29, y, cz + math.sin(a) * 0.29)], 0.0018, 'MS_Metal',
             segs=3, caps=False)


def bicycle(B, y=0.0):
    rh, fh = (-0.52, 0.34), (0.52, 0.34)
    bb, sc = (-0.04, 0.27), (-0.17, 0.79)
    ht_top, ht_bot = (0.40, 0.84), (0.44, 0.69)
    wheel(B, *rh, y=y)
    wheel(B, *fh, y=y)
    P = lambda xz, dy=0.0: (xz[0], y + dy, xz[1])
    tube = 0.017
    pipe(B, [P(sc), P(ht_top)], tube, 'MS_BikeFrame', segs=8)                   # top tube
    pipe(B, [P(bb), P(ht_bot)], 0.02, 'MS_BikeFrame', segs=8)                   # down tube
    pipe(B, [P(bb), P(sc)], tube, 'MS_BikeFrame', segs=8)                       # seat tube
    pipe(B, [P(ht_bot), P(ht_top), P((0.385, 0.90))], 0.022, 'MS_BikeFrame', segs=8)   # head tube
    for dy in (-0.035, 0.035):
        pipe(B, [P(bb, dy * 0.6), P(rh, dy)], 0.011, 'MS_BikeFrame', segs=6)            # chain stays
        pipe(B, [P(sc, dy * 0.3), P(rh, dy)], 0.010, 'MS_BikeFrame', segs=6)            # seat stays
        pipe(B, [P((0.45, 0.66), dy * 0.5), P((0.49, 0.50), dy), P(fh, dy)], 0.011, 'MS_BikeFrame', segs=6)  # fork
    pipe(B, [P(sc), P((-0.20, 0.93))], 0.013, 'MS_Metal', segs=8)               # seat post
    bbox(B, (0.25, 0.13, 0.05), (-0.22, y, 0.955), 'MS_BikeSeat', bevel=0.02)   # saddle
    pipe(B, [P((0.385, 0.90)), P((0.36, 0.97))], 0.012, 'MS_Metal', segs=8)     # stem
    bar = [(0.36, y + s * 0.0, 0.97) for s in (0,)]
    for s in (-1, 1):
        pipe(B, [(0.36, y, 0.97), (0.34, y + s * 0.18, 0.975), (0.27, y + s * 0.27, 0.98)], 0.011, 'MS_Metal', segs=6,
             caps=False)
        pipe(B, [(0.29, y + s * 0.245, 0.98), (0.21, y + s * 0.29, 0.98)], 0.016, 'MS_Tire', segs=8)   # grips
    # drivetrain on the right side (-Y): chainring, cranks, pedals
    cyl(B, 0.095, 0.006, (bb[0], y - 0.045, bb[1]), axis=(0, 1, 0), mat='MS_Metal', segs=20)
    for s, ang in ((-1, 25), (1, 205)):
        a = math.radians(ang)
        end = (bb[0] + math.cos(a) * 0.17, bb[1] + math.sin(a) * 0.17)
        yy = y + s * 0.06
        pipe(B, [(bb[0], yy, bb[1]), (end[0], yy, end[1])], 0.009, 'MS_Metal', segs=6)
        bbox(B, (0.10, 0.07, 0.022), (end[0], yy + s * 0.04, end[1]), 'MS_BikeSeat', bevel=0.006)
    pipe(B, [(bb[0], y - 0.045, bb[1] + 0.095), (rh[0], y - 0.045, rh[1] + 0.035)], 0.004, 'MS_PoleDark', segs=4)
    pipe(B, [(bb[0], y - 0.045, bb[1] - 0.095), (rh[0], y - 0.045, rh[1] - 0.035)], 0.004, 'MS_PoleDark', segs=4)


def bike_rack():
    R = UVB()
    pts = [(-0.36, 0, -0.03), (-0.36, 0, 0.55)]
    for k in range(1, 12):
        a = math.pi - math.pi * k / 12
        pts.append((0.36 * math.cos(a), 0, 0.55 + 0.25 * math.sin(a)))
    pts += [(0.36, 0, 0.55), (0.36, 0, -0.03)]
    pipe(R, pts, 0.024, 'MS_Metal', segs=12)
    for sx in (-0.36, 0.36):
        rev(R, [(0.0, 0.0), (0.075, 0.0), (0.075, 0.012), (0.035, 0.03), (0.0, 0.03)], 12, (sx, 0, 0), (0, 0, 1),
            'MS_Metal')
    Bk0 = UVB()
    yb = -0.075
    bicycle(Bk0, y=yb)
    Bk = UVB()
    Bk.extend(Bk0, Matrix.Translation((0.30, 0.0, 0.0)) @ Matrix.Rotation(math.radians(2.5), 4, 'X'))
    L = UVB()                                               # U-lock through the seat tube and the rack
    xl = 0.30 - 0.155
    pipe(L, [(xl, yb - 0.05, 0.62), (xl, yb - 0.05, 0.44), (xl, 0.035, 0.44), (xl, 0.035, 0.62)], 0.009,
         'MS_PoleDark', segs=8)
    bbox(L, (0.04, 0.13, 0.035), (xl, -0.01, 0.635), 'MS_ButtonBox', bevel=0.008)
    root, objs = assemble('BikeRack', [('Rack', R, None), ('Bicycle', Bk, None), ('Lock', L, None)])
    return root, ('Stainless inverted-U rack (0.72 x 0.80 m) with a teal city bike locked to it (U-lock through the '
                  'seat tube). Rack, Bicycle and Lock are separate children (delete Bicycle/Lock for an empty rack).'), []


def tree_grate():
    B = UVB()
    top, th = 0.006, 0.03
    N = 48

    def annulus(r_in_fn, r_out_fn, mat):
        ins, outs = [], []
        for k in range(N):
            a = 2 * math.pi * k / N
            ri, ro = r_in_fn(a), r_out_fn(a)
            c, s_ = math.cos(a), math.sin(a)
            ins.append((B.v((c * ri, s_ * ri, top)), B.v((c * ri, s_ * ri, top - th))))
            outs.append((B.v((c * ro, s_ * ro, top)), B.v((c * ro, s_ * ro, top - th))))
        for k in range(N):
            j = (k + 1) % N
            B.f((ins[k][0], outs[k][0], outs[j][0], ins[j][0]), mat)
            B.f((ins[j][1], outs[j][1], outs[k][1], ins[k][1]), mat)
            B.f((outs[k][0], outs[k][1], outs[j][1], outs[j][0]), mat)
            B.f((ins[j][0], ins[j][1], ins[k][1], ins[k][0]), mat)

    sq = lambda a: 0.6 / max(abs(math.cos(a)), abs(math.sin(a)))
    annulus(lambda a: 0.555, sq, 'MS_CastIron')                         # square frame around the last ring
    for r0, r1 in ((0.25, 0.29), (0.37, 0.405), (0.48, 0.515)):
        annulus(lambda a, r0=r0: r0, lambda a, r1=r1: r1, 'MS_CastIron')
    for k in range(20):                                                  # radial bars between the rings
        a = 2 * math.pi * k / 20 + math.pi / 20
        for r0, r1 in ((0.285, 0.375), (0.40, 0.485), (0.51, 0.56)):
            rm = (r0 + r1) / 2
            bbox(B, (r1 - r0 + 0.01, 0.022, th), (math.cos(a) * rm, math.sin(a) * rm, top - th / 2), 'MS_CastIron',
                 bevel=0.003, rot=Matrix.Rotation(a, 3, 'Z'))
    bbox(B, (1.18, 1.18, 0.02), (0, 0, -0.06), 'MS_Mulch', bevel=0.0)  # mulch seen through the slots
    root, objs = assemble('TreeGrate', [('TreeGrate_Mesh', B, None)])
    return root, ('1.2 m square cast-iron tree grate, 0.5 m round opening, top 6 mm above the sidewalk (z = 0), mulch '
                  'bed 6 cm below.'), []


def blob(B, centre, radius, mat, seed, squash=0.75, subdiv=2):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    rng = np.random.default_rng(seed)
    ph = rng.uniform(0, 6.28, 6)
    off = len(B.verts)
    for v in bm.verts:
        p = v.co
        n = 1.0 + 0.10 * math.sin(3 * p.x + ph[0]) * math.cos(2 * p.y + ph[1]) + 0.07 * math.sin(4 * p.z + ph[2])
        B.v((centre[0] + p.x * radius * n, centre[1] + p.y * radius * n, centre[2] + p.z * radius * n * squash))
    for f in bm.faces:
        B.f(tuple(off + v.index for v in f.verts), mat)
    bm.free()


def planter():
    B = UVB()
    bbox(B, (1.40, 0.60, 0.52), (0, 0, 0.26), 'MS_Concrete', bevel=0.03)
    bbox(B, (1.30, 0.50, 0.02), (0, 0, 0.515), 'MS_Soil', bevel=0.0)
    for k, (x, r, m) in enumerate([(-0.42, 0.24, 'MS_Shrub'), (0.0, 0.28, 'MS_ShrubDark'), (0.43, 0.23, 'MS_Shrub')]):
        blob(B, (x, 0.0, 0.52 + r * 0.55), r, m, seed=k + 3, subdiv=3)
        for j, (dx, dy, dz, f) in enumerate(((-0.6, 0.25, -0.2, 0.62), (0.55, -0.3, -0.15, 0.6))):
            blob(B, (x + dx * r, dy * r, 0.52 + r * (0.55 + dz)), r * f, m, seed=10 * k + j + 50, subdiv=3)
    rng = np.random.default_rng(17)
    for k in range(14):
        x, y = rng.uniform(-0.6, 0.6), rng.uniform(-0.22, 0.22)
        z = 0.52 + 0.30 * math.exp(-((x + 0.42) ** 2 + y ** 2) * 9) + 0.36 * math.exp(-(x ** 2 + y ** 2) * 8) + \
            0.29 * math.exp(-((x - 0.43) ** 2 + y ** 2) * 9) - 0.005
        blob(B, (x, y, z), 0.025, 'MS_Flower', seed=40 + k, squash=1.0, subdiv=1)
    root, objs = assemble('Planter', [('Planter_Mesh', B, None)])
    return root, 'Concrete planter 1.4 x 0.6 x 0.52 m with three low shrubs and small pink flowers.', []


def manhole():
    B = UVB()
    R = 0.325
    N = 48
    rev(B, [(R, -0.004), (R + 0.004, 0.003), (R + 0.035, 0.003), (R + 0.045, -0.006)], N, (0, 0, 0), (0, 0, 1),
        'MS_CastIron')
    top = []
    for k in range(N):
        a = 2 * math.pi * k / N
        top.append((math.cos(a) * R, math.sin(a) * R))
    vs = [B.v((x, y, 0.004)) for (x, y) in top]
    B.f(tuple(vs), 'MS_Manhole', uv=[(0.5 + x / (2 * R), 0.5 + y / (2 * R)) for (x, y) in top])
    vb = [B.v((x, y, -0.004)) for (x, y) in top]
    B.f(tuple(reversed(vb)), 'MS_CastIron')
    for k in range(N):
        j = (k + 1) % N
        B.f((vs[k], vb[k], vb[j], vs[j]), 'MS_CastIron')
    root, objs = assemble('ManholeCover', [('ManholeCover_Mesh', B, None)])
    return root, '0.65 m cast-iron cover (pattern via T_ManholeCover, planar UVs) in a frame, top 4 mm above z = 0.', \
        ['CITY SEWER · 1926 (manhole)']


def storm_drain():
    B = UVB()
    # curb section with the inlet slot (curb face at y = 0, road toward -Y, curb top 0.15)
    bbox(B, (1.4, 0.18, 0.02), (0, 0.09, 0.01), 'MS_Concrete', bevel=0.004)
    bbox(B, (1.4, 0.18, 0.045), (0, 0.09, 0.1275), 'MS_Concrete', bevel=0.012)
    for sx in (-1, 1):
        bbox(B, (0.25, 0.18, 0.11), (sx * 0.575, 0.09, 0.075), 'MS_Concrete', bevel=0.008)
    bbox(B, (0.90, 0.16, 0.09), (0, 0.10, 0.065), 'MS_Shadow', bevel=0.0)          # dark throat
    bbox(B, (0.92, 0.012, 0.03), (0, -0.004, 0.118), 'MS_CastIron', bevel=0.003)    # steel lintel face
    # gutter grate on the road in front of the inlet + dark pit
    bbox(B, (0.95, 0.50, 0.015), (0, -0.26, -0.0075), 'MS_CastIron', bevel=0.004)
    bbox(B, (0.85, 0.40, 0.04), (0, -0.26, -0.03), 'MS_Shadow', bevel=0.0)
    for k in range(11):
        bbox(B, (0.028, 0.40, 0.022), (-0.36 + k * 0.072, -0.26, 0.001), 'MS_CastIron', bevel=0.004)
    for yy in (-0.39, -0.13):
        bbox(B, (0.85, 0.03, 0.02), (0, yy, 0.0), 'MS_CastIron', bevel=0.004)
    # curb plaque (top of the curb, readable from the sidewalk)
    N = 24
    pts = [(math.cos(2 * math.pi * k / N) * 0.05, math.sin(2 * math.pi * k / N) * 0.05) for k in range(N)]
    vs = [B.v((0.55 + x, 0.09 + y, 0.1525)) for (x, y) in pts]
    B.f(tuple(vs), 'MS_DrainMarker', uv=[(0.5 + x / 0.1, 0.5 + y / 0.1) for (x, y) in pts])
    vb = [B.v((0.55 + x, 0.09 + y, 0.148)) for (x, y) in pts]
    B.f(tuple(reversed(vb)), 'MS_Metal')
    for k in range(N):
        j = (k + 1) % N
        B.f((vs[k], vb[k], vb[j], vs[j]), 'MS_Metal')
    root, objs = assemble('StormDrain', [('StormDrain_Mesh', B, None)])
    return root, ('Curb inlet section (1.4 m, curb face at y = 0, top 0.15 m) with a dark throat and steel lintel, '
                  'gutter grate on the road in front (-Y), NO DUMPING plaque on the curb top.'), \
        ['NO DUMPING · DRAINS TO RIVER']


def a_frame_sign():
    B = UVB()
    ang = math.radians(13)
    for side in (-1, 1):
        sub = UVB()
        plate(sub, 0.56, 0.80, 0.012, (0, -0.012 * 0 - 0.012, 0), '-Y', 'MS_Chalkboard', 'MS_Wood',
              edge_mat='MS_Wood')
        for (sx, sz, px, pz) in ((0.64, 0.045, 0, 0.42), (0.64, 0.045, 0, -0.42), (0.045, 0.88, 0.30, 0),
                                 (0.045, 0.88, -0.30, 0)):
            bbox(sub, (sx, 0.03, sz), (px, -0.004, pz), 'MS_Wood', bevel=0.006)
        rot = Matrix.Rotation(side * ang, 4, 'X') if side < 0 else \
            Matrix.Rotation(math.pi, 4, 'Z') @ Matrix.Rotation(-ang, 4, 'X')
        M = Matrix.Translation((0, 0, 0.0)) @ rot
        # place so the top hinge is at z = 0.92, y = 0 and the board hangs down-outward
        T = Matrix.Translation((0, 0, 0.857)) @ (Matrix.Rotation(-ang, 4, 'X') if side < 0 else
                                                Matrix.Rotation(math.pi, 4, 'Z') @ Matrix.Rotation(-ang, 4, 'X')) @ \
            Matrix.Translation((0, -0.02, -0.44))
        B.extend(sub, T)
    for sx in (-0.2, 0.2):
        cyl(B, 0.012, 0.06, (sx - 0.03, 0, 0.852), axis=(1, 0, 0), mat='MS_Metal', segs=8)
    pipe(B, [(0.26, -0.115, 0.36), (0.26, 0.115, 0.36)], 0.004, 'MS_Metal', segs=4)
    pipe(B, [(-0.26, -0.115, 0.36), (-0.26, 0.115, 0.36)], 0.004, 'MS_Metal', segs=4)
    root, objs = assemble('AFrameSign', [('AFrameSign_Mesh', B, None)])
    return root, 'Cafe sandwich board 0.64 x 0.87 m, chalkboard faces on both sides (front faces -Y).', \
        ['FRESH COFFEE', 'come on in!']


def cafe_table():
    B = UVB()
    rev(B, [(0.0, 0.0), (0.24, 0.0), (0.24, 0.015), (0.16, 0.035), (0.05, 0.06), (0.03, 0.09)], 20, (0, 0, 0),
        (0, 0, 1), 'MS_CafeMetal')
    cyl(B, 0.028, 0.62, (0, 0, 0.08), mat='MS_CafeMetal', segs=12)
    rev(B, [(0.0, 0.69), (0.10, 0.69), (0.10, 0.70), (0.0, 0.70)], 16, (0, 0, 0), (0, 0, 1), 'MS_CafeMetal')
    rev(B, [(0.0, 0.70), (0.30, 0.70), (0.305, 0.705), (0.305, 0.72), (0.30, 0.725), (0.0, 0.725)], 32, (0, 0, 0),
        (0, 0, 1), ['MS_CafeMetal', 'MS_CafeMetal', 'MS_CafeMetal', 'MS_CafeMetal', 'MS_CafeTop'])
    root, objs = assemble('CafeTable', [('CafeTable_Mesh', B, None)])
    return root, 'Bistro table 0.6 m round top at 0.725 m on a cast pedestal.', []


def cafe_chair():
    B = UVB()
    legs = [(-0.19, -0.19, 0.0), (0.19, -0.19, 0.0), (-0.17, 0.18, 0.0), (0.17, 0.18, 0.0)]
    for (x, y, _) in legs[:2]:
        pipe(B, [(x * 1.08, y * 1.1, 0.0), (x * 0.92, y * 0.95, 0.45)], 0.011, 'MS_CafeMetal', segs=6)
    for (x, y, _) in legs[2:]:
        pipe(B, [(x * 1.08, y * 1.15, 0.0), (x * 0.95, y * 0.98, 0.45), (x * 0.92, y * 1.12, 0.86)], 0.011,
             'MS_CafeMetal', segs=6)
    ring = [(0.19 * math.cos(2 * math.pi * k / 16), 0.19 * math.sin(2 * math.pi * k / 16), 0.15) for k in range(17)]
    pipe(B, ring, 0.006, 'MS_CafeMetal', segs=4, caps=False)
    rev(B, [(0.0, 0.43), (0.20, 0.43), (0.205, 0.44), (0.20, 0.45), (0.0, 0.45)], 20, (0, 0, 0), (0, 0, 1),
        'MS_CafeMetal')
    for k in range(5):                                                   # seat slats
        bbox(B, (0.075, 0.38, 0.016), (-0.16 + k * 0.08, 0.0, 0.465), 'MS_Wood', bevel=0.005)
    top = [(0.155 * math.cos(a), 0.20 + 0.03 * math.sin(a), 0.86) for a in np.linspace(math.radians(200),
                                                                                       math.radians(340), 9)]
    pipe(B, top, 0.012, 'MS_CafeMetal', segs=6)
    for k in range(3):                                                   # back slats
        bbox(B, (0.30, 0.016, 0.06), (0, 0.205, 0.60 + k * 0.08), 'MS_Wood', bevel=0.005,
             rot=Matrix.Rotation(math.radians(-8), 3, 'X'))
    root, objs = assemble('CafeChair', [('CafeChair_Mesh', B, None)])
    return root, 'Bistro chair, cafe-green frame with wood slats, seat 0.46 m; faces -Y.', []


# ----------------------------------------------------------------------------------------------------------------
# D pigeon (faces -Y; its left side is +X)
def ellipsoid(B, centre, radii, mat, segs=12, rings=8, rot=None, uv_cyl=False):
    cx, cy, cz = centre
    M = rot if rot is not None else Matrix.Identity(3)
    grid = []
    for i in range(rings + 1):
        th = math.pi * i / rings
        row = []
        for j in range(segs):
            ph = 2 * math.pi * j / segs
            p = Vector((radii[0] * math.sin(th) * math.cos(ph), radii[1] * math.cos(th), radii[2] * math.sin(th) * math.sin(ph)))
            row.append(B.v(Vector(centre) + M @ p))
        grid.append(row)
    for i in range(rings):
        for j in range(segs):
            k = (j + 1) % segs
            uv = None
            if uv_cyl:
                uv = [(j / segs, i / rings), (j / segs, (i + 1) / rings), ((j + 1) / segs, (i + 1) / rings),
                      ((j + 1) / segs, i / rings)]
            B.f((grid[i][j], grid[i + 1][j], grid[i + 1][k], grid[i][k]), mat, uv=uv)


def pigeon_wing(B, side):
    """Folded wing in the shoulder's local space (origin = shoulder), lying back along the body side."""
    L, C = 0.17, 0.085
    n_len, n_ch = 9, 4
    pts = []
    for i in range(n_len + 1):
        t = i / n_len
        chord = C * (1.0 - 0.55 * t ** 1.4) * (0.75 + 0.25 * math.sin(math.pi * min(1.0, t * 1.6)))
        y = 0.01 + t * L
        x_bulge = side * (0.016 * math.sin(math.pi * (0.15 + 0.85 * t)) - 0.004 - 0.012 * t)
        row_top, row_bot = [], []
        for j in range(n_ch + 1):
            u = j / n_ch
            z = 0.012 - u * chord - 0.012 * t
            th = 0.009 * math.sin(math.pi * u) * (1 - 0.6 * t) + 0.002
            row_top.append((x_bulge + side * th, y + 0.01 * u, z))
            row_bot.append((x_bulge - side * th * 0.4, y + 0.01 * u, z))
        pts.append((row_top, row_bot))
    vt = [[B.v(p) for p in r[0]] for r in pts]
    vb = [[B.v(p) for p in r[1]] for r in pts]

    def mat_at(i, j):
        t = i / n_len
        if t > 0.72:
            return 'MS_PigeonBar'                                       # dark primaries
        if (0.34 < t < 0.42) or (0.52 < t < 0.60):
            return 'MS_PigeonBar' if j >= 1 else 'MS_PigeonBody'        # the two wing bars
        return 'MS_PigeonBody'
    for i in range(n_len):
        for j in range(n_ch):
            m = mat_at(i, j)
            B.f((vt[i][j], vt[i + 1][j], vt[i + 1][j + 1], vt[i][j + 1]), m)
            B.f((vb[i][j + 1], vb[i + 1][j + 1], vb[i + 1][j], vb[i][j]), m)
    for i in range(n_len):
        for j in (0, n_ch):
            B.f((vt[i][j], vb[i][j], vb[i + 1][j], vt[i + 1][j]), 'MS_PigeonBody')
    for j in range(n_ch):
        for i in (0, n_len):
            B.f((vt[i][j], vt[i][j + 1], vb[i][j + 1], vb[i][j]), mat_at(min(i, n_len - 1), j))


def pigeon():
    B = UVB()
    tilt = Matrix.Rotation(math.radians(-14), 3, 'X')                 # chest up, tail down
    ellipsoid(B, (0, 0.012, 0.100), (0.058, 0.108, 0.060), 'MS_PigeonBody', segs=14, rings=10, rot=tilt)
    # breast / neck mass (iridescent) sloping forward into the head
    ellipsoid(B, (0, -0.058, 0.142), (0.043, 0.050, 0.058), 'MS_PigeonNeck', segs=14, rings=8, uv_cyl=True,
              rot=Matrix.Rotation(math.radians(-38), 3, 'X'))
    ellipsoid(B, (0, -0.092, 0.198), (0.026, 0.031, 0.026), 'MS_PigeonBody', segs=12, rings=8)
    rev(B, [(0.0, 0.0), (0.0062, 0.0), (0.0045, 0.01), (0.0022, 0.019), (0.0, 0.022)], 8, (0, -0.118, 0.194),
        (0, -0.94, -0.34), 'MS_PigeonBeak')
    ellipsoid(B, (0, -0.119, 0.198), (0.007, 0.006, 0.005), 'MS_PigeonCere', segs=8, rings=5)
    for sx in (-1, 1):
        ellipsoid(B, (sx * 0.0205, -0.100, 0.205), (0.0045, 0.005, 0.005), 'MS_PigeonEye', segs=8, rings=5)
    # tail fan with a dark terminal band
    tail = [(-0.035, 0.085, 0.078), (0.035, 0.085, 0.078), (0.046, 0.175, 0.046), (-0.046, 0.175, 0.046)]
    band = [(-0.046, 0.175, 0.046), (0.046, 0.175, 0.046), (0.048, 0.196, 0.039), (-0.048, 0.196, 0.039)]
    for poly, m in ((tail, 'MS_PigeonBody'), (band, 'MS_PigeonBar')):
        top = [B.v((x, y, z + 0.005)) for (x, y, z) in poly]
        bot = [B.v((x, y, z - 0.005)) for (x, y, z) in poly]
        B.f(tuple(top), m)
        B.f(tuple(reversed(bot)), m)
        for i in range(4):
            j = (i + 1) % 4
            B.f((top[i], bot[i], bot[j], top[j]), m)
    # legs and feet (pink): three forward toes + one back toe
    for sx in (-1, 1):
        x = sx * 0.024
        pipe(B, [(x, -0.004, 0.058), (x, -0.010, 0.032), (x * 1.1, -0.016, 0.008)], 0.0055, 'MS_PigeonFeet', segs=6)
        for ang in (-30, 0, 30):
            a = math.radians(ang)
            pipe(B, [(x * 1.1, -0.016, 0.006), (x * 1.1 + math.sin(a) * 0.03, -0.016 - math.cos(a) * 0.03, 0.003)],
                 0.0032, 'MS_PigeonFeet', segs=5)
        pipe(B, [(x * 1.1, -0.016, 0.006), (x * 1.1, 0.008, 0.003)], 0.003, 'MS_PigeonFeet', segs=5)
    WL, WR = UVB(), UVB()
    pigeon_wing(WL, +1)
    pigeon_wing(WR, -1)
    shoulder_l, shoulder_r = (0.036, -0.040, 0.142), (-0.036, -0.040, 0.142)
    WLw, WRw = UVB(), UVB()
    WLw.extend(WL, Matrix.Translation(shoulder_l) @ Matrix.Rotation(math.radians(-13), 4, 'X') @
               Matrix.Rotation(math.radians(-28), 4, 'Y'))
    WRw.extend(WR, Matrix.Translation(shoulder_r) @ Matrix.Rotation(math.radians(-13), 4, 'X') @
               Matrix.Rotation(math.radians(28), 4, 'Y'))
    root, objs = assemble('Pigeon', [('Pigeon_Mesh', B, None), ('Wing_L', WLw, shoulder_l), ('Wing_R', WRw, shoulder_r)])
    notes = ('Rock pigeon 0.32 m (beak to tail), standing, faces -Y, origin at the feet. Wing_L (+X Blender = bird\'s '
             'left) and Wing_R are separate children with pivots at the shoulders, folded along the body at rest: to '
             'open, yaw them out about Unity Y by about +-70 deg, then flap about the wing\'s length axis.')
    return root, notes, []


# ----------------------------------------------------------------------------------------------------------------
def game_view(root, out_dir, dist, yaw=-25.0, res=(1280, 720), look_h=None):
    """In-game camera: eye height 1.56 m, vertical FOV 60 deg, `dist` m in front of the asset (rotated by yaw)."""
    objs = [o for o in kit.descendants(root) if o.type == 'MESH']
    bpy.context.view_layer.update()
    pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    hi = max(p.z for p in pts)
    cx = (min(p.x for p in pts) + max(p.x for p in pts)) / 2
    cy = (min(p.y for p in pts) + max(p.y for p in pts)) / 2
    th = look_h if look_h is not None else min(max(hi * 0.5, 0.6), 3.2)
    off = Matrix.Rotation(math.radians(yaw), 3, 'Z') @ Vector((0, -dist, 0))
    eye = Vector((cx, cy, 0)) + off + Vector((0, 0, 1.56))
    cam = kit.camera('Cam_game', eye, Vector((cx, cy, th)), vfov=60.0)
    path = os.path.join(out_dir, 'previews', f'{root.name}_game.png')
    kit.render(path, res)
    bpy.data.objects.remove(cam, do_unlink=True)


def ensure_textures(out_dir):
    global TEX_DIR
    TEX_DIR = os.path.join(out_dir, 'textures')
    need = [v['texture'] for v in kit.MATS.values() if v.get('texture')]
    if not all(os.path.exists(os.path.join(TEX_DIR, t)) for t in need):
        py = '/opt/anaconda3/bin/python3' if os.path.exists('/opt/anaconda3/bin/python3') else 'python3'
        subprocess.run([py, os.path.join(TOOLS, 'opening_street_textures.py'), TEX_DIR], check=True)


def run_asset(name, fn, out_dir, do_export=True, do_preview=True, game=None):
    kit.reset()
    root, notes, draft = fn()
    tris = tris_of(root)
    file = export(root, out_dir) if do_export else None
    locs = [o.name for o in kit.descendants(root) if o.type == 'EMPTY' and o is not root]
    mats = kit.used_materials([o for o in kit.descendants(root) if o.type == 'MESH'])
    if do_preview:
        setup_preview_scene()
        ground()
        preview(root, out_dir)
        if game:
            game_view(root, out_dir, **game)
    if do_export:
        kit.update_manifest(out_dir, {name: {'file': file, 'tris': tris, 'locators': locs, 'notes': notes}}, mats, draft)
    log(f'{name}: {tris} tris, locators {locs}')
    return tris


ASSETS = [
    ('TrafficSignalMast', traffic_signal_mast, {'dist': 14.0, 'yaw': -35.0, 'look_h': 4.0}),
    ('PedSignal', ped_signal, None),
    ('PedPole', ped_pole, {'dist': 4.5, 'yaw': -30.0, 'look_h': 1.6}),
    ('StreetLamp', street_lamp, {'dist': 12.0, 'yaw': -40.0, 'look_h': 4.5}),
    ('BusShelter', bus_shelter, {'dist': 7.0, 'yaw': -35.0, 'look_h': 1.3}),
    ('BusStopSign', bus_stop_sign, None),
    ('SignPost', sign_post, {'dist': 5.0, 'yaw': -35.0, 'look_h': 2.4}),
    ('NoParkingSign', no_parking_sign, None),
    ('FireHydrant', fire_hydrant, None),
    ('TrashCan', trash_can, None),
    ('NewsBox_DailyNews', news_box_daily, None),
    ('NewsBox_CityWeekly', news_box_weekly, None),
    ('Bollard', bollard, None),
    ('Bench', bench, None),
    ('ParkingMeter', parking_meter, None),
    ('BikeRack', bike_rack, None),
    ('TreeGrate', tree_grate, None),
    ('Planter', planter, None),
    ('ManholeCover', manhole, None),
    ('StormDrain', storm_drain, None),
    ('AFrameSign', a_frame_sign, None),
    ('CafeTable', cafe_table, None),
    ('CafeChair', cafe_chair, None),
    ('Pigeon', pigeon, None),
]


LINEUP = [  # (asset, x, y, yaw deg) in the spec layout: walker on x = 0 heading +Y, N-S street on the left (x < -2.2)
    ('Bollard', -1.95, 2.6, 0), ('ParkingMeter', -1.95, 4.2, 90), ('StormDrain', -2.2, 5.4, -90),
    ('FireHydrant', -1.85, 6.4, 90), ('TreeGrate', -1.45, 8.0, 0), ('TrashCan', -1.8, 9.2, 0),
    ('StreetLamp', -1.95, 7.3, -90), ('TrafficSignalMast', -1.95, 10.1, 0), ('PedPole', -1.35, 10.25, 0),
    ('PedPole', 1.6, 22.9, 0), ('SignPost', -2.0, 22.8, 0), ('BusStopSign', -1.95, 26.0, 0),
    ('BusShelter', -0.5, 30.0, 90), ('NewsBox_DailyNews', 2.2, 3.4, 90), ('NewsBox_CityWeekly', 2.2, 4.0, 90),
    ('Bench', 2.2, 5.6, 90), ('AFrameSign', 1.7, 7.6, 70), ('Planter', 2.15, 9.0, 90), ('BikeRack', 1.9, 1.5, 90),
    ('CafeTable', 1.75, 25.0, 0), ('CafeChair', 1.35, 25.0, -60), ('CafeChair', 2.1, 25.3, 110),
    ('Pigeon', 0.4, 3.2, 20), ('Pigeon', -0.3, 3.7, -40), ('Pigeon', 0.9, 4.1, 160), ('ManholeCover', -6.2, 6.0, 0),
    ('NoParkingSign', -2.0, 14.0, 90),
]


def lineup(out_dir):
    """In-game check: every exported FBX re-imported into a rough street (spec section 3 layout), seen from the walker's
    eye (1.56 m, vertical FOV 60 deg)."""
    kit.reset()
    scn = setup_preview_scene()
    coll = bpy.data.collections.new('PV')
    scn.collection.children.link(coll)
    kit.street_context(coll)
    for name, x, y, yaw in LINEUP:
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=os.path.join(out_dir, 'fbx', name + '.fbx'))
        new = [o for o in bpy.data.objects if o not in before]
        root = next(o for o in new if o.parent is None)
        root.location = (x, y, 0.15 if name not in ('ManholeCover', 'StormDrain') else 0.0)
        root.rotation_euler = (0, 0, math.radians(yaw))
    for img in bpy.data.images:                      # FBX stores bare texture names; point them at textures/
        cand = os.path.join(TEX_DIR, os.path.basename(img.filepath or img.name))
        if os.path.exists(cand):
            img.filepath = cand
            img.reload()
    for label, eye, tgt in (('lineup_walk', (0.3, -1.0, 1.56), (0.0, 12.0, 0.9)),
                            ('lineup_corner', (0.6, 6.5, 1.56), (-2.5, 14.0, 2.2))):
        kit.camera('Cam_' + label, Vector(eye), Vector(tgt), vfov=60.0)
        kit.render(os.path.join(out_dir, 'previews', f'Street_{label}.png'), (1280, 720))


def main():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    out_dir = os.path.abspath(argv[0] if argv else 'build_art/opening/street')
    only = set(argv[argv.index('--only') + 1].split(',')) if '--only' in argv else None
    for sub in ('fbx', 'previews', 'textures'):
        os.makedirs(os.path.join(out_dir, sub), exist_ok=True)
    ensure_textures(out_dir)
    for name, fn, game in ASSETS:
        if only and name not in only:
            continue
        run_asset(name, fn, out_dir, do_export='--no-export' not in argv, do_preview='--no-preview' not in argv,
                  game=game)
    if '--no-preview' not in argv and (only is None or '--lineup' in argv):
        lineup(out_dir)


if __name__ == '__main__':
    main()
