"""Opening street buildings (kit "buildings"): parametric city buildings for the first-person intro walk. Headless Blender 5.2.

Corner buildings (both street facades detailed, the shop wraps the corner). Origin = the exterior corner where the front
and side facade lines meet (for a chamfered/rounded corner: the virtual intersection of the two lines), at the base.
    CornerCafe, CornerMart          side facade on the viewer's LEFT  (Blender -X), building spans x in [0, W]
    CornerPharmacy, CornerDiner     side facade on the viewer's RIGHT (Blender +X), building spans x in [-W, 0]
Mid-block buildings: origin = centre of the front facade's base line, body toward +Y.
    NoodleBar, Books, Laundromat, PhoneRepair, Bakery, Florist, ForLease, Bank
Background towers (origin = centre of the front facade base line, like mid-blocks): Tower1..Tower4.
Front facade faces -Y (Unity +Z after export). z = 0 is sidewalk level. Units metres. Floors: ground 4.2 m, upper 3.2 m.

Usage (textures first, the generator reads <out>/atlas_layout.json):
  /opt/anaconda3/bin/python3 tools/opening_building_textures.py build_art/opening/buildings
  /Applications/Blender.app/Contents/MacOS/Blender -b -P tools/blender/opening_buildings.py -- build_art/opening/buildings \
      [--only CornerCafe,Bank] [--no-preview] [--verify]
Deterministic: all variation comes from random.Random(<asset name>).
"""
import bmesh
import bpy
import json
import math
import os
import random
import sys
from mathutils import Vector

ARGV = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
POS = [a for a in ARGV if not a.startswith('--')]
OUT = os.path.abspath(POS[0] if POS else 'build_art/opening/buildings')
ONLY = None
for _a in ARGV:
    if _a.startswith('--only'):
        ONLY = set(ARGV[ARGV.index(_a) + 1].split(',')) if _a == '--only' else set(_a.split('=', 1)[1].split(','))
NO_PREVIEW = '--no-preview' in ARGV
VERIFY = '--verify' in ARGV
TEX_DIR = os.path.join(OUT, 'textures')

GROUND_H, FLOOR_H, PARAPET = 4.2, 3.2, 1.0
Z = Vector((0.0, 0.0, 1.0))
EPS = 1e-5

# ------------------------------------------------------------------------------------------------------ materials
# name: (base colour multiplied with the texture, texture in textures/ or None, emission (colour, intensity) or None, note)
MATS = {
    'MB_BrickRed': ('#FFFFFF', 'T_BrickRed.png', None, 'red brick #9B4A3A; tileable, UV 1 unit = 2 m'),
    'MB_BrickBrown': ('#FFFFFF', 'T_BrickBrown.png', None, 'brown brick #7A4B3A; tileable 2 m'),
    'MB_BrickCommon': ('#FFFFFF', 'T_BrickCommon.png', None, 'common brick for party/back walls and parapet insides; tileable 2 m'),
    'MB_BrickTeal': ('#FFFFFF', 'T_BrickTeal.png', None, 'teal-painted brick #4F7D7A; tileable 2 m'),
    'MB_BrickSlate': ('#FFFFFF', 'T_BrickSlate.png', None, 'slate-painted brick #4A4F5A; tileable 2 m'),
    'MB_StoneCream': ('#FFFFFF', 'T_StoneCream.png', None, 'cream ashlar #D8CDB5; tileable 2 m'),
    'MB_PanelLight': ('#FFFFFF', 'T_PanelLight.png', None, 'light stone cladding panels (modern); tileable 2 m'),
    'MB_StoneTrim': ('#DDD4BF', None, None, 'sills, lintels, cornices, copings, quoins'),
    'MB_Concrete': ('#A9A59C', None, None, 'steps, thresholds, plinths, rooftop curbs'),
    'MB_Roof': ('#FFFFFF', 'T_Roof.png', None, 'tar/gravel roof; tileable 2 m'),
    'MB_RoofDark': ('#3E3C3A', None, None, 'water-tower cone, bulkhead roofs'),
    'MB_TrimWhite': ('#ECE7DB', None, None, 'painted window frames'),
    'MB_TrimDark': ('#2B2F33', None, None, 'dark frames, shop mullions, lamp shades'),
    'MB_TrimGreen': ('#2F6B4F', None, None, 'cafe green shopfront paint'),
    'MB_TrimCream': ('#E6DCC4', None, None, 'cream painted woodwork'),
    'MB_Metal': ('#26292D', None, None, 'black iron: fire escape, railings, brackets, pipes'),
    'MB_Steel': ('#8F969D', None, None, 'brushed steel / aluminium'),
    'MB_Chrome': ('#D3D8DD', None, None, 'diner chrome trim'),
    'MB_Brass': ('#C9A45C', None, None, 'door handles, kick plates, house-number frames'),
    'MB_Wood': ('#6B4A32', None, None, 'dark stained wood'),
    'MB_WoodLight': ('#A8825A', None, None, 'light wood: tables, planters, A-frame'),
    'MB_Terracotta': ('#A85A3C', None, None, 'pots and flower boxes'),
    'MB_Foliage': ('#FFFFFF', 'T_Foliage.png', None, 'leaves with flowers; tileable 1 unit = 1 m'),
    'MB_WindowDark': ('#FFFFFF', 'T_WindowDark.png', None, 'SOLID toon surface, not see-through: painted window glass atlas (sky/street reflection, blinds, curtains), 4x3 cells'),
    'MB_WindowLit': ('#808080', 'T_WindowLit.png', ('#FFFFFF', 1.0), 'SOLID toon surface + emission, not see-through: lit window atlas (warm homes, cool offices); emission map = texture'),
    'MB_ShopGlass': ('#CFE0E8', 'T_GlassSheen.png', None,
                     'TRANSPARENT glass: alpha-blended, opacity ~6-9% from the texture alpha, no shadow casting. '
                     'Only on the optional <Name>_Glass child in front of the fake shop interiors; disable that '
                     'child if see-through glass is not wanted.'),
    'MB_LampGlow': ('#FFE7B8', None, ('#FFE7B8', 3.0), 'bulbs, wall lamps, pendant lamps'),
    'MB_SignsLit': ('#FFFFFF', 'T_SignsLit.png', ('#FFFFFF', 2.2), 'lightboxes and neon; emission map = texture (dark backgrounds stay dark)'),
    'MB_NeonGreen': ('#3CDC78', None, ('#3CDC78', 3.0), 'pharmacy cross edge tubes'),
    'MB_NeonPink': ('#FF4F9A', None, ('#FF4F9A', 3.0), 'diner neon tubes'),
    'MB_NeonBlue': ('#4FC3FF', None, ('#4FC3FF', 3.0), 'diner neon tubes'),
    'MB_Awning': ('#FFFFFF', 'T_Awnings.png', None, 'awning fabric atlas: 4 rows (solid green, red, green, navy stripes), each tiles in u'),
    'MB_Details': ('#FFFFFF', 'T_Details.png', None, 'AC fronts, louvres, doors, intercom, fan grilles, meter, skylight'),
    'MB_ACBody': ('#D8D3C7', None, None, 'AC unit / HVAC casing'),
    'MB_WaterTower': ('#FFFFFF', 'T_WoodStaves.png', None, 'water tower staves; tileable 2 m'),
    'MB_Shutter': ('#FFFFFF', 'T_Shutter.png', None, 'roll-down shutter; tileable 2 m'),
    'MB_Grate': ('#FFFFFF', 'T_Grate.png', None, 'fire-escape grating (top of platforms)'),
    'MB_Vinyl': ('#B8322B', None, None, 'diner booth / stool vinyl'),
    'MB_Granite': ('#8E8A82', None, None, 'granite base course, plinths'),
    'MB_TrimTeal': ('#2E5553', None, None, 'dark teal trim'),
    'MB_MetalDark': ('#3A3F45', None, None, 'modern window surrounds, canopies, fins'),
    'MB_TrimBottle': ('#1F4D3A', None, None, 'bottle-green shopfront paint (bookshop, florist)'),
    'MB_LanternRed': ('#E0392B', None, ('#FF5A3C', 1.6), 'paper lanterns (emissive)'),
    'MB_Paper': ('#FFFFFF', 'T_Paper.png', None, 'kraft paper over vacant shop windows; tileable 2 m'),
    'MB_PanelGrey': ('#8C939B', 'T_PanelLight.png', None, 'grey stone panels (bank); colour multiplies T_PanelLight'),
    'MB_TowerGlass': ('#FFFFFF', 'T_TowerGlass.png', None, 'SOLID toon surface, not see-through: tower curtain wall; UV tile = 4 floors x 8 modules'),
    'MB_TowerDark': ('#FFFFFF', 'T_TowerDark.png', None, 'SOLID toon surface, not see-through: tower dark glass + bronze mullions; tile = 4 floors x 8 modules'),
    'MB_TowerBands': ('#FFFFFF', 'T_TowerBands.png', None, 'SOLID toon surface, not see-through: tower ribbon windows + stone bands; tile = 4 floors x 8 modules'),
    'MB_TowerPunched': ('#FFFFFF', 'T_TowerPunched.png', None, 'SOLID toon surface, not see-through: residential tower punched windows; tile = 4 floors x 8 modules'),
    'MB_TowerTrim': ('#B4BAC0', None, None, 'tower rims, crown frames, masts'),
    'MB_TowerCrown': ('#6F7780', None, None, 'tower mechanical crowns / caps'),
    'MB_BeaconRed': ('#FF3030', None, ('#FF3030', 4.0), 'aircraft warning beacon'),
}
SHOPS = ['Cafe', 'Mart', 'Pharmacy', 'Diner', 'Noodle', 'Books', 'Laundry', 'Phone', 'Bakery', 'Florist', 'ForLease',
         'Bank']
for _s in SHOPS:
    MATS['MB_Sign_' + _s] = ('#FFFFFF', 'T_Sign_%s.png' % _s, None, '%s signs, posters and number plates (atlas)' % _s)
    MATS['MB_Int_' + _s] = ('#666666', 'T_Int_%s.png' % _s, ('#FFFFFF', 0.85),
                            '%s fake interior on recessed boxes behind the glass; emission map = texture' % _s)

_LAYOUT = None


def layout():
    global _LAYOUT
    if _LAYOUT is None:
        p = os.path.join(OUT, 'atlas_layout.json')
        if not os.path.exists(p):
            raise SystemExit('missing %s: run tools/opening_building_textures.py first' % p)
        with open(p) as fh:
            _LAYOUT = json.load(fh)
    return _LAYOUT


def slot(tex, name, inset=1.5):
    t = layout()[tex]
    W, H = t['size']
    x, y, w, h = t['slots'][name]['rect']
    return ((x + inset) / W, 1 - (y + h - inset) / H, (x + w - inset) / W, 1 - (y + inset) / H)


def slot_m(tex, name):
    return layout()[tex]['slots'][name]['m']


def quad_uv(r, flip=False):
    u0, v0, u1, v1 = r
    if flip:
        u0, u1 = u1, u0
    return [(u0, v0), (u1, v0), (u1, v1), (u0, v1)]


