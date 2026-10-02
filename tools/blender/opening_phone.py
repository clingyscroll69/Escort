"""Opening street kit, part A + B: the phone held in the walker's right hand, and the wired-earphone parts.
Headless Blender 5.2 (spec docs/superpowers/specs/2026-10-01-opening-street-design.md §5).

  PhoneInHand.fbx  root "PhoneInHand" (origin = display centre on the glass)
                     ├─ Phone        mesh: body + buttons + camera bump + ports + 3.5 mm plug (origin = phone centre)
                     ├─ Hand         mesh: posed right hand + forearm (Quaternius UBC Superhero_Female, CC0) + hoodie sleeve
                     ├─ ScreenCenter empty on the glass at the display centre (Unity +Z out of the screen, +Y = phone top)
                     └─ CableStart   empty at the end of the plug's strain relief (Blender local -Z = out of the plug)
  Phone.fbx        root "Phone" ├─ PhoneBody (same mesh as above) ├─ ScreenCenter └─ CableStart
  Earphones.fbx    roots "Splitter", "Remote", "Earbud" (cable axis = Blender Z, see manifest)

Modelled with the screen facing -Y, top toward +Z (X = right edge as seen from the front), metres.
Usage: Blender -b -P tools/blender/opening_phone.py -- <out_dir> [--only phone|earphones] [--no-export] [--previews a,b]
<out_dir> = build_art/opening/street (fbx/, previews/ and manifest.json are written there).
"""
import bpy, bmesh, math, os, sys, json
import numpy as np
from mathutils import Vector, Matrix, Quaternion
from mathutils.bvhtree import BVHTree

MM = 0.001
UBC_FEMALE = ('/Users/sapnagoel/Documents/coding/Game/ThirdParty/Quaternius/UBC/Universal Base Characters[Standard]/'
              'Base Characters/Unity/Superhero_Female_FullBody.fbx')


def log(*a):
    print('[phone]', *a, flush=True)


# ----------------------------------------------------------------------------------------------------------------
# materials (slot names are the interface to Unity; colours mirrored in manifest.json)
MATS = {
    'MP_Frame':       {'color': '#5B5E66', 'note': 'graphite titanium frame and buttons'},
    'MP_FrameBand':   {'color': '#3A3C42', 'note': 'antenna bands in the frame'},
    'MP_FrontGlass':  {'color': '#0B0B0F', 'note': 'front glass; Unity overlays the lock-screen canvas at ScreenCenter'},
    'MP_BackGlass':   {'color': '#1F3B5C', 'note': 'deep blue back glass and camera plateau'},
    'MP_Chrome':      {'color': '#C9CDD3', 'note': 'lens rings, screws, plug collar (bright, slightly blue grey)'},
    'MP_Lens':        {'color': '#101318', 'note': 'camera lens glass, LiDAR'},
    'MP_Flash':       {'color': '#E9E2C8', 'note': 'camera flash diffuser'},
    'MP_Port':        {'color': '#141518', 'note': 'insides of jack, USB-C port, speaker and mic holes'},
    'MP_Cable':       {'color': '#F4F4F2', 'note': 'white earphone plastic: plug, strain relief, splitter, remote, earbuds'},
    'MP_EarbudGrille': {'color': '#3C3F44', 'note': 'earbud speaker grille'},
    'MP_Skin':        {'color': '#C59B76', 'note': 'sidekick skin'},
    'MP_Nail':        {'color': '#D9B49A', 'note': 'fingernails'},
    'MP_Sleeve':      {'color': '#C98A3A', 'note': 'ochre hoodie sleeve'},
    'MP_SleeveCuff':  {'color': '#A86F2C', 'note': 'ribbed cuff, darker ochre'},
}


def hex_lin(h):
    h = h.lstrip('#')
    c = [int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)]
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)


def material(name):
    m = bpy.data.materials.get(name)
    if m is not None:
        return m
    spec = MATS[name]
    lin = hex_lin(spec['color'])
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*lin, 1.0)
    bsdf = m.node_tree.nodes.get('Principled BSDF')
    if bsdf is not None:
        bsdf.inputs['Base Color'].default_value = (*lin, 1.0)
        bsdf.inputs['Roughness'].default_value = 0.5
    return m


# ----------------------------------------------------------------------------------------------------------------
# mesh building helpers
class Builder:
    """Accumulates vertices / faces / per-face material names; turned into one mesh object at the end."""

    def __init__(self):
        self.verts = []
        self.faces = []
        self.mats = []
        self.smooth = []

    def v(self, p):
        self.verts.append(tuple(p))
        return len(self.verts) - 1

    def f(self, idx, mat, smooth=True):
        self.faces.append(tuple(idx))
        self.mats.append(mat)
        self.smooth.append(smooth)

    def extend(self, other, matrix=None):
        off = len(self.verts)
        for p in other.verts:
            self.verts.append(tuple(matrix @ Vector(p)) if matrix is not None else p)
        for fc, m, s in zip(other.faces, other.mats, other.smooth):
            self.faces.append(tuple(i + off for i in fc))
            self.mats.append(m)
            self.smooth.append(s)


def frame_from_axis(axis):
    axis = Vector(axis).normalized()
    ref = Vector((0, 0, 1)) if abs(axis.z) < 0.9 else Vector((1, 0, 0))
    a1 = axis.cross(ref).normalized()
    a2 = axis.cross(a1).normalized()
    return a1, a2


def revolve(B, profile, segs, origin, axis, mats, start_angle=0.0, a1=None, a2=None, smooth=True):
    """Rings around `axis` through `origin`. profile = [(r, h)] (r = 0 -> pole vertex). mats[k] = material of the band
    between profile points k and k+1 (a single string = all bands)."""
    origin = Vector(origin)
    axis = Vector(axis).normalized()
    if a1 is None:
        a1, a2 = frame_from_axis(axis)
    if isinstance(mats, str):
        mats = [mats] * (len(profile) - 1)
    rings = []
    for (r, h) in profile:
        if r <= 1e-9:
            rings.append([B.v(origin + axis * h)])
            continue
        ring = []
        for i in range(segs):
            t = start_angle + 2 * math.pi * i / segs
            ring.append(B.v(origin + axis * h + (a1 * math.cos(t) + a2 * math.sin(t)) * r))
        rings.append(ring)
    for k in range(len(profile) - 1):
        r0, r1 = rings[k], rings[k + 1]
        if len(r0) == 1 and len(r1) == 1:
            continue
        for i in range(segs):
            j = (i + 1) % segs
            if len(r0) == 1:
                B.f((r0[0], r1[i], r1[j]), mats[k], smooth)
            elif len(r1) == 1:
                B.f((r0[i], r1[0], r0[j]), mats[k], smooth)
            else:
                B.f((r0[i], r1[i], r1[j], r0[j]), mats[k], smooth)
    return rings


def rrect_outline(hx, hz, r, seg, cuts=None):
    """Closed rounded-rectangle loop in the (u, v) plane, counter-clockwise, as (u, v, nu, nv) with outward normals.
    cuts: {'right': [v..], 'left': [v..], 'top': [u..], 'bottom': [u..]} extra points on the straight edges."""
    cuts = cuts or {}
    pts = []
    ix, iz = hx - r, hz - r

    def corner(cx, cz, a0):
        for s in range(1, seg):
            a = a0 + (math.pi / 2) * s / seg
            pts.append((cx + r * math.cos(a), cz + r * math.sin(a), math.cos(a), math.sin(a)))

    # right edge (bottom -> top)
    pts.append((hx, -iz, 1, 0))
    for z in sorted(c for c in cuts.get('right', []) if -iz < c < iz):
        pts.append((hx, z, 1, 0))
    pts.append((hx, iz, 1, 0))
    corner(ix, iz, 0.0)
    pts.append((ix, hz, 0, 1))
    for x in sorted((c for c in cuts.get('top', []) if -ix < c < ix), reverse=True):
        pts.append((x, hz, 0, 1))
    pts.append((-ix, hz, 0, 1))
    corner(-ix, iz, math.pi / 2)
    pts.append((-hx, iz, -1, 0))
    for z in sorted((c for c in cuts.get('left', []) if -iz < c < iz), reverse=True):
        pts.append((-hx, z, -1, 0))
    pts.append((-hx, -iz, -1, 0))
    corner(-ix, -iz, math.pi)
    pts.append((-ix, -hz, 0, -1))
    for x in sorted(c for c in cuts.get('bottom', []) if -ix < c < ix):
        pts.append((x, -hz, 0, -1))
    pts.append((ix, -hz, 0, -1))
    corner(ix, -iz, 1.5 * math.pi)
    return pts