def srgb2lin(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def hex_rgb(h):
    h = h.lstrip('#')
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


# ---------------------------------------------------------------------------------------------- geometry core

class Frame:
    """Local facade frame: u along t (viewer's right when facing the facade), z up, d along n (outward)."""

    def __init__(self, A, t, n, s0=0.0):
        self.A = Vector(A)
        self.t = Vector(t).normalized()
        self.n = Vector(n).normalized()
        self.s0 = s0

    def P(self, u, z, d):
        return self.A + self.t * u + self.n * d + Z * z

    def N(self, nu, nz, nd):
        return self.t * nu + Z * nz + self.n * nd

    def at(self, u, d=0.0):
        return Frame(self.P(u, 0, d), self.t, self.n, self.s0 + u)

    def turned(self, u, d=0.0):
        """Frame at (u, d) whose t points outward (along n): for blade signs and side-on parts."""
        return Frame(self.P(u, 0, d), self.n, -self.t, 0.0)


WORLD = Frame((0, 0, 0), (1, 0, 0), (0, -1, 0))


def newell(pts):
    n = Vector((0.0, 0.0, 0.0))
    for i in range(len(pts)):
        a, b = pts[i], pts[(i + 1) % len(pts)]
        n.x += (a.y - b.y) * (a.z + b.z)
        n.y += (a.z - b.z) * (a.x + b.x)
        n.z += (a.x - b.x) * (a.y + b.y)
    return n


class MeshBuilder:
    def __init__(self):
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new('UVMap')
        self.mats, self.mi = [], {}

    def mat(self, name):
        if name not in MATS:
            raise KeyError('unknown material ' + name)
        if name not in self.mi:
            self.mi[name] = len(self.mats)
            self.mats.append(name)
        return self.mi[name]

    def face(self, pts, mat, normal=None, uvs=None, frame=None, k=0.5):
        pts = [Vector(p) for p in pts]
        nn = newell(pts)
        if nn.length < 1e-10:
            return None
        if normal is not None and nn.dot(Vector(normal)) < 0:
            pts.reverse()
            if uvs is not None:
                uvs = list(reversed(uvs))
            nn = -nn
        if uvs is None:
            uvs = project_uv(pts, nn.normalized(), frame or WORLD, k)
        f = self.bm.faces.new([self.bm.verts.new(p) for p in pts])
        f.material_index = self.mat(mat)
        for loop, uv in zip(f.loops, uvs):
            loop[self.uv].uv = uv
        return f

    def tris(self):
        return sum(len(f.verts) - 2 for f in self.bm.faces)


def project_uv(pts, n, F, k):
    nu, nz, nd = n.dot(F.t), n.dot(Z), n.dot(F.n)
    ax = max(range(3), key=lambda i: abs((nu, nz, nd)[i]))
    out = []
    for p in pts:
        q = p - F.A
        u, z, d = q.dot(F.t) + F.s0, q.dot(Z), q.dot(F.n)
        if ax == 2:
            out.append(((u if nd >= 0 else -u) * k, z * k))
        elif ax == 0:
            out.append(((-d if nu >= 0 else d) * k + F.s0 * k, z * k))
        else:
            out.append((u * k, (d if nz >= 0 else -d) * k))
    return out


# box face corner orders: [bottom-left, bottom-right, top-right, top-left] as seen from outside that face
def box(mb, F, u0, u1, z0, z1, d0, d1, mat, skip='', only=None, uv=None, mats=None, k=0.5):
    """Axis-aligned box in frame F. Faces: F front(+d) K back(-d) L(-u) R(+u) T(+z) B(-z)."""
    P = F.P
    faces = {
        'F': ([P(u0, z0, d1), P(u1, z0, d1), P(u1, z1, d1), P(u0, z1, d1)], F.n),
        'K': ([P(u1, z0, d0), P(u0, z0, d0), P(u0, z1, d0), P(u1, z1, d0)], -F.n),
        'R': ([P(u1, z0, d1), P(u1, z0, d0), P(u1, z1, d0), P(u1, z1, d1)], F.t),
        'L': ([P(u0, z0, d0), P(u0, z0, d1), P(u0, z1, d1), P(u0, z1, d0)], -F.t),
        'T': ([P(u0, z1, d1), P(u1, z1, d1), P(u1, z1, d0), P(u0, z1, d0)], Z),
        'B': ([P(u0, z0, d0), P(u1, z0, d0), P(u1, z0, d1), P(u0, z0, d1)], -Z),
    }
    for key, (pts, nrm) in faces.items():
        if key in skip or (only is not None and key not in only):
            continue
        m = (mats or {}).get(key, mat)
        mb.face(pts, m, normal=nrm, uvs=(uv or {}).get(key), frame=F, k=k)


def obox(mb, c, ax, ay, az, hx, hy, hz, mat, skip=''):
    """Oriented box: centre c, unit axes ax/ay/az, half sizes."""
    c = Vector(c)
    corners = {}
    for sx in (-1, 1):
        for sy in (-1, 1):
            for sz in (-1, 1):
                corners[(sx, sy, sz)] = c + ax * (sx * hx) + ay * (sy * hy) + az * (sz * hz)
    spec = {'+x': (0, 1), '-x': (0, -1), '+y': (1, 1), '-y': (1, -1), '+z': (2, 1), '-z': (2, -1)}
    axes = (ax, ay, az)
    for key, (i, s) in spec.items():
        if key in skip:
            continue
        others = [j for j in range(3) if j != i]
        pts = []
        for a, b in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
            idx = [0, 0, 0]
            idx[i] = s
            idx[others[0]] = a
            idx[others[1]] = b
            pts.append(corners[tuple(idx)])
        mb.face(pts, mat, normal=axes[i] * s)


def sweep(mb, path, profile, mat, closed_path=False, closed_profile=False, caps=True, s0=0.0, k=0.5):
    """Sweep a (d, z) profile along a horizontal polyline with mitred joints. d = outward offset (right-hand normal
    of the path direction, i.e. outward for CCW footprints), z = absolute height."""
    path = [Vector((p[0], p[1], 0.0)) for p in path]
    n = len(path)
    segs = n if closed_path else n - 1
    dirs, norms, lens = [], [], []
    for i in range(segs):
        a, b = path[i], path[(i + 1) % n]
        dv = (b - a)
        lens.append(dv.length)
        dv.normalize()
        dirs.append(dv)
        norms.append(Vector((dv.y, -dv.x, 0.0)))
    miters = []
    for i in range(n):
        if closed_path:
            na, nb = norms[(i - 1) % segs], norms[i % segs]
        else:
            na = norms[i - 1] if i > 0 else norms[0]
            nb = norms[i] if i < segs else norms[segs - 1]
        dd = 1 + na.dot(nb)
        miters.append((na + nb) / dd if dd > 1e-6 else na)

    def pt(i, j):
        d, z = profile[j]
        return path[i] + miters[i] * d + Z * z

    np_ = len(profile)
    pseg = np_ if closed_profile else np_ - 1
    cum = 0.0
    for i in range(segs):
        i2 = (i + 1) % n
        F = Frame(path[i], dirs[i], norms[i], s0 + cum)
        cum += lens[i]
        for j in range(pseg):
            j2 = (j + 1) % np_
            (d0, z0), (d1, z1) = profile[j], profile[j2]
            N = norms[i] * (z1 - z0) + Z * (-(d1 - d0))
            if N.length < 1e-9:
                continue
            mb.face([pt(i, j), pt(i2, j), pt(i2, j2), pt(i, j2)], mat, normal=N, frame=F, k=k)
    if caps and not closed_path:
        F0 = Frame(path[0], dirs[0], norms[0], s0)
        mb.face([pt(0, j) for j in range(np_)], mat, normal=-dirs[0], frame=F0, k=k)
        F1 = Frame(path[-1], dirs[-1], norms[-1], s0 + cum)
        mb.face([pt(n - 1, j) for j in range(np_)], mat, normal=dirs[-1], frame=F1, k=k)


def offset_poly(pts, d):
    """Mitred offset of a closed CCW polygon (d > 0 outward)."""
    pts = [Vector((p[0], p[1], 0.0)) for p in pts]
    n = len(pts)
    out = []
    for i in range(n):
        a, b, c = pts[i - 1], pts[i], pts[(i + 1) % n]
        d1, d2 = (b - a).normalized(), (c - b).normalized()
        n1, n2 = Vector((d1.y, -d1.x, 0)), Vector((d2.y, -d2.x, 0))
        m = (n1 + n2) / (1 + n1.dot(n2))
        out.append(b + m * d)
    return out


def ring(c, ax, r, segs, e1=None):
    ax = Vector(ax).normalized()
    if e1 is None:
        e1 = ax.orthogonal().normalized()
    e2 = ax.cross(e1).normalized()
    return [Vector(c) + (e1 * math.cos(2 * math.pi * i / segs) + e2 * math.sin(2 * math.pi * i / segs)) * r
            for i in range(segs)]


def cylinder(mb, base, axis, r, h, segs, mat, cap0=True, cap1=True, r1=None, cap_mat=None, e1=None, k=0.5):
    axis = Vector(axis).normalized()
    r1 = r if r1 is None else r1
    b0 = ring(base, axis, r, segs, e1)
    top = Vector(base) + axis * h
    b1 = ring(top, axis, r1, segs, e1) if r1 > 0 else None
    circ = 2 * math.pi * max(r, r1)
    for i in range(segs):
        j = (i + 1) % segs
        u0, u1 = i / segs * circ * k, (i + 1) / segs * circ * k
        mid = (b0[i] + b0[j]) * 0.5 - Vector(base)
        if b1 is None:
            mb.face([b0[i], b0[j], top], mat, normal=mid + axis * (r / max(h, 1e-6)) * r,
                    uvs=[(u0, 0), (u1, 0), ((u0 + u1) / 2, h * k)])
        else:
            mb.face([b0[i], b0[j], b1[j], b1[i]], mat, normal=mid, uvs=[(u0, 0), (u1, 0), (u1, h * k), (u0, h * k)])
    if cap0:
        mb.face(list(reversed(b0)), cap_mat or mat, normal=-axis)
    if cap1 and b1 is not None:
        mb.face(b1, cap_mat or mat, normal=axis)


def prism(mb, F, poly_dz, u0, u1, mat, skip_u0=False, skip_u1=False, skip_edges=()):
    """Extrude a (d, z) polygon (side profile, CCW in d-z) along u from u0 to u1 (brackets, wedges)."""
    P = F.P
    n = len(poly_dz)
    for j in range(n):
        if j in skip_edges:
            continue
        (d0, z0), (d1, z1) = poly_dz[j], poly_dz[(j + 1) % n]
        N = F.n * (z1 - z0) + Z * (-(d1 - d0))
        mb.face([P(u0, z0, d0), P(u1, z0, d0), P(u1, z1, d1), P(u0, z1, d1)], mat, normal=N, frame=F)
    if not skip_u0:
        mb.face([P(u0, z, d) for d, z in poly_dz], mat, normal=-F.t, frame=F)
    if not skip_u1:
        mb.face([P(u1, z, d) for d, z in poly_dz], mat, normal=F.t, frame=F)


# ------------------------------------------------------------------------------------------------- building

class Facade:
    def __init__(self, B, idx, a, b, s0, kind, wall_mat):
        self.B, self.idx, self.kind = B, idx, kind
        t = (b - a)
        self.L = t.length
        t.normalize()
        self.F = Frame(a, t, Vector((t.y, -t.x, 0.0)), s0)
        self.wall_mat = wall_mat
        self.zones = []        # (z0, z1, mat): wall material by height band
        self.openings = []

    def opening(self, u0, u1, z0, z1, depth, side=None, top=None, bottom=None, no_bottom=False):
        side = side or self.wall_mat
        self.openings.append(dict(u0=u0, u1=u1, z0=z0, z1=z1, depth=depth, side=side, top=top or side,
                                  bottom=bottom or side, no_bottom=no_bottom or z0 <= EPS))
        self.B.zcuts.update([round(z0, 4), round(z1, 4)])

    def zone(self, z0, z1, mat):
        self.zones.append((z0, z1, mat))
        self.B.zcuts.update([round(z0, 4), round(z1, 4)])

    def mat_at(self, z):
        for z0, z1, m in self.zones:
            if z0 - EPS <= z <= z1 + EPS:
                return m
        return self.wall_mat

    def emit_wall(self, zcuts):
        mb, F = self.B.mb, self.F
        zs = [z for z in zcuts if -EPS <= z <= self.B.top + EPS]
        us = sorted(set([0.0, round(self.L, 4)] + [round(v, 4) for o in self.openings for v in (o['u0'], o['u1'])]))
        us = [u for u in us if -EPS <= u <= self.L + EPS]
        for i in range(len(us) - 1):
            for j in range(len(zs) - 1):
                uc, zc = (us[i] + us[i + 1]) / 2, (zs[j] + zs[j + 1]) / 2
                if any(o['u0'] < uc < o['u1'] and o['z0'] < zc < o['z1'] for o in self.openings):
                    continue
                mb.face([F.P(us[i], zs[j], 0), F.P(us[i + 1], zs[j], 0), F.P(us[i + 1], zs[j + 1], 0),
                         F.P(us[i], zs[j + 1], 0)], self.mat_at(zc), normal=F.n, frame=F)
        for o in self.openings:
            d = -o['depth']
            zz = [z for z in zs if o['z0'] - EPS <= z <= o['z1'] + EPS]
            uu = [u for u in us if o['u0'] - EPS <= u <= o['u1'] + EPS]
            skip_l, skip_r = o.get('skip_sides', (False, False))
            for j in range(len(zz) - 1):
                if not skip_l:
                    mb.face([F.P(o['u0'], zz[j], 0), F.P(o['u0'], zz[j], d), F.P(o['u0'], zz[j + 1], d),
                             F.P(o['u0'], zz[j + 1], 0)], o['side'], normal=F.t, frame=F)
                if not skip_r:
                    mb.face([F.P(o['u1'], zz[j], 0), F.P(o['u1'], zz[j], d), F.P(o['u1'], zz[j + 1], d),
                             F.P(o['u1'], zz[j + 1], 0)], o['side'], normal=-F.t, frame=F)
            for i in range(len(uu) - 1):
                mb.face([F.P(uu[i], o['z1'], 0), F.P(uu[i + 1], o['z1'], 0), F.P(uu[i + 1], o['z1'], d),
                         F.P(uu[i], o['z1'], d)], o['top'], normal=-Z, frame=F)
                if not o['no_bottom']:
                    mb.face([F.P(uu[i], o['z0'], 0), F.P(uu[i + 1], o['z0'], 0), F.P(uu[i + 1], o['z0'], d),
                             F.P(uu[i], o['z0'], d)], o['bottom'], normal=Z, frame=F)


class Building:
    STREET = ('front', 'side', 'chamfer', 'round')

    def __init__(self, name, footprint, kinds, floors, wall_mat, side_mat='MB_BrickCommon', parapet=PARAPET,
                 ground_h=GROUND_H, floor_h=FLOOR_H, parapet_t=0.3, coping_mat='MB_StoneTrim', roof_mat='MB_Roof',
                 lit_frac=0.25, footprint_wd=None, side=None, kind='corner'):
        self.name, self.kind, self.side = name, kind, side
        self.fp = [Vector((x, y, 0.0)) for x, y in footprint]
        self.floors, self.ground_h, self.floor_h = floors, ground_h, floor_h
        self.H = ground_h + (floors - 1) * floor_h
        self.top = self.H + parapet
        self.parapet_t, self.coping_mat, self.roof_mat = parapet_t, coping_mat, roof_mat
        self.wall_mat, self.side_mat = wall_mat, side_mat
        self.rng = random.Random(name)
        self.lit_frac = lit_frac
        self.mb, self.glass = MeshBuilder(), MeshBuilder()
        self.zcuts = {0.0, round(self.top, 4)}
        self.locators = []
        self.rooms = []         # plan polygons of fake interiors (overlap check)
        self.notes = []
        self.copy = []          # draft copy used on this building
        self.footprint_wd = footprint_wd
        self.facades = []
        s = 0.0
        for i, kd in enumerate(kinds):
            a, b = self.fp[i], self.fp[(i + 1) % len(self.fp)]
            f = Facade(self, i, a, b, s, kd, wall_mat if kd in self.STREET else side_mat)
            s += f.L
            self.facades.append(f)

    def fz(self, k):
        return 0.0 if k == 0 else self.ground_h + (k - 1) * self.floor_h

    def fac(self, kind, nth=0):
        return [f for f in self.facades if f.kind == kind][nth]

    def street_path(self):
        """Footprint vertices covering the consecutive street facades (open polyline)."""
        n = len(self.facades)
        street = [f.kind in self.STREET for f in self.facades]
        start = next(i for i in range(n) if street[i] and not street[i - 1])
        idx = [start]
        i = start
        while street[i % n]:
            i += 1
            idx.append(i % n)
            if len(idx) > n:
                break
        return [self.fp[j] for j in idx], self.facades[start].F.s0

    def loc(self, name, p):
        self.locators.append((self.name + '_' + name, Vector(p)))

    def finish(self):
        zc = sorted(self.zcuts)
        for f in self.facades:
            f.emit_wall(zc)
        t = self.parapet_t
        sweep(self.mb, self.fp, [(-t, self.top), (-t, self.H)], self.side_mat, closed_path=True)
        inner = offset_poly(self.fp, -t)
        self.mb.face([Vector((p.x, p.y, self.H)) for p in inner], self.roof_mat, normal=Z)
        sweep(self.mb, self.fp, [(-(t + 0.04), self.top), (0.06, self.top), (0.06, self.top + 0.12),
                                 (-(t + 0.04), self.top + 0.12)], self.coping_mat, closed_path=True,
              closed_profile=True)


# ------------------------------------------------------------------------------------------------ elements

WIN_DARK = ['plain', 'blinds_top', 'curtains_red', 'net', 'plant', 'blinds_full', 'curtains_blue', 'roller', 'shelf',
            'venetian_dark', 'curtains_green', 'lamp_off']
WIN_LIT_HOME = ['warm_room', 'warm_blinds', 'warm_curtains', 'kitchen', 'warm_plant', 'lamp', 'warm_blinds_full', 'tv',
                'frosted']
WIN_LIT_OFFICE = ['office1', 'office2', 'office_blinds']
WIN_DARK_OFFICE = ['plain', 'blinds_full', 'venetian_dark', 'blinds_top']


def glass_pane(B, F, u0, u1, z0, z1, d, lit=None, office=False, variant=None):
    if lit is None:
        lit = B.rng.random() < B.lit_frac
    if variant is None:
        pool = (WIN_LIT_OFFICE if office else WIN_LIT_HOME) if lit else (WIN_DARK_OFFICE if office else WIN_DARK)
        variant = B.rng.choice(pool)
    tex, mat = ('T_WindowLit.png', 'MB_WindowLit') if lit else ('T_WindowDark.png', 'MB_WindowDark')
    B.mb.face([F.P(u0, z0, d), F.P(u1, z0, d), F.P(u1, z1, d), F.P(u0, z1, d)], mat, normal=F.n,
              uvs=quad_uv(slot(tex, variant, inset=3)))
    return lit


def frame_bars(B, F, u0, u1, z0, z1, d0, d1, mat, fw=0.06, rails=(), mulls=()):
    mb = B.mb
    box(mb, F, u0, u0 + fw, z0, z1, d0, d1, mat, only='FR')
    box(mb, F, u1 - fw, u1, z0, z1, d0, d1, mat, only='FL')
    box(mb, F, u0 + fw, u1 - fw, z1 - fw, z1, d0, d1, mat, only='FB')
    box(mb, F, u0 + fw, u1 - fw, z0, z0 + fw, d0, d1, mat, only='FT')
    for zr in rails:
        box(mb, F, u0 + fw, u1 - fw, zr - fw * 0.5, zr + fw * 0.5, d0, d1 + 0.012, mat, only='FTB')
    for um in mulls:
        segs = [z0 + fw] + sorted(zr for zr in rails) + [z1 - fw]
        for a, b in zip(segs[:-1], segs[1:]):
            lo = a + (fw * 0.5 if a != z0 + fw else 0)
            hi = b - (fw * 0.5 if b != z1 - fw else 0)
            box(mb, F, um - fw * 0.4, um + fw * 0.4, lo, hi, d0, d1 - 0.005, mat, only='FLR')


def sill(B, F, u0, u1, z, proj=0.07, h=0.075, mat='MB_StoneTrim', ext=0.07):
    sweep(B.mb, [F.P(u0 - ext, 0, 0), F.P(u1 + ext, 0, 0)],
          [(0, z - h), (proj - 0.02, z - h), (proj, z - h + 0.02), (proj, z - 0.012), (proj - 0.012, z), (0, z)], mat,
          s0=F.s0 + u0)


def lintel(B, F, u0, u1, z, style='flat', mat='MB_StoneTrim'):
    mb = B.mb
    if style == 'flat':
        box(mb, F, u0 - 0.1, u1 + 0.1, z, z + 0.22, 0, 0.035, mat, skip='K')
    elif style == 'key':
        box(mb, F, u0 - 0.1, u1 + 0.1, z, z + 0.22, 0, 0.035, mat, skip='K')
        uc = (u0 + u1) / 2
        prism(mb, F, [(0, z - 0.03), (0.06, z - 0.03), (0.06, z + 0.28), (0, z + 0.28)], uc - 0.07, uc + 0.07, mat,
              skip_edges=(3,))
    elif style == 'hood':
        box(mb, F, u0 - 0.13, u0, z - 0.05, z + 0.12, 0, 0.04, mat, skip='K')     # architrave ears
        box(mb, F, u1, u1 + 0.13, z - 0.05, z + 0.12, 0, 0.04, mat, skip='K')
        box(mb, F, u0 - 0.13, u1 + 0.13, z, z + 0.16, 0, 0.04, mat, skip='K')
        sweep(mb, [F.P(u0 - 0.24, 0, 0), F.P(u1 + 0.24, 0, 0)],
              [(0, z + 0.16), (0.05, z + 0.16), (0.07, z + 0.2), (0.12, z + 0.22), (0.14, z + 0.3), (0.14, z + 0.34),
               (0, z + 0.34)], mat, s0=F.s0 + u0)
        for uc in (u0 - 0.17, u1 + 0.17):
            prism(mb, F, [(0, z - 0.12), (0.05, z - 0.12), (0.11, z + 0.1), (0.11, z + 0.16), (0, z + 0.16)],
                  uc - 0.055, uc + 0.055, mat, skip_edges=(3, 4))
    elif style == 'soldier':
        box(mb, F, u0 - 0.05, u1 + 0.05, z, z + 0.2, 0, 0.02, mat, skip='K')


def window(B, f, uc, z0, w=1.1, h=1.75, depth=0.15, frame='MB_TrimWhite', sill_style='stone', lintel_style='flat',
           sash='double', lit=None, variant=None, office=False, mulls=None, ac=None, flower=None, trim='MB_StoneTrim'):
    F, mb = f.F, B.mb
    u0, u1, z1 = uc - w / 2, uc + w / 2, z0 + h
    f.opening(u0, u1, z0, z1, depth, bottom=trim if sill_style else None)
    lit = glass_pane(B, F, u0, u1, z0, z1, -depth, lit=lit, office=office, variant=variant)
    rails = [z0 + h * 0.52] if sash == 'double' else ([z0 + h * 0.72] if sash == 'transom' else [])
    if mulls is None:
        mulls = [uc] if (sash == 'casement' or w > 1.5) else []
    frame_bars(B, F, u0, u1, z0, z1, -depth, -depth + 0.06, frame, rails=rails, mulls=mulls)
    if sill_style == 'stone':
        sill(B, F, u0, u1, z0, mat=trim)
    if lintel_style:
        lintel(B, F, u0, u1, z1, lintel_style, mat=trim)
    if ac is None:
        ac = (not lit) and B.rng.random() < 0.12 and not office
    if ac:
        ac_unit(B, F, uc + (w * 0.15 if B.rng.random() < 0.5 else -w * 0.15), z0)
    elif flower or (flower is None and B.rng.random() < 0.14 and not office):
        flower_box(B, F, u0 + 0.02, u1 - 0.02, z0)
    return lit


def ac_unit(B, F, uc, z0):
    mb = B.mb
    box(mb, F, uc - 0.33, uc + 0.33, z0 + 0.01, z0 + 0.43, -0.11, 0.42, 'MB_ACBody', skip='K',
        uv={'F': quad_uv(slot('T_Details.png', 'ac'))}, mats={'F': 'MB_Details'})
    box(mb, F, uc - 0.3, uc - 0.26, z0 - 0.25, z0 + 0.01, 0.0, 0.36, 'MB_Metal', skip='K')
    box(mb, F, uc + 0.26, uc + 0.3, z0 - 0.25, z0 + 0.01, 0.0, 0.36, 'MB_Metal', skip='K')


def flower_box(B, F, u0, u1, z0):
    mb = B.mb
    box(mb, F, u0, u1, z0 + 0.0, z0 + 0.2, 0.075, 0.3, 'MB_Terracotta', skip='K')
    box(mb, F, u0 + 0.03, u1 - 0.03, z0 + 0.2, z0 + 0.36, 0.09, 0.28, 'MB_Foliage', skip='KB', k=1.0)
    n = max(2, int((u1 - u0) / 0.3))
    for i in range(n):
        uc = u0 + (i + 0.5) * (u1 - u0) / n
        hh = B.rng.uniform(0.06, 0.16)
        box(mb, F, uc - 0.1, uc + 0.1, z0 + 0.36, z0 + 0.36 + hh, 0.12, 0.26, 'MB_Foliage', skip='KB', k=1.0)


def pilaster(B, f, u0, u1, z0, z1, proj=0.12, mat=None, base_mat='MB_StoneTrim', base_h=0.5, cap=True):
    F, mb = f.F, B.mb
    mat = mat or f.wall_mat
    box(mb, F, u0, u1, z0, z1, 0, proj, mat, skip='KBT')
    if base_mat:
        box(mb, F, u0 - 0.03, u1 + 0.03, z0, z0 + base_h, 0, proj + 0.035, base_mat, skip='KB')
    if cap:
        box(mb, F, u0 - 0.03, u1 + 0.03, z1 - 0.16, z1, 0, proj + 0.035, base_mat or mat, skip='KT')


def threshold(B, F, u0, u1, d0, d1=0.0, z=0.02, mat='MB_Concrete'):
    box(B.mb, F, u0, u1, 0.0, z, d0, d1, mat, only='TF')


def interior_box(B, F, u0, u1, d_front, depth, shop, pano='panoA', pano_off=0.0, z_floor=0.03, z_ceil=3.6):
    mb = B.mb
    tex, mat = 'T_Int_%s.png' % shop, 'MB_Int_' + shop
    d_back = d_front - depth
    B.rooms.append([F.P(u0, 0, d_front), F.P(u1, 0, d_front), F.P(u1, 0, d_back), F.P(u0, 0, d_back)])
    L = layout()[tex]
    W, H = L['size']
    x, y, w, h = L['slots'][pano]['rect']
    v_lo, v_hi = 1 - (y + h - 2) / H, 1 - (y + 2) / H
    pw = L['slots'][pano]['m'][0]
    ph = L['slots'][pano]['m'][1]

    def v(z):
        return v_lo + (v_hi - v_lo) * min(1.0, max(0.0, z / ph))

    a, b = pano_off / pw, (pano_off + (u1 - u0)) / pw
    mb.face([F.P(u0, z_floor, d_back), F.P(u1, z_floor, d_back), F.P(u1, z_ceil, d_back), F.P(u0, z_ceil, d_back)],
            mat, normal=F.n, uvs=[(a, v(z_floor)), (b, v(z_floor)), (b, v(z_ceil)), (a, v(z_ceil))])
    mb.face([F.P(u0, z_floor, d_front), F.P(u1, z_floor, d_front), F.P(u1, z_floor, d_back),
             F.P(u0, z_floor, d_back)], mat, normal=Z, uvs=quad_uv(slot(tex, 'floor', 3)))
    mb.face([F.P(u0, z_ceil, d_back), F.P(u1, z_ceil, d_back), F.P(u1, z_ceil, d_front),
             F.P(u0, z_ceil, d_front)], mat, normal=-Z, uvs=quad_uv(slot(tex, 'ceiling', 3)))
    dep = depth / pw            # side walls continue the panorama around the box corners
    vf, vc = v(z_floor), v(z_ceil)
    mb.face([F.P(u0, z_floor, d_front), F.P(u0, z_floor, d_back), F.P(u0, z_ceil, d_back),
             F.P(u0, z_ceil, d_front)], mat, normal=F.t, uvs=[(a - dep, vf), (a, vf), (a, vc), (a - dep, vc)])
    mb.face([F.P(u1, z_floor, d_back), F.P(u1, z_floor, d_front), F.P(u1, z_ceil, d_front),
             F.P(u1, z_ceil, d_back)], mat, normal=-F.t, uvs=[(b, vf), (b + dep, vf), (b + dep, vc), (b, vc)])
    return d_back


def swatch_box(B, F, u0, u1, z0, z1, d0, d1, shop, idx, skip='K'):
    """Prop inside a fake interior, coloured from the interior texture's swatch row (lit like the room)."""
    tex = 'T_Int_%s.png' % shop
    uv = {}
    for key, s in (('T', 0), ('F', 1), ('L', 2), ('R', 2), ('K', 2), ('B', 2)):
        uv[key] = quad_uv(slot(tex, 'sw%d_%d' % (idx, s), 4))
    box(B.mb, F, u0, u1, z0, z1, d0, d1, 'MB_Int_' + shop, skip=skip, uv=uv)


def shopfront(B, f, u0, u1, shop, pano='panoA', pano_off=0.0, door=None, door_w=1.0, mulls=(), stall_h=0.45,
              transom=2.75, head=3.35, frame='MB_TrimDark', stall='MB_TrimDark', reveal=0.25, depth=1.75,
              transom_div=0.9, stall_panel=True, glass=True, door_frame=None, interior=True, sill_mat=None,
              box_u0=None, box_u1=None):
    """Glazed shopfront set back `reveal` from the facade, with a fake interior box behind it."""
    F, mb = f.F, B.mb
    top = head + 0.08
    f.opening(u0, u1, 0.0, top, reveal, side=f.wall_mat, top=frame)
    dg = -reveal                      # glass plane
    b0, b1 = dg - 0.03, dg + 0.06     # bar depth range
    jw = 0.1
    door_frame = door_frame or frame
    du = (door - door_w / 2, door + door_w / 2) if door is not None else None
    # stallriser (split around the door) + cap
    spans = [(u0 + jw, u1 - jw)] if du is None else [(u0 + jw, du[0] - 0.06), (du[1] + 0.06, u1 - jw)]
    for a, b in spans:
        if b - a < 0.05:
            continue
        box(mb, F, a, b, 0.0, stall_h, dg - 0.05, dg + 0.1, stall, skip='KTB')
        if stall_panel:
            box(mb, F, a + 0.08, b - 0.08, 0.1, stall_h - 0.08, dg + 0.1, dg + 0.12, stall, only='FLRTB')
        box(mb, F, a - 0.02, b + 0.02, stall_h, stall_h + 0.05, dg - 0.05, dg + 0.14, sill_mat or frame, skip='K')
    # jambs, head, transom bar
    box(mb, F, u0, u0 + jw, 0.0, top, b0, b1, frame, only='FR')
    box(mb, F, u1 - jw, u1, 0.0, top, b0, b1, frame, only='FL')
    box(mb, F, u0 + jw, u1 - jw, head, top, b0, b1, frame, only='FB')
    tr_spans = [(u0 + jw, u1 - jw)]
    for a, b in tr_spans:
        box(mb, F, a, b, transom, transom + 0.08, b0, b1, frame, only='FTB')
    # mullions in display windows (stall cap to transom), transom muntins
    for um in mulls:
        box(mb, F, um - 0.03, um + 0.03, stall_h + 0.05, transom, b0, b1, frame, only='FLR')
    nt = max(1, int(round((u1 - u0 - 2 * jw) / transom_div)))
    for i in range(1, nt):
        um = u0 + jw + i * (u1 - u0 - 2 * jw) / nt
        box(mb, F, um - 0.02, um + 0.02, transom + 0.08, head, b0 + 0.01, b1 - 0.01, frame, only='FLR')
    # door
    if du is not None:
        a, b = du
        box(mb, F, a - 0.06, a, 0.0, transom, b0, b1, door_frame, only='FLR')        # door jambs
        box(mb, F, b, b + 0.06, 0.0, transom, b0, b1, door_frame, only='FLR')
        dd0, dd1 = dg - 0.04, dg + 0.02                                                  # leaf, slightly recessed
        box(mb, F, a, a + 0.1, 0.0, transom - 0.02, dd0, dd1, door_frame, only='FR')
        box(mb, F, b - 0.1, b, 0.0, transom - 0.02, dd0, dd1, door_frame, only='FL')
        box(mb, F, a + 0.1, b - 0.1, transom - 0.12, transom - 0.02, dd0, dd1, door_frame, only='FB')
        box(mb, F, a + 0.1, b - 0.1, 0.0, 0.32, dd0, dd1, door_frame, only='FT')
        box(mb, F, a + 0.12, b - 0.12, 0.04, 0.26, dd1, dd1 + 0.008, 'MB_Brass', only='FTLR')   # kick plate
        hx = b - 0.2
        box(mb, F, hx - 0.015, hx + 0.015, 0.85, 1.35, dd1 + 0.04, dd1 + 0.07, 'MB_Brass', skip='K')  # pull bar
        box(mb, F, hx - 0.012, hx + 0.012, 0.9, 0.93, dd1, dd1 + 0.04, 'MB_Brass', only='FTBLR')
        box(mb, F, hx - 0.012, hx + 0.012, 1.27, 1.3, dd1, dd1 + 0.04, 'MB_Brass', only='FTBLR')
    threshold(B, F, u0, u1, dg - 0.05, 0.0)
    if interior:
        # box_u0/box_u1 let corner shops share one L-shaped room instead of two intersecting boxes
        interior_box(B, F, u0 if box_u0 is None else box_u0, u1 if box_u1 is None else box_u1, b0, depth, shop, pano,
                     pano_off)
    if glass:
        gd = dg + 0.015
        gl = B.glass
        z_lo = stall_h + 0.05
        if du is None:
            gl.face([F.P(u0 + jw, z_lo, gd), F.P(u1 - jw, z_lo, gd), F.P(u1 - jw, head, gd), F.P(u0 + jw, head, gd)],
                    'MB_ShopGlass', normal=F.n, frame=F)
        else:
            for a, b in ((u0 + jw, du[0] - 0.06), (du[1] + 0.06, u1 - jw)):
                if b - a > 0.05:
                    gl.face([F.P(a, z_lo, gd), F.P(b, z_lo, gd), F.P(b, head, gd), F.P(a, head, gd)], 'MB_ShopGlass',
                            normal=F.n, frame=F)
            gl.face([F.P(du[0] - 0.06, transom, gd), F.P(du[1] + 0.06, transom, gd), F.P(du[1] + 0.06, head, gd),
                     F.P(du[0] - 0.06, head, gd)], 'MB_ShopGlass', normal=F.n, frame=F)
            gl.face([F.P(du[0], 0.3, dg - 0.01), F.P(du[1], 0.3, dg - 0.01), F.P(du[1], transom - 0.1, dg - 0.01),
                     F.P(du[0], transom - 0.1, dg - 0.01)], 'MB_ShopGlass', normal=F.n, frame=F)
    B.loc('Shop_%d_%d' % (f.idx, int(u0 * 10)), F.P((u0 + u1) / 2, 2.6, dg - depth * 0.5))
    return b0


def sign_board(B, F, uc, zc, tex, name, mat, depth=0.08, d0=0.0, frame='MB_TrimDark', size=None, lit=False):
    w, h = size or slot_m(tex, name)
    box(B.mb, F, uc - w / 2, uc + w / 2, zc - h / 2, zc + h / 2, d0, d0 + depth, frame, skip='K',
        uv={'F': quad_uv(slot(tex, name, 2))}, mats={'F': mat})
    return w, h


def blade_sign(B, F, u, zc, tex, name, mat, d_wall=0.12, thick=0.08, frame='MB_TrimDark', round_segs=0,
               bracket='MB_Metal', size=None):
    """Double-sided sign perpendicular to the facade; text reads correctly from both sides."""
    w, h = size or slot_m(tex, name)
    G = F.turned(u)
    r = slot(tex, name, 2)
    mb = B.mb
    if round_segs:
        c = G.P(d_wall + w / 2, zc, 0)
        rr = w / 2
        for side, nrm, flip in ((thick / 2, G.n, False), (-thick / 2, -G.n, True)):
            pts, uvs = [], []
            for i in range(round_segs):
                a = 2 * math.pi * i / round_segs
                pts.append(c + G.t * (math.cos(a) * rr) + Z * (math.sin(a) * rr) + G.n * side)
                uu = 0.5 + 0.5 * math.cos(a) * (-1 if flip else 1)
                vv = 0.5 + 0.5 * math.sin(a)
                uvs.append((r[0] + (r[2] - r[0]) * uu, r[1] + (r[3] - r[1]) * vv))
            mb.face(pts, mat, normal=nrm, uvs=uvs)
        for i in range(round_segs):
            a0, a1 = 2 * math.pi * i / round_segs, 2 * math.pi * (i + 1) / round_segs
            p0 = c + G.t * (math.cos(a0) * rr) + Z * (math.sin(a0) * rr)
            p1 = c + G.t * (math.cos(a1) * rr) + Z * (math.sin(a1) * rr)
            mb.face([p0 + G.n * (thick / 2), p1 + G.n * (thick / 2), p1 - G.n * (thick / 2), p0 - G.n * (thick / 2)],
                    frame, normal=(p0 + p1) * 0.5 - c)
        top = zc + rr
    else:
        box(mb, G, d_wall, d_wall + w, zc - h / 2, zc + h / 2, -thick / 2, thick / 2, frame,
            uv={'F': quad_uv(r), 'K': quad_uv(r)}, mats={'F': mat, 'K': mat})
        top = zc + h / 2
    # bracket: wall plate + arm + hangers
    box(mb, G, 0.0, d_wall + w + 0.08, top + 0.06, top + 0.11, -0.025, 0.025, bracket)
    box(mb, G, 0.0, 0.03, top - 0.25, top + 0.3, -0.08, 0.08, bracket, skip='L')
    for dd in (d_wall + 0.08, d_wall + w - 0.08):
        box(mb, G, dd - 0.01, dd + 0.01, top - 0.02, top + 0.06, -0.01, 0.01, bracket, only='FKLR')
    obox(mb, G.P(d_wall * 0.5 + w * 0.25, top + 0.26, 0), (G.t * (d_wall + w * 0.5) + Z * (-0.2)).normalized(),
         G.n, (G.t * 0.2 + Z * (d_wall + w * 0.5)).normalized(), (math.hypot(d_wall + w * 0.5, 0.2)) / 2 + 0.02,
         0.015, 0.015, bracket)


def awning(B, f, u0, u1, z_top, proj=1.4, drop=0.55, val=0.3, row='solid_green', valance=None, frame='MB_Metal'):
    """Shed awning with closed cheeks. valance = (tex, slot, mat) for lettering on the front flap."""
    F, mb = f.F, B.mb
    r = slot('T_Awnings.png', row, 3)
    zf = z_top - drop
    ln = math.hypot(proj, drop)
    a, b = (u0 + F.s0) / 2.0, (u1 + F.s0) / 2.0
    top_uv = [(a, r[3]), (b, r[3]), (b, r[1]), (a, r[1])]
    mb.face([F.P(u0, z_top, 0.02), F.P(u1, z_top, 0.02), F.P(u1, zf, proj), F.P(u0, zf, proj)], 'MB_Awning',
            normal=F.N(0, proj, drop), uvs=top_uv)
    mb.face([F.P(u0, z_top - 0.03, 0.02), F.P(u1, z_top - 0.03, 0.02), F.P(u1, zf - 0.03, proj - 0.02),
             F.P(u0, zf - 0.03, proj - 0.02)], 'MB_Awning', normal=F.N(0, -proj, -drop), uvs=top_uv)
    if valance:
        vt, vs, vm = valance
        vuv = quad_uv(slot(vt, vs, 2))
    else:
        vm, vuv = 'MB_Awning', [(a, r[1]), (b, r[1]), (b, r[1] + (r[3] - r[1]) * 0.3), (a, r[1] + (r[3] - r[1]) * 0.3)]
    mb.face([F.P(u0, zf - val, proj), F.P(u1, zf - val, proj), F.P(u1, zf, proj), F.P(u0, zf, proj)], vm,
            normal=F.n, uvs=vuv)
    mb.face([F.P(u0, zf - val, proj - 0.02), F.P(u1, zf - val, proj - 0.02), F.P(u1, zf - 0.03, proj - 0.02),
             F.P(u0, zf - 0.03, proj - 0.02)], 'MB_Awning', normal=-F.n, uvs=vuv)
    mb.face([F.P(u0, zf - val, proj - 0.02), F.P(u1, zf - val, proj - 0.02), F.P(u1, zf - val, proj),
             F.P(u0, zf - val, proj)], 'MB_Awning', normal=-Z, uvs=vuv)
    cheek = [(0.02, z_top), (proj, zf), (proj, zf - val), (0.02, z_top - 0.42)]
    for u, s in ((u0, -1), (u1, 1)):
        pts = [F.P(u, z, d) for d, z in cheek]
        mb.face(pts, 'MB_Awning', normal=F.t * s,
                uvs=[(d * 0.5 + a, r[1] + (r[3] - r[1]) * (0.2 + 0.6 * (z - zf + val) / (drop + val))) for d, z in cheek])
        ui = u - s * 0.02
        mb.face([F.P(ui, z, d) for d, z in cheek], 'MB_Awning', normal=-F.t * s,
                uvs=[(d * 0.5 + a, r[1] + (r[3] - r[1]) * 0.5) for d, z in cheek])
    # wall rail + front tube
    box(mb, F, u0 - 0.05, u1 + 0.05, z_top - 0.02, z_top + 0.06, 0.0, 0.06, frame, skip='K')
    cylinder(mb, F.P(u0 + 0.01, zf - 0.03, proj - 0.03), F.t, 0.02, (u1 - u0) - 0.02, 6, frame, cap0=False,
             cap1=False)


def res_door(B, f, uc, door='door_green', n_steps=3, step_h=0.15, step_d=0.32, w=1.1, h=2.35, recess=0.3,
             number=None, transom=True, surround='MB_StoneTrim', rail=True, lamp=True, hood=True):
    """Residential entrance: recessed panelled door + transom, stone surround and hood, stoop steps with rails,
    house number, wall lamp, intercom."""
    F, mb = f.F, B.mb
    zt = n_steps * step_h
    u0, u1 = uc - w / 2, uc + w / 2
    top = zt + h + (0.55 if transom else 0.0)
    f.opening(u0, u1, zt, top, recess, side=surround, top=surround, bottom='MB_Concrete')
    mb.face([F.P(u0, zt, -recess), F.P(u1, zt, -recess), F.P(u1, zt + h, -recess), F.P(u0, zt + h, -recess)],
            'MB_Details', normal=F.n, uvs=quad_uv(slot('T_Details.png', door, 2)))
    frame_bars(B, F, u0, u1, zt, zt + h + 0.04, -recess, -recess + 0.05, 'MB_TrimDark', fw=0.05)
    if transom:
        glass_pane(B, F, u0, u1, zt + h + 0.04, top, -recess, lit=True, variant='warm_room')
        frame_bars(B, F, u0, u1, zt + h + 0.04, top, -recess, -recess + 0.05, 'MB_TrimDark', fw=0.05,
                   mulls=[uc - w / 6, uc + w / 6])
    # surround (architrave) + hood
    box(mb, F, u0 - 0.16, u0, zt, top + 0.0, 0, 0.05, surround, skip='K')
    box(mb, F, u1, u1 + 0.16, zt, top + 0.0, 0, 0.05, surround, skip='K')
    box(mb, F, u0 - 0.16, u1 + 0.16, top, top + 0.18, 0, 0.05, surround, skip='K')
    if hood:
        sweep(mb, [F.P(u0 - 0.3, 0, 0), F.P(u1 + 0.3, 0, 0)],
              [(0, top + 0.18), (0.08, top + 0.18), (0.12, top + 0.24), (0.2, top + 0.27), (0.22, top + 0.36),
               (0.22, top + 0.4), (0, top + 0.4)], surround, s0=F.s0 + u0)
        for uu in (u0 - 0.22, u1 + 0.22):
            prism(mb, F, [(0, top - 0.25), (0.06, top - 0.25), (0.15, top + 0.1), (0.15, top + 0.18),
                          (0, top + 0.18)], uu - 0.06, uu + 0.06, surround, skip_edges=(3, 4))
    # steps (each runs under the door surround width)
    sw0, sw1 = u0 - 0.25, u1 + 0.25
    for k in range(n_steps):
        dz0, dz1 = k * step_h, (k + 1) * step_h
        box(mb, F, sw0, sw1, dz0, dz1, 0.0, (n_steps - k) * step_d, 'MB_Concrete', skip='KB')
    # stoop rails
    if rail and n_steps >= 2:
        for uu in (sw0 + 0.04, sw1 - 0.04):
            run = n_steps * step_d
            p0 = F.P(uu, 0.95, run - 0.12)
            p1 = F.P(uu, zt + 0.95, 0.05)
            ax = (p1 - p0).normalized()
            obox(mb, (p0 + p1) * 0.5, ax, F.t, ax.cross(F.t).normalized(), (p1 - p0).length / 2, 0.02, 0.02,
                 'MB_Metal')
            box(mb, F, uu - 0.02, uu + 0.02, 0.0, 0.97, run - 0.14, run - 0.1, 'MB_Metal', skip='B')
            box(mb, F, uu - 0.02, uu + 0.02, zt, zt + 0.97, 0.03, 0.07, 'MB_Metal', skip='B')
            for kk in range(1, n_steps):
                dd = run - kk * step_d
                box(mb, F, uu - 0.012, uu + 0.012, kk * step_h, kk * step_h + 0.95, dd - 0.012, dd + 0.012,
                    'MB_Metal', skip='B')
    if number:
        tex, nm, mat = number
        sign_board(B, F, u1 + 0.42, zt + 1.75, tex, nm, mat, depth=0.03, frame='MB_Brass')
    if lamp:
        lu = u0 - 0.4
        box(mb, F, lu - 0.07, lu + 0.07, zt + 1.9, zt + 2.1, 0.0, 0.06, 'MB_Metal', skip='K')
        box(mb, F, lu - 0.08, lu + 0.08, zt + 1.75, zt + 2.0, 0.06, 0.2, 'MB_LampGlow', skip='K')
        box(mb, F, lu - 0.1, lu + 0.1, zt + 2.0, zt + 2.05, 0.04, 0.22, 'MB_Metal')
        B.loc('Lamp_%d_%d' % (f.idx, int(uc * 10)), F.P(lu, zt + 1.9, 0.3))
    box(mb, F, u1 + 0.24, u1 + 0.44, zt + 1.05, zt + 1.39, 0.0, 0.025, 'MB_Details', skip='K',
        uv={'F': quad_uv(slot('T_Details.png', 'intercom', 2))})
    return zt


def drainpipe(B, F, u, z0=0.12, z1=None, r=0.055, d=0.1, mat='MB_Metal'):
    mb = B.mb
    z1 = (B.top - 0.35) if z1 is None else z1
    cylinder(mb, F.P(u, z0 + 0.25, d), Z, r, z1 - z0 - 0.25, 10, mat, cap0=False, cap1=False)
    # hopper head at the top, shoe at the bottom
    box(mb, F, u - 0.16, u + 0.16, z1, z1 + 0.28, d - 0.12, d + 0.14, mat, skip='K')
    obox(mb, F.P(u, z0 + 0.14, d + 0.07), (Z * 0.7 + F.n * 0.7).normalized(), F.t,
         (F.n * 0.7 - Z * 0.7).normalized(), 0.17, r, r, mat)
    zz = z0 + 1.0
    while zz < z1 - 0.3:
        box(mb, F, u - 0.07, u + 0.07, zz, zz + 0.05, 0.0, d + r + 0.012, mat, skip='K')
        zz += 2.2


def cornice(B, profile, mat, path=None, s0=None):
    if path is None:
        path, s0 = B.street_path()
    sweep(B.mb, path, profile, mat, s0=s0 or 0.0)


def brackets_along(B, f, us, z, mat, size=(0.14, 0.4, 0.32)):
    w, h, p = size
    for u in us:
        prism(B.mb, f.F, [(0, z - h), (0.07, z - h), (p, z - 0.08), (p, z), (0, z)], u - w / 2, u + w / 2, mat,
              skip_edges=(3, 4))


def dentils(B, f, u0, u1, z, mat, spacing=0.26, w=0.11, h=0.13, proj=0.14):
    n = int((u1 - u0) / spacing)
    off = (u1 - u0 - (n - 1) * spacing) / 2
    for i in range(n):
        uc = u0 + off + i * spacing
        box(B.mb, f.F, uc - w / 2, uc + w / 2, z - h, z, 0, proj, mat, skip='KT')


# ------------------------------------------------------------------------------------------------ roof kit

def water_tower(B, x, y, r=1.75, legs_h=3.0, tank_h=3.2):
    mb, zb = B.mb, B.H
    s = r * 0.78
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(mb, WORLD, x + sx * s - 0.09, x + sx * s + 0.09, zb, zb + legs_h, -(y + sy * s) - 0.09,
                -(y + sy * s) + 0.09, 'MB_Metal', skip='B')
    # cross bracing on each side
    for (ax, ay) in ((1, 0), (0, 1)):
        for side in (-1, 1):
            for diag in (-1, 1):
                if ax:
                    p0 = Vector((x - s * diag, y + side * s, zb + 0.3))
                    p1 = Vector((x + s * diag, y + side * s, zb + legs_h - 0.3))
                else:
                    p0 = Vector((x + side * s, y - s * diag, zb + 0.3))
                    p1 = Vector((x + side * s, y + s * diag, zb + legs_h - 0.3))
                a = (p1 - p0).normalized()
                obox(mb, (p0 + p1) / 2, a, Vector((ay, ax, 0)), a.cross(Vector((ay, ax, 0))).normalized(),
                     (p1 - p0).length / 2, 0.03, 0.03, 'MB_Metal')
    for sx in (-1, 1):
        box(mb, WORLD, x - s - 0.1, x + s + 0.1, zb + legs_h - 0.2, zb + legs_h, -(y + sx * s) - 0.08,
            -(y + sx * s) + 0.08, 'MB_Metal')
    box(mb, WORLD, x - r - 0.25, x + r + 0.25, zb + legs_h, zb + legs_h + 0.15, -y - r - 0.25, -y + r + 0.25,
        'MB_WoodLight')
    t0 = zb + legs_h + 0.15
    cylinder(mb, (x, y, t0), Z, r, tank_h, 16, 'MB_WaterTower', cap0=True, cap1=False, cap_mat='MB_Wood')
    for hz in (0.35, 1.2, 2.1, 2.95):
        cylinder(mb, (x, y, t0 + hz), Z, r + 0.035, 0.06, 16, 'MB_Metal', cap0=False, cap1=False)
    cylinder(mb, (x, y, t0 + tank_h), Z, r + 0.18, 1.15, 16, 'MB_RoofDark', cap0=True, cap1=False, r1=0.0)
    cylinder(mb, (x, y, t0 + tank_h + 1.0), Z, 0.07, 0.45, 6, 'MB_Metal', cap0=False)
    # ladder up the tank
    lx, ly = x + r + 0.12, y
    for off in (-0.22, 0.22):
        box(mb, WORLD, lx - 0.025, lx + 0.025, zb, t0 + tank_h + 0.2, -(ly + off) - 0.025, -(ly + off) + 0.025,
            'MB_Metal')
    zz = zb + 0.3
    while zz < t0 + tank_h:
        box(mb, WORLD, lx - 0.015, lx + 0.015, zz, zz + 0.03, -ly - 0.22, -ly + 0.22, 'MB_Metal', only='TBLRF')
        zz += 0.32


def bulkhead(B, x0, x1, y0, y1, h=2.7, door_side='front', mat=None):
    """Roof stair bulkhead (box room) with a steel door, coping and a lamp."""
    mb, zb = B.mb, B.H
    mat = mat or B.side_mat
    box(mb, WORLD, x0, x1, zb, zb + h, -y1, -y0, mat, skip='B')
    box(mb, WORLD, x0 - 0.06, x1 + 0.06, zb + h, zb + h + 0.12, -y1 - 0.06, -y0 + 0.06, B.coping_mat, skip='')
    if door_side == 'front':
        F = Frame((x0, y0, 0), (1, 0, 0), (0, -1, 0))
        uc = (x1 - x0) / 2
    else:
        F = Frame((x1, y0, 0), (0, 1, 0), (1, 0, 0))
        uc = (y1 - y0) / 2
    box(mb, F, uc - 0.48, uc + 0.48, zb, zb + 2.1, 0.0, 0.03, 'MB_Details', skip='KB',
        uv={'F': quad_uv(slot('T_Details.png', 'door_steel', 2))}, mats={'L': 'MB_Metal', 'R': 'MB_Metal',
                                                                       'T': 'MB_Metal'})
    box(mb, F, uc - 0.6, uc + 0.6, zb + 2.15, zb + 2.25, 0.0, 0.35, 'MB_Metal', skip='K')
    box(mb, F, uc - 0.07, uc + 0.07, zb + 2.3, zb + 2.45, 0.0, 0.1, 'MB_LampGlow', skip='K')


def vent(B, x, y, kind='mushroom', h=0.7, r=0.16):
    mb, zb = B.mb, B.H
    if kind == 'mushroom':
        cylinder(mb, (x, y, zb), Z, r, h, 8, 'MB_Steel', cap0=False, cap1=False)
        cylinder(mb, (x, y, zb + h), Z, r * 2.0, r * 1.2, 8, 'MB_Steel', cap0=True, cap1=False, r1=0.02)
    elif kind == 'box':
        box(mb, WORLD, x - 0.35, x + 0.35, zb, zb + h, -y - 0.3, -y + 0.3, 'MB_Steel', skip='B',
            uv={'F': quad_uv(slot('T_Details.png', 'vent', 2))}, mats={'F': 'MB_Details'})
        box(mb, WORLD, x - 0.42, x + 0.42, zb + h, zb + h + 0.06, -y - 0.37, -y + 0.37, 'MB_Steel')
    elif kind == 'pipe':
        cylinder(mb, (x, y, zb), Z, r * 0.5, h, 8, 'MB_Metal', cap0=False)
        cylinder(mb, (x, y, zb + h - 0.05), Z, r * 0.85, 0.15, 8, 'MB_Metal', cap0=True, cap1=True)


def hvac(B, x, y, w=1.6, d=1.2, h=0.95):
    mb, zb = B.mb, B.H
    box(mb, WORLD, x - w / 2 - 0.1, x + w / 2 + 0.1, zb, zb + 0.15, -y - d / 2 - 0.1, -y + d / 2 + 0.1, 'MB_Concrete',
        skip='B')
    box(mb, WORLD, x - w / 2, x + w / 2, zb + 0.15, zb + h, -y - d / 2, -y + d / 2, 'MB_ACBody', skip='BT',
        uv={'F': quad_uv(slot('T_Details.png', 'vent', 2))}, mats={'F': 'MB_Details'})
    fr = min(w, d) * 0.42
    for cx in ([x - w / 4, x + w / 4] if w > 1.3 * d else [x]):
        pass
    box(mb, WORLD, x - w / 2, x + w / 2, zb + h - 0.001, zb + h, -y - d / 2, -y + d / 2, 'MB_Details', only='T',
        uv={'T': quad_uv(slot('T_Details.png', 'fan', 2))})


def antenna(B, x, y, h=3.2):
    mb, zb = B.mb, B.H
    cylinder(mb, (x, y, zb), Z, 0.035, h, 6, 'MB_Metal', cap0=False)
    for k, ln in enumerate((1.4, 1.1, 0.8)):
        zz = zb + h - 0.25 - k * 0.45
        box(mb, WORLD, x - ln / 2, x + ln / 2, zz, zz + 0.03, -y - 0.015, -y + 0.015, 'MB_Metal')
    box(mb, WORLD, x - 0.2, x + 0.2, zb, zb + 0.05, -y - 0.2, -y + 0.2, 'MB_Concrete', skip='B')


def chimney(B, x, y, w=0.9, d=0.55, h=1.6, pots=2):
    mb, zb = B.mb, B.H
    box(mb, WORLD, x - w / 2, x + w / 2, zb, zb + h, -y - d / 2, -y + d / 2, B.side_mat, skip='B')
    box(mb, WORLD, x - w / 2 - 0.05, x + w / 2 + 0.05, zb + h, zb + h + 0.1, -y - d / 2 - 0.05, -y + d / 2 + 0.05,
        B.coping_mat)
    for i in range(pots):
        px = x + (i - (pots - 1) / 2) * (w / pots)
        cylinder(B.mb, (px, y, zb + h + 0.1), Z, 0.11, 0.35, 8, 'MB_Terracotta', cap0=False, cap1=True,
                 cap_mat='MB_Metal')


def skylight(B, x, y, w=1.2, d=1.0):
    mb, zb = B.mb, B.H
    box(mb, WORLD, x - w / 2, x + w / 2, zb, zb + 0.35, -y - d / 2, -y + d / 2, 'MB_Concrete', skip='BT')
    p = [Vector((x - w / 2, y - d / 2, zb + 0.35)), Vector((x + w / 2, y - d / 2, zb + 0.35)),
         Vector((x + w / 2, y + d / 2, zb + 0.65)), Vector((x - w / 2, y + d / 2, zb + 0.65))]
    mb.face(p, 'MB_Details', normal=Z, uvs=quad_uv(slot('T_Details.png', 'skylight', 2)))
    mb.face([Vector((x - w / 2, y + d / 2, zb + 0.35)), Vector((x + w / 2, y + d / 2, zb + 0.35)),
             Vector((x + w / 2, y + d / 2, zb + 0.65)), Vector((x - w / 2, y + d / 2, zb + 0.65))], 'MB_Concrete',
            normal=Vector((0, 1, 0)))
    for sx in (-1, 1):
        mb.face([Vector((x + sx * w / 2, y - d / 2, zb + 0.35)), Vector((x + sx * w / 2, y + d / 2, zb + 0.35)),
                 Vector((x + sx * w / 2, y + d / 2, zb + 0.65))], 'MB_Concrete', normal=Vector((sx, 0, 0)))


def satellite_dish(B, F, u, z, d=0.0, r=0.38, face_dir=None):
    """Wall-mounted dish (frame F at facade), aimed roughly south/up."""
    mb = B.mb
    base = F.P(u, z, d)
    box(mb, F, u - 0.06, u + 0.06, z - 0.12, z + 0.12, d, d + 0.04, 'MB_Metal', skip='K')
    arm_end = F.P(u, z + 0.05, d + 0.45)
    obox(mb, (base + arm_end) / 2 + F.n * 0.02, (arm_end - base).normalized(), F.t,
         (arm_end - base).normalized().cross(F.t).normalized(), (arm_end - base).length / 2, 0.02, 0.02, 'MB_Metal')
    aim = (face_dir or (F.n + Z * 0.55)).normalized()
    cylinder(mb, arm_end - aim * 0.05, aim, r * 0.18, 0.12, 10, 'MB_TrimWhite', cap0=True, cap1=False, r1=r)
    lnb = arm_end + aim * 0.42
    obox(mb, (arm_end + lnb) / 2, aim, F.t, aim.cross(F.t).normalized(), 0.22, 0.012, 0.012, 'MB_Metal')
    box(mb, Frame(lnb, F.t, F.n), -0.04, 0.04, -0.04, 0.04, -0.06, 0.02, 'MB_Metal')


def fire_escape(B, f, u0, u1, floors, depth=1.25, drop=True, gooseneck=True):
    """Iron fire escape: grated platform per floor, railings with balusters, alternating stair flights, braces,
    drop ladder at the bottom, gooseneck ladder over the parapet."""
    F, mb = f.F, B.mb
    plats = [B.fz(k) + 0.4 for k in floors]
    sw = 0.62                       # stair width (in d)
    sd0, sd1 = depth - 0.07 - sw, depth - 0.07
    run = 2.6
    for i, zp in enumerate(plats):
        last = i == len(plats) - 1
        hole = None
        if i > 0:                   # stair arrives through this platform
            hole = (u1 - 0.25 - run, u1 - 0.25) if i % 2 == 1 else (u0 + 0.25, u0 + 0.25 + run)
        # slab: back strip full length + front strip split around the stair hole
        box(mb, F, u0, u1, zp - 0.06, zp, 0.05, sd0, 'MB_Metal', skip='K', uv={'T': None},
            mats={'T': 'MB_Grate'})
        segs = [(u0, u1)] if hole is None else [(u0, hole[0]), (hole[1], u1)]
        for a, b in segs:
            if b - a > 0.05:
                box(mb, F, a, b, zp - 0.06, zp, sd0, depth, 'MB_Metal', mats={'T': 'MB_Grate'})
        # rails: front + ends
        for zr in (zp + 0.98, zp + 0.5):
            box(mb, F, u0, u1, zr - 0.025, zr + 0.025, depth - 0.04, depth, 'MB_Metal')
            for uu in (u0, u1 - 0.04):
                box(mb, F, uu, uu + 0.04, zr - 0.025, zr + 0.025, 0.05, depth - 0.04, 'MB_Metal', skip='KF')
        for uu in (u0, u1 - 0.04):
            box(mb, F, uu, uu + 0.04, zp, zp + 1.0, depth - 0.04, depth, 'MB_Metal', skip='B')
        n_bal = int((u1 - u0) / 0.32)
        for k in range(1, n_bal):
            uu = u0 + k * (u1 - u0) / n_bal
            box(mb, F, uu - 0.011, uu + 0.011, zp, zp + 0.955, depth - 0.03, depth - 0.008, 'MB_Metal', only='FKLR')
        for uu in (u0 + 0.02, u1 - 0.02):
            nb = int((depth - 0.1) / 0.32)
            for k in range(1, nb + 1):
                dd = 0.05 + k * (depth - 0.1) / (nb + 1)
                box(mb, F, uu - 0.011, uu + 0.011, zp, zp + 0.955, dd - 0.011, dd + 0.011, 'MB_Metal', only='FKLR')
        # braces under the platform (two diagonal struts into the wall)
        for uu in (u0 + 0.3, u1 - 0.3):
            p0, p1 = F.P(uu, zp - 0.95, 0.0), F.P(uu, zp - 0.07, depth - 0.15)
            a = (p1 - p0).normalized()
            obox(mb, (p0 + p1) / 2, a, F.t, a.cross(F.t).normalized(), (p1 - p0).length / 2, 0.025, 0.025, 'MB_Metal')
        # stair flight down to the previous platform
        if i > 0:
            zl = plats[i - 1]
            if i % 2 == 1:
                ua, ub = u1 - 0.25 - run, u1 - 0.25   # bottom at ua (left), top at ub? no: rises toward +u
                u_bot, u_top = ua, ub
            else:
                u_bot, u_top = u0 + 0.25 + run, u0 + 0.25
            steps = 14
            for s in range(steps):
                t0 = s / steps
                uu = u_bot + (u_top - u_bot) * (s + 0.5) / steps
                zz = zl + (zp - zl) * (s + 1) / (steps + 1)
                box(mb, F, uu - 0.11, uu + 0.11, zz - 0.03, zz, sd0 + 0.04, sd1 - 0.04, 'MB_Metal')
            for dd in (sd0 + 0.02, sd1 - 0.02):
                p0, p1 = F.P(u_bot, zl, dd), F.P(u_top, zp, dd)
                a = (p1 - p0).normalized()
                obox(mb, (p0 + p1) / 2 + Z * 0.0, a, F.n, a.cross(F.n).normalized(), (p1 - p0).length / 2, 0.02, 0.09,
                     'MB_Metal')
                q0, q1 = p0 + Z * 0.9, p1 + Z * 0.9
                obox(mb, (q0 + q1) / 2, a, F.n, a.cross(F.n).normalized(), (q1 - q0).length / 2, 0.018, 0.018,
                     'MB_Metal')
    if drop and plats:
        zp = plats[0]
        ul = u1 - 0.45 if len(plats) > 1 else u0 + 0.5
        for off in (-0.2, 0.2):
            box(mb, F, ul + off - 0.02, ul + off + 0.02, 2.3, zp + 1.0, depth - 0.02, depth + 0.02, 'MB_Metal')
        zz = 2.45
        while zz < zp + 0.9:
            box(mb, F, ul - 0.2, ul + 0.2, zz, zz + 0.025, depth - 0.012, depth + 0.012, 'MB_Metal', only='FKTB')
            zz += 0.3
    if gooseneck and plats:
        zp = plats[-1]
        ug = u0 + 0.6
        for off in (-0.2, 0.2):
            box(mb, F, ug + off - 0.02, ug + off + 0.02, zp, B.top + 0.9, 0.1, 0.14, 'MB_Metal')
            box(mb, F, ug + off - 0.02, ug + off + 0.02, B.top + 0.86, B.top + 0.9, -0.5, 0.14, 'MB_Metal')
        zz = zp + 0.3
        while zz < B.top + 0.8:
            box(mb, F, ug - 0.2, ug + 0.2, zz, zz + 0.025, 0.108, 0.132, 'MB_Metal', only='FKTB')
            zz += 0.3


def aframe_sign(B, F, u, d, tex, name, mat):
    w, h = slot_m(tex, name)
    r = slot(tex, name, 2)
    mb = B.mb
    top = F.P(u, h + 0.05, d)
    for s in (1, -1):
        foot = F.P(u, 0.0, d + s * 0.28)
        down = (foot - top).normalized()
        ax_w = F.t
        nrm = ax_w.cross(down).normalized() * (1 if s > 0 else -1)
        if nrm.dot(F.n * s) < 0:
            nrm = -nrm
        c = (top + foot) / 2 + nrm * 0.015
        L = (foot - top).length
        pts = [c - ax_w * (w / 2) + down * (L / 2), c + ax_w * (w / 2) + down * (L / 2),
               c + ax_w * (w / 2) - down * (L / 2), c - ax_w * (w / 2) - down * (L / 2)]
        if s < 0:
            pts = [c + ax_w * (w / 2) + down * (L / 2), c - ax_w * (w / 2) + down * (L / 2),
                   c - ax_w * (w / 2) - down * (L / 2), c + ax_w * (w / 2) - down * (L / 2)]
        mb.face(pts, mat, normal=nrm, uvs=quad_uv(r))
        obox(mb, c - nrm * 0.018 - down * 0.02, ax_w, down, nrm, w / 2 + 0.03, L / 2 - 0.005, 0.018, 'MB_WoodLight', skip='+z')


def blob(mb, c, rx, rz, mat, segs=8, rings=4, ry=None, k=1.0, flat_bottom=False):
    """Low-poly ellipsoid (shrubs, lanterns, flower heads)."""
    c = Vector(c)
    ry = rx if ry is None else ry
    pts = []
    for i in range(1, rings):
        th = math.pi * i / rings
        z = -math.cos(th) * rz
        rr = math.sin(th)
        pts.append([c + Vector((math.cos(2 * math.pi * j / segs + i * 0.3) * rx * rr,
                                math.sin(2 * math.pi * j / segs + i * 0.3) * ry * rr, z)) for j in range(segs)])
    bot, top = c - Z * rz, c + Z * rz
    for j in range(segs):
        j2 = (j + 1) % segs
        if not flat_bottom:
            mb.face([bot, pts[0][j2], pts[0][j]], mat, normal=(pts[0][j] + pts[0][j2]) * 0.5 - c - Z * rz, k=k)
        mb.face([pts[-1][j], pts[-1][j2], top], mat, normal=(pts[-1][j] + pts[-1][j2]) * 0.5 - c + Z * rz, k=k)
        for i in range(len(pts) - 1):
            a, b, cc, d = pts[i][j], pts[i][j2], pts[i + 1][j2], pts[i + 1][j]
            mb.face([a, b, cc, d], mat, normal=(a + b + cc + d) * 0.25 - c, k=k)
    if flat_bottom:
        mb.face(list(reversed(pts[0])), mat, normal=-Z, k=k)


def potted_shrub(B, F, u, d, h=1.1, r=0.32):
    mb = B.mb
    c = F.P(u, 0, d)
    cylinder(mb, c, Z, r, 0.55, 8, 'MB_Terracotta', cap0=False, cap1=True, r1=r * 1.15, cap_mat='MB_Wood')
    blob(mb, c + Z * (0.55 + h * 0.42), r * 1.45, h * 0.45, 'MB_Foliage', segs=9, rings=4)


def pendant(B, F, u, d, z_ceil=3.6, drop=1.0, shade='MB_TrimDark'):
    mb = B.mb
    zb = z_ceil - drop
    box(mb, F, u - 0.006, u + 0.006, zb + 0.18, z_ceil, d - 0.006, d + 0.006, 'MB_Metal', only='FKLR')
    cylinder(mb, F.P(u, zb, d), Z, 0.2, 0.2, 10, shade, cap0=True, cap1=False, r1=0.06, cap_mat='MB_LampGlow')
    cylinder(mb, F.P(u, zb - 0.05, d), Z, 0.07, 0.07, 6, 'MB_LampGlow', cap0=True, cap1=True)


def cafe_table(B, F, u, d, chairs=2):
    mb = B.mb
    cylinder(mb, F.P(u, 0.03, d), Z, 0.22, 0.04, 8, 'MB_Metal', cap0=False)
    cylinder(mb, F.P(u, 0.07, d), Z, 0.035, 0.66, 6, 'MB_Metal', cap0=False, cap1=False)
    cylinder(mb, F.P(u, 0.73, d), Z, 0.32, 0.04, 12, 'MB_TrimWhite', cap0=True, cap1=True)
    for s in ((-1, 1) if chairs == 2 else (1,)):
        cu = u + s * 0.5
        box(mb, F, cu - 0.19, cu + 0.19, 0.44, 0.48, d - 0.19, d + 0.19, 'MB_Wood')
        for du in (-0.16, 0.16):
            for dd in (-0.16, 0.16):
                box(mb, F, cu + du - 0.015, cu + du + 0.015, 0.03, 0.44, d + dd - 0.015, d + dd + 0.015, 'MB_Wood',
                    only='FKLR')
        bu = cu + s * 0.17
        box(mb, F, bu - 0.02, bu + 0.02, 0.48, 0.95, d - 0.18, d + 0.18, 'MB_Wood')


def cellar_doors(B, F, u0, u1, d0, d1):
    box(B.mb, F, u0, u1, 0.0, 0.035, d0, d1, 'MB_Steel', skip='B',
        uv={'T': quad_uv(slot('T_Details.png', 'door_steel', 2))}, mats={'T': 'MB_Details'})


# --------------------------------------------------------------------------------------------- buildings

def floors_upper(B):
    return list(range(1, B.floors))


def upper_windows(B, f, us, w=1.1, h=1.75, sill_off=0.85, lintel_style='flat', frame='MB_TrimWhite',
                  sill_style='stone', top_course=False, skip=(), **kw):
    for k in floors_upper(B):
        z0 = B.fz(k) + sill_off
        for i, u in enumerate(us):
            if (k, i) in skip:
                continue
            ss = sill_style
            if top_course and k == B.floors - 1:
                ss = None
            window(B, f, u, z0, w=w, h=h, frame=frame, sill_style=ss, lintel_style=lintel_style, **kw)


def build_cafe():
    """CornerCafe: 14 x 14 m, 5 floors, red brick, side facade on the viewer's LEFT (-X), chamfered corner door."""
    W, D, c = 14.0, 14.0, 1.6
    B = Building('CornerCafe', [(0, D), (0, c), (c, 0), (W, 0), (W, D)],
                 ['side', 'chamfer', 'front', 'party', 'back'], 5, 'MB_BrickRed', side='left',
                 footprint_wd=(W, D))
    side, ch, front, party, back = B.facades
    TS, MS = 'T_Sign_Cafe.png', 'MB_Sign_Cafe'
    for f in (side, ch, front):
        f.zone(0.0, 0.5, 'MB_StoneTrim')               # stone base course
    # ---- ground floor: front (u 0 = chamfer end .. 12.4 = party wall)
    shopfront(B, front, 0.6, 4.6, 'Cafe', 'panoA', 0.0, mulls=[2.6], frame='MB_TrimGreen', stall='MB_TrimGreen')
    shopfront(B, front, 5.1, 8.6, 'Cafe', 'panoA', 4.3, mulls=[6.85], frame='MB_TrimGreen', stall='MB_TrimGreen')
    for a, b in ((0.0, 0.6), (4.6, 5.1), (8.6, 9.2)):
        pilaster(B, front, a, b, 0.0, 3.5, proj=0.1)
    res_door(B, front, 10.1, door='door_green', number=(TS, 'number', MS))
    drainpipe(B, front.F, 12.15)
    # front: pendant lamps + tables inside the cafe boxes
    dg = -0.25 - 0.03
    for u in (1.6, 3.6, 6.0, 7.8):
        pendant(B, front.F, u, dg - 0.7, drop=1.05, shade='MB_TrimGreen')
    for u in (2.6, 6.85):
        cafe_table(B, front.F, u, dg - 0.55)
    # ---- chamfer: corner door with sidelights
    shopfront(B, ch, 0.18, ch.L - 0.18, 'Cafe', 'panoA', 2.2, door=ch.L / 2, door_w=1.0, frame='MB_TrimGreen',
              stall='MB_TrimGreen', depth=0.65)
    # ---- side (u 0 = back .. 12.4 = chamfer)
    shopfront(B, side, 5.6, 11.8, 'Cafe', 'panoB', 0.6, mulls=[7.65, 9.75], frame='MB_TrimGreen',
              stall='MB_TrimGreen')
    for a, b in ((5.0, 5.6), (11.8, 12.4)):
        pilaster(B, side, a, b, 0.0, 3.5, proj=0.1)
    for u in (6.6, 8.7, 10.8):
        pendant(B, side.F, u, dg - 0.7, drop=1.05, shade='MB_TrimGreen')
    for u in (6.6, 8.7, 10.8):
        cafe_table(B, side.F, u, dg - 0.55)
    # service door + kitchen window
    side.opening(1.05, 2.0, 0.0, 2.15, 0.12, side='MB_Concrete', top='MB_Concrete')
    box(B.mb, side.F, 1.05, 2.0, 0.0, 2.15, -0.12, -0.11, 'MB_Details', only='F',
        uv={'F': quad_uv(slot('T_Details.png', 'door_steel', 2))})
    box(B.mb, side.F, 0.95, 2.1, 2.15, 2.3, 0, 0.06, 'MB_Concrete', skip='K')
    box(B.mb, side.F, 1.43, 1.62, 2.4, 2.62, 0.0, 0.14, 'MB_LampGlow', skip='K')
    window(B, side, 3.3, 1.7, w=1.0, h=1.2, frame='MB_TrimDark', sill_style='stone', lintel_style='flat',
           sash='fixed', lit=True, variant='kitchen', ac=False, flower=False)
    for u in (2.95, 3.65):                                                   # security bars
        box(B.mb, side.F, u - 0.012, u + 0.012, 1.7, 2.9, 0.02, 0.045, 'MB_Metal', skip='K')
    cellar_doors(B, side.F, 3.0, 4.3, 0.15, 1.35)
    drainpipe(B, side.F, 0.3)
    # ---- fascia signs, awnings, blade sign, shopfront cornice
    sign_board(B, front.F, 4.6, 3.84, TS, 'fascia_front', MS, frame='MB_TrimGreen')
    sign_board(B, side.F, 8.7, 3.84, TS, 'fascia_side', MS, frame='MB_TrimGreen')
    sign_board(B, ch.F, ch.L / 2, 3.78, TS, 'corner', MS, frame='MB_TrimGreen')
    awning(B, front, 0.65, 8.55, 3.48, proj=1.45, valance=(TS, 'valance_front', MS))
    awning(B, side, 5.65, 11.75, 3.48, proj=1.45, valance=(TS, 'valance_side', MS))
    blade_sign(B, front.F, 0.3, 4.95, TS, 'blade', MS, round_segs=16, frame='MB_TrimGreen')
    cornice(B, [(0, 4.14), (0.1, 4.14), (0.14, 4.2), (0.24, 4.24), (0.28, 4.32), (0.28, 4.38), (0, 4.38)],
            'MB_StoneTrim')
    # ---- upper floors
    upper_windows(B, front, [1.5, 3.6, 5.7, 7.8, 9.9], lintel_style='key', top_course=True)
    upper_windows(B, ch, [ch.L / 2], w=0.95, lintel_style='key', top_course=True)
    fe_us = [1.9, 4.1]
    for k in floors_upper(B):
        for u in fe_us:
            window(B, side, u, B.fz(k) + 0.85, w=1.1, h=2.0, lintel_style='key', ac=False, flower=False,
                   sill_style=None if k == B.floors - 1 else 'stone')
    upper_windows(B, side, [6.6, 8.7, 10.8], lintel_style='key', top_course=True)
    fire_escape(B, side, 0.75, 5.25, floors_upper(B))
    # top-floor sill course + main cornice with brackets
    zc = B.fz(4) + 0.85
    cornice(B, [(0, zc - 0.12), (0.08, zc - 0.12), (0.08, zc - 0.03), (0.06, zc), (0, zc)], 'MB_StoneTrim')
    zt = B.top
    cornice(B, [(0, zt - 0.95), (0.06, zt - 0.95), (0.06, zt - 0.8), (0.16, zt - 0.75), (0.16, zt - 0.6),
                (0.44, zt - 0.5), (0.5, zt - 0.42), (0.5, zt - 0.22), (0.46, zt - 0.18), (0.46, zt - 0.12),
                (0, zt - 0.12)], 'MB_TrimCream')
    for f, wins in ((front, [1.5, 3.6, 5.7, 7.8, 9.9]), (side, [1.9, 4.1, 6.6, 8.7, 10.8])):
        mids = [(a + b) / 2 for a, b in zip(wins[:-1], wins[1:])]
        us = [0.3, f.L - 0.3] + [m + o for m in mids for o in (-0.22, 0.22)]
        brackets_along(B, f, sorted(us), zt - 0.95, 'MB_TrimCream', size=(0.13, 0.42, 0.42))
    brackets_along(B, ch, [0.3, ch.L - 0.3], zt - 0.95, 'MB_TrimCream', size=(0.13, 0.42, 0.42))
    # string course above the 2nd floor windows
    # back / party walls: a few windows on the back
    for k in floors_upper(B):
        for u in (3.0, 7.0, 11.0):
            window(B, back, u, B.fz(k) + 0.85, w=0.9, h=1.5, frame='MB_TrimDark', sill_style='stone',
                   lintel_style=None, sash='double', ac=False, flower=False, trim='MB_Concrete')
    drainpipe(B, back.F, 0.4)
    # ---- outside furniture
    aframe_sign(B, front.F, 4.85, 0.75, TS, 'aframe', MS)
    for u in (0.12, ch.L - 0.12):
        potted_shrub(B, ch.F, u, 0.45)
    box(B.mb, front.F, 0.15, 0.45, 1.2, 1.62, 0.1, 0.115, MS, skip='K', uv={'F': quad_uv(slot(TS, 'hours', 2))})
    # ---- roof
    water_tower(B, 10.2, 10.0)
    bulkhead(B, 2.2, 4.6, 9.5, 12.6, door_side='front')
    chimney(B, 13.3, 5.0, w=0.6, d=1.0, h=1.5)
    chimney(B, 13.3, 2.4, w=0.6, d=0.9, h=1.3, pots=1)
    vent(B, 6.0, 4.0, 'mushroom')
    vent(B, 7.2, 4.6, 'box')
    vent(B, 5.0, 8.0, 'pipe', h=1.1)
    antenna(B, 3.0, 4.0)
    skylight(B, 7.5, 9.0)
    satellite_dish(B, front.F, 11.35, B.fz(3) + 1.5)
    B.copy += [('fascia', 'DAILY GRIND / COFFEE · ESPRESSO · PASTRY'), ('awning valance', 'DAILY GRIND · COFFEE'),
               ('corner board', 'Daily Grind'), ('blade sign', 'COFFEE (cup icon)'),
               ('A-frame', 'TODAY: FLAT WHITE · OAT LATTE · COLD BREW · BANANA BREAD · 7AM-7PM'),
               ('menu board (interior)', 'MENU: ESPRESSO 2.80 · AMERICANO 3.20 · FLAT WHITE 3.90 · LATTE 4.20 · CROISSANT 3.50'),
               ('door plaque', 'OPEN MON-FRI 7-7 / SAT-SUN 8-6 / WIFI · OAT MILK'), ('house number', '27')]
    B.notes.append('Shop wraps the chamfered corner (1.6 m chamfer, entrance on the chamfer). Fire escape and service '
                   'door on the side facade; water tower at the back corner of the roof.')
    B.loc('Entrance', ch.F.P(ch.L / 2, 0, 0))
    # in-game views: walker passes the front 2.6 m out heading for the corner; whip-pan looks down the side street
    B.extra_views = [('walk', Vector((6.1, -2.6, 1.56)), Vector((-20.0, -0.6, 2.4)), 60, sun_from(222, 30)),
                     ('pan', Vector((-12.35, -2.6, 1.56)), Vector((-8.0, 20.0, 3.0)), 60, sun_from(222, 30))]
    B.loc('Door_Apartments', front.F.P(10.1, 0.45, 0))
    return B


# ------------------------------------------------------------------------------------------ extra helpers

def polyline_offset(pts, d):
    pts = [Vector((p[0], p[1], 0.0)) for p in pts]
    n = len(pts)
    dirs = [(pts[i + 1] - pts[i]).normalized() for i in range(n - 1)]
    norms = [Vector((v.y, -v.x, 0.0)) for v in dirs]
    out = []
    for i in range(n):
        na = norms[i - 1] if i > 0 else norms[0]
        nb = norms[i] if i < n - 1 else norms[-1]
        out.append(pts[i] + (na + nb) / (1 + na.dot(nb)) * d)
    return out


def surround(B, F, u0, u1, z0, z1, mat, w=0.12, proj=0.16):
    """Modern projecting window frame ('picture frame') around an opening."""
    mb = B.mb
    box(mb, F, u0 - w, u0, z0, z1, 0, proj, mat, only='FLR')
    box(mb, F, u1, u1 + w, z0, z1, 0, proj, mat, only='FLR')
    box(mb, F, u0 - w, u1 + w, z1, z1 + w, 0, proj, mat, skip='K')
    box(mb, F, u0 - w, u1 + w, z0 - w, z0, 0, proj, mat, skip='K')


def pediment(B, F, u0, u1, z, h=0.42, proj=0.14, mat='MB_StoneTrim'):
    mb, P = B.mb, F.P
    box(mb, F, u0 - 0.12, u1 + 0.12, z, z + 0.13, 0, proj + 0.03, mat, skip='K')
    uc, a, b, zt, d1 = (u0 + u1) / 2, u0 - 0.06, u1 + 0.06, z + 0.13, proj - 0.02
    mb.face([P(a, zt, d1), P(b, zt, d1), P(uc, zt + h, d1)], mat, normal=F.n, frame=F)
    mb.face([P(b, zt, 0), P(b, zt, d1), P(uc, zt + h, d1), P(uc, zt + h, 0)], mat, normal=F.N(h, b - uc, 0), frame=F)
    mb.face([P(a, zt, d1), P(a, zt, 0), P(uc, zt + h, 0), P(uc, zt + h, d1)], mat, normal=F.N(-h, uc - a, 0), frame=F)
    for (x0, z0_), (x1, z1_) in (((a - 0.08, zt - 0.02), (uc, zt + h + 0.06)), ((uc, zt + h + 0.06), (b + 0.08, zt - 0.02))):
        p0, p1 = P(x0, z0_, d1 + 0.02), P(x1, z1_, d1 + 0.02)
        ax = (p1 - p0).normalized()
        obox(mb, (p0 + p1) / 2, ax, ax.cross(F.n).normalized() * -1, F.n, (p1 - p0).length / 2, 0.05, 0.035, mat)


def quoins(B, fa, fb, z0, z1, mat='MB_StoneTrim', h=0.6, gap=0.035, la=0.75, lb=0.45, proj=0.035):
    corner = fb.F.A
    k, z = 0, z0
    while z + h <= z1 + 1e-6:
        l1, l2 = (la, lb) if k % 2 == 0 else (lb, la)
        sweep(B.mb, [fa.F.P(fa.L - l1, 0, 0), corner, fb.F.P(l2, 0, 0)],
              [(0, z), (proj, z), (proj, z + h - gap), (0, z + h - gap)], mat)
        z += h
        k += 1


def balconette(B, F, u0, u1, z, depth=0.5, mat='MB_Metal', slab='MB_StoneTrim'):
    mb = B.mb
    box(mb, F, u0, u1, z - 0.12, z, 0, depth, slab, skip='K')
    for uu in (u0 + 0.15, u1 - 0.15):
        prism(mb, F, [(0, z - 0.45), (0.06, z - 0.45), (depth - 0.08, z - 0.12), (0, z - 0.12)], uu - 0.06, uu + 0.06,
              slab, skip_edges=(2, 3))
    top = z + 0.92
    box(mb, F, u0 + 0.02, u1 - 0.02, top - 0.035, top, depth - 0.05, depth - 0.015, mat)
    for uu in (u0 + 0.02, u1 - 0.055):
        box(mb, F, uu, uu + 0.035, top - 0.035, top, 0.0, depth - 0.05, mat, skip='K')
    n = int((u1 - u0) / 0.13)
    for i in range(n + 1):
        uu = u0 + 0.04 + i * (u1 - u0 - 0.08) / n
        box(mb, F, uu - 0.009, uu + 0.009, z, top - 0.035, depth - 0.042, depth - 0.024, mat, only='FKLR')
    for uu in (u0 + 0.037, u1 - 0.037):
        for j in range(1, 4):
            dd = j * (depth - 0.05) / 4
            box(mb, F, uu - 0.009, uu + 0.009, z, top - 0.035, dd - 0.009, dd + 0.009, mat, only='FKLR')


def booth(B, F, uc, d_front, length=0.95, seat='MB_Vinyl', trim='MB_Chrome'):
    """Diner booth perpendicular to the glass: table against the window, high-back benches either side."""
    mb = B.mb
    d0, d1 = d_front - 0.04, d_front - 0.04 - length
    box(mb, F, uc - 0.34, uc + 0.34, 0.72, 0.77, d1 + 0.12, d0, 'MB_TrimWhite',
        mats={'F': trim, 'L': trim, 'R': trim, 'K': trim})
    box(mb, F, uc - 0.04, uc + 0.04, 0.03, 0.72, d1 + 0.42, d1 + 0.52, trim, only='FKLR')
    for s in (-1, 1):
        a, b = sorted((uc + s * 0.4, uc + s * 0.92))
        box(mb, F, a, b, 0.03, 0.46, d1, d0, seat)
        bu0, bu1 = (b - 0.15, b) if s > 0 else (a, a + 0.15)
        box(mb, F, bu0, bu1, 0.46, 1.22, d1, d0, seat)
        box(mb, F, bu0 - 0.012, bu1 + 0.012, 1.22, 1.26, d1 - 0.012, d0 + 0.012, trim)


def poster(B, F, u, zc, d, tex, name, mat, size=None):
    w, h = size or slot_m(tex, name)
    B.mb.face([F.P(u - w / 2, zc - h / 2, d), F.P(u + w / 2, zc - h / 2, d), F.P(u + w / 2, zc + h / 2, d),
               F.P(u - w / 2, zc + h / 2, d)], mat, normal=F.n, uvs=quad_uv(slot(tex, name, 2)))


def cross_sign(B, F, u, zc, size=0.92, thick=0.17, d_wall=0.22):
    """Pharmacy cross: plus-shaped double-sided lightbox (lit atlas faces, neon-green rim) on a bracket."""
    mb, G = B.mb, F.turned(u)
    r = slot('T_SignsLit.png', 'pharm_cross', 3)
    c, s, a = d_wall + size / 2, size / 2, size / 6
    outline = [(a, s), (a, a), (s, a), (s, -a), (a, -a), (a, -s), (-a, -s), (-a, -a), (-s, -a), (-s, a), (-a, a),
               (-a, s)]
    for side, nrm in ((thick / 2, G.n), (-thick / 2, -G.n)):
        pts = [G.P(c + x, zc + y, side) for x, y in outline]
        uvs = [(r[0] + (r[2] - r[0]) * (x / size + 0.5), r[1] + (r[3] - r[1]) * (y / size + 0.5)) for x, y in outline]
        mb.face(pts, 'MB_SignsLit', normal=nrm, uvs=uvs)
    for i in range(len(outline)):
        (x0, y0), (x1, y1) = outline[i], outline[(i + 1) % len(outline)]
        p0, p1 = G.P(c + x0, zc + y0, 0), G.P(c + x1, zc + y1, 0)
        mb.face([p0 + G.n * (thick / 2), p1 + G.n * (thick / 2), p1 - G.n * (thick / 2), p0 - G.n * (thick / 2)],
                'MB_NeonGreen', normal=(p0 + p1) / 2 - G.P(c, zc, 0))
    box(mb, G, 0.0, d_wall + 0.02, zc - 0.05, zc + 0.05, -0.05, 0.05, 'MB_Metal')
    box(mb, G, 0.0, 0.03, zc - 0.3, zc + 0.3, -0.12, 0.12, 'MB_Metal', skip='L')
    box(mb, G, 0.0, c + a, zc + s + 0.08, zc + s + 0.13, -0.025, 0.025, 'MB_Metal')
    box(mb, G, c - 0.01, c + 0.01, zc + s, zc + s + 0.08, -0.01, 0.01, 'MB_Metal', only='FKLR')
    B.loc('SignLight_Cross', G.P(c, zc, 0))


def curve_shop(B, segs, shop, pano='panoB', pano_off=0.0, reveal=0.22, depth=1.3, stall_h=0.5, transom=2.75,
               head=3.35, frame='MB_Chrome', stall='MB_Chrome'):
    """Glazing wrapped around a rounded corner (one facade per arc segment) with a shared curved interior."""
    mb = B.mb
    top = head + 0.08
    dg = -reveal
    for i, f in enumerate(segs):
        f.opening(0.0, f.L, 0.0, top, reveal, side=frame, top=frame)
        f.openings[-1]['skip_sides'] = (i > 0, i < len(segs) - 1)
        F = f.F
        box(mb, F, 0.0, f.L, 0.0, stall_h, dg - 0.05, dg + 0.1, stall, only='F')
        for zr in (0.12, 0.24, 0.36):
            box(mb, F, 0.0, f.L, zr, zr + 0.03, dg + 0.1, dg + 0.125, stall, only='FTB')
        box(mb, F, 0.0, f.L, stall_h, stall_h + 0.05, dg - 0.05, dg + 0.14, frame, only='FTB')
        box(mb, F, 0.0, f.L, transom, transom + 0.08, dg - 0.03, dg + 0.06, frame, only='FTB')
        box(mb, F, 0.0, f.L, head, top, dg - 0.03, dg + 0.06, frame, only='FB')
        B.glass.face([F.P(0, stall_h + 0.05, dg + 0.015), F.P(f.L, stall_h + 0.05, dg + 0.015),
                      F.P(f.L, head, dg + 0.015), F.P(0, head, dg + 0.015)], 'MB_ShopGlass', normal=F.n, frame=F)
    # joint mullions (+ end jambs)
    pts = [segs[0].F.A] + [f.F.P(f.L, 0, 0) for f in segs]
    for j, p in enumerate(pts):
        fa = segs[max(0, j - 1)]
        fb = segs[min(len(segs) - 1, j)]
        m = (fa.F.n + fb.F.n).normalized()
        t = Z.cross(m).normalized() * -1
        c = p + m * (dg + 0.015)
        obox(mb, c + Z * (top / 2), t, Z, m, 0.04 if 0 < j < len(pts) - 1 else 0.06, top / 2, 0.045, frame)
    # shared curved interior
    zf, zc = 0.03, 3.6
    front = polyline_offset(pts, dg - 0.03)
    back = polyline_offset(pts, dg - depth)
    B.rooms.append(front + list(reversed(back)))
    tex, mat = 'T_Int_%s.png' % shop, 'MB_Int_' + shop
    L = layout()[tex]
    W, H = L['size']
    x, y, w, h = L['slots'][pano]['rect']
    v_lo, v_hi = 1 - (y + h - 2) / H, 1 - (y + 2) / H
    pw, ph = L['slots'][pano]['m']
    acc = pano_off
    for i in range(len(pts) - 1):
        a, b = back[i], back[i + 1]
        ln = (b - a).length
        ua, ub = acc / pw, (acc + ln * 1.6) / pw
        acc += ln * 1.6
        mb.face([a + Z * zf, b + Z * zf, b + Z * zc, a + Z * zc], mat, normal=segs[i].F.n,
                uvs=[(ua, v_lo + (v_hi - v_lo) * zf / ph), (ub, v_lo + (v_hi - v_lo) * zf / ph), (ub, v_hi), (ua, v_hi)])
        mb.face([front[i] + Z * zf, front[i + 1] + Z * zf, b + Z * zf, a + Z * zf], mat, normal=Z,
                uvs=quad_uv(slot(tex, 'floor', 3)))
        mb.face([a + Z * zc, b + Z * zc, front[i + 1] + Z * zc, front[i] + Z * zc], mat, normal=-Z,
                uvs=quad_uv(slot(tex, 'ceiling', 3)))
    vlo = v_lo + (v_hi - v_lo) * zf / ph
    for j, sgn in ((0, 1), (len(pts) - 1, -1)):
        f = segs[0] if j == 0 else segs[-1]
        u_a = (pano_off - depth) / pw if j == 0 else acc / pw
        u_b = pano_off / pw if j == 0 else (acc + depth) / pw
        uvs = [(u_a, vlo), (u_b, vlo), (u_b, v_hi), (u_a, v_hi)] if j == 0 else [(u_b, vlo), (u_a, vlo), (u_a, v_hi),
                                                                                    (u_b, v_hi)]
        mb.face([front[j] + Z * zf, back[j] + Z * zf, back[j] + Z * zc, front[j] + Z * zc], mat, normal=f.F.t * sgn,
                uvs=uvs)
    for f in segs:
        threshold(B, f.F, 0.0, f.L, dg - 0.05, 0.0)
    B.loc('Shop_curve', (front[len(front) // 2] + back[len(back) // 2]) / 2 + Z * 2.6)
    return pts


def lit_board(B, F, uc, zc, name, size=None, depth=0.22, frame='MB_MetalDark', d0=0.0):
    return sign_board(B, F, uc, zc, 'T_SignsLit.png', name, 'MB_SignsLit', depth=depth, d0=d0, frame=frame,
                      size=size)


def steel_tank(B, x, y, r=1.4, h=2.6, legs=1.2):
    mb, zb = B.mb, B.H
    for a in range(4):
        ang = math.pi / 4 + a * math.pi / 2
        px, py = x + math.cos(ang) * r * 0.75, y + math.sin(ang) * r * 0.75
        box(mb, WORLD, px - 0.08, px + 0.08, zb, zb + legs, -py - 0.08, -py + 0.08, 'MB_Metal', skip='B')
    cylinder(mb, (x, y, zb + legs), Z, r, h, 14, 'MB_Steel', cap0=True, cap1=False)
    cylinder(mb, (x, y, zb + legs + h), Z, r, 0.45, 14, 'MB_Steel', cap0=False, cap1=False, r1=0.25)
    cylinder(mb, (x, y, zb + legs + h + 0.45), Z, 0.25, 0.2, 8, 'MB_Steel', cap0=False, cap1=True)
    for hz in (0.5, 1.3, 2.1):
        cylinder(mb, (x, y, zb + legs + hz), Z, r + 0.03, 0.05, 14, 'MB_Metal', cap0=False, cap1=False)


def canopy(B, F, u0, u1, z, proj=1.2, mat='MB_MetalDark', lights=2, edge=None):
    F = F.F if isinstance(F, Facade) else F
    mb = B.mb
    box(mb, F, u0, u1, z, z + 0.16, 0, proj, mat, skip='K')
    for i in range(lights):
        uu = u0 + (i + 0.5) * (u1 - u0) / lights
        box(mb, F, uu - 0.09, uu + 0.09, z - 0.012, z, proj * 0.45, proj * 0.6, 'MB_LampGlow', only='BFLRK')
    if edge:
        box(mb, F, u0 - 0.01, u1 + 0.01, z + 0.04, z + 0.08, proj, proj + 0.025, edge, skip='K')
    for uu in (u0 + 0.2, u1 - 0.2):
        p0, p1 = F.P(uu, z + 0.9, 0.0), F.P(uu, z + 0.16, proj - 0.1)
        ax = (p1 - p0).normalized()
        obox(mb, (p0 + p1) / 2, ax, F.t, ax.cross(F.t).normalized(), (p1 - p0).length / 2, 0.015, 0.015, 'MB_Metal')


# ------------------------------------------------------------------------------------------ CornerMart

def build_mart():
    """CornerMart: 15.4 x 15 m, 6 floors, light stone panels; 24/7 convenience store wrapping the corner (side LEFT)."""
    W, D = 15.4, 15.0
    B = Building('CornerMart', [(0, D), (0, 0), (W, 0), (W, D)], ['side', 'front', 'party', 'back'], 6,
                 'MB_PanelLight', side='left', footprint_wd=(W, D), coping_mat='MB_MetalDark', lit_frac=0.4)
    side, front, party, back = B.facades
    TS, MS = 'T_Sign_Mart.png', 'MB_Sign_Mart'
    for f in (side, front):
        f.zone(0.0, 0.35, 'MB_Granite')
    # corner column + piers
    pilaster(B, front, 0.0, 0.4, 0.0, 3.5, proj=0.14, mat='MB_MetalDark', base_mat=None, cap=False)
    pilaster(B, side, side.L - 0.4, side.L, 0.0, 3.5, proj=0.14, mat='MB_MetalDark', base_mat=None, cap=False)
    sf = dict(frame='MB_Steel', stall='MB_MetalDark', stall_h=0.32, transom=2.95, head=3.35, transom_div=1.3)
    shopfront(B, front, 0.4, 6.4, 'Mart', 'panoA', 0.0, door=1.55, door_w=1.8, mulls=[3.0, 4.7], box_u0=0.28, **sf)
    shopfront(B, front, 6.9, 11.4, 'Mart', 'panoA', 6.0, mulls=[8.4, 9.9], **sf)
    pilaster(B, front, 6.4, 6.9, 0.0, 3.5, proj=0.1, mat='MB_PanelLight', base_mat='MB_Granite', base_h=0.35, cap=False)
    shopfront(B, side, 4.4, side.L - 0.4, 'Mart', 'panoB', 0.0, mulls=[6.9, 9.3, 11.9], box_u1=side.L - 2.03, **sf)
    pilaster(B, side, 3.9, 4.4, 0.0, 3.5, proj=0.1, mat='MB_PanelLight', base_mat='MB_Granite', base_h=0.35, cap=False)
    # sliding door centre stile
    box(B.mb, front.F, 1.53, 1.57, 0.0, 2.75, -0.29, -0.2, 'MB_Steel', only='FLR')
    canopy(B, front, 0.2, 2.9, 3.06, proj=1.3, edge='MB_LampGlow')
    # big lightbox fascia (emissive), wrapping the corner
    lit_board(B, front.F, 5.5, 3.98, 'mart_front', size=(8.1, 0.95), depth=0.28)
    lit_board(B, side.F, 9.1, 3.98, 'mart_side', size=(6.4, 0.95), depth=0.28)
    dgp = -0.25 - 0.06
    for i, u in enumerate((2.3, 3.85, 5.55, 7.65, 9.15, 10.6)):
        poster(B, front.F, u, 2.15, dgp, TS, 'poster%d' % (i % 6), MS)
    for i, u in enumerate((5.6, 8.1, 10.6, 13.0)):
        poster(B, side.F, u, 2.15, dgp, TS, 'poster%d' % ((i + 2) % 6), MS)
    poster(B, front.F, 9.15, 1.0, dgp - 0.01, 'T_SignsLit.png', 'mart_open', 'MB_SignsLit')
    poster(B, side.F, 12.4, 1.0, dgp - 0.01, 'T_SignsLit.png', 'mart_open', 'MB_SignsLit')
    box(B.mb, front.F, 0.05, 0.35, 1.3, 1.5, 0.14, 0.155, MS, skip='K', uv={'F': quad_uv(slot(TS, 'hours', 2))})
    # office entrance (side, back end)
    res_door(B, side, 1.9, door='door_black', n_steps=1, number=(TS, 'office_plaque', MS), surround='MB_MetalDark',
             hood=False, rail=False, transom=True)
    canopy(B, side, 0.9, 2.9, 3.05, proj=0.9, lights=1)
    drainpipe(B, front.F, W - 0.25, mat='MB_MetalDark')
    # ice merchandiser outside
    box(B.mb, front.F, 11.9, 12.65, 0.0, 1.15, 0.1, 0.8, 'MB_TrimWhite', skip='B',
        uv={'F': quad_uv(slot(TS, 'poster5', 2))}, mats={'F': MS})
    # band over the shop + floor bands
    cornice(B, [(0, 4.42), (0.2, 4.42), (0.2, 4.62), (0, 4.62)], 'MB_MetalDark')
    for k in range(2, B.floors):
        cornice(B, [(0, B.fz(k) - 0.05), (0.05, B.fz(k) - 0.05), (0.05, B.fz(k) + 0.05), (0, B.fz(k) + 0.05)],
                'MB_PanelLight')
    # offices: large windows in dark projecting frames
    for f, us in ((front, [1.7, 4.7, 7.7, 10.7, 13.7]), (side, [1.6, 4.55, 7.5, 10.45, 13.4])):
        for k in floors_upper(B):
            z0 = B.fz(k) + 0.75
            for u in us:
                window(B, f, u, z0, w=2.3, h=2.05, depth=0.2, frame='MB_Steel', sill_style=None, lintel_style=None,
                       sash='transom', office=True, mulls=[u], ac=False, flower=False)
                surround(B, f.F, u - 1.15, u + 1.15, z0, z0 + 2.05, 'MB_MetalDark', w=0.1, proj=0.18)
    zt = B.top
    cornice(B, [(0, zt - 0.4), (0.12, zt - 0.4), (0.12, zt - 0.12), (0, zt - 0.12)], 'MB_MetalDark')
    for k in floors_upper(B):
        for u in (3.5, 7.5, 11.5):
            window(B, back, u, B.fz(k) + 0.8, w=1.6, h=1.8, frame='MB_Steel', sill_style='stone', lintel_style=None,
                   sash='fixed', office=True, ac=False, flower=False, trim='MB_Concrete')
    # roof
    bulkhead(B, 2.0, 4.6, 10.5, 13.6, door_side='front', mat='MB_PanelLight')
    hvac(B, 8.0, 5.0)
    hvac(B, 11.0, 5.0)
    hvac(B, 8.0, 9.5, w=1.3, d=1.3)
    steel_tank(B, 11.6, 11.0)
    vent(B, 5.5, 4.5, 'box')
    vent(B, 4.0, 6.5, 'mushroom')
    vent(B, 13.4, 8.0, 'pipe', h=1.3)
    antenna(B, 2.5, 3.0, h=4.5)
    satellite_dish(B, Frame((4.6, 11.6, B.H), (0, 1, 0), (1, 0, 0)), -0.4, 1.6)
    B.copy += [('lightbox fascia (front + side)', '24/7 MART'), ('window LED', 'OPEN'),
               ('posters', 'HOT COFFEE $1 · COLD DRINKS 2/$3 · SNACK DEAL 3/$5 · ATM INSIDE · FRESH FRUIT 99c · ICE'),
               ('door plaque', 'OPEN 24 HOURS'), ('office plaque', '300 / OFFICES 2-6'),
               ('interior', 'GROCERY · COLD DRINKS · SNACKS · CANDY · HOT COFFEE · ATM')]
    B.notes.append('Sharp corner with a slim steel corner column; sliding glass doors at the corner under a steel '
                   'canopy; offices above with large windows in dark projecting frames (40% lit, cool white).')
    B.loc('Entrance', front.F.P(1.55, 0, 0))
    B.extra_views = [('walk', Vector((-18.9, -18.6, 1.56)), Vector((0.0, 2.0, 6.0)), 60, sun_from(222, 30))]
    return B


# ------------------------------------------------------------------------------------------ CornerPharmacy

def build_pharmacy():
    """CornerPharmacy: 15.4 x 17 m, 7 floors, cream stone, classic cornices; side facade on the viewer's RIGHT."""
    W, D = 15.4, 17.0
    B = Building('CornerPharmacy', [(-W, 0), (0, 0), (0, D), (-W, D)], ['front', 'side', 'back', 'party'], 7,
                 'MB_StoneCream', side='right', footprint_wd=(W, D), lit_frac=0.22)
    front, side, back, party = B.facades
    TS, MS = 'T_Sign_Pharmacy.png', 'MB_Sign_Pharmacy'
    for f in (front, side):
        f.zone(0.0, 0.6, 'MB_Granite')
    sf = dict(frame='MB_TrimDark', stall='MB_Granite', stall_h=0.6, transom=2.8, head=3.35)
    # front (u 0 = party end .. 15.4 = corner)
    shopfront(B, front, 0.8, 6.8, 'Pharmacy', 'panoB', 1.0, mulls=[2.8, 4.8], **sf)
    shopfront(B, front, 7.4, 14.6, 'Pharmacy', 'panoA', 0.0, door=13.4, door_w=1.1, mulls=[9.3, 11.2],
              box_u1=W - 0.28, **sf)
    for a, b in ((0.0, 0.8), (6.8, 7.4), (14.6, 15.4)):
        pilaster(B, front, a, b, 0.0, 3.6, proj=0.12, base_mat='MB_Granite', base_h=0.6)
    # side (u 0 = corner .. 17 = back)
    shopfront(B, side, 0.8, 8.6, 'Pharmacy', 'panoA', 2.5, mulls=[3.4, 6.0], box_u0=2.03, **sf)
    shopfront(B, side, 9.2, 12.0, 'Pharmacy', 'panoB', 5.0, mulls=[], **sf)
    for a, b in ((0.0, 0.8), (8.6, 9.2), (12.0, 12.6)):
        pilaster(B, side, a, b, 0.0, 3.6, proj=0.12, base_mat='MB_Granite', base_h=0.6)
    res_door(B, side, 14.4, door='door_black', number=(TS, 'number', MS), surround='MB_StoneTrim')
    drainpipe(B, side.F, 16.75)
    drainpipe(B, front.F, 0.25)
    dgp = -0.25 - 0.06
    for f, items in ((front, ((2.8, 'poster0'), (11.2, 'poster1'))), (side, ((3.4, 'poster2'),))):
        for u, nm in items:
            poster(B, f.F, u + 0.85, 1.75, dgp, TS, nm, MS)
    poster(B, front.F, 12.25, 3.08, dgp - 0.02, 'T_SignsLit.png', 'pharm_letters', 'MB_SignsLit', size=(1.6, 0.25))
    box(B.mb, front.F, 14.85, 15.15, 1.25, 1.69, 0.12, 0.135, MS, skip='K', uv={'F': quad_uv(slot(TS, 'hours', 2))})
    sign_board(B, front.F, 7.1, 3.92, TS, 'fascia_front', MS, frame='MB_TrimDark', depth=0.1)
    sign_board(B, side.F, 6.0, 3.92, TS, 'fascia_side', MS, frame='MB_TrimDark', depth=0.1)
    cross_sign(B, front.F, 15.0, 5.35)
    cornice(B, [(0, 4.28), (0.1, 4.28), (0.14, 4.33), (0.26, 4.38), (0.3, 4.46), (0.3, 4.54), (0, 4.54)],
            'MB_StoneTrim')
    quoins(B, front, side, 4.6, B.H - 0.3)
    # piano nobile (floor 2): tall windows, pediments, balconettes; band below
    zb = B.fz(1) + 0.7
    cornice(B, [(0, zb - 0.14), (0.09, zb - 0.14), (0.09, zb - 0.03), (0.07, zb), (0, zb)], 'MB_StoneTrim')
    fus = [1.5, 3.9, 6.3, 8.7, 11.1, 13.5]
    sus = [1.9, 4.25, 6.6, 8.95, 11.3, 13.65, 15.95]
    for f, us, bal in ((front, fus, (2, 3)), (side, sus, (2, 4))):
        for i, u in enumerate(us):
            window(B, f, u, zb, w=1.15, h=2.05, frame='MB_TrimDark', sill_style=None, lintel_style=None,
                   ac=False, flower=False)
            box(B.mb, f.F, u - 0.7, u - 0.575, zb, zb + 2.05, 0, 0.05, 'MB_StoneTrim', skip='K')
            box(B.mb, f.F, u + 0.575, u + 0.7, zb, zb + 2.05, 0, 0.05, 'MB_StoneTrim', skip='K')
            pediment(B, f.F, u - 0.7, u + 0.7, zb + 2.05)
            if i in bal:
                balconette(B, f.F, u - 0.8, u + 0.8, zb)
    for k in range(2, B.floors - 1):
        style = 'hood' if k == 2 else 'key'
        for f, us in ((front, fus), (side, sus)):
            for u in us:
                window(B, f, u, B.fz(k) + 0.85, w=1.15, h=1.75, frame='MB_TrimDark', lintel_style=style)
    za = B.fz(B.floors - 1) + 0.75
    cornice(B, [(0, za - 0.16), (0.1, za - 0.16), (0.1, za - 0.03), (0.08, za), (0, za)], 'MB_StoneTrim')
    for f, us in ((front, fus), (side, sus)):
        for u in us:
            window(B, f, u, za, w=1.15, h=1.35, frame='MB_TrimDark', sill_style=None, lintel_style=None,
                   sash='casement', ac=False)
    # main cornice: dentils + modillions + crown
    zt = B.top
    zc0 = zt - 1.25
    cornice(B, [(0, zc0), (0.16, zc0), (0.16, zc0 + 0.22), (0.24, zc0 + 0.26), (0.24, zc0 + 0.4), (0.62, zc0 + 0.48),
                (0.7, zc0 + 0.52), (0.7, zc0 + 0.7), (0.62, zc0 + 0.76), (0, zc0 + 0.76)], 'MB_StoneTrim')
    for f, us in ((front, fus), (side, sus)):
        dentils(B, f, 0.3, f.L - 0.3, zc0, 'MB_StoneTrim', proj=0.22)
        mods = sorted([0.35, f.L - 0.35] + [(a + b) / 2 for a, b in zip(us[:-1], us[1:])] + us)
        for u in mods:
            prism(B.mb, f.F, [(0.16, zc0 + 0.22), (0.2, zc0 + 0.22), (0.58, zc0 + 0.42), (0.58, zc0 + 0.47),
                              (0.24, zc0 + 0.47), (0.24, zc0 + 0.26)], u - 0.07, u + 0.07, 'MB_StoneTrim',
                  skip_edges=(4, 5))
    # parapet panels
    for f in (front, side):
        n = int(f.L / 2.4)
        for i in range(n):
            u0 = 0.4 + i * (f.L - 0.8) / n
            box(B.mb, f.F, u0 + 0.2, u0 + (f.L - 0.8) / n - 0.2, zt - 0.38, zt - 0.14, 0, 0.03, 'MB_StoneTrim',
                skip='K')
    for k in floors_upper(B):
        for u in (3.0, 7.7, 12.4):
            window(B, back, u, B.fz(k) + 0.85, w=1.0, h=1.6, frame='MB_TrimDark', lintel_style=None, ac=False,
                   flower=False, trim='MB_Concrete')
    # roof
    bulkhead(B, -12.8, -10.2, 12.0, 15.2, door_side='front')
    steel_tank(B, -4.5, 12.5, r=1.6, h=3.0, legs=1.6)
    hvac(B, -8.0, 6.0)
    vent(B, -11.5, 5.0, 'mushroom')
    vent(B, -6.0, 9.0, 'box')
    vent(B, -2.5, 4.5, 'pipe', h=1.2)
    chimney(B, -14.7, 9.0, w=0.6, d=1.4, h=2.0, pots=2)
    antenna(B, -9.5, 13.5, h=3.6)
    skylight(B, -3.5, 6.5)
    B.copy += [('fascia (front)', 'PHARMACY / PRESCRIPTIONS · HEALTH · BEAUTY'),
               ('fascia (side)', 'PHARMACY / OPEN 8 AM - 9 PM'), ('blade sign', 'green cross (no text)'),
               ('window LED', 'PHARMACY'),
               ('posters', 'FLU SHOTS - NO APPOINTMENT NEEDED · SPRING ALLERGY RELIEF · VITAMINS BUY 1 GET 1 HALF PRICE'),
               ('door plaque', 'HOURS MON-SAT 8-9 / SUN 10-6 / PHARMACIST ON DUTY'), ('house number', '401'),
               ('interior', 'PRESCRIPTIONS · HEALTH & BEAUTY · VITAMINS')]
    B.notes.append('Classic cream stone: granite base, quoins, piano nobile with pediments and two balconettes per '
                   'street facade, dentil + modillion main cornice. Emissive green cross blade sign at the corner.')
    B.loc('Entrance', front.F.P(13.4, 0, 0))
    B.extra_views = [('walk', Vector((18.9, -2.6, 1.56)), Vector((0.0, 3.0, 6.5)), 60, sun_from(138, 30))]
    return B


# ------------------------------------------------------------------------------------------ CornerDiner

def build_diner():
    """CornerDiner: 16 x 15.4 m, 4 floors, teal-painted brick, rounded corner; side facade on the viewer's RIGHT."""
    W, D, r, nseg = 16.0, 15.4, 2.6, 6
    arc = [(-r + r * math.cos(math.radians(-90 + 90 * i / nseg)), r + r * math.sin(math.radians(-90 + 90 * i / nseg)))
           for i in range(1, nseg + 1)]
    fp = [(-W, 0), (-r, 0)] + arc + [(0, D), (-W, D)]
    kinds = ['front'] + ['round'] * nseg + ['side', 'back', 'party']
    B = Building('CornerDiner', fp, kinds, 4, 'MB_BrickTeal', side='right', footprint_wd=(W, D),
                 coping_mat='MB_TrimWhite', lit_frac=0.3)
    front = B.facades[0]
    segs = B.facades[1:1 + nseg]
    side = B.facades[1 + nseg]
    back = B.facades[2 + nseg]
    TS, MS = 'T_Sign_Diner.png', 'MB_Sign_Diner'
    for f in [front, side] + segs:
        f.zone(0.0, 0.5, 'MB_TrimTeal')
    sf = dict(frame='MB_Chrome', stall='MB_Chrome', stall_h=0.5, transom=2.75, head=3.35)
    # front (u 0 = party end .. 13.4 = curve start)
    res_door(B, front, 1.45, door='door_red', number=(TS, 'number', MS), surround='MB_TrimWhite', hood=False)
    shopfront(B, front, 2.9, 13.0, 'Diner', 'panoA', 0.0, door=12.0, door_w=1.0, mulls=[4.9, 6.9, 8.9, 10.9], **sf)
    pilaster(B, front, 2.5, 2.9, 0.0, 3.5, proj=0.1, base_mat='MB_TrimTeal', base_h=0.5, cap=False)
    pilaster(B, front, 13.0, front.L, 0.0, 3.5, proj=0.1, base_mat='MB_TrimTeal', base_h=0.5, cap=False)
    for f in (front,):
        for zr in (0.12, 0.24, 0.36):
            box(B.mb, f.F, 3.0, 11.44, zr, zr + 0.03, -0.25 + 0.1, -0.25 + 0.125, 'MB_Chrome', only='FTB')
    dgf = -0.25 - 0.03
    for u in (3.9, 5.9, 7.9, 9.9):
        booth(B, front.F, u, dgf)
    curve_shop(B, segs, 'Diner', 'panoB', 2.0)
    shopfront(B, side, 0.4, 6.4, 'Diner', 'panoB', 4.0, mulls=[2.4, 4.4], **sf)
    pilaster(B, side, 0.0, 0.4, 0.0, 3.5, proj=0.1, base_mat='MB_TrimTeal', base_h=0.5, cap=False)
    pilaster(B, side, 6.4, 6.9, 0.0, 3.5, proj=0.1, base_mat='MB_TrimTeal', base_h=0.5, cap=False)
    for zr in (0.12, 0.24, 0.36):
        box(B.mb, side.F, 0.5, 6.3, zr, zr + 0.03, -0.25 + 0.1, -0.25 + 0.125, 'MB_Chrome', only='FTB')
    for u in (1.4, 3.4, 5.4):
        booth(B, side.F, u, dgf)
    # kitchen door + small window on the side
    side.opening(7.6, 8.55, 0.0, 2.15, 0.12, side='MB_Concrete', top='MB_Concrete')
    box(B.mb, side.F, 7.6, 8.55, 0.0, 2.15, -0.12, -0.11, 'MB_Details', only='F',
        uv={'F': quad_uv(slot('T_Details.png', 'door_steel', 2))})
    box(B.mb, side.F, 7.5, 8.65, 2.15, 2.3, 0, 0.06, 'MB_Concrete', skip='K')
    window(B, side, 10.0, 1.8, w=1.2, h=1.0, frame='MB_TrimWhite', sash='fixed', lit=True, variant='kitchen',
           lintel_style=None, ac=False, flower=False, trim='MB_TrimWhite')
    vent_f = side.F
    box(B.mb, vent_f, 9.6, 10.4, 2.95, 3.35, 0.0, 0.25, 'MB_Steel', skip='K',
        uv={'F': quad_uv(slot('T_Details.png', 'vent', 2))}, mats={'F': 'MB_Details'})
    drainpipe(B, side.F, side.L - 0.3, mat='MB_TrimWhite')
    drainpipe(B, front.F, 0.25, mat='MB_TrimWhite')
    # signs: neon fascia, side fascia, vertical blade on the curve, OPEN neon, canopy over the door
    box(B.mb, front.F, 2.9, 13.0, 3.45, 3.5, 0.0, 0.05, 'MB_Chrome', skip='K')
    lit_board(B, front.F, 7.6, 3.88, 'diner_main', size=(4.2, 0.75), depth=0.16, frame='MB_Chrome')
    sign_board(B, side.F, 3.4, 3.83, TS, 'fascia_side', MS, frame='MB_Chrome', depth=0.08)
    for f in segs:
        box(B.mb, f.F, 0.0, f.L, 3.6, 3.66, 0.0, 0.05, 'MB_Chrome', only='FTB')
        box(B.mb, f.F, 0.0, f.L, 3.8, 3.86, 0.0, 0.05, 'MB_Chrome', only='FTB')
        box(B.mb, f.F, 0.0, f.L, 3.98, 4.04, 0.0, 0.05, 'MB_NeonPink', only='FTB')
    mid = nseg // 2
    jp = segs[mid].F.A
    m = (segs[mid - 1].F.n + segs[mid].F.n).normalized()
    Fj = Frame(jp, Z.cross(m).normalized() * -1, m)
    blade_sign(B, Fj, 0.0, 6.35, 'T_SignsLit.png', 'diner_blade', 'MB_SignsLit', d_wall=0.25, thick=0.2,
               frame='MB_Chrome')
    poster(B, front.F, 10.9, 2.2, -0.25 - 0.07, 'T_SignsLit.png', 'diner_open', 'MB_SignsLit', size=(1.2, 0.34))
    poster(B, side.F, 4.4, 2.2, -0.25 - 0.07, TS, 'poster0', MS)
    poster(B, front.F, 4.9, 2.25, -0.25 - 0.07, TS, 'poster1', MS)
    canopy(B, front, 11.2, 12.8, 3.0, proj=1.0, mat='MB_Chrome', lights=2, edge='MB_NeonBlue')
    cornice(B, [(0, 4.24), (0.12, 4.24), (0.16, 4.3), (0.22, 4.34), (0.22, 4.42), (0, 4.42)], 'MB_TrimWhite')
    # upper floors: white trim, soldier lintels; wrap-around corner windows on the curve
    fus = [1.6, 4.1, 6.6, 9.1, 11.6]
    sus = [1.6, 4.1, 6.6, 9.1, 11.6]
    for k in floors_upper(B):
        z0 = B.fz(k) + 0.85
        for f, us in ((front, fus), (side, sus)):
            for u in us:
                window(B, f, u, z0, w=1.1, h=1.7, frame='MB_TrimWhite', lintel_style='soldier', trim='MB_TrimWhite')
        for f in segs:
            window(B, f, f.L / 2, z0, w=f.L - 0.14, h=1.7, frame='MB_TrimWhite', sill_style=None, lintel_style=None,
                   sash='fixed', ac=False, flower=False, trim='MB_TrimWhite')
        arcp = [segs[0].F.A] + [f.F.P(f.L, 0, 0) for f in segs]
        sweep(B.mb, arcp, [(0, z0 - 0.07), (0.07, z0 - 0.07), (0.07, z0), (0, z0)], 'MB_TrimWhite')
        sweep(B.mb, arcp, [(0, z0 + 1.7), (0.05, z0 + 1.7), (0.05, z0 + 1.88), (0, z0 + 1.88)], 'MB_TrimWhite')
    zt = B.top
    for zz in (zt - 0.62, zt - 0.46, zt - 0.3):
        cornice(B, [(0, zz), (0.05, zz), (0.05, zz + 0.06), (0, zz + 0.06)], 'MB_TrimWhite')
    cornice(B, [(0, B.H - 0.1), (0.3, B.H - 0.1), (0.3, B.H + 0.06), (0, B.H + 0.06)], 'MB_TrimWhite')
    for k in floors_upper(B):
        for u in (3.0, 8.0, 13.0):
            window(B, back, u, B.fz(k) + 0.85, w=0.9, h=1.5, frame='MB_TrimWhite', lintel_style=None, ac=False,
                   flower=False, trim='MB_Concrete')
    # roof
    bulkhead(B, -13.5, -11.0, 11.5, 14.6, door_side='front', mat='MB_BrickCommon')
    hvac(B, -7.0, 8.0)
    vent(B, -4.0, 6.0, 'box')
    vent(B, -9.5, 4.0, 'mushroom')
    vent(B, -5.0, 11.0, 'pipe', h=1.4)
    chimney(B, -15.3, 6.0, w=0.6, d=1.2, h=1.6)
    antenna(B, -3.5, 10.5, h=3.0)
    satellite_dish(B, side.F, 8.2, B.fz(2) + 1.6)
    B.copy += [('neon fascia', 'Diner · OPEN 24H'), ('vertical blade (neon)', 'DINER'), ('window neon', 'OPEN 24H'),
               ('side fascia', 'EAT · DRINK · SINCE 1954'),
               ('window posters', 'BREAKFAST ALL DAY / PANCAKES · EGGS · HASH · FREE COFFEE REFILLS'),
               ('house number', '88'), ('interior', 'BURGERS 9.50 · PANCAKES 7.25 · SHAKES 5.00 · PIE 4.50')]
    B.notes.append('Rounded corner (r 2.6 m, 6 facets) with wrap-around glazing and corner windows above; chrome '
                   'trim, red vinyl booths behind the glass; vertical neon DINER blade on the corner.')
    B.loc('Entrance', front.F.P(12.0, 0, 0))
    B.extra_views = [('walk', Vector((-4.1, -18.6, 1.56)), Vector((-3.0, 0.0, 5.0)), 60, sun_from(138, 30))]
    return B


# ------------------------------------------------------------------------------------------ mid-block kit

def midblock(name, W, D, floors, wall, side_mat='MB_BrickCommon', **kw):
    B = Building(name, [(-W / 2, 0), (W / 2, 0), (W / 2, D), (-W / 2, D)], ['front', 'party', 'back', 'party'],
                 floors, wall, side_mat=side_mat, footprint_wd=(W, D), kind='mid', **kw)
    return B


def side_windows(B, from_floor=2, w=0.9, h=1.4, n=2):
    for f in (B.facades[1], B.facades[3]):
        us = [f.L * (0.4 + 0.25 * i) for i in range(n)]
        for k in range(from_floor, B.floors):
            for u in us:
                window(B, f, u, B.fz(k) + 0.9, w=w, h=h, frame='MB_TrimDark', lintel_style=None, trim='MB_Concrete',
                       ac=False, flower=False)
    back = B.facades[2]
    nb = max(2, int(back.L / 4))
    for k in range(1, B.floors):
        for i in range(nb):
            window(B, back, back.L * (i + 0.5) / nb, B.fz(k) + 0.9, w=0.9, h=1.5, frame='MB_TrimDark',
                   lintel_style=None, trim='MB_Concrete', ac=False, flower=False)


def shop_cornice(B, mat='MB_StoneTrim', z=4.14, proj=0.28):
    cornice(B, [(0, z), (0.1, z), (0.14, z + 0.06), (proj - 0.04, z + 0.1), (proj, z + 0.18), (proj, z + 0.24),
                (0, z + 0.24)], mat)


def top_cornice(B, mat, proj=0.42, brackets=None, f=None):
    zt = B.top
    cornice(B, [(0, zt - 0.85), (0.06, zt - 0.85), (0.06, zt - 0.7), (0.14, zt - 0.66), (0.14, zt - 0.52),
                (proj - 0.06, zt - 0.42), (proj, zt - 0.38), (proj, zt - 0.2), (0, zt - 0.2)], mat)
    if brackets:
        brackets_along(B, f or B.facades[0], brackets, zt - 0.85, mat, size=(0.13, 0.38, 0.34))


def roof_kit(B, bulk=True, tank=False, chim=True, dish=False):
    W, D = B.footprint_wd
    r = B.rng
    if bulk:
        x0 = -W / 2 + 0.8 if r.random() < 0.5 else W / 2 - 3.2
        bulkhead(B, x0, x0 + 2.4, D - 3.6, D - 0.6, door_side='front')
    vent(B, r.uniform(-W / 4, W / 4), r.uniform(2.0, D / 2), 'mushroom')
    vent(B, r.uniform(-W / 3, W / 3), r.uniform(D / 2, D - 4.5), 'box')
    vent(B, r.uniform(-W / 3, W / 3), r.uniform(1.5, D - 5), 'pipe', h=1.1)
    antenna(B, r.uniform(-W / 3, W / 3), r.uniform(1.5, 4.0), h=r.uniform(2.6, 3.6))
    if chim:
        chimney(B, W / 2 - 0.65, r.uniform(4, D - 5), w=0.6, d=1.1, h=1.5)
    if tank:
        water_tower(B, r.uniform(-W / 4, W / 4), D - 3.2, r=1.5, legs_h=2.6, tank_h=2.8)
    if dish:
        satellite_dish(B, Frame((-W / 2 + 1.5, D / 2, B.H), (0, -1, 0), (-1, 0, 0)), 0.0, 1.4)


def lantern(B, F, u, z, d, r=0.22, h=0.3):
    mb = B.mb
    c = F.P(u, z, d)
    blob(mb, c, r, h, 'MB_LanternRed', segs=10, rings=5)
    cylinder(mb, c + Z * (h - 0.04), Z, r * 0.55, 0.08, 8, 'MB_Metal', cap0=False)
    cylinder(mb, c - Z * (h + 0.04), Z, r * 0.55, 0.08, 8, 'MB_Metal', cap1=False)
    box(mb, F, u - 0.006, u + 0.006, z + h + 0.04, z + h + 0.45, d - 0.006, d + 0.006, 'MB_Metal', only='FKLR')
    box(mb, F, u - 0.025, u + 0.025, z - h - 0.25, z - h - 0.04, d - 0.025, d + 0.025, 'MB_LanternRed', only='FKLR')


def text3d(B, F, text, uc, zc, size, depth, mat, font='Georgia Bold.ttf', d0=0.0, spacing=1.0):
    """Extruded letters (Blender font -> mesh) placed on facade frame F, back faces dropped."""
    cu = bpy.data.curves.new('txt', 'FONT')
    cu.body = text
    cu.font = bpy.data.fonts.load('/System/Library/Fonts/Supplemental/' + font, check_existing=True)
    cu.size = size
    cu.extrude = depth / 2
    cu.resolution_u = 2
    cu.align_x, cu.align_y = 'CENTER', 'CENTER'
    cu.space_character = spacing
    ob = bpy.data.objects.new('txt', cu)
    bpy.context.scene.collection.objects.link(ob)
    dg = bpy.context.evaluated_depsgraph_get()
    ev = ob.evaluated_get(dg)
    me = ev.to_mesh()
    for poly in me.polygons:
        if poly.normal.z < -0.99:
            continue
        pts = [F.P(uc + me.vertices[i].co.x, zc + me.vertices[i].co.y, d0 + depth / 2 + me.vertices[i].co.z)
               for i in poly.vertices]
        n = poly.normal
        B.mb.face(pts, mat, normal=F.t * n.x + Z * n.y + F.n * n.z, frame=F)
    ev.to_mesh_clear()
    bpy.data.objects.remove(ob)
    bpy.data.curves.remove(cu)


def shutter(B, f, u0, u1, z1=3.05, reveal=0.12, sticker=None):
    """Closed roll-down shutter in an opening: slats, guide rails, housing box, bottom bar + lock."""
    F, mb = f.F, B.mb
    f.opening(u0, u1, 0.0, z1 + 0.4, reveal, side=f.wall_mat, top='MB_Steel')
    a, b = u0 + 0.06, u1 - 0.06
    mb.face([F.P(a, 0.0, -reveal + 0.04), F.P(b, 0.0, -reveal + 0.04), F.P(b, z1, -reveal + 0.04),
             F.P(a, z1, -reveal + 0.04)], 'MB_Shutter', normal=F.n, frame=F)
    box(mb, F, u0, a, 0.0, z1 + 0.4, -reveal, -reveal + 0.1, 'MB_Steel', only='FR')
    box(mb, F, b, u1, 0.0, z1 + 0.4, -reveal, -reveal + 0.1, 'MB_Steel', only='FL')
    box(mb, F, a, b, z1, z1 + 0.4, -reveal, -reveal + 0.12, 'MB_Steel', only='FB')
    box(mb, F, a, b, 0.0, 0.07, -reveal + 0.04, -reveal + 0.075, 'MB_Steel', only='FT')
    for uu in ((a + b) / 2 - 0.5, (a + b) / 2 + 0.5):
        box(mb, F, uu - 0.06, uu + 0.06, 0.02, 0.1, -reveal + 0.075, -reveal + 0.1, 'MB_Metal', skip='K')
    threshold(B, F, u0, u1, -reveal, 0.0)
    if sticker:
        tex, nm, mat = sticker
        poster(B, F, (a + b) / 2, 1.5, -reveal + 0.045, tex, nm, mat)


def papered(B, F, u0, u1, z0, z1, d):
    B.mb.face([F.P(u0, z0, d), F.P(u1, z0, d), F.P(u1, z1, d), F.P(u0, z1, d)], 'MB_Paper', normal=F.n, frame=F)


def bucket_stand(B, F, u0, u1, d0=0.15, d1=1.0):
    """Tiered wooden stand of flower buckets on the sidewalk, plus a few buckets on the ground."""
    mb, r = B.mb, B.rng
    tiers = [(0.0, 0.25, d1 - 0.05, d1 - 0.35), (0.25, 0.5, d1 - 0.38, d0 + 0.3), (0.5, 0.75, d0 + 0.28, d0)]
    for z0, z1, dd0, dd1 in tiers:
        a, b = sorted((dd0, dd1))
        box(mb, F, u0, u1, z0, z1, a, b, 'MB_WoodLight', skip='B')
        n = int((u1 - u0) / 0.38)
        for i in range(n):
            uc = u0 + (i + 0.5) * (u1 - u0) / n
            c = F.P(uc, z1, (a + b) / 2)
            cylinder(mb, c, Z, 0.13, 0.32, 8, 'MB_Steel', cap0=False, cap1=True, r1=0.15, cap_mat='MB_Foliage')
            blob(mb, c + Z * (0.42 + r.uniform(0, 0.06)), r.uniform(0.17, 0.21), r.uniform(0.16, 0.22), 'MB_Foliage',
                 segs=7, rings=3)
    for i in range(3):
        uc = u1 + 0.35 + i * 0.42
        c = F.P(uc, 0.0, d1 - 0.3 - (i % 2) * 0.35)
        cylinder(mb, c, Z, 0.14, 0.38, 8, 'MB_Steel', cap0=False, cap1=True, r1=0.16, cap_mat='MB_Foliage')
        blob(mb, c + Z * 0.55, 0.2, 0.25, 'MB_Foliage', segs=7, rings=3)


def book_cart(B, F, u, d, tex, mat):
    mb = B.mb
    box(mb, F, u - 0.65, u + 0.65, 0.62, 0.7, d - 0.3, d + 0.3, 'MB_Wood', skip='')
    for uu in (u - 0.6, u + 0.55):
        for dd in (d - 0.26, d + 0.22):
            box(mb, F, uu, uu + 0.05, 0.0, 0.62, dd, dd + 0.05, 'MB_Wood', only='FKLR')
    box(mb, F, u - 0.6, u + 0.6, 0.45, 0.62, d + 0.28, d + 0.3, 'MB_Wood', only='F',
        uv={'F': quad_uv(slot(tex, 'bin', 2))}, mats={'F': mat})
    cols = ['MB_TrimGreen', 'MB_Vinyl', 'MB_TrimCream', 'MB_TrimDark', 'MB_Brass', 'MB_TrimBottle']
    x = u - 0.6
    while x < u + 0.55:
        w = B.rng.uniform(0.04, 0.08)
        h = B.rng.uniform(0.16, 0.26)
        box(mb, F, x, x + w, 0.7, 0.7 + h, d - 0.22, d + 0.2, cols[B.rng.randrange(len(cols))], skip='B')
        x += w + 0.008


def build_noodle():
    B = midblock('NoodleBar', 12.0, 15.0, 4, 'MB_StoneCream', lit_frac=0.3)
    front = B.facades[0]
    TS, MS = 'T_Sign_Noodle.png', 'MB_Sign_Noodle'
    front.zone(0.0, 0.5, 'MB_Granite')
    res_door(B, front, 1.25, door='door_blue', number=(TS, 'number', MS))
    shopfront(B, front, 2.6, 11.4, 'Noodle', 'panoA', 0.0, door=9.8, door_w=1.0, mulls=[4.8, 7.0], frame='MB_Wood',
              stall='MB_Wood', transom_div=0.7)
    pilaster(B, front, 11.4, 12.0, 0.0, 3.55, proj=0.1, base_mat='MB_Granite')
    pilaster(B, front, 2.2, 2.6, 0.0, 3.55, proj=0.1, base_mat='MB_Granite')
    poster(B, front.F, 9.8, 2.48, -0.25 + 0.09, TS, 'noren', MS, size=(1.0, 0.55))
    poster(B, front.F, 3.7, 1.75, -0.31, TS, 'menu', MS)
    poster(B, front.F, 8.4, 2.2, -0.32, 'T_SignsLit.png', 'open_neon', 'MB_SignsLit', size=(0.8, 0.36))
    sign_board(B, front.F, 7.0, 3.86, TS, 'fascia', MS, frame='MB_Wood')
    box(B.mb, front.F, 2.5, 11.5, 3.42, 3.5, 0.0, 0.62, 'MB_Wood', skip='K')        # lantern beam
    for u in (3.2, 5.6, 8.0, 10.8):
        lantern(B, front.F, u, 2.85, 0.48)
        B.loc('Lantern_%d' % int(u * 10), front.F.P(u, 2.85, 0.48))
    shop_cornice(B)
    us = [1.5, 4.5, 7.5, 10.5]
    upper_windows(B, front, us, frame='MB_TrimWhite', lintel_style='flat', trim='MB_StoneTrim')
    top_cornice(B, 'MB_StoneTrim', brackets=[0.3, 3.0, 6.0, 9.0, 11.7])
    drainpipe(B, front.F, 11.85)
    side_windows(B)
    roof_kit(B)
    B.copy += [('fascia', 'NOODLE BAR / RAMEN · DUMPLINGS · BAO'), ('noren (door curtain)', 'OPEN + bowl icon'),
               ('window menu', 'MENU: SHOYU RAMEN 12 · MISO RAMEN 13 · UDON 11 · GYOZA 7 · BAO 6 · LUNCH 11-3'),
               ('window neon', 'OPEN'), ('interior boards', 'RAMEN 12 · UDON 11 · GYOZA 7 · BAO 6'),
               ('house number', '112')]
    B.notes.append('Dark wood shopfront, four emissive red paper lanterns on a beam under the sign, noren over the '
                   'door.')
    return B


def build_books():
    B = midblock('Books', 12.0, 16.0, 5, 'MB_BrickRed', lit_frac=0.25)
    front = B.facades[0]
    TS, MS = 'T_Sign_Books.png', 'MB_Sign_Books'
    front.zone(0.0, 0.5, 'MB_StoneTrim')
    shopfront(B, front, 0.9, 9.3, 'Books', 'panoA', 0.0, door=5.1, door_w=1.1, mulls=[1.9, 2.9, 3.9, 6.3, 7.3, 8.3],
              frame='MB_TrimBottle', stall='MB_TrimBottle', transom=2.55, transom_div=0.5)
    for a, b in ((0.0, 0.9), (9.3, 9.8)):
        pilaster(B, front, a, b, 0.0, 3.55, proj=0.12, mat='MB_TrimBottle', base_mat='MB_TrimBottle', base_h=0.5)
    res_door(B, front, 10.85, door='door_black', number=(TS, 'number', MS), n_steps=2)
    sign_board(B, front.F, 5.1, 3.85, TS, 'fascia', MS, frame='MB_TrimBottle', depth=0.12)
    box(B.mb, front.F, 4.7, 5.0, 1.15, 1.55, -0.22, -0.205, MS, only='F', uv={'F': quad_uv(slot(TS, 'hours', 2))})
    book_cart(B, front.F, 2.6, 0.75, TS, MS)
    shop_cornice(B, mat='MB_TrimBottle')
    us = [1.5, 4.3, 7.2, 10.2]
    upper_windows(B, front, us, frame='MB_TrimWhite', lintel_style='key')
    top_cornice(B, 'MB_TrimCream', brackets=[0.3, 2.9, 5.75, 8.7, 11.7])
    drainpipe(B, front.F, 11.8)
    side_windows(B)
    roof_kit(B, tank=True, bulk=False)
    B.copy += [('fascia', 'BOOKS / USED & NEW · EST. 1972'), ('book cart', 'ALL BOOKS $1'),
               ('door plaque', 'BOOKS / OPEN 10-8 / CLOSED MON'), ('interior', 'FICTION · HISTORY · NEW ARRIVALS'),
               ('house number', '106')]
    B.notes.append('Bottle-green small-paned shopfront, $1 book cart on the sidewalk, rooftop water tower.')
    return B


def build_laundromat():
    B = midblock('Laundromat', 10.0, 14.0, 3, 'MB_BrickSlate', lit_frac=0.3)
    front = B.facades[0]
    TS, MS = 'T_Sign_Laundry.png', 'MB_Sign_Laundry'
    front.zone(0.0, 0.45, 'MB_Concrete')
    shopfront(B, front, 0.5, 7.7, 'Laundry', 'panoA', 0.0, door=6.75, door_w=1.0, mulls=[2.4, 4.3], frame='MB_Steel',
              stall='MB_Steel', stall_h=0.4, transom_div=1.2)
    pilaster(B, front, 7.7, 8.1, 0.0, 3.55, proj=0.1, base_mat='MB_Concrete', base_h=0.45, cap=False)
    res_door(B, front, 9.0, door='door_blue', number=(TS, 'number', MS), n_steps=2, w=1.0, hood=False)
    sign_board(B, front.F, 4.1, 3.87, TS, 'fascia', MS, frame='MB_TrimWhite', depth=0.1)
    poster(B, front.F, 1.45, 1.55, -0.31, TS, 'poster0', MS)
    poster(B, front.F, 3.35, 1.55, -0.31, TS, 'poster1', MS)
    poster(B, front.F, 5.3, 2.3, -0.32, 'T_SignsLit.png', 'open_neon', 'MB_SignsLit', size=(0.8, 0.36))
    box(B.mb, front.F, 6.05, 6.25 + 0.16, 1.25, 1.49, -0.22, -0.205, MS, only='F', uv={'F': quad_uv(slot(TS, 'hours', 2))})
    dgi = -0.25 - 0.03
    for u in (1.2, 1.75, 2.3, 4.6, 5.15):                                       # plastic chairs facing out
        box(B.mb, front.F, u - 0.2, u + 0.2, 0.42, 0.46, dgi - 0.75, dgi - 0.35, 'MB_Terracotta')
        box(B.mb, front.F, u - 0.2, u + 0.2, 0.46, 0.85, dgi - 0.79, dgi - 0.75, 'MB_Terracotta')
        box(B.mb, front.F, u - 0.17, u + 0.17, 0.03, 0.42, dgi - 0.72, dgi - 0.4, 'MB_Steel', only='FKLR')
    shop_cornice(B, mat='MB_TrimWhite')
    upper_windows(B, front, [1.6, 5.0, 8.4], frame='MB_TrimWhite', lintel_style='soldier', trim='MB_TrimWhite')
    top_cornice(B, 'MB_TrimWhite', proj=0.3)
    drainpipe(B, front.F, 0.2, mat='MB_TrimWhite')
    side_windows(B, from_floor=1)
    roof_kit(B, chim=False)
    hvac(B, 1.5, 6.0, w=1.3, d=1.0)
    B.copy += [('fascia', 'LAUNDROMAT / COIN WASH · DRY · FOLD'), ('posters', 'WASH $3.50 / DRY 25c / 8 MIN · '
               'DROP-OFF SERVICE / READY BY 5PM'), ('window neon', 'OPEN'), ('door plaque', 'OPEN 6AM-11PM / LAST WASH 10PM'),
               ('interior', 'WASHERS · DRYERS'), ('house number', '96')]
    B.notes.append('Bright fluorescent interior: rows of washers/dryers on the back wall, orange chairs at the window.')
    return B


def build_phone():
    B = midblock('PhoneRepair', 14.0, 16.0, 6, 'MB_BrickBrown', lit_frac=0.25)
    front = B.facades[0]
    TS, MS = 'T_Sign_Phone.png', 'MB_Sign_Phone'
    front.zone(0.0, 0.5, 'MB_Concrete')
    shutter(B, front, 0.8, 7.8, sticker=(TS, 'sticker', MS))
    pilaster(B, front, 0.0, 0.8, 0.0, 3.55, proj=0.1, base_mat='MB_Concrete')
    pilaster(B, front, 7.8, 8.4, 0.0, 3.55, proj=0.1, base_mat='MB_Concrete')
    sign_board(B, front.F, 4.3, 3.86, TS, 'fascia', MS, frame='MB_TrimDark', depth=0.1)
    res_door(B, front, 10.6, door='door_red', number=(TS, 'number', MS), n_steps=4, step_h=0.15)
    window(B, front, 12.75, 1.3, w=1.0, h=1.5, frame='MB_TrimWhite', lintel_style='flat', ac=False, flower=False)
    for u in (12.5, 13.0):
        box(B.mb, front.F, u - 0.012, u + 0.012, 1.3, 2.8, 0.02, 0.045, 'MB_Metal', skip='K')
    shop_cornice(B, mat='MB_StoneTrim')
    us = [1.3, 4.1, 7.0, 9.9, 12.7]
    upper_windows(B, front, us, frame='MB_TrimWhite', lintel_style='flat', skip={(k, i) for k in range(1, 6)
                                                                                  for i in (1, 2, 3)})
    for k in floors_upper(B):
        for u in us[1:4]:
            window(B, front, u, B.fz(k) + 0.85, w=1.1, h=2.0, frame='MB_TrimWhite', lintel_style='flat', ac=False,
                   flower=False, sill_style=None)
    fire_escape(B, front, 2.85, 11.25, floors_upper(B), depth=1.2)
    top_cornice(B, 'MB_TrimCream', brackets=[0.3, 2.7, 5.55, 8.45, 11.3, 13.7])
    drainpipe(B, front.F, 13.85)
    side_windows(B)
    roof_kit(B, dish=True)
    B.copy += [('fascia', 'PHONE REPAIR / SCREENS · BATTERIES · UNLOCK · ACCESSORIES'),
               ('shutter sticker', 'OPEN 10AM-8PM / WALK-INS WELCOME'), ('house number', '118')]
    B.notes.append('Shop closed at 7:52: roll-down shutter down. Apartment entrance with a 4-step stoop and rails; '
                   'front fire escape over the middle bays.')
    return B


def build_bakery():
    B = midblock('Bakery', 16.0, 16.0, 5, 'MB_BrickTeal', lit_frac=0.3)
    front = B.facades[0]
    TS, MS = 'T_Sign_Bakery.png', 'MB_Sign_Bakery'
    front.zone(0.0, 0.5, 'MB_TrimTeal')
    shopfront(B, front, 1.0, 11.6, 'Bakery', 'panoA', 0.0, door=9.7, door_w=1.0, mulls=[3.2, 5.4, 7.6],
              frame='MB_TrimCream', stall='MB_TrimTeal', transom_div=0.9)
    for a, b in ((0.0, 1.0), (11.6, 12.3)):
        pilaster(B, front, a, b, 0.0, 3.5, proj=0.1, base_mat='MB_TrimTeal')
    res_door(B, front, 13.9, door='door_green', number=(TS, 'number', MS), surround='MB_TrimWhite')
    sign_board(B, front.F, 6.3, 3.86, TS, 'fascia', MS, frame='MB_TrimCream', depth=0.1)
    awning(B, front, 0.95, 11.65, 3.42, proj=1.5, drop=0.6, row='stripe_red')
    poster(B, front.F, 4.3, 1.0, -0.31, TS, 'decal', MS)
    poster(B, front.F, 8.6, 2.2, -0.32, 'T_SignsLit.png', 'open_neon', 'MB_SignsLit', size=(0.8, 0.36))
    aframe_sign(B, front.F, 12.0, 0.8, TS, 'aframe', MS)
    cafe_table(B, front.F, 2.6, 0.95)
    cafe_table(B, front.F, 5.6, 0.95)
    shop_cornice(B, mat='MB_TrimWhite')
    us = [1.4, 4.0, 6.6, 9.4, 12.0, 14.6]
    upper_windows(B, front, us, frame='MB_TrimWhite', lintel_style='soldier', trim='MB_TrimWhite')
    top_cornice(B, 'MB_TrimWhite', brackets=[0.3, 2.7, 5.3, 8.0, 10.7, 13.3, 15.7])
    drainpipe(B, front.F, 0.2, mat='MB_TrimWhite')
    side_windows(B)
    roof_kit(B)
    B.copy += [('fascia', 'Bakery / FRESH BREAD DAILY · SINCE 1961'), ('window decal', 'SOURDOUGH · CROISSANTS / COFFEE TO GO'),
               ('A-frame', 'FRESH TODAY: CINNAMON BUNS · RYE LOAF · PAIN AU CHOC · BAGELS · OPEN 6:30'),
               ('window neon', 'OPEN'), ('interior', "TODAY'S BREAD: SOURDOUGH 6 · BAGUETTE 3 · RYE 5 · CROISSANT 3.5"),
               ('house number', '74')]
    B.notes.append('Red/cream striped awning, two bistro tables and an A-frame on the sidewalk.')
    return B


def build_florist():
    B = midblock('Florist', 10.0, 14.0, 4, 'MB_BrickRed', lit_frac=0.25)
    front = B.facades[0]
    TS, MS = 'T_Sign_Florist.png', 'MB_Sign_Florist'
    front.zone(0.0, 0.5, 'MB_StoneTrim')
    shopfront(B, front, 0.6, 7.4, 'Florist', 'panoA', 0.0, door=6.4, door_w=1.0, mulls=[2.6, 4.5],
              frame='MB_TrimWhite', stall='MB_TrimBottle')
    pilaster(B, front, 7.4, 7.9, 0.0, 3.5, proj=0.1)
    res_door(B, front, 8.95, door='door_blue', number=(TS, 'number', MS), n_steps=2, w=1.0)
    sign_board(B, front.F, 4.0, 3.86, TS, 'fascia', MS, frame='MB_TrimBottle', depth=0.1)
    awning(B, front, 0.55, 7.45, 3.42, proj=1.3, drop=0.55, row='stripe_green')
    bucket_stand(B, front.F, 0.8, 3.8)
    aframe_sign(B, front.F, 5.3, 1.15, TS, 'aframe', MS)
    for u in (1.3, 2.3, 3.3):
        box(B.mb, front.F, u - 0.15, u + 0.15, 0.78, 0.94, 1.0, 1.006, MS, only='F', uv={'F': quad_uv(slot(TS, 'tag', 2))})
    potted_shrub(B, front.F, 7.65, 0.45, h=0.9, r=0.28)
    shop_cornice(B, mat='MB_StoneTrim')
    upper_windows(B, front, [1.6, 5.0, 8.4], frame='MB_TrimWhite', lintel_style='key', flower=True)
    top_cornice(B, 'MB_StoneTrim', brackets=[0.3, 3.3, 6.7, 9.7])
    drainpipe(B, front.F, 9.8)
    side_windows(B)
    roof_kit(B, bulk=False)
    B.copy += [('fascia', 'FLORIST / FLOWERS · PLANTS · GIFTS'),
               ('A-frame', 'SPRING: TULIPS 3 FOR $10 · DAFFODILS · POTTED HERBS · BOUQUETS FROM $15'),
               ('bucket tags', '$8'), ('house number', '58')]
    B.notes.append('Tiered stand of flower buckets on the sidewalk, green striped awning, flower boxes upstairs.')
    return B


def build_forlease():
    B = midblock('ForLease', 12.0, 15.0, 3, 'MB_StoneCream', lit_frac=0.2)
    front = B.facades[0]
    TS, MS = 'T_Sign_ForLease.png', 'MB_Sign_ForLease'
    front.zone(0.0, 0.5, 'MB_Granite')
    shopfront(B, front, 0.8, 9.0, 'Cafe', door=7.8, door_w=1.0, mulls=[3.0, 5.2], frame='MB_TrimDark',
              stall='MB_TrimDark', interior=False, glass=False)
    for a, b in ((0.0, 0.8), (9.0, 9.5)):
        pilaster(B, front, a, b, 0.0, 3.5, proj=0.1, base_mat='MB_Granite')
    papered(B, front.F, 0.9, 8.9, 0.45, 3.4, -0.25 - 0.035)
    poster(B, front.F, 3.4, 1.65, -0.25 - 0.025, TS, 'forlease', MS)
    poster(B, front.F, 6.1, 1.55, -0.25 - 0.025, TS, 'agent', MS)
    poster(B, front.F, 7.8, 1.5, -0.25 - 0.045, TS, 'agent', MS, size=(0.45, 0.6))
    sign_board(B, front.F, 4.9, 3.86, TS, 'fascia_old', MS, frame='MB_TrimDark', depth=0.08)
    res_door(B, front, 10.75, door='door_black', number=(TS, 'number', MS), n_steps=2, w=1.0)
    shop_cornice(B, mat='MB_StoneTrim')
    upper_windows(B, front, [1.5, 4.5, 7.5, 10.5], frame='MB_TrimWhite', lintel_style='hood')
    top_cornice(B, 'MB_StoneTrim', proj=0.36)
    drainpipe(B, front.F, 0.2)
    side_windows(B, from_floor=1)
    roof_kit(B, bulk=False)
    B.copy += [('window poster', 'FOR LEASE / RETAIL SPACE / 1,200 SQ FT / CALL 555-0142'),
               ('agent posters', 'AVAILABLE NOW / 555-0142'), ('faded old fascia', 'HARDWARE & PAINT'),
               ('house number', '64')]
    B.notes.append('Vacant shop: windows papered over (MB_Paper, kraft sheets + tape), faded sign of the old '
                   'hardware store.')
    return B


def build_bank():
    B = midblock('Bank', 18.0, 18.0, 7, 'MB_PanelGrey', lit_frac=0.45, coping_mat='MB_MetalDark')
    front = B.facades[0]
    TS, MS = 'T_Sign_Bank.png', 'MB_Sign_Bank'
    front.zone(0.0, 0.4, 'MB_Granite')
    sf = dict(frame='MB_Steel', stall='MB_Granite', stall_h=0.4, transom=2.9, head=3.6, transom_div=1.5)
    shopfront(B, front, 1.0, 12.4, 'Bank', 'panoA', 0.0, door=7.6, door_w=2.0, mulls=[2.9, 4.8, 10.0], **sf)
    box(B.mb, front.F, 7.58, 7.62, 0.0, 2.75, -0.29, -0.2, 'MB_Steel', only='FLR')
    shopfront(B, front, 13.0, 17.0, 'Bank', 'panoB', 0.0, door=14.0, door_w=1.0, mulls=[15.5], **sf)
    for a, b in ((0.0, 1.0), (12.4, 13.0), (17.0, 18.0)):
        pilaster(B, front, a, b, 0.0, 3.95, proj=0.14, mat='MB_Granite', base_mat=None, cap=False)
    canopy(B, front, 5.8, 9.4, 3.05, proj=1.6, mat='MB_Steel', lights=3)
    box(B.mb, front.F, 1.0, 17.0, 3.95, 4.75, 0.0, 0.12, 'MB_MetalDark', skip='K')
    text3d(B, front.F, 'CITY BANK', 6.7, 4.35, 0.62, 0.08, 'MB_Steel', d0=0.12, spacing=1.12)
    poster(B, front.F, 15.0, 3.25, -0.31, 'T_SignsLit.png', 'bank_atm', 'MB_SignsLit')
    box(B.mb, front.F, 12.5, 12.9, 1.2, 1.7, 0.14, 0.155, MS, skip='K', uv={'F': quad_uv(slot(TS, 'hours', 2))})
    box(B.mb, front.F, 0.2, 0.8, 2.6, 2.9, 0.14, 0.155, MS, skip='K', uv={'F': quad_uv(slot(TS, 'number', 2))})
    # curtain wall: 1.5 m modules, glass with steel mullions, dark spandrels, full-height fins every 3 m
    mods = [0.75 + i * 1.5 for i in range(12)]
    for k in floors_upper(B):
        z0 = B.fz(k) + 0.45
        for u in mods:
            window(B, front, u, z0, w=1.36, h=2.5, depth=0.08, frame='MB_Steel', sill_style=None, lintel_style=None,
                   sash='transom', office=True, mulls=[], ac=False, flower=False)
        cornice(B, [(0, B.fz(k) + 0.02), (0.1, B.fz(k) + 0.02), (0.1, B.fz(k) + 0.14), (0, B.fz(k) + 0.14)],
                'MB_MetalDark')
    for i in range(7):
        u = i * 3.0
        box(B.mb, front.F, u - 0.05 if i else 0.0, u + 0.05 if i < 6 else 18.0, 4.8, B.top - 0.3, 0.0, 0.42,
            'MB_Steel', skip='K')
    cornice(B, [(0, B.top - 0.9), (0.45, B.top - 0.9), (0.45, B.top - 0.12), (0, B.top - 0.12)], 'MB_MetalDark')
    side_windows(B, from_floor=1, w=1.4, h=1.8)
    # rooftop: mechanical penthouse + plant
    box(B.mb, WORLD, -5.0, 5.0, B.H, B.H + 3.0, -14.0, -6.0, 'MB_PanelGrey', skip='B')
    box(B.mb, WORLD, -5.15, 5.15, B.H + 3.0, B.H + 3.2, -14.15, -5.85, 'MB_MetalDark')
    for x in (-3.5, 0.0, 3.5):
        box(B.mb, WORLD, x - 1.2, x + 1.2, B.H + 0.6, B.H + 2.4, -6.0, -5.98, 'MB_Details', only='F',
            uv={'F': quad_uv(slot('T_Details.png', 'vent', 2))})
    hvac(B, -6.0, 3.5)
    hvac(B, 6.0, 3.5)
    antenna(B, 7.0, 15.0, h=5.0)
    satellite_dish(B, Frame((5.0, 8.0, B.H), (0, 1, 0), (1, 0, 0)), 1.0, 1.2)
    B.copy += [('3D letters', 'CITY BANK'), ('ATM sign (lit)', 'ATM 24H'), ('door plaque', 'CITY BANK / MON-FRI 9-5 / '
               'SAT 9-1 / ATM 24 HOURS'), ('house number', '200'), ('interior', 'CITY BANK · ATM · WELCOME / OPEN 9-5')]
    B.notes.append('Granite base, steel-framed glazed lobby with canopy, extruded steel CITY BANK letters on a dark '
                   'band, curtain-wall floors (1.5 m modules, steel fins every 3 m), rooftop penthouse.')
    return B


# ---------------------------------------------------------------------------------------------- towers

TOWER_STYLE = {   # material, texture, lit-window rect inside one module (fx0, fz0, fx1, fz1)
    'glass': ('MB_TowerGlass', 'T_TowerGlass.png', (0.03, 0.27, 0.97, 0.985)),
    'dark': ('MB_TowerDark', 'T_TowerDark.png', (0.03, 0.27, 0.97, 0.985)),
    'bands': ('MB_TowerBands', 'T_TowerBands.png', (0.02, 0.36, 0.98, 0.985)),
    'punched': ('MB_TowerPunched', 'T_TowerPunched.png', (26 / 128, 62 / 256, 102 / 128, 216 / 256)),
}


class Tower(Building):
    def __init__(self, name, W, D, floors, fh, style, lit_frac=0.045, office=True):
        super().__init__(name, [(-W / 2, 0), (W / 2, 0), (W / 2, D), (-W / 2, D)], ['front', 'party', 'back', 'party'],
                         floors, TOWER_STYLE[style][0], side_mat=TOWER_STYLE[style][0], footprint_wd=(W, D),
                         kind='tower', lit_frac=lit_frac, ground_h=fh, floor_h=fh)
        self.fh, self.style, self.office = fh, style, office
        self.top = floors * fh

    def finish(self):
        pass


def tower_section(B, x0, x1, y0, y1, f0, f1, rim=0.6, rim_mat='MB_TowerTrim'):
    mb, fh = B.mb, B.fh
    mat, tex, (fx0, fz0, fx1, fz1) = TOWER_STYLE[B.style]
    corners = [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]
    for i in range(4):
        a, b = Vector((*corners[i], 0.0)), Vector((*corners[(i + 1) % 4], 0.0))
        t = b - a
        L = t.length
        t.normalize()
        F = Frame(a, t, Vector((t.y, -t.x, 0.0)))
        nm = max(1, int(round(L / 1.8)))
        mw = L / nm

        def q(ua, ub, za, zb, f, m=None, uvs=None):
            if ub - ua < 1e-4 or zb - za < 1e-4:
                return
            if uvs is None:
                fl = f % 4
                uvs = [(ua / mw / 8, (fl + (za - f * fh) / fh) / 4), (ub / mw / 8, (fl + (za - f * fh) / fh) / 4),
                       (ub / mw / 8, (fl + (zb - f * fh) / fh) / 4), (ua / mw / 8, (fl + (zb - f * fh) / fh) / 4)]
            mb.face([F.P(ua, za, 0), F.P(ub, za, 0), F.P(ub, zb, 0), F.P(ua, zb, 0)], m or mat, normal=F.n, uvs=uvs)

        for f in range(f0, f1):
            z0, z1 = f * fh, (f + 1) * fh
            lit = [k for k in range(nm) if B.rng.random() < B.lit_frac]
            run0 = 0
            for k in lit + [nm]:
                q(run0 * mw, k * mw, z0, z1, f)
                if k == nm:
                    break
                ua, ub = k * mw, (k + 1) * mw
                la, lb = ua + fx0 * mw, ua + fx1 * mw
                za, zb = z0 + fz0 * fh, z0 + fz1 * fh
                q(ua, ub, z0, za, f)
                q(ua, ub, zb, z1, f)
                q(ua, la, za, zb, f)
                q(lb, ub, za, zb, f)
                pool = WIN_LIT_OFFICE if B.office else ['warm_room', 'warm_blinds', 'warm_curtains', 'lamp', 'kitchen']
                q(la, lb, za, zb, f, m='MB_WindowLit', uvs=quad_uv(slot('T_WindowLit.png', B.rng.choice(pool), 4)))
                run0 = k + 1
    ztop = f1 * fh
    mb.face([Vector((x0 + 0.3, y0 + 0.3, ztop)), Vector((x1 - 0.3, y0 + 0.3, ztop)), Vector((x1 - 0.3, y1 - 0.3, ztop)),
             Vector((x0 + 0.3, y1 - 0.3, ztop))], 'MB_Roof', normal=Z)
    if rim:
        sweep(mb, corners, [(-0.3, ztop), (0.12, ztop), (0.12, ztop + rim), (-0.3, ztop + rim)], rim_mat,
              closed_path=True, closed_profile=True)
    return ztop


def tower_box(B, x0, x1, y0, y1, z0, z1, mat, fins=0, fin_mat='MB_TowerTrim'):
    box(B.mb, WORLD, x0, x1, z0, z1, -y1, -y0, mat, skip='B')
    for i in range(fins):
        zz = z0 + (i + 0.5) * (z1 - z0) / fins
        box(B.mb, WORLD, x0 - 0.15, x1 + 0.15, zz - 0.08, zz + 0.08, -y1 - 0.15, -y0 + 0.15, fin_mat, skip='')


def mast(B, x, y, z, h, r=0.25, beacon=True):
    cylinder(B.mb, (x, y, z), Z, r, h, 6, 'MB_TowerTrim', cap0=False, r1=r * 0.35)
    if beacon:
        blob(B.mb, (x, y, z + h + 0.2), 0.35, 0.35, 'MB_BeaconRed', segs=6, rings=3)
        B.loc('Beacon', (x, y, z + h + 0.2))


def build_tower1():
    B = Tower('Tower1', 30.0, 30.0, 24, 3.6, 'glass')
    zt = tower_section(B, -15, 15, 0, 30, 0, 24)
    tower_box(B, -12, 12, 3, 27, zt, zt + 7.2, 'MB_TowerCrown', fins=6)
    box(B.mb, WORLD, -12.3, 12.3, zt + 7.2, zt + 7.6, -27.3, -2.7, 'MB_TowerTrim')
    mast(B, 6.0, 20.0, zt + 7.6, 9.0)
    B.top = zt
    B.notes.append('24 floors x 3.6 m glass curtain wall (1.8 m modules), louvred mechanical crown, mast + red beacon.')
    return B


def build_tower2():
    B = Tower('Tower2', 32.0, 24.0, 30, 3.6, 'bands')
    z1 = tower_section(B, -16, 16, 0, 24, 0, 22)
    z2 = tower_section(B, -13, 13, 3, 21, 22, 27)
    z3 = tower_section(B, -10, 10, 6, 18, 27, 30)
    for x in (-10, 10):
        for y in (6, 18):
            box(B.mb, WORLD, x - 0.5, x + 0.5, z3, z3 + 7.0, -y - 0.5, -y + 0.5, 'MB_TowerTrim', skip='B')
    sweep(B.mb, [(-10, 6), (10, 6), (10, 18), (-10, 18)], [(-0.5, z3 + 6.2), (0.5, z3 + 6.2), (0.5, z3 + 7.0),
                                                           (-0.5, z3 + 7.0)], 'MB_TowerTrim', closed_path=True,
          closed_profile=True)
    tower_box(B, -6, 6, 9, 15, z3, z3 + 4.0, 'MB_TowerCrown', fins=3)
    B.top = z3
    B.notes.append('30 floors, horizontal ribbon windows with stone bands; setbacks at floors 22 and 27; open frame crown.')
    return B


def build_tower3():
    B = Tower('Tower3', 24.0, 24.0, 18, 3.3, 'punched', lit_frac=0.07, office=False)
    zt = tower_section(B, -12, 12, 0, 24, 0, 18, rim=1.0)
    tower_box(B, -4, 4, 14, 21, zt, zt + 3.4, 'MB_TowerPunched')
    B.H = zt
    water_tower(B, -6.5, 8.0, r=1.9, legs_h=3.2, tank_h=3.4)
    hvac(B, 5.0, 6.0, w=2.0, d=1.5)
    B.top = zt
    B.notes.append('18-floor residential tower: punched windows with curtains, small balconies, rooftop water tower.')
    return B


def build_tower4():
    B = Tower('Tower4', 34.0, 30.0, 36, 3.6, 'dark')
    tower_section(B, -17, 17, 0, 30, 0, 28)
    tower_section(B, -14, 14, 3, 27, 28, 33)
    z3 = tower_section(B, -11, 11, 6, 24, 33, 36)
    apex = Vector((0.0, 15.0, z3 + 9.0))
    base = [Vector((-11, 6, z3 + 0.6)), Vector((11, 6, z3 + 0.6)), Vector((11, 24, z3 + 0.6)), Vector((-11, 24, z3 + 0.6))]
    for i in range(4):
        a, b = base[i], base[(i + 1) % 4]
        B.mb.face([a, b, apex], 'MB_TowerCrown', normal=((a + b) / 2 - Vector((0, 15, z3))).normalized() + Z)
    mast(B, 0.0, 15.0, z3 + 8.5, 16.0, r=0.4)
    B.top = z3
    B.notes.append('36 floors of dark glass with bronze mullions, two setbacks, pyramid cap + spire with red beacon.')
    return B


BUILDERS = {'CornerCafe': build_cafe, 'CornerMart': build_mart, 'CornerPharmacy': build_pharmacy,
            'CornerDiner': build_diner, 'NoodleBar': build_noodle, 'Books': build_books,
            'Laundromat': build_laundromat, 'PhoneRepair': build_phone, 'Bakery': build_bakery,
            'Florist': build_florist, 'ForLease': build_forlease, 'Bank': build_bank,
            'Tower1': build_tower1, 'Tower2': build_tower2, 'Tower3': build_tower3, 'Tower4': build_tower4}


# ------------------------------------------------------------------------------------------- export & preview

def make_material(name):
    m = bpy.data.materials.get(name)
    if m:
        return m
    col, tex, emi, note = MATS[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes.get('Principled BSDF')
    rgb = hex_rgb(col)
    bsdf.inputs['Base Color'].default_value = (*[srgb2lin(c) for c in rgb], 1.0)
    bsdf.inputs['Roughness'].default_value = 0.8
    if 'Specular IOR Level' in bsdf.inputs:
        bsdf.inputs['Specular IOR Level'].default_value = 0.25
    m.diffuse_color = (*[srgb2lin(c) for c in rgb], 1.0)
    img_node = None
    if tex:
        path = os.path.join(TEX_DIR, tex)
        if os.path.exists(path):
            img = bpy.data.images.load(path, check_existing=True)
            img_node = nt.nodes.new('ShaderNodeTexImage')
            img_node.image = img
            img_node.interpolation = 'Linear'
            if col.upper() != '#FFFFFF':
                mix = nt.nodes.new('ShaderNodeMix')
                mix.data_type = 'RGBA'
                mix.blend_type = 'MULTIPLY'
                mix.inputs['Factor'].default_value = 1.0
                nt.links.new(img_node.outputs['Color'], mix.inputs[6])
                mix.inputs[7].default_value = (*[srgb2lin(c) for c in rgb], 1.0)
                nt.links.new(mix.outputs[2], bsdf.inputs['Base Color'])
            else:
                nt.links.new(img_node.outputs['Color'], bsdf.inputs['Base Color'])
            if name == 'MB_ShopGlass':
                nt.links.new(img_node.outputs['Alpha'], bsdf.inputs['Alpha'])
                try:
                    m.surface_render_method = 'BLENDED'
                except Exception:
                    m.blend_method = 'BLEND'
    if emi:
        ecol, strength = emi
        if img_node is not None:
            nt.links.new(img_node.outputs['Color'], bsdf.inputs['Emission Color'])
        else:
            bsdf.inputs['Emission Color'].default_value = (*[srgb2lin(c) for c in hex_rgb(ecol)], 1.0)
        bsdf.inputs['Emission Strength'].default_value = strength
    return m


def mesh_from_builder(mbld, name):
    bm = mbld.bm
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=2e-4)
    ngons = [f for f in bm.faces if len(f.verts) > 4]
    if ngons:
        bmesh.ops.triangulate(bm, faces=ngons, quad_method='BEAUTY', ngon_method='EAR_CLIP')
    bad = [f for f in bm.faces if f.calc_area() < 1e-9]
    if bad:
        bmesh.ops.delete(bm, geom=bad, context='FACES')
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    for mname in mbld.mats:
        me.materials.append(make_material(mname))
    for p in me.polygons:
        p.use_smooth = True
    me.set_sharp_from_angle(angle=math.radians(35))
    me.update()
    return me


def inside(p, poly):
    c = False
    for i in range(len(poly)):
        a, b = poly[i], poly[i - 1]
        if (a.y > p.y) != (b.y > p.y) and p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y + 1e-12) + a.x:
            c = not c
    return c


def check_rooms(B):
    """Fake-interior rooms must not intersect (walls would show through glass): sample each room's plan."""
    bad = []
    for i, a in enumerate(B.rooms):
        xs, ys = [p.x for p in a], [p.y for p in a]
        pts = [Vector((min(xs) + (max(xs) - min(xs)) * (ix + 0.5) / 24, min(ys) + (max(ys) - min(ys)) * (iy + 0.5) / 24,
                       0)) for ix in range(24) for iy in range(24)]
        pts = [p for p in pts if inside(p, a)]
        for j, b in enumerate(B.rooms):
            if j > i:
                n = sum(1 for p in pts if inside(p, b))
                if n > 0.01 * max(1, len(pts)):
                    bad.append((i, j, n))
    print('[check] %-15s rooms=%d overlaps=%s' % (B.name, len(B.rooms), bad or 'none'))
    return not bad


def tri_count(me):
    return sum(len(p.vertices) - 2 for p in me.polygons)


def build_objects(B):
    root = bpy.data.objects.new(B.name, None)
    root.empty_display_type = 'PLAIN_AXES'
    bpy.context.scene.collection.objects.link(root)
    me = mesh_from_builder(B.mb, B.name + '_Mesh')
    ob = bpy.data.objects.new(B.name + '_Mesh', me)
    bpy.context.scene.collection.objects.link(ob)
    ob.parent = root
    objs = [root, ob]
    tris = tri_count(me)
    glass_tris = 0
    if B.glass.bm.faces:
        gm = mesh_from_builder(B.glass, B.name + '_Glass')
        go = bpy.data.objects.new(B.name + '_Glass', gm)
        bpy.context.scene.collection.objects.link(go)
        go.parent = root
        objs.append(go)
        glass_tris = tri_count(gm)
    for nm, p in B.locators:
        e = bpy.data.objects.new(nm, None)
        e.empty_display_type = 'PLAIN_AXES'
        e.empty_display_size = 0.3
        e.location = p
        bpy.context.scene.collection.objects.link(e)
        e.parent = root
        objs.append(e)
    return root, ob, objs, tris, glass_tris, me


def export_fbx(B, objs):
    os.makedirs(os.path.join(OUT, 'fbx'), exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    path = os.path.join(OUT, 'fbx', B.name + '.fbx')
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH', 'EMPTY'}, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             mesh_smooth_type='FACE', use_mesh_modifiers=True, add_leaf_bones=False,
                             path_mode='STRIP')
    return path


def setup_preview_scene():
    scn = bpy.context.scene
    scn.render.engine = 'BLENDER_EEVEE'
    scn.render.resolution_x, scn.render.resolution_y = 1280, 720
    scn.render.image_settings.file_format = 'JPEG'
    scn.render.image_settings.quality = 88
    try:
        scn.eevee.taa_render_samples = 32
    except Exception:
        pass
    scn.view_settings.view_transform = 'Standard'
    scn.view_settings.look = 'None'
    scn.view_settings.exposure = 0.0
    world = bpy.data.worlds.new('W')
    world.use_nodes = True
    bg = world.node_tree.nodes['Background']
    bg.inputs['Color'].default_value = (0.62, 0.68, 0.76, 1)
    bg.inputs['Strength'].default_value = 0.85
    scn.world = world
    sun = bpy.data.objects.new('Sun', bpy.data.lights.new('Sun', 'SUN'))
    sun.data.energy = 3.2
    sun.data.angle = math.radians(2)
    scn.collection.objects.link(sun)
    gmat = bpy.data.materials.new('PreviewGround')
    gmat.diffuse_color = (0.5, 0.48, 0.45, 1)
    gmat.use_nodes = True
    gmat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.47, 0.45, 0.41, 1)
    road = bpy.data.materials.new('PreviewRoad')
    road.use_nodes = True
    road.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.045, 0.05, 0.06, 1)
    cam = bpy.data.objects.new('Cam', bpy.data.cameras.new('Cam'))
    scn.collection.objects.link(cam)
    scn.camera = cam
    return sun, cam, gmat, road


def ground(B, gmat, road):
    """Sidewalk slab around the street facades + asphalt beyond, slightly below z=0 (preview only)."""
    objs = []
    minx = min(p.x for p in B.fp) - 30
    maxx = max(p.x for p in B.fp) + 30
    miny = min(p.y for p in B.fp) - 30
    maxy = max(p.y for p in B.fp) + 30
    for nm, z, x0, x1, y0, y1, m in (('PrevRoad', -0.17, minx, maxx, miny, maxy, road),):
        me = bpy.data.meshes.new(nm)
        bm = bmesh.new()
        vs = [bm.verts.new((x0, y0, z)), bm.verts.new((x1, y0, z)), bm.verts.new((x1, y1, z)), bm.verts.new((x0, y1, z))]
        bm.faces.new(vs)
        bm.to_mesh(me)
        me.materials.append(m)
        o = bpy.data.objects.new(nm, me)
        bpy.context.scene.collection.objects.link(o)
        objs.append(o)
    # sidewalk band (4.8 m) along street facades
    path, _ = B.street_path() if B.kind != 'tower' else ([], 0)
    if path:
        bm = bmesh.new()
        me = bpy.data.meshes.new('PrevWalk')
        sweep_mb = MeshBuilder()
        sweep(sweep_mb, path, [(-0.5, -0.0015), (4.8, -0.0015)], 'MB_Concrete', caps=False)
        sweep(sweep_mb, path, [(4.8, -0.0015), (4.8, -0.17)], 'MB_Concrete', caps=False)
        sweep_mb.bm.to_mesh(me)
        me.materials.append(gmat)
        o = bpy.data.objects.new('PrevWalk', me)
        bpy.context.scene.collection.objects.link(o)
        objs.append(o)
    return objs


def look(cam, loc, target, fov_v=60.0):
    cam.location = Vector(loc)
    d = Vector(target) - cam.location
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    cam.data.sensor_fit = 'VERTICAL'
    cam.data.angle = math.radians(fov_v)
    cam.data.clip_end = 2000


def render(path):
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def preview(B, views):
    sun, cam, gmat, road = setup_preview_scene()
    ground(B, gmat, road)
    os.makedirs(os.path.join(OUT, 'previews'), exist_ok=True)
    outs = []
    for tag, loc, target, fov, sun_rot in views:
        sun.rotation_euler = sun_rot
        look(cam, loc, target, fov)
        p = os.path.join(OUT, 'previews', '%s_%s.jpg' % (B.name, tag))
        render(p)
        outs.append(p)
    return outs


def sun_from(az_deg, el_deg):
    """Sun rotation for light coming FROM azimuth (deg, 0 = +Y, 90 = +X) at elevation."""
    az, el = math.radians(az_deg), math.radians(el_deg)
    dirv = -Vector((math.sin(az) * math.cos(el), math.cos(az) * math.cos(el), math.sin(el)))
    return dirv.to_track_quat('-Z', 'Y').to_euler()


def default_views(B):
    H = B.top + 4.0 if B.kind != 'tower' else B.top
    if B.kind == 'corner':
        sx = -1 if B.side == 'left' else 1      # side facade direction
        W, D = B.footprint_wd
        cx = (W / 2) * (-sx)
        tgt = Vector((cx * 0.6, D * 0.3, H * 0.42))
        az = 222 if B.side == 'left' else 138
        dist = max(H * 1.55, 34.0)
        three = (tgt + Vector((sx * 0.62, -0.78, 0.0)) * dist + Vector((0, 0, H * 0.32)), tgt, 42, sun_from(az, 38))
        street = (Vector((sx * 6.5, -7.5, 1.56)), Vector((cx * 0.15, 2.0, 4.2)), 60, sun_from(az, 32))
        return [('34', *three), ('street', *street)]
    W, D = B.footprint_wd
    if B.kind == 'tower':
        hm = H + 12
        return [('34', Vector((-1.1 * hm, -1.5 * hm, hm * 0.75)), Vector((0, D / 2, hm * 0.45)), 40, sun_from(215, 35)),
                ('street', Vector((-60.0, -200.0, 1.56)), Vector((0, D / 2, hm * 0.35)), 60, sun_from(215, 25))]
    return [('34', Vector((-14, -24, H * 0.7)), Vector((0, D * 0.3, H * 0.42)), 42, sun_from(200, 38)),
            ('street', Vector((-5.5, -8.5, 1.56)), Vector((1.5, 0, 3.8)), 60, sun_from(200, 32))]


# ---------------------------------------------------------------------------------------------- manifest

def manifest_path():
    return os.path.join(OUT, 'manifest.json')


def load_manifest():
    p = manifest_path()
    if os.path.exists(p):
        with open(p) as fh:
            return json.load(fh)
    return {'kit': 'buildings', 'materials': {}, 'models': {}, 'draft_copy': []}


def update_manifest(B, tris, glass_tris, mesh, previews):
    m = load_manifest()
    m['kit'] = 'buildings'
    m['conventions'] = {
        'units': 'metres; z=0 (Unity y=0) is sidewalk level; front facade faces Blender -Y = Unity +Z',
        'origin': 'corner buildings: exterior corner where the front and side facade lines meet (virtual corner for '
                  'chamfered/rounded corners); mid-block: centre of the front facade base line, body toward +Y '
                  '(Unity -Z); towers: same as mid-block (front facade centre, base)',
        'material_colors': 'color multiplies the texture (white = use texture as-is); emission uses the texture as '
                           'emission map when a texture is set',
        'uv': 'tileable surfaces 1 UV unit = 2 m; signs, windows, interiors use atlas UVs (see atlas_layout.json)',
        'children': '<Name>_Mesh (opaque), optional <Name>_Glass (transparent shop glass), <Name>_LOC_* locators '
                    '(empties: shop interior light centres, wall lamps, entrances)',
    }
    used = set(mesh.materials.keys())
    for nm in sorted(set(B.mb.mats) | set(B.glass.mats)):
        col, tex, emi, note = MATS[nm]
        m['materials'][nm] = {'color': col, 'texture': tex,
                              'emission': ({'color': emi[0], 'intensity': emi[1]} if emi else None), 'note': note}
        m['materials'][nm]['transparent'] = nm == 'MB_ShopGlass'
    xs = [v.co.x for v in mesh.vertices]
    ys = [v.co.y for v in mesh.vertices]
    zs = [v.co.z for v in mesh.vertices]
    W, D = B.footprint_wd
    m['models'][B.name] = {
        'file': 'fbx/%s.fbx' % B.name, 'tris': tris, 'glass_tris': glass_tris,
        'footprint': {'w': W, 'd': D}, 'height': round(B.top + 0.12, 2), 'height_max': round(max(zs), 2),
        'floors': B.floors, 'side_facade': B.side,
        'bounds_blender': {'min': [round(min(xs), 2), round(min(ys), 2), round(min(zs), 2)],
                           'max': [round(max(xs), 2), round(max(ys), 2), round(max(zs), 2)]},
        'materials': sorted(set(B.mb.mats) | set(B.glass.mats)),
        'locators': [nm for nm, _ in B.locators],
        'previews': [os.path.relpath(p, OUT) for p in previews],
        'notes': ' '.join(B.notes),
    }
    m['draft_copy'] = [d for d in m.get('draft_copy', []) if d.get('model') != B.name]
    for where, text in B.copy:
        m['draft_copy'].append({'model': B.name, 'where': where, 'text': text})
    m['models'] = dict(sorted(m['models'].items()))
    m['materials'] = dict(sorted(m['materials'].items()))
    with open(manifest_path(), 'w') as fh:
        json.dump(m, fh, indent=1)


# ------------------------------------------------------------------------------------------------- verify

def verify():
    man = load_manifest()
    ok = True
    for name, info in man['models'].items():
        bpy.ops.wm.read_factory_settings(use_empty=True)
        path = os.path.join(OUT, info['file'])
        bpy.ops.import_scene.fbx(filepath=path)
        roots = [o for o in bpy.data.objects if o.parent is None]
        meshes = [o for o in bpy.data.objects if o.type == 'MESH']
        tris = 0
        lo = Vector((1e9, 1e9, 1e9))
        hi = -lo
        for o in meshes:
            if o.name.endswith('_Glass'):
                continue
            me = o.data
            tris += sum(len(p.vertices) - 2 for p in me.polygons)
            for v in me.vertices:
                w = o.matrix_world @ v.co
                lo = Vector((min(lo.x, w.x), min(lo.y, w.y), min(lo.z, w.z)))
                hi = Vector((max(hi.x, w.x), max(hi.y, w.y), max(hi.z, w.z)))
        root = roots[0] if roots else None
        exp_lo, exp_hi = Vector(info['bounds_blender']['min']), Vector(info['bounds_blender']['max'])
        good = (len(roots) == 1 and root.name == name and abs(tris - info['tris']) <= 2 and
                (lo - exp_lo).length < 0.05 and (hi - exp_hi).length < 0.05 and
                root.matrix_world.translation.length < 1e-4)
        ok &= good
        print('[verify] %-15s %s root=%s scale=%s tris=%d bounds=%s..%s children=%d' % (
            name, 'OK  ' if good else 'FAIL', root.name if root else None,
            tuple(round(s, 3) for s in root.scale) if root else None, tris,
            tuple(round(v, 2) for v in lo), tuple(round(v, 2) for v in hi), len(root.children) if root else 0))
    print('[verify] ALL OK' if ok else '[verify] FAILURES')


# --------------------------------------------------------------------------------------------------- main

def main():
    if VERIFY:
        verify()
        return
    names = [n for n in BUILDERS if ONLY is None or n in ONLY]
    for name in names:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        B = BUILDERS[name]()
        B.finish()
        check_rooms(B)
        root, ob, objs, tris, glass_tris, me = build_objects(B)
        path = export_fbx(B, objs)
        previews = []
        if not NO_PREVIEW:
            previews = preview(B, default_views(B) + getattr(B, 'extra_views', []))
        update_manifest(B, tris, glass_tris, me, previews)
        print('[buildings] %-15s tris=%d glass=%d mats=%d -> %s' % (name, tris, glass_tris, len(me.materials), path))


main()