def sweep(B, outline, profile, origin, ax_u, ax_v, ax_h, mat_fn, cap_first=None, cap_last=None, smooth=True):
    """Extrude a closed outline along a profile [(d, h)]: ring k = outline inset by d (along -normal), lifted by h along
    ax_h. mat_fn(k, i) -> material of the quad between rings k, k+1 and outline points i, i+1."""
    origin, ax_u, ax_v, ax_h = Vector(origin), Vector(ax_u), Vector(ax_v), Vector(ax_h)
    rings = []
    for (d, h) in profile:
        rings.append([B.v(origin + ax_u * (u - d * nu) + ax_v * (w - d * nw) + ax_h * h) for (u, w, nu, nw) in outline])
    n = len(outline)
    for k in range(len(profile) - 1):
        for i in range(n):
            j = (i + 1) % n
            B.f((rings[k][i], rings[k][j], rings[k + 1][j], rings[k + 1][i]), mat_fn(k, i), smooth)
    if cap_first:
        B.f(tuple(reversed(rings[0])), cap_first, smooth)
    if cap_last:
        B.f(tuple(rings[-1]), cap_last, smooth)
    return rings


def to_object(B, name, recalc=True, sharp_angle=35.0, triangulate=True):
    me = bpy.data.meshes.new(name)
    me.from_pydata(B.verts, [], B.faces)
    me.validate(clean_customdata=False)
    names = []
    for m in B.mats:
        if m not in names:
            names.append(m)
    for n in names:
        me.materials.append(material(n))
    idx = {n: i for i, n in enumerate(names)}
    for p, m in zip(me.polygons, B.mats):
        p.material_index = idx[m]
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-7)
    if recalc:
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    if triangulate:
        bmesh.ops.triangulate(bm, faces=bm.faces, quad_method='BEAUTY', ngon_method='BEAUTY')
    bm.to_mesh(me)
    bm.free()
    me.set_sharp_from_angle(angle=math.radians(sharp_angle))
    return obj


def tri_count(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


# ----------------------------------------------------------------------------------------------------------------
# A. the phone
PH_W, PH_H, PH_T = 73.6, 147.1, 8.0          # mm
PH_R = 10.6
DISP_W, DISP_H, DISP_R = 69.2, 142.7, 8.5
HX, HZ, HY = PH_W / 2, PH_H / 2, PH_T / 2
JACK_X = -12.0                                # seen from the front: left of centre
PLUG = {'collar': 0.8, 'housing': 16.0, 'relief': 10.0, 'r_housing': 2.75, 'r_relief0': 2.15, 'r_relief1': 1.1}
PLUG_END_Z = -HZ - PLUG['collar'] - PLUG['housing'] - PLUG['relief']
BANDS = {'right': [58.0, -58.0], 'left': [58.0, -58.0], 'top': [-18.0], 'bottom': [-24.5, 24.5]}
BAND_W = 1.1
# buttons (centre along the side in mm, length): action + volume on the left (-X), power on the right (+X)
BUTTONS = [('left', 44.5, 6.5), ('left', 29.0, 10.0), ('left', 16.0, 10.0), ('right', 29.0, 17.0)]
CAM_C = (HX - 4.2 - 17.4, HZ - 4.2 - 17.8)    # camera plateau centre (x, z): top-left as seen from the back
CAM_HALF = (17.4, 17.8)
CAM_R = 7.8


def phone_profile():
    """Cross-section (inset d, y) in mm from the front cap ring to the back cap ring, plus one material per segment.
    Support rings (flat strips next to the big flat faces) keep the glass and the side band shading flat."""
    front = [(1.75, -HY), (1.30, -HY), (0.85, -3.96), (0.58, -3.84),      # glass edge
             (0.52, -3.70), (0.30, -3.52), (0.12, -3.30), (0.02, -3.02),  # frame lip + rounding
             (0.00, -2.70), (0.00, -2.30)]                                # side band support ring
    pts = front + [(d, -y) for (d, y) in reversed(front)]
    G, F, Bk = 'MP_FrontGlass', 'MP_Frame', 'MP_BackGlass'
    mats = [G, G, G] + [F] * 13 + [Bk, Bk, Bk]
    assert len(mats) == len(pts) - 1
    return pts, mats


def band_intervals():
    """Outline-parameter intervals (side, lo, hi) of the antenna bands."""
    out = []
    for side, cs in BANDS.items():
        for c in cs:
            out.append((side, c - BAND_W / 2, c + BAND_W / 2))
    return out


def build_phone_body(B):
    cuts = {s: [] for s in ('right', 'left', 'top', 'bottom')}
    for side, lo, hi in band_intervals():
        cuts[side] += [lo, hi]
    # extra cuts so the boolean port cutters and buttons land on evenly sized quads
    cuts['bottom'] += [JACK_X - 3.5, JACK_X + 3.5, -6.0, 6.0, 8.0, 22.0]
    outline = rrect_outline(HX, HZ, PH_R, 14, cuts)
    prof, mats = phone_profile()

    def side_of(i):
        u, w, nu, nw = outline[i]
        u2, w2, _, _ = outline[(i + 1) % len(outline)]
        if abs(nu - 1) < 1e-6 and abs(u - HX) < 1e-6 and abs(u2 - HX) < 1e-6:
            return 'right', (w + w2) / 2
        if abs(nu + 1) < 1e-6 and abs(u + HX) < 1e-6 and abs(u2 + HX) < 1e-6:
            return 'left', (w + w2) / 2
        if abs(nw - 1) < 1e-6 and abs(w - HZ) < 1e-6 and abs(w2 - HZ) < 1e-6:
            return 'top', (u + u2) / 2
        if abs(nw + 1) < 1e-6 and abs(w + HZ) < 1e-6 and abs(w2 + HZ) < 1e-6:
            return 'bottom', (u + u2) / 2
        return None, None

    bands = band_intervals()

    def mat_fn(k, i):
        m = mats[k]
        if m == 'MP_Frame':
            side, c = side_of(i)
            if side and any(s == side and lo < c < hi for s, lo, hi in bands):
                return 'MP_FrameBand'
        return m

    outline_m = [(u * MM, w * MM, nu, nw) for (u, w, nu, nw) in outline]
    prof_m = [(d * MM, y * MM) for (d, y) in prof]
    sweep(B, outline_m, prof_m, (0, 0, 0), (1, 0, 0), (0, 0, 1), (0, 1, 0), mat_fn,
          cap_first='MP_FrontGlass', cap_last='MP_BackGlass')


def build_buttons(B):
    for side, zc, length in BUTTONS:
        sx = 1 if side == 'right' else -1
        hb = 2.7  # button height across the frame (along Y), mm
        r = hb / 2
        # stadium outline in the (z, y) plane of the side, extruded outward along +-X
        outline = rrect_outline(length / 2, hb / 2, r * 0.999, 8)
        outline_m = [(u * MM, w * MM, nu, nw) for (u, w, nu, nw) in outline]
        prof = [(0.0, -0.5), (0.0, 0.42), (0.12, 0.58), (0.32, 0.66), (0.6, 0.68)]
        prof_m = [(d * MM, h * MM) for (d, h) in prof]
        origin = Vector((sx * HX * MM, 0, zc * MM))
        sweep(B, outline_m, prof_m, origin, (0, 0, 1), (0, 1, 0), (sx, 0, 0), lambda k, i: 'MP_Frame',
              cap_first=None, cap_last='MP_Frame')


def build_camera(B):
    cx, cz = CAM_C
    # plateau: rounded square on the back glass, raised 1.1 mm, rounded rim
    outline = rrect_outline(CAM_HALF[0], CAM_HALF[1], CAM_R, 12)
    outline_m = [(u * MM, w * MM, nu, nw) for (u, w, nu, nw) in outline]
    prof = [(0.0, -0.3), (0.0, 0.45), (0.06, 0.72), (0.22, 0.95), (0.5, 1.08), (0.9, 1.12), (1.3, 1.12)]
    prof_m = [(d * MM, h * MM) for (d, h) in prof]
    base = Vector((cx * MM, HY * MM, cz * MM))
    # the plateau's u axis must be -X so the rounded square is the same seen from the back (any is fine: symmetric)
    sweep(B, outline_m, prof_m, base, (1, 0, 0), (0, 0, 1), (0, 1, 0), lambda k, i: 'MP_BackGlass',
          cap_first=None, cap_last='MP_BackGlass')
    top = HY + 1.12
    axis = Vector((0, 1, 0))
    a1, a2 = Vector((1, 0, 0)), Vector((0, 0, 1))
    lenses = [(cx + 8.6, cz + 8.8), (cx + 8.6, cz - 8.8), (cx - 8.4, cz)]   # +X = left column seen from the back
    for (lx, lz) in lenses:
        o = Vector((lx * MM, top * MM, lz * MM))
        ring = [(5.15, -0.2), (5.15, 0.95), (5.35, 1.3), (5.8, 1.42), (6.3, 1.36), (6.6, 1.1), (6.65, 0.3), (6.7, -0.2)]
        revolve(B, [(r * MM, h * MM) for (r, h) in ring], 40, o, axis, 'MP_Chrome', a1=a1, a2=a2)
        glass = [(0.0, 1.06), (2.2, 1.03), (4.0, 0.96), (5.2, 0.86)]
        revolve(B, [(r * MM, h * MM) for (r, h) in glass], 40, o, axis, 'MP_Lens', a1=a1, a2=a2)
        inner = [(2.75, 1.045), (2.9, 1.11), (3.15, 1.11), (3.3, 1.02)]      # thin raised inner ring catches light
        revolve(B, [(r * MM, h * MM) for (r, h) in inner], 32, o, axis, 'MP_Chrome', a1=a1, a2=a2)
    # flash (top) and LiDAR (bottom) in the right column seen from the back, mic hole near the top
    fl = Vector(((cx - 8.4) * MM, top * MM, (cz + 11.2) * MM))
    revolve(B, [(r * MM, h * MM) for (r, h) in [(0.0, 0.22), (2.0, 0.22), (2.45, 0.16), (2.6, 0.0), (2.6, -0.2)]], 24, fl,
            axis, 'MP_Flash', a1=a1, a2=a2)
    li = Vector(((cx - 8.4) * MM, top * MM, (cz - 11.2) * MM))
    revolve(B, [(r * MM, h * MM) for (r, h) in [(0.0, 0.18), (2.3, 0.18), (2.7, 0.08), (2.8, -0.2)]], 24, li,
            axis, 'MP_Lens', a1=a1, a2=a2)
    mic = Vector(((cx - 0.5) * MM, top * MM, (cz + 13.6) * MM))
    revolve(B, [(r * MM, h * MM) for (r, h) in [(0.0, 0.03), (0.55, 0.03), (0.6, -0.1)]], 10, mic, axis, 'MP_Port',
            a1=a1, a2=a2)


def build_plug(B):
    """White 3.5 mm plug pointing down (-Z) out of the jack; ends at PLUG_END_Z where the simulated cable starts."""
    o = Vector((JACK_X * MM, 0, -HZ * MM))
    axis = Vector((0, 0, -1))
    a1, a2 = Vector((1, 0, 0)), Vector((0, 1, 0))
    c, hs, rl = PLUG['collar'], PLUG['housing'], PLUG['relief']
    rh, r0, r1 = PLUG['r_housing'], PLUG['r_relief0'], PLUG['r_relief1']
    # metal collar (bit of the plug shaft showing between phone and housing)
    revolve(B, [(r * MM, h * MM) for (r, h) in [(0, -1.5), (1.72, -1.5), (1.72, c + 0.1), (0, c + 0.1)]], 20, o, axis,
            'MP_Chrome', a1=a1, a2=a2)
    hous = [(1.6, c), (2.25, c), (2.55, c + 0.12), (2.72, c + 0.45), (rh, c + 1.0), (rh, c + hs - 1.0),
            (2.7, c + hs - 0.4), (2.5, c + hs - 0.1), (2.3, c + hs)]
    relief = [(r0, c + hs), (r0, c + hs + 0.4)]
    n = 6
    for i in range(1, n + 1):
        t = i / n
        relief.append((r0 + (r1 - r0) * (t ** 0.85), c + hs + 0.4 + (rl - 0.6) * t))
    relief += [(r1 * 0.75, c + hs + rl - 0.05), (0.0, c + hs + rl)]
    prof = [(0.0, c)] + hous + relief
    revolve(B, [(r * MM, h * MM) for (r, h) in prof], 24, o, axis, 'MP_Cable', a1=a1, a2=a2)


def port_cutters():
    """Cutter objects for the bottom-edge openings (boolean difference, cutter faces keep MP_Port)."""
    cut = Builder()
    bz = -HZ
    # 3.5 mm jack (hidden by the plug, but real)
    revolve(cut, [(r * MM, h * MM) for (r, h) in [(0, -2.0), (1.78, -2.0), (1.78, 11.0), (0, 11.0)]], 24,
            Vector((JACK_X * MM, 0, bz * MM)), Vector((0, 0, 1)), 'MP_Port')
    # USB-C: stadium 8.6 x 2.9 mm, 6 mm deep
    outline = rrect_outline(4.3, 1.45, 1.449, 8)
    sweep(cut, [(u * MM, w * MM, nu, nw) for (u, w, nu, nw) in outline], [(0, -2.0 * MM), (0, 6.0 * MM)],
          Vector((0, 0, bz * MM)), (1, 0, 0), (0, 1, 0), (0, 0, 1), lambda k, i: 'MP_Port',
          cap_first='MP_Port', cap_last='MP_Port')
    # speaker holes right of the port, mic holes left of it
    for x in [9.5 + 2.2 * i for i in range(6)] + [-5.6, -7.4]:
        revolve(cut, [(r * MM, h * MM) for (r, h) in [(0, -2.0), (0.62, -2.0), (0.62, 2.2), (0, 2.2)]], 12,
                Vector((x * MM, 0, bz * MM)), Vector((0, 0, 1)), 'MP_Port')
    return to_object(cut, 'PortCutter', triangulate=False)


def build_screws(B):
    for x in (-3.9 - 2.4, 6.3):  # pentalobe-ish screws either side of the port (tiny chrome dots)
        o = Vector((x * MM, 0, -HZ * MM))
        revolve(B, [(r * MM, h * MM) for (r, h) in [(0, -0.12), (0.55, -0.1), (0.62, 0.05)]], 12, o,
                Vector((0, 0, -1)), 'MP_Chrome')


def build_phone():
    body = Builder()
    build_phone_body(body)
    obj = to_object(body, 'Phone', triangulate=False)
    cutter = port_cutters()
    mod = obj.modifiers.new('ports', 'BOOLEAN')
    mod.operation = 'DIFFERENCE'
    mod.solver = 'EXACT'
    mod.material_mode = 'TRANSFER'
    mod.object = cutter
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier='ports')
    bpy.data.objects.remove(cutter, do_unlink=True)
    # the rest of the parts are separate shells merged into the same mesh
    parts = Builder()
    build_buttons(parts)
    build_camera(parts)
    build_plug(parts)
    build_screws(parts)
    pobj = to_object(parts, 'PhoneParts', triangulate=False)
    bpy.ops.object.select_all(action='DESELECT')
    pobj.select_set(True)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.join()
    finish_mesh(obj)
    return obj


def finish_mesh(obj, sharp_angle=35.0):
    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.triangulate(bm, faces=bm.faces, quad_method='BEAUTY', ngon_method='BEAUTY')
    bm.to_mesh(me)
    bm.free()
    me.set_sharp_from_angle(angle=math.radians(sharp_angle))


# ----------------------------------------------------------------------------------------------------------------
# signed distance to the phone (mm, phone frame) used to pose the fingers against it
def sdf_phone(P):
    """P: (N, 3) array in metres (phone frame). Returns signed distance in mm (negative inside)."""
    p = P / MM
    # body: rounded rectangle in XZ extruded along Y with rounded long edges
    qx = np.abs(p[:, 0]) - (HX - PH_R)
    qz = np.abs(p[:, 2]) - (HZ - PH_R)
    d2 = np.hypot(np.maximum(qx, 0), np.maximum(qz, 0)) + np.minimum(np.maximum(qx, qz), 0) - PH_R
    dy = np.abs(p[:, 1]) - HY
    re = 1.2
    a, b = d2 + re, dy + re
    d = np.hypot(np.maximum(a, 0), np.maximum(b, 0)) + np.minimum(np.maximum(a, b), 0) - re

    def box(c, h):
        q = np.abs(p - np.array(c)) - np.array(h)
        return np.linalg.norm(np.maximum(q, 0), axis=1) + np.minimum(np.max(q, axis=1), 0)

    for side, zc, length in BUTTONS:
        sx = 1 if side == 'right' else -1
        d = np.minimum(d, box((sx * (HX + 0.3), 0, zc), (0.4, 1.35, length / 2)))
    d = np.minimum(d, box((CAM_C[0], HY + 1.0, CAM_C[1]), (CAM_HALF[0], 1.0, CAM_HALF[1])))
    # plug: capsule along -Z
    a0 = np.array([JACK_X, 0, -HZ])
    a1 = np.array([JACK_X, 0, PLUG_END_Z])
    ab = a1 - a0
    t = np.clip(((p - a0) @ ab) / (ab @ ab), 0, 1)
    dc = np.linalg.norm(p - (a0 + t[:, None] * ab), axis=1) - PLUG['r_housing']
    return np.minimum(d, dc)


# ----------------------------------------------------------------------------------------------------------------
# the hand: Quaternius UBC Superhero_Female (CC0) right forearm + hand, posed around the phone with our own forward
# kinematics + linear-blend skinning (numpy). The posed mesh is baked; no armature is exported.
DIGITS = {
    'thumb': ['thumb_01_r', 'thumb_02_r', 'thumb_03_r', 'thumb_04_leaf_r'],
    'index': ['index_01_r', 'index_02_r', 'index_03_r', 'index_04_leaf_r'],
    'middle': ['middle_01_r', 'middle_02_r', 'middle_03_r', 'middle_04_leaf_r'],
    'ring': ['ring_01_r', 'ring_02_r', 'ring_03_r', 'ring_04_leaf_r'],
    'pinky': ['pinky_01_r', 'pinky_02_r', 'pinky_03_r', 'pinky_04_leaf_r'],
}
ARM_BONES = ['lowerarm_r', 'hand_r'] + [b for d in DIGITS.values() for b in d]
BIDX = {n: i for i, n in enumerate(ARM_BONES)}


def load_ubc_arm(cut_forearm):
    """Right forearm + hand of the UBC female: rest vertices (armature space), skin weights over ARM_BONES, faces."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=UBC_FEMALE)
    new = [o for o in bpy.data.objects if o not in before]
    arm = next(o for o in new if o.type == 'ARMATURE')
    body = next(o for o in new if o.type == 'MESH' and o.name.lower().startswith('superhero'))
    rest = {b.name: b.matrix_local.copy() for b in arm.data.bones}
    parent = {b.name: (b.parent.name if b.parent else None) for b in arm.data.bones}
    me = body.data
    gi = {g.index: g.name for g in body.vertex_groups}
    W0 = rest['hand_r'].translation.copy()
    fa = (rest['lowerarm_r'].translation - W0).normalized()     # toward the elbow
    keep, wts, dom = [], [], []
    for v in me.vertices:
        gs = [(gi[g.group], g.weight) for g in v.groups if g.weight > 1e-4]
        if not gs:
            continue
        dn = max(gs, key=lambda x: x[1])[0]
        if dn not in BIDX or (v.co - W0).dot(fa) > cut_forearm:
            continue
        w = np.zeros(len(ARM_BONES))
        for n, wt in gs:
            if n in BIDX:
                w[BIDX[n]] += wt
        keep.append(v.index)
        wts.append(w / w.sum())
        dom.append(BIDX[dn])
    remap = {o: i for i, o in enumerate(keep)}
    faces = [[remap[i] for i in p.vertices] for p in me.polygons if all(i in remap for i in p.vertices)]
    V = np.array([tuple(me.vertices[i].co) for i in keep])
    for o in new:
        bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.armatures, bpy.data.materials, bpy.data.images):
        for blk in list(coll):
            if blk.users == 0:
                coll.remove(blk)
    log(f'UBC arm: {len(V)} verts, {len(faces)} faces')
    return {'rest': rest, 'parent': parent, 'V': V, 'W': np.array(wts), 'dom': np.array(dom), 'faces': faces}


def rot_xyz(ax, ay, az):
    return (Matrix.Rotation(math.radians(ax), 4, 'X') @ Matrix.Rotation(math.radians(ay), 4, 'Y')
            @ Matrix.Rotation(math.radians(az), 4, 'Z'))


class HandRig:
    def __init__(self, data):
        self.d = data
        self.rest = data['rest']
        self.parent = data['parent']
        self.rest_inv = {n: m.inverted() for n, m in self.rest.items()}
        self.rel = {n: self.rest_inv[self.parent[n]] @ self.rest[n] for n in ARM_BONES if n not in ('lowerarm_r',)}
        r = self.rest
        self.W0 = r['hand_r'].translation.copy()
        self.I0 = r['index_01_r'].translation.copy()
        self.P0 = r['pinky_01_r'].translation.copy()
        self.M0 = r['middle_01_r'].translation.copy()
        k0 = (self.P0 - self.I0).normalized()
        n0 = Vector((0, 0, -1))                                   # T-pose palms face down
        n0 = (n0 - k0 * n0.dot(k0)).normalized()
        self.F0 = Matrix((k0, n0, k0.cross(n0))).transposed()
        self.e0 = (r['lowerarm_r'].translation - self.W0).normalized()
        self.Hv = np.c_[data['V'], np.ones(len(data['V']))]

    def hand_transform(self, P):
        """Rigid transform rest -> phone frame from the index knuckle position, knuckle-line direction and palm normal."""
        k = Vector(P['knuckle_line']).normalized()
        n = Vector(P['palm_normal'])
        n = (n - k * n.dot(k)).normalized()
        R = Matrix((k, n, k.cross(n))).transposed() @ self.F0.transposed()
        if P.get('rotvec'):
            rv = Vector(P['rotvec'])
            if rv.length > 1e-9:
                R = Matrix.Rotation(rv.length, 3, rv.normalized()) @ R
        A = Vector(P['index_mcp']) * MM
        S = Matrix.Diagonal((HAND_SCALE, HAND_SCALE, HAND_SCALE, 1.0))
        return Matrix.Translation(A) @ R.to_4x4() @ S @ Matrix.Translation(-self.I0), R

    def pose(self, P):
        G, R = self.hand_transform(P)
        W = G @ self.W0
        e = Vector(P['forearm_dir']).normalized()
        sw = (R @ self.e0).rotation_difference(e).to_matrix()
        S = Matrix.Diagonal((HAND_SCALE, HAND_SCALE, HAND_SCALE, 1.0))
        Gf = Matrix.Translation(W) @ (sw @ R).to_4x4() @ S @ Matrix.Translation(-self.W0)
        Pm = {'lowerarm_r': Gf @ self.rest['lowerarm_r'], 'hand_r': G @ self.rest['hand_r']}
        for digit, bones in DIGITS.items():
            base = P['base'][digit]
            flex = P['flex'][digit]
            for k, b in enumerate(bones):
                if k == 0:
                    Rl = rot_xyz(*base) @ Matrix.Rotation(math.radians(flex[0]), 4, 'X')
                elif k < 3:
                    Rl = Matrix.Rotation(math.radians(flex[k]), 4, 'X')
                else:
                    Rl = Matrix.Identity(4)
                Pm[b] = Pm[self.parent[b]] @ self.rel[b] @ Rl
        D = np.array([np.array(Pm[b] @ self.rest_inv[b]) for b in ARM_BONES])
        return D, Pm

    def skin(self, D, idx=None):
        Hv = self.Hv if idx is None else self.Hv[idx]
        Wt = self.d['W'] if idx is None else self.d['W'][idx]
        return np.einsum('nb,bij,nj->ni', Wt, D, Hv)[:, :3]

    def digit_verts(self, digit):
        ids = [BIDX[b] for b in DIGITS[digit]]
        sel = np.isin(self.d['dom'], ids)
        seg = np.array([min(ids.index(x), 2) for x in self.d['dom'][sel]])
        return np.nonzero(sel)[0], seg

    def settle_palm(self, P, gap, groups=('hand_r',)):
        """Slide the hand along its palm normal until the palm is `gap` mm from the phone."""
        ids = [BIDX[g] for g in groups]
        sel = np.nonzero(np.isin(self.d['dom'], ids))[0]
        n = Vector(P['palm_normal']).normalized()
        for _ in range(30):
            D, _ = self.pose(P)
            sd = sdf_phone(self.skin(D, sel)).min()
            if abs(sd - gap) < 0.05:
                break
            P['index_mcp'] = tuple(Vector(P['index_mcp']) + n * (sd - gap))
        return sd

    def close(self, P, digit, joints=(0, 1, 2), speed=(1.0, 1.0, 0.75), limits=(95, 105, 80), margin=0.5, step=0.5):
        """Grasp closing: flex the joints together until each phalanx touches the phone, freezing proximal joints on
        contact (GraspIt-style). Returns the final flex angles."""
        idx, seg = self.digit_verts(digit)
        ang = list(P['flex'][digit])
        active = [k in joints for k in range(3)]
        for _ in range(600):
            if not any(active):
                break
            trial = [min(a + step * s, lim) if act else a for a, s, lim, act in zip(ang, speed, limits, active)]
            P['flex'][digit] = trial
            D, _ = self.pose(P)
            sd = sdf_phone(self.skin(D, idx))
            hit = [bool(np.any(sd[seg == s] < margin)) for s in range(3)]
            if any(hit):
                s = hit.index(True)
                for k in range(s + 1):
                    active[k] = False
                P['flex'][digit] = ang
                continue
            ang = trial
            for k in range(3):
                if active[k] and ang[k] >= limits[k]:
                    active[k] = False
        P['flex'][digit] = ang
        return ang


# Hand pose (phone frame, mm / degrees). The knuckle line runs index -> pinky; the palm normal points out of the palm.
# Fingers close automatically onto the phone (HandRig.close); `flex` holds the starting curl, `base` the extra
# rotation of each digit's first bone (local X = flex, Y = twist, Z = spread).
HAND_SCALE = 0.92      # UBC superhero hand (19 cm) -> ~17.5 cm, a typical adult hand next to a 147 mm phone
HAND_POSE = {
    'index_mcp': (14.0, 30.0, -14.0),
    'knuckle_line': (-0.42, 0.01, -0.91),
    'palm_normal': (-0.1, -0.99, 0.03),
    'rotvec': (0.0, 0.0, 0.0),
    'forearm_dir': None,                       # None = FOREARM_DIR
    'base': {'thumb': (0, 0, 0), 'index': (0, 0, 0), 'middle': (0, 0, 0), 'ring': (0, 0, 0), 'pinky': (0, 0, 0)},
    'flex': {'thumb': [0, 20, 20], 'index': [45, 50, 35], 'middle': [45, 55, 38], 'ring': [45, 55, 38],
             'pinky': [60, 90, 60]},
}
CUFF_START = 40.0       # mm from the wrist joint toward the elbow
SKIN_KEEP = 75.0        # forearm skin kept (hidden inside the cuff beyond CUFF_START)
SLEEVE_LEN = 260.0      # sleeve end, mm from the wrist (off-screen in the reading pose)
NAILS = {               # distal bone, width, length (mm), start along the bone (fraction of the distal phalanx)
    'thumb': ('thumb_03_r', 10.5, 11.5, 0.30),
    'index': ('index_03_r', 8.6, 9.5, 0.36),
    'middle': ('middle_03_r', 9.0, 10.0, 0.36),
    'ring': ('ring_03_r', 8.4, 9.5, 0.36),
    'pinky': ('pinky_03_r', 7.2, 8.0, 0.36),
}


# Grip targets for the solver (phone frame, mm, boxes = (lo, hi) per axis). Cupped right-hand grip: knuckles behind the
# right half, fingers across the back with the tips peeking round the left edge (index highest), the pinky curled under
# the bottom edge right of centre (clear of the jack at x = -12), the thumb along the lower right border of the screen
# (out of the upper two thirds of the display), wrist right of the bottom corner, forearm down-right toward the viewer.
GRIP_BOXES = {
    'index_tip': ((-44.0, -38.5), (-5.0, 1.5), (-16.0, 6.0)),
    'index_pip': ((-24.0, -4.0), (8.0, 20.0), (-16.0, 6.0)),
    'middle_tip': ((-44.0, -38.5), (-5.0, 1.5), (-34.0, -15.0)),
    'middle_pip': ((-22.0, -2.0), (8.0, 20.0), (-34.0, -15.0)),
    'ring_tip': ((-43.0, -37.5), (-5.0, 2.0), (-53.0, -33.0)),
    'ring_pip': ((-18.0, 2.0), (8.0, 20.0), (-53.0, -33.0)),
    'pinky_tip': ((4.0, 20.0), (-9.0, 1.0), (-87.0, -78.0)),
    'pinky_pip': ((14.0, 34.0), (-4.0, 14.0), (-93.0, -80.0)),
    'thumb_tip': ((24.0, 33.0), (-14.0, -8.5), (-40.0, -26.0)),
    'thumb_ip': ((31.0, 42.0), (-16.0, -6.0), (-64.0, -47.0)),
    'wrist': ((55.0, 95.0), (5.0, 45.0), (-100.0, -62.0)),
}
FOREARM_DIR = (0.52, -0.30, -0.80)   # wrist -> elbow in the phone frame: down-right, toward the viewer's body
FINGERS4 = ('index', 'middle', 'ring', 'pinky')


def box_dist2(p, box):
    return sum(max(0.0, lo - v, v - hi) ** 2 for v, (lo, hi) in zip(p, box))


def seg_dist(p1, q1, p2, q2):
    """Closest distance between segments p1q1 and p2q2 (numpy 3-vectors)."""
    d1, d2, r = q1 - p1, q2 - p2, p1 - p2
    a, e, f = d1 @ d1, d2 @ d2, d2 @ r
    c, b = d1 @ r, d1 @ d2
    den = a * e - b * b
    s = np.clip((b * f - c * e) / den, 0, 1) if den > 1e-12 else 0.0
    t = (b * s + f) / e
    if t < 0:
        t, s = 0.0, np.clip(-c / a, 0, 1)
    elif t > 1:
        t, s = 1.0, np.clip((b - c) / a, 0, 1)
    return float(np.linalg.norm((p1 + d1 * s) - (p2 + d2 * t)))


class GripSolver:
    """Fits the hand placement + all digit angles to GRIP_TARGETS with contact / no-penetration / no finger overlap /
    wrist-bend terms, by deterministic coordinate descent."""

    def __init__(self, rig, P):
        self.rig, self.P = rig, json.loads(json.dumps(P))
        self.tip = {}
        for digit, bones in DIGITS.items():
            idx, _ = rig.digit_verts(digit)
            b3 = rig.rest[bones[2]]
            head, ax = np.array(b3.translation), np.array(b3.col[1].xyz.normalized())
            self.tip[digit] = idx[np.argmax((rig.d['V'][idx] - head) @ ax)]
        self.seg = {d: rig.digit_verts(d) for d in DIGITS}
        self.palm = np.nonzero(rig.d['dom'] == BIDX['hand_r'])[0]
        self.fd0 = Vector(FOREARM_DIR).normalized()
        self.wpen = 40.0

    def unpack(self, x):
        P = self.P
        P['index_mcp'] = tuple(x[0:3])
        P['rotvec'] = tuple(x[3:6])
        for i, d in enumerate(FINGERS4):
            sp, f1, f2, f3 = x[6 + 4 * i: 10 + 4 * i]
            P['base'][d] = (0.0, 0.0, float(sp))
            P['flex'][d] = [float(f1), float(f2), float(f3)]
        bx, by, bz, f2, f3 = x[22:27]
        P['base']['thumb'] = (float(bx), float(by), float(bz))
        P['flex']['thumb'] = [0.0, float(f2), float(f3)]
        a, b = x[27:29]
        fd = Matrix.Rotation(a, 3, 'X') @ Matrix.Rotation(b, 3, 'Z') @ self.fd0
        P['forearm_dir'] = tuple(fd)
        P['forearm_rot'] = (float(a), float(b))
        return P

    def pack(self, P):
        x = list(P['index_mcp']) + list(P.get('rotvec') or (0, 0, 0))
        for d in FINGERS4:
            x += [P['base'][d][2]] + list(P['flex'][d])
        x += list(P['base']['thumb']) + list(P['flex']['thumb'][1:])
        x += list(P.get('forearm_rot') or (0.0, 0.0))
        return np.array(x, dtype=float)

    def energy(self, x, report=False):
        P = self.unpack(x)
        rig = self.rig
        D, Pm = rig.pose(P)
        V = rig.skin(D)
        sd = sdf_phone(V)
        terms = {}
        terms['pen'] = self.wpen * float(np.sum(np.maximum(0.0, 0.5 - sd) ** 2))
        tip = 0.0
        tp = {d: V[self.tip[d]] / MM for d in DIGITS}
        jp = {d: np.array(Pm[DIGITS[d][2 if d == 'thumb' else 1]].translation) / MM for d in DIGITS}
        for d in DIGITS:
            tip += box_dist2(tp[d], GRIP_BOXES[d + '_tip'])
            tip += box_dist2(jp[d], GRIP_BOXES[d + ('_ip' if d == 'thumb' else '_pip')])
        tip += box_dist2(np.array(Pm['hand_r'].translation) / MM, GRIP_BOXES['wrist'])
        tip += max(0.0, tp['middle'][2] - (tp['index'][2] - 13.0)) ** 2
        tip += max(0.0, tp['ring'][2] - (tp['middle'][2] - 13.0)) ** 2
        # thumb: distal phalanx points up the edge, nail faces the viewer
        ty = Pm['thumb_03_r'].col[1].xyz.normalized()
        tz = Pm['thumb_03_r'].col[2].xyz.normalized()
        tip += 200.0 * max(0.0, 0.75 - ty.z) ** 2 + 200.0 * max(0.0, 0.4 - (-tz).dot(Vector((0, -1, 0)))) ** 2
        terms['tips'] = tip
        con = 0.0
        for d in ('index', 'middle', 'ring'):
            idx, seg = self.seg[d]
            m = float(sd[idx][seg <= 1].min())
            con += 2.0 * (m - 0.7) ** 2
        idx, seg = self.seg['thumb']
        con += 2.0 * (float(sd[idx][seg >= 1].min()) - 0.6) ** 2
        con += 1.0 * (float(sd[self.palm].min()) - 1.5) ** 2
        terms['contact'] = con
        # neighbouring fingers must not overlap (phalanx capsules, radius ~7.4 mm after scaling)
        cap = 0.0
        rr = 2 * 7.4 * HAND_SCALE / 0.92 - 1.0
        segs = {}
        for d in FINGERS4:
            bs = DIGITS[d]
            segs[d] = [(np.array(Pm[b].translation) / MM, np.array(Pm[bs[k + 1]].translation) / MM)
                       for k, b in enumerate(bs[:3])]
        for a_, b_ in (('index', 'middle'), ('middle', 'ring'), ('ring', 'pinky')):
            for (p1, q1) in segs[a_]:
                for (p2, q2) in segs[b_]:
                    dd = seg_dist(p1, q1, p2, q2)
                    if dd < rr:
                        cap += 5.0 * (rr - dd) ** 2
        terms['fingers'] = cap
        # wrist bend and forearm deviation from the reading pose
        fa = -Vector(P['forearm_dir']).normalized()
        hy = Pm['hand_r'].col[1].xyz.normalized()
        bend = math.degrees(fa.angle(hy))
        dev = math.degrees(Vector(P['forearm_dir']).angle(self.fd0))
        terms['wrist'] = 2.0 * max(0.0, bend - 40.0) ** 2 + 2.0 * max(0.0, dev - 25.0) ** 2
        # joint ranges
        lim = 0.0
        for i, d in enumerate(FINGERS4):
            sp, f1, f2, f3 = x[6 + 4 * i: 10 + 4 * i]
            lim += max(0, abs(sp) - 15) ** 2 + max(0, f1 - 90) ** 2 + max(0, -10 - f1) ** 2
            lim += max(0, f2 - 105) ** 2 + max(0, -f2) ** 2 + max(0, f3 - 80) ** 2 + max(0, -f3) ** 2
            lim += 0.002 * (sp ** 2) + 0.02 * (f3 - 0.7 * f2) ** 2 + max(0, -5 - f1) ** 2 * 3
        bx, by, bz, f2, f3 = x[22:27]
        lim += sum(max(0, abs(v) - 70) ** 2 for v in (bx, by, bz)) + max(0, f2 - 70) ** 2 + max(0, -15 - f2) ** 2
        lim += max(0, f3 - 80) ** 2 + max(0, -15 - f3) ** 2
        terms['limits'] = lim
        E = sum(terms.values())
        if report:
            log('energy', round(E, 2), {k: round(v, 2) for k, v in terms.items()}, f'wrist bend {bend:.1f} dev {dev:.1f}',
                'min sd', round(float(sd.min()), 2))
            for d in DIGITS:
                log(f'  {d}: tip {np.round(tp[d], 1)} joint {np.round(jp[d], 1)} flex {np.round(P["flex"][d], 1)} '
                    f'base {np.round(P["base"][d], 1)}')
            log('  wrist', np.round(np.array(Pm['hand_r'].translation) / MM, 1))
        return E

    def multistart(self, x0, starts=24, seed=7):
        """Deterministic random restarts over the hand orientation/position, short descents, then refine the best."""
        rng = np.random.default_rng(seed)
        cands = []
        for k in range(starts):
            x = x0.copy()
            if k > 0:
                x[0:3] += rng.uniform(-12, 12, 3)
                ax = rng.normal(size=3)
                ax /= np.linalg.norm(ax)
                x[3:6] = ax * rng.uniform(0, 0.6)
                x[22:25] = rng.uniform(-50, 50, 3)
            self.wpen = 2.0
            x, e = self.solve(x, sweeps=60, quiet=True)
            self.wpen = 40.0
            e = self.energy(x)
            log(f'start {k}: E={e:.1f}')
            cands.append((e, k, x))
        cands.sort(key=lambda c: c[0])
        best = None
        for e, k, x in cands[:3]:
            x, e = self.solve(x, sweeps=300, quiet=True)
            log(f'refined start {k}: E={e:.1f}')
            if best is None or e < best[1]:
                best = (x, e)
        return best

    def solve(self, x0, sweeps=400, quiet=False):
        steps = np.array([4, 4, 4, 0.08, 0.08, 0.08] + [5, 10, 10, 10] * 4 + [12, 12, 12, 10, 10] + [0.08, 0.08])
        x, e = x0.copy(), self.energy(x0)
        st = steps.copy()
        for it in range(sweeps):
            improved = False
            for i in range(len(x)):
                for sgn in (1.0, -1.0):
                    xt = x.copy()
                    xt[i] += sgn * st[i]
                    et = self.energy(xt)
                    if et < e:
                        x, e = xt, et
                        improved = True
                        # keep moving while it pays
                        while True:
                            xt = x.copy()
                            xt[i] += sgn * st[i]
                            et = self.energy(xt)
                            if et >= e:
                                break
                            x, e = xt, et
                        break
            if not improved:
                st *= 0.5
                if np.max(st / steps) < 0.01:
                    break
            if it % 20 == 0 and not quiet:
                log(f'solve sweep {it} E={e:.2f} step {np.max(st / steps):.3f}')
        return x, e


def pose_hand(rig, P, solve=False):
    P = json.loads(json.dumps(P))                          # deep copy
    if solve:
        gs = GripSolver(rig, P)
        x, e = gs.multistart(gs.pack(P))
        P = gs.unpack(x)
        gs.energy(x, report=True)
        keep = {k: P[k] for k in ('index_mcp', 'knuckle_line', 'palm_normal', 'rotvec', 'forearm_dir', 'forearm_rot',
                                  'base', 'flex')}
        log('SOLVED_POSE ' + json.dumps(keep))
    D, Pm = rig.pose(P)
    return P, D, Pm


def build_nails(B, verts, faces, Pm, rest):
    """Nail plates projected onto the posed fingertips (ray cast onto the skin, lifted 0.28 mm)."""
    bvh = BVHTree.FromPolygons([tuple(v) for v in verts], faces)
    for digit, (bone, wn, ln, start) in NAILS.items():
        M = Pm[bone]
        head = M.translation
        y = M.col[1].xyz.normalized()            # along the phalanx
        x = M.col[0].xyz.normalized()
        dorsal = -M.col[2].xyz.normalized()      # local +Z is the palm side
        L = (rest[bone].translation - rest[DIGITS[digit][3]].translation).length / MM   # distal phalanx, mm
        nu, nv = 5, 6
        grid = []
        for j in range(nv):
            t = j / (nv - 1)
            row = []
            for i in range(nu):
                s = -1 + 2 * i / (nu - 1)
                taper = 1.0 - 0.10 * max(0.0, 0.25 - t) / 0.25 - 0.22 * max(0.0, t - 0.6) / 0.4
                along = start * L + t * ln - (abs(s) ** 2) * 1.8 * max(0.0, t - 0.7) / 0.3
                p0 = head + y * (along * MM) + x * (s * wn / 2 * taper * MM) + dorsal * 0.03
                loc, nrm, _, _ = bvh.ray_cast(p0, -dorsal, 0.06)
                if loc is None:
                    row.append(None)
                    continue
                if nrm.dot(dorsal) < 0:
                    nrm = -nrm
                row.append(B.v(loc + nrm * (0.28 * MM)))
            grid.append(row)
        for j in range(nv - 1):
            for i in range(nu - 1):
                q = (grid[j][i], grid[j + 1][i], grid[j + 1][i + 1], grid[j][i + 1])
                if None not in q:
                    B.f(q, 'MP_Nail')


def ellipse_fit(pts2):
    """Principal axes of 2D points: centre of the bounds, axes (columns, unit), half extents along them."""
    c = pts2.mean(axis=0)
    w, vecs = np.linalg.eigh(np.cov((pts2 - c).T))
    proj = (pts2 - c) @ vecs
    half = (proj.max(axis=0) - proj.min(axis=0)) / 2
    mid = (proj.max(axis=0) + proj.min(axis=0)) / 2
    return c + vecs @ mid, vecs, half


def build_sleeve(B, Pm, skin_verts, dom):
    """Ochre hoodie sleeve with a ribbed cuff from CUFF_START mm above the wrist, following the forearm axis.
    One continuous open surface: cuff inner wall -> rolled lip -> ribbed cuff -> seam lip -> baggy sleeve."""
    Mf = Pm['lowerarm_r']
    W = Pm['hand_r'].translation
    e = (Mf.translation - W).normalized()            # wrist -> elbow
    a1 = Pm['hand_r'].col[2].xyz
    a1 = (a1 - e * a1.dot(e)).normalized()
    a2 = e.cross(a1).normalized()
    fv = skin_verts[dom == BIDX['lowerarm_r']]
    rel = fv - np.array(W)
    s = rel @ np.array(e)
    sel = (s > CUFF_START * MM - 0.004) & (s < SKIN_KEEP * MM + 0.01)
    pts2 = np.c_[rel[sel] @ np.array(a1), rel[sel] @ np.array(a2)]
    c2, axes, half = ellipse_fit(pts2)
    q = (pts2 - c2) @ axes
    scale = float(np.max(np.hypot(q[:, 0] / half[0], q[:, 1] / half[1])))
    half = half * scale
    log(f'forearm section at the cuff: half extents {np.round(half / MM, 1)} mm (scale {scale:.2f})')
    ctr = W + a1 * float(c2[0]) + a2 * float(c2[1])
    b1 = a1 * float(axes[0, 0]) + a2 * float(axes[1, 0])
    b2 = a1 * float(axes[0, 1]) + a2 * float(axes[1, 1])
    if b1.cross(b2).dot(e) < 0:
        b2 = -b2
    ra, rb = float(half[0]) + 3.2 * MM, float(half[1]) + 3.2 * MM
    segs = 56

    def ring(sv, ka, kb, rib=0.0, fold=None):
        out = []
        for i in range(segs):
            t = 2 * math.pi * i / segs
            rr = 1.0 + (rib if i % 2 == 0 else -rib) + (fold(t) if fold else 0.0)
            out.append(B.v(ctr + e * (sv * MM) + (b1 * math.cos(t) * ra * ka + b2 * math.sin(t) * rb * kb) * rr))
        return out

    def fold_fn(k, amp):
        ph = 0.9 * k
        return lambda t: amp * (0.045 * math.sin(3 * t + ph) + 0.03 * math.sin(5 * t + 2.1 * ph + 1.0)
                                + 0.02 * math.sin(7 * t - ph))

    cs = CUFF_START
    thk = 2.6 * MM / ra                                  # cloth thickness relative to the cuff radius
    rings = [
        ('MP_SleeveCuff', ring(cs + 10, 1 - thk, 1 - thk * 1.05)),
        ('MP_SleeveCuff', ring(cs + 0.7, 1 - thk, 1 - thk * 1.05)),
        ('MP_SleeveCuff', ring(cs - 0.5, 1 - thk * 0.5, 1 - thk * 0.5, 0.01)),
        ('MP_SleeveCuff', ring(cs, 1.0, 1.0, 0.03)),
    ]
    for sv in (6, 16, 28, 40, 50):
        g = 1 + 0.0035 * sv
        rings.append(('MP_SleeveCuff', ring(cs + sv, g, g, 0.035)))
    # seam: the sleeve fabric overhangs the top of the cuff, then balloons out with soft folds
    base = cs + 50
    body = [(-2.5, 1.30, 0.3), (0.5, 1.40, 0.6), (6, 1.47, 0.8), (18, 1.53, 1.0), (40, 1.58, 1.0), (70, 1.63, 0.9),
            (105, 1.68, 0.8), (145, 1.72, 0.7), (190, 1.75, 0.6), (SLEEVE_LEN - base, 1.76, 0.5)]
    for k, (sv, g, amp) in enumerate(body):
        rings.append(('MP_Sleeve', ring(base + sv, g, g * 0.97, 0.0, fold_fn(k, amp))))
    for k in range(len(rings) - 1):
        r0 = rings[k][1]
        mat, r1 = rings[k + 1]
        for i in range(segs):
            j = (i + 1) % segs
            B.f((r0[i], r0[j], r1[j], r1[i]), mat)


def build_hand(rig, P, solve=False):
    P, D, Pm = pose_hand(rig, P, solve)
    verts = rig.skin(D)
    faces = rig.d['faces']
    B = Builder()
    for p in verts:
        B.v(p)
    for f in faces:
        B.f(f, 'MP_Skin')
    build_nails(B, verts, faces, Pm, rig.rest)
    build_sleeve(B, Pm, verts, rig.d['dom'])
    obj = to_object(B, 'Hand', recalc=False, triangulate=False)
    # skin: subdivide once for a smoother close-up silhouette (the base mesh is ~1.6k tris)
    return obj, P, Pm


# ----------------------------------------------------------------------------------------------------------------
# reading pose (first-person preview and the forearm direction): eye 1.56 m, camera pitched down, phone 0.36 m away
EYE = Vector((0.0, 0.0, 1.56))
CAM_PITCH = 24.0              # degrees below the horizon
CAM_VFOV = 60.0
FP_RES = (1280, 720)
FP_NDC = (0.42, -0.40)        # where the phone centre sits on screen (x right, y up, -1..1)
FP_DIST = 0.36
FP_TILT = 12.0                # phone held this much more upright than facing the eye ("seen slightly from above")
FP_ROLL = 7.0                 # top of the phone leans this much toward the body midline (degrees)
ELBOW = Vector((0.20, 0.10, -0.60))   # elbow relative to the eye (right, forward, down)


def fp_camera_matrix():
    return Matrix.Translation(EYE) @ Matrix.Rotation(math.radians(90 - CAM_PITCH), 4, 'X')


def reading_pose():
    """World matrix of the phone centre frame (X right edge, Y into the screen, Z top) in the first-person preview,
    and the forearm direction (wrist -> elbow) expressed in the phone frame."""
    C = fp_camera_matrix()
    ty = math.tan(math.radians(CAM_VFOV / 2))
    tx = ty * FP_RES[0] / FP_RES[1]
    d_cam = Vector((FP_NDC[0] * tx, FP_NDC[1] * ty, -1.0)).normalized()
    centre = C @ (d_cam * FP_DIST)
    f = (EYE - centre).normalized()                         # screen normal facing the eye
    right = (C.to_3x3() @ Vector((1, 0, 0))).normalized()
    cand = [Matrix.Rotation(math.radians(a), 3, right) @ f for a in (FP_TILT, -FP_TILT)]
    f = min(cand, key=lambda v: v.z)                        # more upright = lower z component of the normal
    up = Vector((0, 0, 1))
    up = (up - f * up.dot(f)).normalized()
    up = Matrix.Rotation(math.radians(FP_ROLL), 3, f) @ up
    yph = -f
    zph = up
    xph = yph.cross(zph).normalized()
    R = Matrix((xph, yph, zph)).transposed()
    M = Matrix.Translation(centre) @ R.to_4x4()
    elbow_w = EYE + ELBOW
    fd = (R.transposed() @ (elbow_w - centre)).normalized()
    return M, fd


# ----------------------------------------------------------------------------------------------------------------
def make_empty(name, loc=(0, 0, 0), parent=None, size=0.01, display='ARROWS'):
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = display
    e.empty_display_size = size
    bpy.context.scene.collection.objects.link(e)
    e.location = loc
    if parent is not None:
        e.parent = parent
    return e


def export_fbx(path, objs):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH', 'EMPTY'},
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z',
                             axis_up='Y', mesh_smooth_type='FACE', use_mesh_modifiers=True, add_leaf_bones=False,
                             path_mode='STRIP', bake_anim=False)
    log('exported', path)


def descendants(o):
    out = [o]
    for c in o.children:
        out += descendants(c)
    return out


# ----------------------------------------------------------------------------------------------------------------
# previews (Workbench, material colours)
def setup_render(res, bg='#CBD3DA'):
    scn = bpy.context.scene
    scn.render.engine = 'BLENDER_WORKBENCH'
    scn.render.resolution_x, scn.render.resolution_y = res
    scn.render.resolution_percentage = 100
    scn.render.film_transparent = False
    scn.view_settings.view_transform = 'Standard'
    sh = scn.display.shading
    sh.light = 'STUDIO'
    sh.color_type = 'MATERIAL'
    sh.show_cavity = True
    sh.cavity_type = 'WORLD'
    sh.show_object_outline = True
    sh.object_outline_color = (0.05, 0.05, 0.06)
    sh.show_specular_highlight = True
    scn.display.render_aa = '8'
    if scn.world is None:
        scn.world = bpy.data.worlds.new('World')
    scn.world.color = hex_lin(bg)
    return scn


def preview_mat(name, hexcol):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.diffuse_color = (*hex_lin(hexcol), 1)
    return m


def add_box(name, size, loc, mat, coll):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Translation(loc) @ Matrix.Diagonal((*size, 1)))
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    o.data.materials.append(mat)
    coll.objects.link(o)
    return o


def add_text(name, body, size, loc, rot, mat, coll, align='CENTER'):
    cu = bpy.data.curves.new(name, 'FONT')
    cu.body = body
    cu.size = size
    cu.align_x = align
    cu.align_y = 'CENTER'
    o = bpy.data.objects.new(name, cu)
    o.data.materials.append(mat)
    o.matrix_world = Matrix.Translation(loc) @ rot.to_4x4()
    coll.objects.link(o)
    return o


def mock_screen(root, coll):
    """Preview-only stand-in for the Unity lock screen, laid on the display area (not exported)."""
    R = root.matrix_world.to_3x3()
    o = root.matrix_world.translation
    fwd = R @ Vector((0, -1, 0))
    up = R @ Vector((0, 0, 1))
    rt = R @ Vector((1, 0, 0))
    rot = Matrix((rt, up, fwd)).transposed()           # text plane: x right, y up, z out of the screen
    bg = bpy.data.meshes.new('MockScreen')
    bm = bmesh.new()
    out = rrect_outline(DISP_W / 2, DISP_H / 2, DISP_R, 8)
    vs = [bm.verts.new(o + fwd * 0.00005 + rt * (u * MM) + up * (w * MM)) for (u, w, _, _) in out]
    bm.faces.new(vs)
    bm.to_mesh(bg)
    bm.free()
    ob = bpy.data.objects.new('MockScreen', bg)
    ob.data.materials.append(preview_mat('PV_Screen', '#1B2A44'))
    coll.objects.link(ob)
    white = preview_mat('PV_White', '#F2F4F8')
    add_text('MockTime', '7:52', 0.019, o + fwd * 0.0001 + up * 0.040, rot, white, coll)
    add_text('MockDate', 'Tuesday, March 18', 0.0042, o + fwd * 0.0001 + up * 0.058, rot, white, coll)
    card = add_box('MockCard', (0.060, 0.0001, 0.014), Vector((0, 0, 0)), preview_mat('PV_Card', '#3C4C66'), coll)
    card.matrix_world = Matrix.Translation(o + fwd * 0.0001 + up * 0.004) @ Matrix((rt, -fwd, up)).transposed().to_4x4()
    add_text('MockNote', 'Shift at 8:00', 0.0045, o + fwd * 0.0002 + up * 0.004, rot, white, coll)
    for sx in (-1, 1):
        add_box('MockBtn', (0.009, 0.0001, 0.009), Vector((0, 0, 0)), preview_mat('PV_Btn', '#55647C'), coll).matrix_world = \
            Matrix.Translation(o + fwd * 0.0001 + rt * (sx * 0.022) - up * 0.058) @ Matrix((rt, -fwd, up)).transposed().to_4x4()


def street_context(coll):
    """Rough sidewalk / road / crosswalk so the first-person preview reads in context (not exported)."""
    add_box('PV_Sidewalk', (4.8, 10.5, 0.15), Vector((0.2, 5.25 - 0.0, 0.075 - 1.56 + 1.56 - 0.0)),
            preview_mat('PV_Side', '#B9B4AA'), coll)
    add_box('PV_Road', (60, 12, 0.02), Vector((0, 16.5, -0.0)), preview_mat('PV_Asph', '#3A3F47'), coll)
    add_box('PV_Road2', (12, 60, 0.02), Vector((-8.2, 16.5, 0.0)), preview_mat('PV_Asph', '#3A3F47'), coll)
    for k in range(12):
        add_box(f'PV_Bar{k}', (3.2, 0.5, 0.03), Vector((0, 10.75 + k * 1.0, 0.01)), preview_mat('PV_Paint', '#E8E6DF'), coll)
    add_box('PV_SideFar', (4.8, 20, 0.15), Vector((0.2, 32.5, 0.075)), preview_mat('PV_Side', '#B9B4AA'), coll)
    add_box('PV_BldgE', (6, 9, 9), Vector((5.6, 1.5, 4.5)), preview_mat('PV_Brick', '#9B4A3A'), coll)
    add_box('PV_BldgNE', (6, 14, 12), Vector((5.6, 34, 6)), preview_mat('PV_Stone', '#D8CDB5'), coll)
    add_box('PV_BldgNW', (8, 14, 10), Vector((-22.6, 34, 5)), preview_mat('PV_Teal', '#4F7D7A'), coll)


def camera(name, loc, target, lens=None, vfov=None, res=(900, 900)):
    cd = bpy.data.cameras.new(name)
    cam = bpy.data.objects.new(name, cd)
    bpy.context.scene.collection.objects.link(cam)
    cam.location = loc
    cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    cd.clip_start = 0.005
    cd.clip_end = 400
    if vfov is not None:
        cd.sensor_fit = 'VERTICAL'
        cd.angle_y = math.radians(vfov)
    else:
        cd.lens = lens or 50
    bpy.context.scene.camera = cam
    return cam


def render(path, res):
    scn = bpy.context.scene
    scn.render.resolution_x, scn.render.resolution_y = res
    scn.render.filepath = path
    bpy.ops.render.render(write_still=True)
    log('preview', path)


# ----------------------------------------------------------------------------------------------------------------
# manifest (shared with tools/blender/opening_street.py: each script merges its own entries)
def update_manifest(out_dir, models, mat_names, draft_copy=()):
    path = os.path.join(out_dir, 'manifest.json')
    man = {'materials': {}, 'models': {}, 'draft_copy': []}
    if os.path.exists(path):
        with open(path) as fh:
            man.update(json.load(fh))
    for n in mat_names:
        spec = MATS[n]
        man['materials'][n] = {'color': spec['color'], 'texture': spec.get('texture'), 'emission': spec.get('emission'),
                               'note': spec['note']}
    man['models'].update(models)
    dc = [d for d in man.get('draft_copy', []) if d not in draft_copy] + list(draft_copy)
    man['draft_copy'] = dc
    man['materials'] = dict(sorted(man['materials'].items()))
    man['models'] = dict(sorted(man['models'].items()))
    with open(path, 'w') as fh:
        json.dump(man, fh, indent=2)
    log('manifest updated', path)


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def used_materials(objs):
    out = []
    for o in objs:
        if o.type == 'MESH':
            for s in o.material_slots:
                if s.material and s.material.name in MATS and s.material.name not in out:
                    out.append(s.material.name)
    return out


def build_phone_in_hand(out_dir, export=True, dev=False, solve=False):
    reset()
    M_read, fdir = reading_pose()
    phone = build_phone()
    rig = HandRig(load_ubc_arm(SKIN_KEEP * MM))
    P = json.loads(json.dumps(HAND_POSE))
    if P.get('forearm_dir') is None:
        P['forearm_dir'] = FOREARM_DIR
    hand, Pp, Pm = build_hand(rig, P, solve)
    root = make_empty('PhoneInHand', size=0.03)
    for o in (phone, hand):
        o.parent = root
        o.location = (0, HY * MM, 0)
    sc = make_empty('ScreenCenter', (0, 0, 0), root, 0.02)
    cs = make_empty('CableStart', (JACK_X * MM, HY * MM, PLUG_END_Z * MM), root, 0.01)
    tris = {'Phone': tri_count(phone), 'Hand': tri_count(hand)}
    log('tris', tris)
    fbx_dir = os.path.join(out_dir, 'fbx')
    os.makedirs(fbx_dir, exist_ok=True)
    if export:
        export_fbx(os.path.join(fbx_dir, 'PhoneInHand.fbx'), [root, phone, hand, sc, cs])
        phone.name = 'PhoneBody'
        root.name = 'Phone'
        export_fbx(os.path.join(fbx_dir, 'Phone.fbx'), [root, phone, sc, cs])
        root.name = 'PhoneInHand'
        phone.name = 'Phone'
    # previews
    pv = os.path.join(out_dir, 'previews') if not dev else os.path.join(out_dir, 'dev')
    os.makedirs(pv, exist_ok=True)
    scn = setup_render(FP_RES)
    coll = bpy.data.collections.new('PreviewOnly')
    scn.collection.children.link(coll)
    root.matrix_world = M_read @ Matrix.Translation((0, -HY * MM, 0))
    bpy.context.view_layer.update()
    mock_screen(root, coll)
    street_context(coll)
    cam = camera('FP', EYE, EYE + Vector((0, 1, 0)), vfov=CAM_VFOV)
    cam.matrix_world = fp_camera_matrix()
    render(os.path.join(pv, 'PhoneInHand_firstperson.png'), FP_RES)
    for o in list(coll.objects):
        if o.name.startswith('PV_'):
            o.hide_render = True
    # object views around the phone at the origin (mock screen moves with it)
    M0 = Matrix.Translation((0, -HY * MM, 0))
    mock = [o for o in coll.objects if o.name.startswith('Mock')]
    for o in mock:
        o.matrix_world = M0 @ root.matrix_world.inverted() @ o.matrix_world
    root.matrix_world = M0
    bpy.context.view_layer.update()
    c = Vector((0.0, 0.0, -0.035))
    shots = {'threequarter': ((0.30, -0.36, 0.20), c, 50), 'back': ((-0.10, 0.46, 0.06), c, 50)}
    if dev:
        shots.update({'front': ((0, -0.55, -0.03), c, 50), 'right': ((0.55, 0.0, -0.03), c, 50),
                      'bottom': ((0.02, -0.12, -0.55), Vector((0, 0, -0.07)), 50),
                      'left': ((-0.55, 0.02, -0.03), c, 50), 'backlow': ((0.2, 0.40, -0.25), c, 50)})
    for label, (loc, tgt, lens) in shots.items():
        camera('Cam_' + label, Vector(loc), tgt, lens=lens)
        render(os.path.join(pv, f'PhoneInHand_{label}.png'), (900, 900))
    mats = used_materials([phone, hand])
    return {
        'PhoneInHand': {'file': 'fbx/PhoneInHand.fbx', 'tris': tris['Phone'] + tris['Hand'],
                        'locators': ['ScreenCenter', 'CableStart'],
                        'notes': ('Root at the display centre on the glass. Children: Phone (body+buttons+camera+ports+'
                                  'plug, origin at the phone centre, 4 mm behind the root), Hand (posed UBC female right '
                                  'hand + forearm + ochre hoodie sleeve, same origin), ScreenCenter (Unity +Z out of the '
                                  f'screen, +Y to the phone top; display {DISP_W} x {DISP_H} mm, corner radius {DISP_R} mm; '
                                  'offset the UI canvas ~0.1 mm along +Z), CableStart (end of the plug strain relief, '
                                  'cable leaves along Unity -Y = phone down; strain relief end radius 1.1 mm). '
                                  f'Tris: Phone {tris["Phone"]}, Hand {tris["Hand"]}. The sleeve runs {SLEEVE_LEN / 10:.0f} cm '
                                  'up the forearm (open end off-screen in the reading pose).')},
        'Phone': {'file': 'fbx/Phone.fbx', 'tris': tris['Phone'], 'locators': ['ScreenCenter', 'CableStart'],
                  'notes': 'Phone + plug only. Root "Phone" at the display centre, mesh child "PhoneBody" (origin at '
                           'the phone centre), same empties and offsets as PhoneInHand.'},
    }, mats


def main():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    out_dir = os.path.abspath(argv[0] if argv else 'build_art/opening/street')
    only = argv[argv.index('--only') + 1] if '--only' in argv else None
    dev = '--dev' in argv
    export = '--no-export' not in argv and not dev
    os.makedirs(out_dir, exist_ok=True)
    if only in (None, 'phone'):
        models, mats = build_phone_in_hand(out_dir, export=export, dev=dev, solve='--solve-hand' in argv)
        if export:
            update_manifest(out_dir, models, mats)
    if only in (None, 'earphones') and 'build_earphones' in globals():
        models, mats = build_earphones(out_dir, export=export, dev=dev)
        if export:
            update_manifest(out_dir, models, mats)


main()
