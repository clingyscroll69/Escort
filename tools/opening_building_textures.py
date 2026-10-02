#!/usr/bin/env python3
"""Procedural textures for the opening's city buildings (kit "buildings"). PIL + numpy only: no downloads, no AI imagery.

Writes <out_dir>/textures/T_*.png (sRGB PNG, <= 1024 px) and <out_dir>/atlas_layout.json: the pixel rect of every atlas
slot plus the physical size (metres) each sign was drawn for. tools/blender/opening_buildings.py reads that layout to
UV-map signs, window variants and fake shop interiors, so run this script first.

Usage:  /opt/anaconda3/bin/python3 tools/opening_building_textures.py build_art/opening/buildings
Deterministic: every random draw comes from a fixed seed.
"""
import json
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

TEX = None          # <out_dir>/textures
LAYOUT = {}         # texture file -> {"size": [w, h], "slots": {name: {"rect": [x, y, w, h], ...}}}

SUP = '/System/Library/Fonts/Supplemental/'
SYS = '/System/Library/Fonts/'
FONTS = {
    'futura': (SUP + 'Futura.ttc', 0), 'futura_bold': (SUP + 'Futura.ttc', 2), 'futura_cond': (SUP + 'Futura.ttc', 4),
    'optima_bold': (SYS + 'Optima.ttc', 1), 'optima_black': (SYS + 'Optima.ttc', 4),
    'didot_bold': (SUP + 'Didot.ttc', 2), 'copper': (SUP + 'Copperplate.ttc', 2),
    'avenir_heavy': (SYS + 'Avenir Next Condensed.ttc', 8), 'avenir_demi': (SYS + 'Avenir Next Condensed.ttc', 2),
    'helv_black': (SYS + 'HelveticaNeue.ttc', 9), 'helv_bold': (SYS + 'HelveticaNeue.ttc', 1),
    'helv_med': (SYS + 'HelveticaNeue.ttc', 10), 'helv_cond': (SYS + 'HelveticaNeue.ttc', 4),
    'gill_ultra': (SUP + 'GillSans.ttc', 6), 'gill_bold': (SUP + 'GillSans.ttc', 1), 'gill': (SUP + 'GillSans.ttc', 0),
    'rock_bold': (SUP + 'Rockwell.ttc', 2), 'clarendon': (SUP + 'SuperClarendon.ttc', 7),
    'clarendon_bold': (SUP + 'SuperClarendon.ttc', 5),
    'script': (SUP + 'SignPainter.ttc', 1), 'snell': (SUP + 'SnellRoundhand.ttc', 2),
    'chalk': (SUP + 'Chalkduster.ttf', 0), 'arial_black': (SUP + 'Arial Black.ttf', 0),
    'arial_bold': (SUP + 'Arial Bold.ttf', 0), 'arial_round': (SUP + 'Arial Rounded Bold.ttf', 0),
    'georgia_bold': (SUP + 'Georgia Bold.ttf', 0), 'georgia_it': (SUP + 'Georgia Italic.ttf', 0),
    'impact': (SUP + 'Impact.ttf', 0), 'bask_bold': (SUP + 'Baskerville.ttc', 1),
    'din_cond': (SUP + 'DIN Condensed Bold.ttf', 0), 'din_alt': (SUP + 'DIN Alternate Bold.ttf', 0),
    'typewriter': (SUP + 'AmericanTypewriter.ttc', 2), 'phosphate': (SUP + 'Phosphate.ttc', 1),
    'pt_narrow': (SUP + 'PTSans.ttc', 2), 'seravek_bold': (SUP + 'Seravek.ttc', 9),
}
_FC = {}


def font(key, px):
    px = max(4, int(round(px)))
    k = (key, px)
    if k not in _FC:
        path, idx = FONTS[key]
        _FC[k] = ImageFont.truetype(path, px, index=idx)
    return _FC[k]


# ----------------------------------------------------------------------------------------------------------- colour

def C(h, a=255):
    h = h.lstrip('#')
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), a)


def mix(c1, c2, t):
    return tuple(int(round(a + (b - a) * t)) for a, b in zip(c1, c2))


def shade(c, f, a=None):
    out = [max(0, min(255, int(round(v * f)))) for v in c[:3]]
    return tuple(out) + ((a if a is not None else (c[3] if len(c) > 3 else 255)),)


def alpha(c, a):
    return tuple(c[:3]) + (int(a),)


def f3(h):
    return np.array(C(h)[:3], dtype=np.float64) / 255.0


# ------------------------------------------------------------------------------------------------------ numpy bits

def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def tile_noise(n, scale, octaves=3, seed=0, m=None):
    """Tileable value noise (FFT-filtered white noise), range 0..1. n = width, m = height (default n)."""
    m = m or n
    rng = np.random.default_rng(seed)
    acc = np.zeros((m, n))
    amp = 1.0
    fy = np.fft.fftfreq(m)[:, None] * m
    fx = np.fft.fftfreq(n)[None, :] * n
    r = np.sqrt(fx ** 2 + fy ** 2)
    for o in range(octaves):
        f = scale * (2 ** o)
        white = rng.standard_normal((m, n))
        nn = np.real(np.fft.ifft2(np.fft.fft2(white) * np.exp(-(r / f) ** 2)))
        nn = (nn - nn.mean()) / (nn.std() + 1e-9)
        acc += nn * amp
        amp *= 0.5
    return (acc - acc.min()) / (acc.max() - acc.min() + 1e-9)


def arr2img(a):
    a = np.clip(a, 0, 1)
    if a.ndim == 2:
        a = np.repeat(a[..., None], 3, axis=2)
    return Image.fromarray((a * 255 + 0.5).astype(np.uint8))


def save(img, name, keep_alpha=False):
    if not keep_alpha and img.mode != 'RGB':
        img = img.convert('RGB')
    img.save(os.path.join(TEX, name))
    print('  wrote', name, img.size)


def register(tex, size, slots, **meta):
    LAYOUT[tex] = {'size': list(size), 'slots': slots, **meta}


# --------------------------------------------------------------------------------------------- drawing canvas (2x)

class Canvas:
    """Metre-space drawing: x right, y UP from the bottom edge. Draws supersampled, `result()` downsamples."""

    def __init__(self, w_px, h_px, w_m, h_m, bg=(0, 0, 0, 255), ss=2):
        self.out = (w_px, h_px)
        self.ss = ss
        self.W, self.H = w_px * ss, h_px * ss
        self.sx, self.sy = self.W / w_m, self.H / h_m
        self.w_m, self.h_m = w_m, h_m
        self.img = Image.new('RGBA', (self.W, self.H), bg)
        self.d = ImageDraw.Draw(self.img, 'RGBA')

    def X(self, x):
        return x * self.sx

    def Y(self, y):
        return self.H - y * self.sy

    def box(self, x0, y0, x1, y1):
        return [self.X(min(x0, x1)), self.Y(max(y0, y1)), self.X(max(x0, x1)), self.Y(min(y0, y1))]

    def _paint(self, fn, *cols):
        """ImageDraw does not blend on RGBA images, so translucent colours go through an overlay."""
        if any(c is not None and len(c) == 4 and c[3] < 255 for c in cols):
            ov = Image.new('RGBA', self.img.size, (0, 0, 0, 0))
            fn(ImageDraw.Draw(ov))
            self.img.alpha_composite(ov)
        else:
            fn(self.d)

    def rect(self, x0, y0, x1, y1, fill, outline=None, w=0.0):
        b = self.box(x0, y0, x1, y1)
        self._paint(lambda d: d.rectangle(b, fill=fill, outline=outline,
                                          width=max(1, int(w * self.sx)) if outline else 0), fill, outline)

    def rrect(self, x0, y0, x1, y1, r, fill, outline=None, w=0.0):
        b = self.box(x0, y0, x1, y1)
        self._paint(lambda d: d.rounded_rectangle(b, radius=r * self.sx, fill=fill, outline=outline,
                                                  width=max(1, int(w * self.sx)) if outline else 0), fill, outline)

    def ellipse(self, cx, cy, rx, ry, fill, outline=None, w=0.0):
        b = self.box(cx - rx, cy - ry, cx + rx, cy + ry)
        self._paint(lambda d: d.ellipse(b, fill=fill, outline=outline,
                                        width=max(1, int(w * self.sx)) if outline else 0), fill, outline)

    def poly(self, pts, fill, outline=None):
        P = [(self.X(x), self.Y(y)) for x, y in pts]
        self._paint(lambda d: d.polygon(P, fill=fill, outline=outline), fill, outline)

    def line(self, pts, fill, w):
        P = [(self.X(x), self.Y(y)) for x, y in pts]
        self._paint(lambda d: d.line(P, fill=fill, width=max(1, int(round(w * self.sx)))), fill)

    def text(self, x, y, s, key, size_m, fill, anchor='mm', spacing=0.0, stroke=0.0, stroke_fill=None):
        f = font(key, size_m * self.sy)
        if len(fill) == 4 and fill[3] < 255:
            ov = Canvas(*self.out, self.w_m, self.h_m, bg=(0, 0, 0, 0), ss=self.ss)
            ov.text(x, y, s, key, size_m, alpha(fill, 255), anchor, spacing, stroke, stroke_fill)
            a = ov.img.getchannel('A').point(lambda v: v * fill[3] // 255)
            ov.img.putalpha(a)
            self.img.alpha_composite(ov.img)
            return
        if spacing <= 0:
            self.d.text((self.X(x), self.Y(y)), s, font=f, fill=fill, anchor=anchor,
                        stroke_width=int(stroke * self.sy), stroke_fill=stroke_fill)
            return
        # letter-spaced: measure total width, then place glyph by glyph
        sp = spacing * self.sx
        widths = [f.getlength(ch) for ch in s]
        total = sum(widths) + sp * (len(s) - 1)
        if anchor[0] == 'm':
            cx = self.X(x) - total / 2
        elif anchor[0] == 'r':
            cx = self.X(x) - total
        else:
            cx = self.X(x)
        for ch, wd in zip(s, widths):
            self.d.text((cx, self.Y(y)), ch, font=f, fill=fill, anchor='l' + anchor[1],
                        stroke_width=int(stroke * self.sy), stroke_fill=stroke_fill)
            cx += wd + sp

    def text_width_m(self, s, key, size_m, spacing=0.0):
        f = font(key, size_m * self.sy)
        return (sum(f.getlength(ch) for ch in s) + spacing * self.sx * (len(s) - 1)) / self.sx

    def fit_text(self, x, y, s, key, max_w, max_h, fill, anchor='mm', spacing=0.0, stroke=0.0, stroke_fill=None):
        size = max_h
        for _ in range(40):
            if self.text_width_m(s, key, size, spacing) <= max_w:
                break
            size *= 0.95
        self.text(x, y, s, key, size, fill, anchor, spacing, stroke, stroke_fill)
        return size

    def vgrad(self, x0, y0, x1, y1, c_bot, c_top):
        bx = [int(v) for v in self.box(x0, y0, x1, y1)]
        w, h = max(1, bx[2] - bx[0]), max(1, bx[3] - bx[1])
        t = np.linspace(1, 0, h)[:, None, None]
        a = np.array(c_bot, dtype=np.float64) * (1 - t) + np.array(c_top, dtype=np.float64) * t
        a = np.repeat(a, w, axis=1)
        layer = Image.fromarray(a.clip(0, 255).astype(np.uint8))
        self.img.alpha_composite(layer, (bx[0], bx[1]))

    def hgrad(self, x0, y0, x1, y1, c_left, c_right):
        bx = [int(v) for v in self.box(x0, y0, x1, y1)]
        w, h = max(1, bx[2] - bx[0]), max(1, bx[3] - bx[1])
        t = np.linspace(0, 1, w)[None, :, None]
        a = np.array(c_left, dtype=np.float64) * (1 - t) + np.array(c_right, dtype=np.float64) * t
        a = np.repeat(a, h, axis=0)
        layer = Image.fromarray(a.clip(0, 255).astype(np.uint8))
        self.img.alpha_composite(layer, (bx[0], bx[1]))

    def glow(self, x, y, r, color, strength=0.8, power=2.0):
        """Soft radial light: blends `color` with alpha falling off over radius r (metres)."""
        R = int(r * self.sx)
        if R < 2:
            return
        cx, cy = int(self.X(x)), int(self.Y(y))
        yy, xx = np.mgrid[-R:R + 1, -R:R + 1]
        d = np.sqrt(xx ** 2 + (yy * self.sx / self.sy) ** 2) / R
        a = np.clip(1 - d, 0, 1) ** power * strength
        layer = np.zeros((2 * R + 1, 2 * R + 1, 4))
        layer[..., :3] = color[:3]
        layer[..., 3] = a * 255
        self.img.alpha_composite(Image.fromarray(layer.astype(np.uint8)), (cx - R, cy - R))

    def sheen(self, strength=1.0, seed=0):
        """Diagonal glass reflection streaks (painted interiors are seen through shop glass)."""
        rng = np.random.default_rng(seed)
        layer = Image.new('RGBA', (self.W, self.H), (0, 0, 0, 0))
        dl = ImageDraw.Draw(layer)
        x = rng.uniform(0.0, 0.3) * self.W
        while x < self.W * 1.3:
            w = rng.uniform(0.04, 0.12) * self.W
            a = int(rng.uniform(10, 26) * strength)
            sk = 0.45 * self.H
            dl.polygon([(x, 0), (x + w, 0), (x + w - sk, self.H), (x - sk, self.H)], fill=(255, 255, 255, a))
            x += w + rng.uniform(0.15, 0.45) * self.W
        layer = layer.filter(ImageFilter.GaussianBlur(3 * self.ss))
        self.img.alpha_composite(layer)

    def result(self):
        return self.img.resize(self.out, Image.LANCZOS)


# ------------------------------------------------------------------------------------------- tileable surfaces

def masonry(name, base, mortar, courses, per_row, joint=5.0, var=0.07, dark_frac=0.0, light_frac=0.0, bond=0.5,
            bevel=0.10, grain=0.05, wear=0.0, seed=1, n=1024, hue_var=0.03, joint_noise=0.08):
    """Running/stack-bond masonry, tileable at n px = 2 m."""
    rng = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float64) + 0.5
    ch, bw = n / courses, n / per_row
    row = (np.floor(yy / ch).astype(int)) % courses
    fy = yy - np.floor(yy / ch) * ch
    bxf = xx / bw + (row % 2) * bond
    col = np.floor(bxf).astype(int) % per_row
    fx = (bxf - np.floor(bxf)) * bw
    mask = smoothstep(joint - 0.8, joint + 0.8, fy) * smoothstep(joint - 0.8, joint + 0.8, fx)
    T = rng.normal(0, 1, (courses, per_row, 3))
    f = 1 + var * T[row, col, 0]
    if dark_frac:
        f *= np.where(rng.random((courses, per_row))[row, col] < dark_frac, 0.80, 1.0)
    if light_frac:
        f *= np.where(rng.random((courses, per_row))[row, col] < light_frac, 1.10, 1.0)
    b = f3(base)
    unit = b[None, None, :] * f[..., None]
    unit[..., 0] *= 1 + hue_var * T[row, col, 1]
    unit[..., 2] *= 1 - hue_var * T[row, col, 1]
    g = tile_noise(n, 110, 2, seed + 7) - 0.5
    unit *= (1 + grain * 2 * g)[..., None]
    top = np.exp(-np.maximum(fy - joint, 0) / (0.06 * ch + 1.5)) * (fy >= joint)
    bot = np.exp(-np.maximum(ch - fy, 0) / (0.06 * ch + 1.5))
    left = np.exp(-np.maximum(fx - joint, 0) / 3.0) * (fx >= joint)
    unit *= (1 + bevel * top - bevel * 1.3 * bot + bevel * 0.4 * left)[..., None]
    m = f3(mortar)
    mort = m[None, None, :] * (1 + joint_noise * (tile_noise(n, 60, 2, seed + 11) - 0.5) * 2)[..., None]
    out = unit * mask[..., None] + mort * (1 - mask[..., None])
    if wear:
        out *= (1 + wear * (tile_noise(n, 3, 3, seed + 3) - 0.5) * 2)[..., None]
    save(arr2img(out), name)
    register(name, (n, n), {}, tile_m=2.0)


def roof_texture():
    n = 512
    base = f3('#5C5E61')
    nz = tile_noise(n, 60, 3, 21)
    speck = tile_noise(n, 200, 1, 22)
    out = base[None, None, :] * (0.86 + 0.28 * nz[..., None])
    out = np.where((speck > 0.72)[..., None], out * 1.18, out)
    out = np.where((speck < 0.22)[..., None], out * 0.84, out)
    # roll-roofing seams every 1 m (2 per tile)
    yy = np.arange(n)[:, None]
    seam = (np.abs((yy % (n // 2)) - 2) < 2)
    out = np.where(seam[..., None], out * 0.78, out)
    save(arr2img(out), 'T_Roof.png')
    register('T_Roof.png', (n, n), {}, tile_m=2.0)


def wood_staves():
    n = 512
    rng = np.random.default_rng(31)
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float64)
    staves = 10
    sw = n / staves
    k = np.floor(xx / sw).astype(int)
    fx = xx - k * sw
    tone = rng.normal(0, 1, staves)[k] * 0.06
    grain = tile_noise(n, 8, 2, 33)  # stretched later
    g2 = np.array(Image.fromarray((grain * 255).astype(np.uint8)).resize((n, n)))  # keep
    streak = tile_noise(n, 40, 2, 34)
    base = f3('#8B6E52')
    out = base[None, None, :] * (1 + tone[..., None]) * (0.88 + 0.22 * streak[..., None])
    gap = smoothstep(2.5, 4.0, fx) * smoothstep(2.5, 4.0, sw - fx)
    out *= (0.55 + 0.45 * gap)[..., None]
    weather = tile_noise(n, 3, 2, 35)
    out = out * (0.92 + 0.12 * weather[..., None])
    save(arr2img(out), 'T_WoodStaves.png')
    register('T_WoodStaves.png', (n, n), {}, tile_m=2.0)


def shutter():
    n = 512
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float64)
    slats = 28
    sh = n / slats
    fy = (yy % sh) / sh
    prof = 0.82 + 0.28 * np.sin(fy * math.pi) - 0.25 * smoothstep(0.85, 1.0, fy)
    base = f3('#9298A0')
    streak = tile_noise(n, 30, 2, 41)
    dirt = tile_noise(n, 4, 2, 42)
    out = base[None, None, :] * prof[..., None] * (0.93 + 0.1 * streak[..., None]) * (0.9 + 0.12 * dirt[..., None])
    save(arr2img(out), 'T_Shutter.png')
    register('T_Shutter.png', (n, n), {}, tile_m=2.0)


def grate():
    n = 256
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float64)
    bars = ((xx % 16) < 4) | ((yy % 64) < 6)
    out = np.where(bars[..., None], f3('#3A3E43')[None, None, :], f3('#16181B')[None, None, :])
    save(arr2img(out), 'T_Grate.png')
    register('T_Grate.png', (n, n), {}, tile_m=2.0)


def awnings():
    """4 rows of 256 px, each tileable horizontally: u = metres / 2 along the awning, v within the row."""
    n = 1024
    rows = [('solid_green', '#2F6B4F', None), ('stripe_red', '#B8372F', '#F1E7D4'),
            ('stripe_green', '#2F6B4F', '#EFE6D2'), ('stripe_navy', '#2C3E5C', '#F2EEE6')]
    img = np.zeros((n, n, 3))
    weave = tile_noise(n, 300, 1, 51, m=256)
    folds = tile_noise(n, 6, 1, 52, m=256)
    slots = {}
    xx = np.arange(n)[None, :].astype(np.float64)
    for i, (nm, c1, c2) in enumerate(rows):
        a = np.repeat(f3(c1)[None, None, :], 256, 0).repeat(n, 1)
        if c2:
            period = n / 8.0  # 25 cm stripes
            t = (xx % period) / period
            m = smoothstep(0.49, 0.51, t) * (1 - smoothstep(0.98, 1.0, t)) + (1 - smoothstep(0.0, 0.02, t)) * 0
            b = np.repeat(f3(c2)[None, None, :], 256, 0).repeat(n, 1)
            a = a * (1 - m[..., None]) + b * m[..., None]
        a *= (0.95 + 0.06 * weave[..., None]) * (0.95 + 0.08 * folds[..., None])
        img[i * 256:(i + 1) * 256] = a
        slots[nm] = {'rect': [0, i * 256 + 4, n, 248]}
    save(arr2img(img), 'T_Awnings.png')
    register('T_Awnings.png', (n, n), slots, tile_m=2.0, note='rows tile horizontally; u = metres/2')


def foliage():
    n = 512
    rng = np.random.default_rng(61)
    img = Image.new('RGBA', (n, n), C('#3E5F2E'))
    d = ImageDraw.Draw(img, 'RGBA')
    greens = ['#4C7A36', '#5B8C3E', '#3F6A2E', '#6E9E48', '#557F3A', '#2F5124']

    def blob(x, y, rx, ry, col):
        for ox in (-n, 0, n):
            for oy in (-n, 0, n):
                d.ellipse([x - rx + ox, y - ry + oy, x + rx + ox, y + ry + oy], fill=col)

    for _ in range(900):
        x, y = rng.uniform(0, n, 2)
        r = rng.uniform(6, 16)
        blob(x, y, r, r * rng.uniform(0.5, 0.9), C(greens[rng.integers(len(greens))]))
    flowers = ['#E8436B', '#F2C230', '#F5F0E6', '#C8417F', '#E86A2C', '#9B5FC0', '#F07C9A']
    for _ in range(420):
        x, y = rng.uniform(0, n, 2)
        r = rng.uniform(3.5, 7)
        col = C(flowers[rng.integers(len(flowers))])
        blob(x, y, r, r, col)
        blob(x, y, r * 0.35, r * 0.35, C('#F7E3A0'))
    save(img, 'T_Foliage.png')
    register('T_Foliage.png', (n, n), {}, tile_m=1.0)


# -------------------------------------------------------------------------------------------- window atlases

WIN_CELLS = [(c * 256, r * 341, 256, 341) for r in range(3) for c in range(4)]


def reflection_layer(cv, rng, strength=1.0):
    """Sky + street reflection painted in normalised (0..1) cell space."""
    top, mid, bot = C('#B3C3D0'), C('#73838F'), C('#2B323A')
    cv.vgrad(0, 0.5, 1, 1, mid, top)
    cv.vgrad(0, 0, 1, 0.5, bot, mid)
    # reflected building across the street
    x = rng.uniform(-0.3, 0.1)
    while x < 1:
        w = rng.uniform(0.35, 0.7)
        h = rng.uniform(0.3, 0.62)
        col = mix(C('#3B444D'), C('#6B5A52'), rng.uniform(0, 1))
        cv.rect(x, 0, x + w, h, alpha(col, 255))
        for wy in np.arange(0.06, h - 0.06, 0.13):
            for wx in np.arange(x + 0.05, x + w - 0.05, 0.12):
                cv.rect(wx, wy, wx + 0.06, wy + 0.08, alpha(mix(col, C('#9DB0BF'), 0.35), 255))
        x += w + rng.uniform(0.0, 0.1)


def sheen_cell(cv, rng, a=40):
    ov = Canvas(*cv.out, 1.0, 1.0, bg=(0, 0, 0, 0), ss=cv.ss)
    x = rng.uniform(0.2, 0.9)
    w = rng.uniform(0.12, 0.22)
    ov.poly([(x, 1), (x + w, 1), (x + w - 0.6, 0), (x - 0.6, 0)], (255, 255, 255, a))
    ov.poly([(x + w + 0.07, 1), (x + w + 0.11, 1), (x + w - 0.49, 0), (x + w - 0.53, 0)], (255, 255, 255, int(a * 0.7)))
    cv.img.alpha_composite(ov.img.filter(ImageFilter.GaussianBlur(6 * cv.ss)))


def interior_dark(cv, rng, kind):
    wall = mix(C('#20262D'), C('#3A3430'), rng.uniform(0, 1))
    cv.rect(0, 0, 1, 1, wall)
    cv.vgrad(0, 0.75, 1, 1, wall, shade(wall, 0.7))
    if kind == 'blinds_top':
        cv.rect(0, 0.62, 1, 1, C('#CFC4AE'))
        for y in np.arange(0.62, 1.0, 0.035):
            cv.rect(0, y, 1, y + 0.008, C('#A39780'))
        cv.rect(0, 0.6, 1, 0.625, C('#8E846F'))
    elif kind == 'blinds_full':
        cv.rect(0, 0, 1, 1, C('#D2D0C8'))
        for y in np.arange(0.0, 1.0, 0.03):
            cv.rect(0, y, 1, y + 0.007, C('#A7A59C'))
    elif kind == 'venetian_dark':
        cv.rect(0, 0, 1, 1, C('#59606A'))
        for y in np.arange(0.0, 1.0, 0.03):
            cv.rect(0, y, 1, y + 0.012, C('#3B4048'))
    elif kind in ('curtains_red', 'curtains_blue', 'curtains_green', 'curtain_one'):
        col = {'curtains_red': '#8E4A45', 'curtains_blue': '#4A5E7E', 'curtains_green': '#56704F',
               'curtain_one': '#B08A4A'}[kind]
        sides = [(0, 0.3), (0.7, 1)] if kind != 'curtain_one' else [(0, 0.45)]
        for x0, x1 in sides:
            cv.rect(x0, 0, x1, 1, C(col))
            for fx in np.arange(x0, x1, 0.06):
                cv.rect(fx, 0, fx + 0.025, 1, shade(C(col), 0.78))
            cv.rect(x0, 0.97, x1, 1, shade(C(col), 0.6))
    elif kind == 'net':
        cv.rect(0, 0, 1, 1, C('#BFC2C0'))
        for y in np.arange(0.02, 1.0, 0.06):
            for x in np.arange(0.02 + (int(y * 50) % 2) * 0.04, 1.0, 0.08):
                cv.ellipse(x, y, 0.012, 0.009, C('#A6AAA9'))
        cv.rect(0, 0, 1, 0.05, C('#D6D8D4'))
    elif kind == 'roller':
        cv.rect(0, 0.55, 1, 1, C('#E2D6B8'))
        cv.rect(0, 0.53, 1, 0.56, C('#9C8D6C'))
        cv.ellipse(0.5, 0.515, 0.03, 0.02, C('#9C8D6C'))
    elif kind == 'plant':
        cv.rect(0.32, 0, 0.62, 0.12, C('#6E4430'))
        for _ in range(18):
            x, y = rng.uniform(0.18, 0.78), rng.uniform(0.1, 0.5)
            cv.ellipse(x, y, 0.07, 0.05, C('#2C3F27'))
    elif kind == 'shelf':
        cv.rect(0.05, 0, 0.6, 0.85, C('#2E2723'))
        for y in np.arange(0.05, 0.8, 0.17):
            cv.rect(0.07, y, 0.58, y + 0.012, C('#413730'))
            x = 0.08
            while x < 0.56:
                w = rng.uniform(0.02, 0.05)
                cv.rect(x, y + 0.012, x + w, y + rng.uniform(0.09, 0.14),
                        alpha(mix(C('#4A3B33'), C('#5B6470'), rng.uniform(0, 1)), 255))
                x += w + 0.005
    elif kind == 'lamp_off':
        cv.poly([(0.42, 0.95), (0.58, 0.95), (0.62, 0.86), (0.38, 0.86)], C('#3C3A37'))
        cv.line([(0.5, 1), (0.5, 0.95)], C('#2A2A2A'), 0.01)


def interior_lit(cv, rng, kind):
    if kind.startswith('office'):
        wall = C('#DCE4E8')
        cv.vgrad(0, 0, 1, 1, C('#B9C4CB'), wall)
        # perspective ceiling light panels
        for row, (y, h, w) in enumerate([(0.93, 0.035, 0.36), (0.86, 0.025, 0.26), (0.81, 0.018, 0.19)]):
            for cx in (0.25, 0.75):
                cv.rect(cx - w / 2, y, cx + w / 2, y + h, C('#FFFFFF'))
        cv.rect(0, 0.76, 1, 0.765, C('#A9B4BA'))
        # partitions + monitors
        cv.rect(0, 0, 1, 0.3, C('#8E979D'))
        for x in np.arange(0.06, 1, 0.3):
            cv.rect(x, 0.3, x + 0.14, 0.4, C('#2B3035'))
            cv.rect(x + 0.06, 0.28, x + 0.08, 0.31, C('#2B3035'))
        if kind == 'office_blinds':
            cv.rect(0, 0.55, 1, 1, C('#E8ECEE'))
            for y in np.arange(0.55, 1.0, 0.03):
                cv.rect(0, y, 1, y + 0.008, C('#C2CACF'))
        if kind == 'office_plant':
            for _ in range(10):
                cv.ellipse(rng.uniform(0.75, 0.95), rng.uniform(0.3, 0.55), 0.06, 0.05, C('#4E7A3A'))
            cv.rect(0.8, 0.25, 0.9, 0.33, C('#6E5A4A'))
        return
    if kind == 'tv':
        cv.vgrad(0, 0, 1, 1, C('#3D5E8A'), C('#7FA6D9'))
        cv.glow(0.5, 0.35, 0.6, C('#A9C8F0'), 0.6)
        cv.rect(0, 0, 1, 0.25, C('#30405A'))
        return
    if kind == 'frosted':
        cv.vgrad(0, 0, 1, 1, C('#E2D2B0'), C('#F4EAD3'))
        for y in np.arange(0, 1, 0.05):
            cv.line([(0, y), (1, y + 0.03)], C('#EADDBF'), 0.01)
        return
    wall = mix(C('#F2C88A'), C('#F0D4A8'), rng.uniform(0, 1))
    cv.vgrad(0, 0, 1, 1, shade(wall, 0.78), wall)
    cv.glow(0.5, 0.98, 0.7, C('#FFF1D2'), 0.85)
    if kind == 'warm_room':
        cv.rect(0.15, 0.45, 0.45, 0.7, C('#B27C4A'))
        cv.rect(0.18, 0.48, 0.42, 0.67, C('#E6B880'))
        cv.rect(0, 0, 1, 0.18, C('#9A6439'))
    elif kind == 'warm_blinds':
        cv.rect(0, 0.5, 1, 1, C('#FFE0A8'))
        for y in np.arange(0.5, 1.0, 0.035):
            cv.rect(0, y, 1, y + 0.01, C('#C98D4F'))
    elif kind == 'warm_blinds_full':
        cv.rect(0, 0, 1, 1, C('#FFD99A'))
        for y in np.arange(0.0, 1.0, 0.035):
            cv.rect(0, y, 1, y + 0.01, C('#C7884A'))
    elif kind == 'warm_curtains':
        for x0, x1 in [(0, 0.38), (0.62, 1)]:
            cv.rect(x0, 0, x1, 1, C('#F2A65C'))
            for fx in np.arange(x0, x1, 0.07):
                cv.rect(fx, 0, fx + 0.03, 1, C('#D9843E'))
    elif kind == 'kitchen':
        cv.rect(0, 0.62, 1, 0.92, C('#E9DCC4'))
        for x in np.arange(0.0, 1.0, 0.25):
            cv.rect(x + 0.01, 0.63, x + 0.24, 0.91, C('#F4EADA'))
            cv.rect(x + 0.2, 0.7, x + 0.22, 0.78, C('#9C8B70'))
        cv.rect(0, 0, 1, 0.3, C('#C9A57B'))
        cv.rect(0, 0.3, 1, 0.33, C('#E8D6B8'))
    elif kind == 'warm_plant':
        cv.rect(0.3, 0, 0.6, 0.13, C('#7B4A2E'))
        for _ in range(16):
            cv.ellipse(rng.uniform(0.15, 0.75), rng.uniform(0.12, 0.52), 0.07, 0.05, C('#5B6A2E'))
    elif kind == 'lamp':
        cv.glow(0.72, 0.55, 0.45, C('#FFE6B5'), 0.9)
        cv.poly([(0.63, 0.62), (0.81, 0.62), (0.77, 0.5), (0.67, 0.5)], C('#F7E2B8'))
        cv.rect(0.715, 0.05, 0.725, 0.5, C('#6A4A30'))
        cv.rect(0, 0, 1, 0.15, C('#8C5A35'))
        cv.rect(0.05, 0.15, 0.45, 0.35, C('#A8683C'))


def window_atlases():
    dark_kinds = ['plain', 'blinds_top', 'curtains_red', 'net', 'plant', 'blinds_full', 'curtains_blue', 'roller',
                  'shelf', 'venetian_dark', 'curtains_green', 'lamp_off']
    lit_kinds = ['warm_room', 'warm_blinds', 'warm_curtains', 'kitchen', 'warm_plant', 'lamp', 'warm_blinds_full', 'tv',
                 'office1', 'office2', 'office_blinds', 'frosted']
    for tex, kinds, lit in (('T_WindowDark.png', dark_kinds, False), ('T_WindowLit.png', lit_kinds, True)):
        atlas = Image.new('RGB', (1024, 1024), (0, 0, 0))
        slots = {}
        for i, (kind, rect) in enumerate(zip(kinds, WIN_CELLS)):
            rng = np.random.default_rng(100 + i + (50 if lit else 0))
            x, y, w, h = rect
            cv = Canvas(w, h, 1.0, 1.0)
            if lit:
                interior_lit(cv, rng, kind)
                cv.sheen(0.6, seed=i)
            else:
                interior_dark(cv, rng, kind)
                refl = Canvas(w, h, 1.0, 1.0)
                reflection_layer(refl, rng)
                r = refl.img.copy()
                # reflection strength: strong at the top of the glass, weaker low down (interior shows through)
                a = np.linspace(170, 105, r.size[1])[:, None].repeat(r.size[0], 1)
                if kind in ('blinds_full', 'net', 'roller', 'blinds_top'):
                    a *= 0.75
                r.putalpha(Image.fromarray(a.astype(np.uint8)))
                cv.img.alpha_composite(r)
                sheen_cell(cv, rng, 34)
            atlas.paste(cv.result().convert('RGB'), (x, y))
            slots[kind] = {'rect': [x, y, w, h], 'index': i}
        save(atlas, tex)
        register(tex, (1024, 1024), slots, order=kinds)


# ------------------------------------------------------------------------------------------------- packers

class Packer:
    """Skyline packer for sign atlases (pad px of background bleed around each slot); tallest items first."""

    def __init__(self, w=1024, pad=6):
        self.w, self.pad = w, pad
        self.sky = [(0, w, 0)]  # (x, width, y)
        self.top = 0

    def _fit(self, i, W):
        x = self.sky[i][0]
        if x + W > self.w:
            return None
        y, j = 0, i
        while True:
            if j >= len(self.sky):
                return None
            sx, sw, sy = self.sky[j]
            y = max(y, sy)
            if sx + sw >= x + W:
                return y
            j += 1

    def add(self, name, w, h):
        p = self.pad
        W, H = int(w) + 2 * p, int(h) + 2 * p
        best = None
        for i in range(len(self.sky)):
            y = self._fit(i, W)
            if y is not None and (best is None or (y + H, self.sky[i][0]) < (best[0] + H, best[1])):
                best = (y, self.sky[i][0])
        if best is None:
            raise ValueError('sign wider than atlas: %s' % name)
        y, x = best
        new = []
        for sx, sw, sy in self.sky:
            ex = sx + sw
            if ex <= x or sx >= x + W:
                new.append((sx, sw, sy))
                continue
            if sx < x:
                new.append((sx, x - sx, sy))
            if ex > x + W:
                new.append((x + W, ex - x - W, sy))
        new.append((x, W, y + H))
        new.sort()
        merged = []
        for seg in new:
            if merged and merged[-1][2] == seg[2] and merged[-1][0] + merged[-1][1] == seg[0]:
                merged[-1] = (merged[-1][0], merged[-1][1] + seg[1], seg[2])
            else:
                merged.append(seg)
        self.sky = merged
        self.top = max(self.top, y + H)
        return (x + p, y + p, int(w), int(h))

    def height(self):
        for s in (128, 256, 512, 1024):
            if self.top <= s:
                return s
        raise ValueError('atlas overflow: %d px' % self.top)


def build_sign_atlas(tex, signs, w=1024, bg=(40, 40, 40)):
    """signs: list of (name, w_m, h_m, ppm, draw_fn(canvas), bleed_rgba). Each slot is drawn at its true aspect."""
    pk = Packer(w)
    rects, dims = {}, {}
    for name, wm, hm, ppm, fn, bleed in signs:
        pw = min(w - 2 * pk.pad, int(round(wm * ppm)))
        dims[name] = (pw, max(8, int(round(hm * ppm * (pw / (wm * ppm))))))
    best = None
    for key in (lambda k: (-dims[k][1], -dims[k][0], k), lambda k: (-dims[k][0], -dims[k][1], k),
                lambda k: (-dims[k][0] * dims[k][1], k)):
        trial = Packer(w)
        try:
            tr = {name: trial.add(name, *dims[name]) for name in sorted(dims, key=key)}
        except ValueError:
            continue
        if best is None or trial.top < best[0].top:
            best = (trial, tr)
    if best is None:
        raise ValueError('cannot pack ' + tex)
    pk, rects = best
    H = pk.height()
    atlas = Image.new('RGBA', (w, H), bg + (255,))
    slots = {}
    for name, wm, hm, ppm, fn, bleed in signs:
        x, y, pw, ph = rects[name]
        p = pk.pad
        # bleed: fill the padded area with the sign's edge colour first
        atlas.paste(Image.new('RGBA', (pw + 2 * p, ph + 2 * p), bleed), (x - p, y - p))
        cv = Canvas(pw, ph, wm, hm, bg=bleed)
        fn(cv)
        atlas.alpha_composite(cv.result(), (x, y))
        slots[name] = {'rect': [x, y, pw, ph], 'm': [wm, hm]}
    save(atlas, tex)
    register(tex, (w, H), slots)


# -------------------------------------------------------------------------------------------- sign helpers

def border(cv, inset, col, w):
    cv.rect(inset, inset, cv.w_m - inset, cv.h_m - inset, None, outline=col, w=w)


def board(bg, edge=None, edge_w=0.0, inner=None, inner_inset=0.0, inner_w=0.0):
    def fn(cv):
        cv.rect(0, 0, cv.w_m, cv.h_m, bg)
        cv.vgrad(0, cv.h_m * 0.5, cv.w_m, cv.h_m, alpha(bg, 0), (255, 255, 255, 22))
        if edge:
            border(cv, 0, edge, edge_w)
        if inner:
            border(cv, inner_inset, inner, inner_w)
    return fn


def cup_icon(cv, cx, cy, s, col, steam=True):
    """Coffee cup: s = cup height in metres."""
    cv.poly([(cx - 0.55 * s, cy + 0.45 * s), (cx + 0.55 * s, cy + 0.45 * s), (cx + 0.42 * s, cy - 0.45 * s),
             (cx - 0.42 * s, cy - 0.45 * s)], col)
    cv.ellipse(cx + 0.62 * s, cy + 0.08 * s, 0.2 * s, 0.22 * s, None, outline=col, w=0.07 * s)
    cv.rect(cx - 0.7 * s, cy - 0.58 * s, cx + 0.7 * s, cy - 0.5 * s, col)
    if steam:
        for k in (-0.25, 0.05, 0.35):
            pts = [(cx + k * s + 0.08 * s * math.sin(t * 3.1), cy + 0.55 * s + t * 0.45 * s) for t in np.linspace(0, 1, 9)]
            cv.line(pts, col, 0.05 * s)


def neon(cv, x, y, s, key, size, tube, spacing=0.0, anchor='mm', glow_r=10):
    """Neon tube text: blurred halo + saturated tube + white-hot core."""
    halo = Canvas(*cv.out, cv.w_m, cv.h_m, bg=(0, 0, 0, 0), ss=cv.ss)
    halo.text(x, y, s, key, size, alpha(tube, 255), anchor, spacing, stroke=size * 0.06, stroke_fill=alpha(tube, 255))
    h = halo.img.filter(ImageFilter.GaussianBlur(glow_r * cv.ss))
    cv.img.alpha_composite(h)
    cv.img.alpha_composite(h)
    cv.text(x, y, s, key, size, alpha(mix(tube, (255, 255, 255, 255), 0.25), 255), anchor, spacing,
            stroke=size * 0.03, stroke_fill=alpha(tube, 255))
    cv.text(x, y, s, key, size, alpha(mix(tube, (255, 255, 255, 255), 0.75), 255), anchor, spacing)


def number_plate(num, bg='#1F2A24', fg='#E9DFC7', key='georgia_bold'):
    def fn(cv):
        cv.rrect(0, 0, cv.w_m, cv.h_m, 0.03, C(bg))
        cv.rrect(0.015, 0.015, cv.w_m - 0.015, cv.h_m - 0.015, 0.025, None, outline=C(fg), w=0.008)
        cv.fit_text(cv.w_m / 2, cv.h_m / 2, num, key, cv.w_m * 0.8, cv.h_m * 0.62, C(fg))
    return fn


def poster(title, sub, bg, fg, accent, key='arial_black', price=None):
    def fn(cv):
        w, h = cv.w_m, cv.h_m
        cv.rect(0, 0, w, h, C(bg))
        cv.rect(0, h * 0.72, w, h, C(accent))
        cv.fit_text(w / 2, h * 0.86, title, key, w * 0.86, h * 0.17, C(fg))
        if price:
            cv.ellipse(w * 0.5, h * 0.42, w * 0.32, w * 0.32, C(accent))
            cv.fit_text(w * 0.5, h * 0.42, price, 'arial_black', w * 0.5, h * 0.16, C(fg))
        cv.fit_text(w / 2, h * 0.1, sub, 'helv_bold', w * 0.85, h * 0.07, C(accent if bg != accent else fg))
    return fn


def chalk_menu(title, lines, frame='#5A3B24'):
    def fn(cv):
        w, h = cv.w_m, cv.h_m
        cv.rect(0, 0, w, h, C(frame))
        cv.rect(0.03, 0.03, w - 0.03, h - 0.03, C('#23282A'))
        cv.text(w / 2, h - 0.12, title, 'chalk', 0.09, C('#F2EEE2'))
        y = h - 0.24
        for ln in lines:
            cv.fit_text(w / 2, y, ln, 'chalk', w - 0.12, 0.05, C('#E6E0CF'))
            y -= 0.085
        cv.line([(0.1, h - 0.18), (w - 0.1, h - 0.18)], C('#B9B2A0'), 0.006)
    return fn


# -------------------------------------------------------------------------------- shop interiors (fake rooms)

INT_PANO_M = (8.0, 3.6)   # each panorama covers 8 m x 3.6 m of back wall (box height)


def int_layout():
    slots = {'panoA': {'rect': [0, 0, 1024, 448], 'm': list(INT_PANO_M)},
             'panoB': {'rect': [0, 448, 1024, 448], 'm': list(INT_PANO_M)},
             'floor': {'rect': [0, 896, 256, 128]}, 'ceiling': {'rect': [256, 896, 256, 128]},
             'side': {'rect': [512, 896, 256, 128]}}
    for i in range(8):
        for s in range(3):
            slots['sw%d_%d' % (i, s)] = {'rect': [768 + i * 32, 896 + s * 42, 32, 42 if s < 2 else 44]}
    return slots


def build_interior(tex, draw_a, draw_b, floor, ceiling, side, swatches, seed=0):
    img = Image.new('RGB', (1024, 1024), (0, 0, 0))
    for k, fn in enumerate((draw_a, draw_b)):
        cv = Canvas(1024, 448, *INT_PANO_M, bg=C('#808080'))
        fn(cv, np.random.default_rng(seed + k))
        # ambient occlusion: darker at the floor line and under the ceiling
        cv.vgrad(0, 0, 8, 0.35, (0, 0, 0, 70), (0, 0, 0, 0))
        cv.vgrad(0, 3.25, 8, 3.6, (0, 0, 0, 0), (0, 0, 0, 80))
        cv.sheen(0.9, seed=seed + 10 + k)
        img.paste(cv.result().convert('RGB'), (0, k * 448))
    for (x, c_top, c_bot) in ((0, floor[0], floor[1]), (256, ceiling[0], ceiling[1]), (512, side[0], side[1])):
        cv = Canvas(256, 128, 1, 1, bg=C(c_top))
        cv.vgrad(0, 0, 1, 1, C(c_bot), C(c_top))
        img.paste(cv.result().convert('RGB'), (x, 896))
    d = ImageDraw.Draw(img)
    for i, col in enumerate(swatches):
        for s, f in enumerate((1.12, 0.92, 0.7)):
            r = [768 + i * 32, 896 + s * 42, 768 + i * 32 + 31, 896 + s * 42 + (41 if s < 2 else 43)]
            d.rectangle(r, fill=shade(C(col), f)[:3])
    save(img, tex)
    register(tex, (1024, 1024), int_layout(), swatches=swatches)


def shelf_unit(cv, rng, x0, x1, y0, y1, rows, frame, palette, item_h=(0.12, 0.26), back=None, gap=0.02):
    if back:
        cv.rect(x0, y0, x1, y1, back)
    step = (y1 - y0) / rows
    for r in range(rows):
        sy = y0 + r * step
        cv.rect(x0, sy, x1, sy + 0.035, frame)
        x = x0 + 0.02
        top = min(step - 0.06, item_h[1])
        while x < x1 - 0.06:
            w = rng.uniform(0.05, 0.16)
            h = rng.uniform(item_h[0], top)
            col = C(palette[rng.integers(len(palette))])
            cv.rect(x, sy + 0.035, min(x + w, x1 - 0.02), sy + 0.035 + h, col)
            cv.rect(x, sy + 0.035 + h * 0.6, min(x + w, x1 - 0.02), sy + 0.035 + h * 0.75, shade(col, 1.25))
            x += w + gap
    cv.rect(x0 - 0.03, y0, x0, y1, frame)
    cv.rect(x1, y0, x1 + 0.03, y1, frame)
    cv.rect(x0 - 0.03, y1, x1 + 0.03, y1 + 0.04, frame)


def plant(cv, rng, x, h, pot='#B4623F', leaves=('#3F6B33', '#4E7F3D', '#5D9046', '#2F5528'), w=0.5):
    cv.poly([(x - 0.18 * w / 0.5, 0), (x + 0.18 * w / 0.5, 0), (x + 0.22 * w / 0.5, 0.32), (x - 0.22 * w / 0.5, 0.32)],
            C(pot))
    cv.rect(x - 0.24 * w / 0.5, 0.3, x + 0.24 * w / 0.5, 0.36, shade(C(pot), 0.85))
    for _ in range(int(26 * h)):
        a = rng.uniform(-1.2, 1.2)
        r = rng.uniform(0.2, 1.0) * h
        lx, ly = x + math.sin(a) * r * 0.45 * w / 0.5, 0.36 + math.cos(a) * r * 0.9
        cv.ellipse(lx, ly, rng.uniform(0.07, 0.14), rng.uniform(0.04, 0.08), C(leaves[rng.integers(len(leaves))]))


def hanging_plant(cv, rng, x, y_top):
    cv.line([(x, 3.6), (x, y_top)], C('#3A2E25'), 0.012)
    cv.ellipse(x, y_top - 0.08, 0.16, 0.1, C('#C9A27A'))
    for _ in range(26):
        cv.ellipse(x + rng.uniform(-0.3, 0.3), y_top - rng.uniform(0.05, 0.75), 0.06, 0.04,
                   C(['#3F6B33', '#4E7F3D', '#5D9046'][rng.integers(3)]))


def pendant_glow(cv, x, y, col='#FFE2A8', r=0.9):
    cv.glow(x, y, r, C(col), 0.55)
    cv.line([(x, 3.6), (x, y + 0.12)], C('#2A2622'), 0.01)
    cv.poly([(x - 0.13, y), (x + 0.13, y), (x + 0.06, y + 0.12), (x - 0.06, y + 0.12)], C('#2F3A33'))
    cv.ellipse(x, y - 0.01, 0.06, 0.035, C('#FFF6DA'))


def picture(cv, rng, x0, y0, x1, y1, frame='#3A2A1E'):
    cv.rect(x0, y0, x1, y1, C(frame))
    cv.rect(x0 + 0.04, y0 + 0.04, x1 - 0.04, y1 - 0.04, C('#EFE6D2'))
    cols = ['#C46A3F', '#2F6B4F', '#D9A441', '#6C8EA8', '#9B4A3A', '#3E4C5E']
    for _ in range(3):
        cx, cy = rng.uniform(x0 + 0.1, x1 - 0.1), rng.uniform(y0 + 0.1, y1 - 0.1)
        r = rng.uniform(0.05, min(x1 - x0, y1 - y0) * 0.3)
        cv.ellipse(cx, cy, r, r, C(cols[rng.integers(len(cols))]))


# ---- café

def cafe_a(cv, rng):
    cv.rect(0, 0, 8, 3.6, C('#EFE2C8'))
    cv.vgrad(0, 1.1, 8, 3.6, C('#E9D9BA'), C('#F6ECD9'))
    # green subway-tile wainscot
    cv.rect(0, 0, 8, 1.15, C('#2F6B4F'))
    for y in np.arange(0, 1.15, 0.075):
        cv.line([(0, y), (8, y)], C('#285C44'), 0.008)
        off = 0.075 if int(round(y / 0.075)) % 2 else 0
        for x in np.arange(off, 8, 0.15):
            cv.line([(x, y), (x, y + 0.075)], C('#285C44'), 0.006)
    cv.rect(0, 1.12, 8, 1.17, C('#E8DCC2'))
    # back counter
    cv.rect(0.4, 0, 6.6, 0.95, C('#7A5233'))
    for x in np.arange(0.5, 6.6, 0.6):
        cv.rect(x, 0.12, x + 0.5, 0.8, C('#86603F'))
    cv.rect(0.35, 0.92, 6.65, 1.0, C('#D8CCB5'))
    # espresso machine
    cv.rect(1.4, 1.0, 2.5, 1.5, C('#C7CBCF'))
    cv.rect(1.45, 1.38, 2.45, 1.48, C('#9DA3A9'))
    for gx in (1.7, 2.2):
        cv.rect(gx - 0.06, 1.12, gx + 0.06, 1.26, C('#2C2F33'))
        cv.rect(gx - 0.05, 1.0, gx + 0.05, 1.06, C('#F4F1EA'))
    for cx in np.arange(1.5, 2.45, 0.13):
        cv.rect(cx, 1.5, cx + 0.09, 1.58, C('#F7F4EE'))
    # grinder
    cv.rect(2.7, 1.0, 2.9, 1.35, C('#2B2B2B'))
    cv.poly([(2.66, 1.35), (2.94, 1.35), (2.86, 1.6), (2.74, 1.6)], C('#5E4026'))
    # pastry case
    cv.rect(3.9, 1.0, 5.6, 1.55, C('#B9D3D9'))
    for sy in (1.06, 1.3):
        cv.rect(3.95, sy, 5.55, sy + 0.02, C('#E8F0F0'))
        for px in np.arange(4.05, 5.5, 0.22):
            cv.ellipse(px, sy + 0.08, 0.09, 0.055, C(['#D9A441', '#C27A3A', '#E9C77B', '#8B4B2E'][rng.integers(4)]))
    cv.rect(3.88, 1.53, 5.62, 1.57, C('#4A3A2C'))
    # register
    cv.rect(6.0, 1.0, 6.4, 1.18, C('#2E2E2E'))
    cv.rect(6.05, 1.18, 6.35, 1.4, C('#3A3F45'))
    cv.rect(6.08, 1.22, 6.32, 1.37, C('#9FD0E0'))
    # shelves with cups and jars
    for sy in (1.8, 2.2):
        cv.rect(0.5, sy, 3.4, sy + 0.04, C('#6B4A30'))
        x = 0.6
        while x < 3.3:
            if rng.random() < 0.5:
                cv.rect(x, sy + 0.04, x + 0.1, sy + 0.14, C('#F4F0E6'))
                x += 0.13
            else:
                cv.rect(x, sy + 0.04, x + 0.12, sy + 0.24, C(['#C9A27A', '#7A5233', '#2F6B4F', '#B8372F'][rng.integers(4)]))
                cv.rect(x, sy + 0.2, x + 0.12, sy + 0.24, C('#3A2E25'))
                x += 0.16
    # menu board
    cv.rect(3.75, 1.75, 6.65, 2.95, C('#5A3B24'))
    cv.rect(3.82, 1.82, 6.58, 2.88, C('#23282A'))
    cv.text(5.2, 2.73, 'MENU', 'chalk', 0.16, C('#F2EEE2'))
    items = [('ESPRESSO', '2.80'), ('AMERICANO', '3.20'), ('FLAT WHITE', '3.90'), ('LATTE', '4.20'), ('CROISSANT', '3.50')]
    for k, (a, b) in enumerate(items):
        y = 2.52 - k * 0.15
        cv.text(3.95, y, a, 'chalk', 0.085, C('#E6E0CF'), anchor='lm')
        cv.text(6.45, y, b, 'chalk', 0.085, C('#F2C86A'), anchor='rm')
    # kitchen door with porthole
    cv.rect(6.9, 0, 7.75, 2.2, C('#5E4026'))
    cv.rect(6.95, 0, 7.7, 2.15, C('#7A5233'))
    cv.ellipse(7.32, 1.6, 0.16, 0.16, C('#FFE7B8'))
    plant(cv, rng, 7.85, 1.0)
    hanging_plant(cv, rng, 0.25, 3.1)
    for x in (1.2, 3.3, 5.4):
        pendant_glow(cv, x, 2.75)
    cv.glow(4.0, 3.4, 2.5, C('#FFF0D0'), 0.25)


def cafe_b(cv, rng):
    # exposed brick upper wall
    cv.rect(0, 0, 8, 3.6, C('#9B4A3A'))
    for y in np.arange(1.0, 3.6, 0.075):
        cv.line([(0, y), (8, y)], C('#C9B6A0'), 0.01)
        off = 0.11 if int(round(y / 0.075)) % 2 else 0
        for x in np.arange(off, 8, 0.22):
            cv.line([(x, y), (x, y + 0.075)], C('#C9B6A0'), 0.008)
    cv.vgrad(0, 1.0, 8, 3.6, (255, 220, 170, 50), (0, 0, 0, 30))
    # wood wainscot + green banquette
    cv.rect(0, 0, 8, 1.05, C('#6B4A30'))
    cv.rect(0, 1.02, 8, 1.07, C('#4A3221'))
    cv.rect(0.2, 0.42, 7.8, 1.0, C('#2F6B4F'))
    for x in np.arange(0.35, 7.8, 0.3):
        cv.ellipse(x, 0.75, 0.018, 0.018, C('#245540'))
    cv.rect(0.2, 0.42, 7.8, 0.5, C('#3C7D5E'))
    cv.rect(0.2, 0.0, 7.8, 0.42, C('#4A3221'))
    # tables + chairs
    for x in np.arange(0.9, 7.6, 1.45):
        cv.rect(x - 0.03, 0, x + 0.03, 0.72, C('#2A2A2A'))
        cv.rect(x - 0.18, 0, x + 0.18, 0.04, C('#2A2A2A'))
        cv.ellipse(x, 0.74, 0.34, 0.04, C('#EDEAE3'))
        cv.rect(x - 0.34, 0.72, x + 0.34, 0.75, C('#D9D5CC'))
        for s in (-0.55, 0.55):
            cx = x + s
            cv.rect(cx - 0.17, 0.44, cx + 0.17, 0.48, C('#3B2A1E'))
            cv.rect(cx - 0.15, 0, cx - 0.12, 0.44, C('#3B2A1E'))
            cv.rect(cx + 0.12, 0, cx + 0.15, 0.44, C('#3B2A1E'))
            cv.rect(cx + (0.13 if s > 0 else -0.16), 0.48, cx + (0.16 if s > 0 else -0.13), 0.95, C('#3B2A1E'))
            cv.ellipse(x + s * 0.3, 0.8, 0.05, 0.035, C('#F7F4EE'))
    for k, x in enumerate((0.6, 2.2, 3.7)):
        picture(cv, rng, x, 1.5, x + 0.9 + 0.2 * (k % 2), 2.3 + 0.1 * (k % 2))
    # round logo
    cv.ellipse(5.9, 2.15, 0.62, 0.62, C('#2F6B4F'))
    cv.ellipse(5.9, 2.15, 0.55, 0.55, None, outline=C('#E9DFC7'), w=0.02)
    cup_icon(cv, 5.9, 2.2, 0.36, C('#E9DFC7'))
    cv.text(5.9, 1.72, 'DAILY GRIND', 'futura_bold', 0.09, C('#E9DFC7'))
    # coffee bag shelf
    cv.rect(6.9, 1.9, 7.9, 1.94, C('#4A3221'))
    for x in np.arange(6.95, 7.85, 0.19):
        cv.rect(x, 1.94, x + 0.15, 2.22, C(['#C9A27A', '#2F6B4F', '#E9DFC7', '#7A5233'][rng.integers(4)]))
    for x in (1.4, 3.0, 4.6, 7.4):
        cv.glow(x, 2.6, 0.6, C('#FFD9A0'), 0.55)
        cv.rect(x - 0.06, 2.5, x + 0.06, 2.64, C('#3A3A3A'))
        cv.ellipse(x, 2.68, 0.06, 0.05, C('#FFF0C8'))
    for x in (2.4, 6.6):
        pendant_glow(cv, x, 2.85)
    hanging_plant(cv, rng, 7.7, 3.0)


# ---- mart

MART_GOODS = ['#E53935', '#FDD835', '#1E88E5', '#43A047', '#FB8C00', '#8E24AA', '#F4F4F4', '#00ACC1', '#D81B60',
              '#6D4C41', '#FFB300', '#3949AB']


def mart_a(cv, rng):
    cv.rect(0, 0, 8, 3.6, C('#F3F5F6'))
    cv.vgrad(0, 2.2, 8, 3.6, C('#F3F5F6'), C('#FFFFFF'))
    cv.rect(0, 2.95, 8, 3.1, C('#D32F2F'))
    cv.text(2.4, 3.03, 'GROCERY', 'arial_black', 0.11, C('#FFFFFF'))
    cv.text(6.5, 3.03, 'COLD DRINKS', 'arial_black', 0.11, C('#FFFFFF'))
    shelf_unit(cv, rng, 0.2, 4.8, 0.12, 2.25, 5, C('#B0B7BC'), MART_GOODS, back=C('#DDE2E5'))
    cv.rect(0.15, 0, 4.85, 0.12, C('#8C969C'))
    # fridges
    cv.rect(5.0, 0, 8.0, 2.45, C('#3C4650'))
    for k in range(4):
        x0 = 5.08 + k * 0.73
        cv.rect(x0, 0.12, x0 + 0.68, 2.3, C('#DDF0F7'))
        cv.glow(x0 + 0.34, 2.1, 0.5, C('#FFFFFF'), 0.6)
        for sy in np.arange(0.2, 2.2, 0.36):
            cv.rect(x0 + 0.03, sy, x0 + 0.65, sy + 0.02, C('#9FB3BD'))
            for bx in np.arange(x0 + 0.05, x0 + 0.62, 0.075):
                col = C(MART_GOODS[rng.integers(len(MART_GOODS))])
                cv.rect(bx, sy + 0.02, bx + 0.055, sy + 0.25, col)
                cv.rect(bx + 0.015, sy + 0.25, bx + 0.04, sy + 0.3, shade(col, 0.8))
        cv.rect(x0 + 0.6, 0.9, x0 + 0.63, 1.5, C('#C7CED3'))
    for x in (1.0, 3.0, 5.0, 7.0):
        cv.rect(x - 0.6, 3.45, x + 0.6, 3.52, C('#FFFFFF'))
        cv.glow(x, 3.45, 0.9, C('#FFFFFF'), 0.5)
    for x, t in ((1.6, 'SNACKS'), (3.8, 'CANDY')):
        cv.rect(x - 0.45, 2.48, x + 0.45, 2.78, C('#1E5AA8'))
        cv.text(x, 2.63, t, 'arial_black', 0.14, C('#FFFFFF'))
        cv.line([(x - 0.3, 2.78), (x - 0.3, 3.6)], C('#666666'), 0.01)
        cv.line([(x + 0.3, 2.78), (x + 0.3, 3.6)], C('#666666'), 0.01)


def mart_b(cv, rng):
    cv.rect(0, 0, 8, 3.6, C('#F1F3F4'))
    cv.vgrad(0, 2.2, 8, 3.6, C('#F1F3F4'), C('#FFFFFF'))
    # counter + register + small-goods wall
    shelf_unit(cv, rng, 0.2, 3.2, 1.1, 2.6, 6, C('#8C969C'), MART_GOODS, item_h=(0.08, 0.16), back=C('#D9DEE1'))
    cv.rect(0.1, 0, 3.4, 1.0, C('#C62828'))
    cv.rect(0.05, 0.98, 3.45, 1.06, C('#ECEFF1'))
    cv.rect(0.5, 1.06, 0.95, 1.25, C('#263238'))
    cv.rect(0.55, 1.25, 0.9, 1.5, C('#37474F'))
    cv.rect(0.58, 1.28, 0.87, 1.47, C('#80DEEA'))
    for x in np.arange(1.3, 3.2, 0.3):
        cv.rect(x, 1.06, x + 0.22, 1.2, C(MART_GOODS[rng.integers(len(MART_GOODS))]))
    # coffee station
    cv.rect(3.7, 0, 5.4, 0.95, C('#5D4037'))
    cv.rect(3.65, 0.95, 5.45, 1.0, C('#ECEFF1'))
    for x in (3.85, 4.4, 4.95):
        cv.rect(x, 1.0, x + 0.4, 1.65, C('#263238'))
        cv.rect(x + 0.05, 1.4, x + 0.35, 1.58, C('#FFB74D'))
        cv.rect(x + 0.15, 1.05, x + 0.25, 1.15, C('#ECEFF1'))
    cv.rect(3.7, 1.85, 5.4, 2.25, C('#4E342E'))
    cv.text(4.55, 2.05, 'HOT COFFEE', 'arial_black', 0.13, C('#FFE082'))
    # hot food case
    cv.rect(5.6, 0, 7.0, 1.0, C('#9E9E9E'))
    cv.rect(5.6, 1.0, 7.0, 1.6, C('#FFCC80'))
    cv.glow(6.3, 1.4, 0.7, C('#FFB74D'), 0.6)
    for x in np.arange(5.7, 6.9, 0.22):
        cv.ellipse(x + 0.1, 1.15, 0.09, 0.05, C('#C77D3A'))
        cv.ellipse(x + 0.1, 1.4, 0.09, 0.05, C('#D9A441'))
    # ATM
    cv.rect(7.2, 0, 7.9, 1.75, C('#1E5AA8'))
    cv.rect(7.28, 1.0, 7.82, 1.4, C('#0D2A4F'))
    cv.rect(7.33, 1.05, 7.77, 1.35, C('#7FC4F0'))
    cv.text(7.55, 1.6, 'ATM', 'arial_black', 0.14, C('#FFFFFF'))
    for x in (1.0, 3.0, 5.0, 7.0):
        cv.rect(x - 0.6, 3.45, x + 0.6, 3.52, C('#FFFFFF'))
        cv.glow(x, 3.45, 0.9, C('#FFFFFF'), 0.5)


# ---- pharmacy

PHARM_GOODS = ['#FFFFFF', '#E3F2FD', '#C8E6C9', '#FFCDD2', '#FFF9C4', '#B3E5FC', '#F8BBD0', '#DCEDC8', '#2E7D32',
               '#1565C0', '#EF6C00']


def pharm_a(cv, rng):
    cv.rect(0, 0, 8, 3.6, C('#F4F7F5'))
    cv.vgrad(0, 2.3, 8, 3.6, C('#F4F7F5'), C('#FFFFFF'))
    cv.rect(0, 0, 8, 0.12, C('#1B5E20'))
    shelf_unit(cv, rng, 0.2, 4.6, 0.12, 2.2, 6, C('#E0E6E2'), PHARM_GOODS, item_h=(0.08, 0.2), back=C('#E8EEEA'))
    # prescription counter
    cv.rect(4.9, 0, 8.0, 1.05, C('#2E7D32'))
    cv.rect(4.85, 1.02, 8.0, 1.1, C('#F5F5F5'))
    shelf_unit(cv, rng, 5.0, 7.9, 1.35, 2.35, 4, C('#CFD8DC'), ['#FFFFFF', '#ECEFF1', '#E3F2FD', '#FFF8E1'],
               item_h=(0.06, 0.14), back=C('#ECEFF1'))
    cv.rect(5.0, 2.55, 7.9, 2.9, C('#1B5E20'))
    cv.text(6.45, 2.725, 'PRESCRIPTIONS', 'helv_bold', 0.16, C('#FFFFFF'), spacing=0.02)
    for x in np.arange(5.3, 7.9, 0.9):
        cv.rect(x, 1.1, x + 0.35, 1.3, C('#FFFFFF'))
    for x in (1.0, 3.0, 5.0, 7.0):
        cv.rect(x - 0.6, 3.45, x + 0.6, 3.52, C('#FFFFFF'))
        cv.glow(x, 3.45, 0.9, C('#FFFFFF'), 0.45)


def pharm_b(cv, rng):
    cv.rect(0, 0, 8, 3.6, C('#F6F4F5'))
    cv.rect(0, 0, 8, 0.12, C('#1B5E20'))
    shelf_unit(cv, rng, 0.2, 3.8, 0.12, 2.2, 6, C('#E0E0E0'), ['#F8BBD0', '#E1BEE7', '#FFCDD2', '#FFFFFF', '#212121',
                                                                 '#D7CCC8', '#F48FB1'], item_h=(0.06, 0.18),
               back=C('#F3E5F5'))
    cv.rect(0.2, 2.45, 3.8, 2.75, C('#AD1457'))
    cv.text(2.0, 2.6, 'HEALTH & BEAUTY', 'helv_bold', 0.15, C('#FFFFFF'))
    shelf_unit(cv, rng, 4.2, 7.8, 0.12, 2.2, 6, C('#E0E0E0'), ['#FFB300', '#FB8C00', '#FFF59D', '#FFFFFF', '#43A047',
                                                                 '#1E88E5'], item_h=(0.08, 0.16), back=C('#FFF8E1'))
    cv.rect(4.2, 2.45, 7.8, 2.75, C('#2E7D32'))
    cv.text(6.0, 2.6, 'VITAMINS', 'helv_bold', 0.15, C('#FFFFFF'))
    for x in (1.0, 3.0, 5.0, 7.0):
        cv.rect(x - 0.6, 3.45, x + 0.6, 3.52, C('#FFFFFF'))
        cv.glow(x, 3.45, 0.9, C('#FFFFFF'), 0.45)


# ---- diner

def diner_a(cv, rng):
    cv.rect(0, 0, 8, 3.6, C('#F4EEE3'))
    # tile band + chrome back bar
    for x in np.arange(0, 8, 0.15):
        for y in np.arange(1.0, 2.0, 0.15):
            cv.rect(x + 0.005, y + 0.005, x + 0.145, y + 0.145, C('#FBF8F2'))
    cv.rect(0, 1.95, 8, 2.05, C('#C62828'))
    cv.rect(0, 0, 8, 0.95, C('#B0BEC5'))
    for y in np.arange(0.1, 0.95, 0.09):
        cv.rect(0, y, 8, y + 0.03, C('#ECEFF1'))
    cv.rect(0, 0.95, 8, 1.02, C('#C62828'))
    # pie case, coffee pots, shake mixer
    cv.rect(0.4, 1.02, 1.7, 1.65, C('#D6EEF2'))
    for sy in (1.08, 1.35):
        for px in np.arange(0.5, 1.6, 0.36):
            cv.poly([(px, sy), (px + 0.3, sy), (px + 0.26, sy + 0.12), (px + 0.04, sy + 0.12)],
                    C(['#C0563B', '#E8B04A', '#8E3B5E', '#F2E3B3'][rng.integers(4)]))
    for x in (2.2, 2.55, 2.9):
        cv.rect(x, 1.02, x + 0.25, 1.4, C('#37474F'))
        cv.ellipse(x + 0.125, 1.2, 0.09, 0.1, C('#5D3A1A'))
    cv.rect(3.3, 1.02, 3.6, 1.5, C('#B0BEC5'))
    cv.rect(3.36, 1.1, 3.54, 1.3, C('#F8BBD0'))
    # pass-through to the kitchen
    cv.rect(4.2, 1.15, 6.2, 1.95, C('#B0BEC5'))
    cv.rect(4.28, 1.2, 6.12, 1.9, C('#FFD180'))
    cv.glow(5.2, 1.6, 0.8, C('#FFE0B2'), 0.6)
    for x in np.arange(4.4, 6.0, 0.4):
        cv.rect(x, 1.2, x + 0.25, 1.24, C('#ECEFF1'))
    # menu boards
    for k, (t, p) in enumerate((('BURGERS', '9.50'), ('PANCAKES', '7.25'), ('SHAKES', '5.00'), ('PIE', '4.50'))):
        x = 0.5 + k * 1.9
        cv.rect(x, 2.35, x + 1.6, 3.0, C('#212121'))
        cv.text(x + 0.8, 2.78, t, 'clarendon', 0.17, C('#FFEB3B'))
        cv.text(x + 0.8, 2.52, p, 'clarendon', 0.12, C('#FFFFFF'))
    # neon clock
    cv.glow(7.4, 1.5, 0.45, C('#FF4081'), 0.6)
    cv.ellipse(7.4, 1.5, 0.28, 0.28, C('#FFFFFF'), outline=C('#FF4081'), w=0.04)
    cv.line([(7.4, 1.5), (7.4, 1.68)], C('#212121'), 0.02)
    cv.line([(7.4, 1.5), (7.52, 1.45)], C('#212121'), 0.02)
    for x in (1.0, 3.0, 5.0, 7.0):
        cv.glow(x, 3.4, 1.0, C('#FFF3E0'), 0.4)
        cv.ellipse(x, 3.45, 0.25, 0.05, C('#FFFFFF'))


def diner_b(cv, rng):
    cv.rect(0, 0, 8, 3.6, C('#F4EEE3'))
    # checkerboard wainscot
    for x in np.arange(0, 8, 0.15):
        for y in np.arange(0, 1.2, 0.15):
            if (int(round(x / 0.15)) + int(round(y / 0.15))) % 2 == 0:
                cv.rect(x, y, x + 0.15, y + 0.15, C('#212121'))
            else:
                cv.rect(x, y, x + 0.15, y + 0.15, C('#FAFAFA'))
    cv.rect(0, 1.2, 8, 1.28, C('#B0BEC5'))
    cv.rect(0, 2.2, 8, 2.28, C('#4F7D7A'))
    # booths
    for x in np.arange(0.2, 6.8, 1.6):
        cv.rect(x, 0, x + 0.18, 1.35, C('#C62828'))
        cv.rect(x + 1.2, 0, x + 1.38, 1.35, C('#C62828'))
        cv.rect(x, 1.3, x + 0.18, 1.38, C('#ECEFF1'))
        cv.rect(x + 1.2, 1.3, x + 1.38, 1.38, C('#ECEFF1'))
        cv.rect(x + 0.18, 0.42, x + 1.2, 0.5, C('#B71C1C'))
        cv.rect(x + 0.35, 0.72, x + 1.03, 0.78, C('#ECEFF1'))
        cv.rect(x + 0.66, 0, x + 0.72, 0.72, C('#90A4AE'))
        cv.ellipse(x + 0.55, 0.82, 0.07, 0.04, C('#FFFFFF'))
        cv.rect(x + 0.84, 0.78, x + 0.9, 0.92, C('#E53935'))
        picture(cv, rng, x + 0.4, 1.6, x + 1.0, 2.05, frame='#B0BEC5')
        cv.glow(x + 0.69, 2.5, 0.5, C('#FFE0B2'), 0.5)
    # jukebox
    cv.rect(6.9, 0, 7.8, 1.5, C('#6D4C41'))
    cv.ellipse(7.35, 1.5, 0.45, 0.35, C('#FF7043'))
    cv.ellipse(7.35, 1.5, 0.36, 0.27, C('#FFD54F'))
    cv.ellipse(7.35, 1.5, 0.27, 0.19, C('#4FC3F7'))
    cv.rect(7.0, 0.5, 7.7, 1.2, C('#FFF59D'))
    cv.glow(7.35, 1.1, 0.7, C('#FFAB40'), 0.5)
    cv.rect(0, 3.0, 8, 3.1, C('#C62828'))
    for x in (1.0, 3.0, 5.0, 7.0):
        cv.glow(x, 3.4, 1.0, C('#FFF3E0'), 0.4)


def interiors_corner():
    build_interior('T_Int_Cafe.png', cafe_a, cafe_b, ('#8A6446', '#5E4330'), ('#F3E7D2', '#D9C8AC'),
                   ('#EADCC2', '#C9B597'),
                   ['#7A5233', '#2F6B4F', '#EFE6D2', '#2A2A2A', '#B4623F', '#F7F4EE', '#C9A45C', '#4E7F3D'], seed=200)
    build_interior('T_Int_Mart.png', mart_a, mart_b, ('#D5D9DC', '#AEB4B8'), ('#FFFFFF', '#E6EAEC'),
                   ('#F3F5F6', '#D5DADD'),
                   ['#B0B7BC', '#D32F2F', '#1E88E5', '#FDD835', '#43A047', '#F4F4F4', '#37474F', '#FB8C00'], seed=210)
    build_interior('T_Int_Pharmacy.png', pharm_a, pharm_b, ('#E3E8E4', '#BFC8C2'), ('#FFFFFF', '#E8EDEA'),
                   ('#F4F7F5', '#D7DED9'),
                   ['#E0E6E2', '#2E7D32', '#FFFFFF', '#F8BBD0', '#1565C0', '#FFF9C4', '#455A64', '#C8E6C9'], seed=220)
    build_interior('T_Int_Diner.png', diner_a, diner_b, ('#E9E4DA', '#4A4A4A'), ('#FFF8EC', '#E7DCC8'),
                   ('#F4EEE3', '#D8CFBE'),
                   ['#C62828', '#B0BEC5', '#ECEFF1', '#212121', '#4F7D7A', '#FFD54F', '#6D4C41', '#FFFFFF'], seed=230)


# ------------------------------------------------------------------------------------------------ shop signs

def sign_cafe():
    green, cream, gold = C('#2F6B4F'), C('#EFE6D2'), C('#C9A45C')

    def fascia(cv):
        board(green, inner=gold, inner_inset=0.05, inner_w=0.018)(cv)
        w, h = cv.w_m, cv.h_m
        cv.fit_text(w / 2, h * 0.56, 'DAILY GRIND', 'futura_bold', w * 0.6, h * 0.5, cream, spacing=0.06)
        cv.fit_text(w / 2, h * 0.2, 'COFFEE  ·  ESPRESSO  ·  PASTRY', 'futura', w * 0.5, h * 0.15, gold, spacing=0.03)
        for x in (w * 0.1, w * 0.9):
            cup_icon(cv, x, h * 0.45, h * 0.42, cream)

    def valance(cv):
        w, h = cv.w_m, cv.h_m
        cv.rect(0, 0, w, h, green)
        cv.rect(0, h * 0.06, w, h * 0.1, alpha(cream, 200))
        cv.rect(0, h * 0.9, w, h * 0.94, alpha(cream, 200))
        cv.fit_text(w / 2, h * 0.5, 'DAILY GRIND  ·  COFFEE', 'futura_bold', w * 0.8, h * 0.56, cream, spacing=0.05)

    def blade(cv):
        w, h = cv.w_m, cv.h_m
        cv.rect(0, 0, w, h, green)
        cv.ellipse(w / 2, h / 2, w * 0.46, h * 0.46, None, outline=gold, w=0.02)
        cup_icon(cv, w / 2, h * 0.56, h * 0.3, cream)
        cv.fit_text(w / 2, h * 0.2, 'COFFEE', 'futura_bold', w * 0.5, h * 0.11, cream, spacing=0.02)

    def corner(cv):
        board(green, inner=gold, inner_inset=0.03, inner_w=0.012)(cv)
        cv.fit_text(cv.w_m / 2, cv.h_m * 0.5, 'Daily Grind', 'script', cv.w_m * 0.85, cv.h_m * 0.78, cream)

    signs = [
        ('fascia_front', 7.0, 0.62, 146, fascia, green),
        ('fascia_side', 4.4, 0.62, 146, fascia, green),
        ('valance_front', 7.8, 0.3, 131, valance, green),
        ('valance_side', 5.2, 0.3, 131, valance, green),
        ('blade', 0.76, 0.76, 300, blade, green),
        ('corner', 1.5, 0.4, 300, corner, green),
        ('aframe', 0.56, 0.8, 300, chalk_menu('TODAY', ['FLAT WHITE', 'OAT LATTE', 'COLD BREW', 'BANANA BREAD', '7AM - 7PM']),
         C('#5A3B24')),
        ('number', 0.36, 0.24, 300, number_plate('27'), C('#1F2A24')),
        ('hours', 0.3, 0.42, 300, lambda cv: (cv.rect(0, 0, cv.w_m, cv.h_m, C('#F4EEE0')),
                                               cv.text(cv.w_m / 2, cv.h_m * 0.82, 'OPEN', 'futura_bold', 0.06, green),
                                               cv.text(cv.w_m / 2, cv.h_m * 0.6, 'MON-FRI 7-7', 'futura', 0.032, C('#333333')),
                                               cv.text(cv.w_m / 2, cv.h_m * 0.45, 'SAT-SUN 8-6', 'futura', 0.032, C('#333333')),
                                               cv.text(cv.w_m / 2, cv.h_m * 0.2, 'WIFI · OAT MILK', 'futura', 0.026, green)),
         C('#F4EEE0')),
    ]
    build_sign_atlas('T_Sign_Cafe.png', signs, bg=(47, 107, 79))


def sign_mart():
    red, white, blue, yellow = C('#D32F2F'), C('#FFFFFF'), C('#1E5AA8'), C('#FDD835')
    signs = [
        ('poster0', 0.7, 1.0, 220, poster('HOT COFFEE', 'ANY SIZE · ALL DAY', '#4E342E', '#FFFFFF', '#FFB300',
                                         price='$1'), C('#4E342E')),
        ('poster1', 0.7, 1.0, 220, poster('COLD DRINKS', 'GRAB & GO', '#1E5AA8', '#FFFFFF', '#00ACC1', price='2/$3'),
         C('#1E5AA8')),
        ('poster2', 0.7, 1.0, 220, poster('SNACK DEAL', 'MIX & MATCH', '#FDD835', '#212121', '#D32F2F', price='3/$5'),
         C('#FDD835')),
        ('poster3', 0.7, 1.0, 220, poster('ATM INSIDE', 'OPEN 24 HOURS', '#FFFFFF', '#1E5AA8', '#1E5AA8'), C('#FFFFFF')),
        ('poster4', 0.7, 1.0, 220, poster('FRESH FRUIT', 'EVERY MORNING', '#43A047', '#FFFFFF', '#FDD835',
                                         price='99¢'), C('#43A047')),
        ('poster5', 0.7, 1.0, 220, poster('ICE', 'BAGS IN STOCK', '#E3F2FD', '#1565C0', '#1565C0'), C('#E3F2FD')),
        ('office_plaque', 0.5, 0.36, 300, lambda cv: (cv.rect(0, 0, cv.w_m, cv.h_m, C('#C7CCD1')),
                                                       cv.rect(0.015, 0.015, cv.w_m - 0.015, cv.h_m - 0.015, C('#E9ECEE')),
                                                       cv.text(cv.w_m / 2, cv.h_m * 0.7, '300', 'helv_black', 0.12,
                                                               C('#263238')),
                                                       cv.text(cv.w_m / 2, cv.h_m * 0.3, 'OFFICES 2-6', 'helv_bold', 0.045,
                                                               C('#263238'))), C('#C7CCD1')),
        ('hours', 0.36, 0.24, 300, lambda cv: (cv.rect(0, 0, cv.w_m, cv.h_m, red),
                                               cv.text(cv.w_m / 2, cv.h_m * 0.6, 'OPEN', 'arial_black', 0.08, white),
                                               cv.text(cv.w_m / 2, cv.h_m * 0.25, '24 HOURS', 'arial_black', 0.05, yellow)),
         red),
    ]
    build_sign_atlas('T_Sign_Mart.png', signs, bg=(211, 47, 47))


def sign_pharmacy():
    green, white, cream = C('#1B5E20'), C('#FFFFFF'), C('#E8DFC9')

    def fascia(text, sub):
        def fn(cv):
            board(green, inner=cream, inner_inset=0.04, inner_w=0.014)(cv)
            w, h = cv.w_m, cv.h_m
            cv.fit_text(w / 2, h * 0.58, text, 'optima_bold', w * 0.66, h * 0.52, white, spacing=0.09)
            cv.fit_text(w / 2, h * 0.2, sub, 'optima_bold', w * 0.5, h * 0.14, cream, spacing=0.04)
            for x in (w * 0.08, w * 0.92):
                s = h * 0.18
                cv.rect(x - s, h * 0.5 - s / 3, x + s, h * 0.5 + s / 3, C('#4CAF50'))
                cv.rect(x - s / 3, h * 0.5 - s, x + s / 3, h * 0.5 + s, C('#4CAF50'))
        return fn

    signs = [
        ('fascia_front', 8.4, 0.7, 120, fascia('PHARMACY', 'PRESCRIPTIONS  ·  HEALTH  ·  BEAUTY'), green),
        ('fascia_side', 7.2, 0.7, 140, fascia('PHARMACY', 'OPEN 8 AM - 9 PM'), green),
        ('poster0', 0.8, 1.1, 200, poster('FLU SHOTS', 'NO APPOINTMENT NEEDED', '#FFFFFF', '#1B5E20', '#2E7D32'),
         C('#FFFFFF')),
        ('poster1', 0.8, 1.1, 200, poster('SPRING', 'ALLERGY RELIEF', '#E3F2FD', '#1565C0', '#43A047'), C('#E3F2FD')),
        ('poster2', 0.8, 1.1, 200, poster('VITAMINS', 'BUY 1 GET 1 HALF PRICE', '#FFF8E1', '#E65100', '#FB8C00'),
         C('#FFF8E1')),
        ('number', 0.42, 0.26, 300, number_plate('401', bg='#1B5E20', fg='#F4F1E8', key='optima_bold'), green),
        ('hours', 0.32, 0.44, 300, lambda cv: (cv.rect(0, 0, cv.w_m, cv.h_m, C('#F7F7F2')),
                                               cv.text(cv.w_m / 2, cv.h_m * 0.82, 'HOURS', 'helv_bold', 0.05, green),
                                               cv.text(cv.w_m / 2, cv.h_m * 0.62, 'MON-SAT 8-9', 'helv_med', 0.03,
                                                       C('#333333')),
                                               cv.text(cv.w_m / 2, cv.h_m * 0.47, 'SUN 10-6', 'helv_med', 0.03,
                                                       C('#333333')),
                                               cv.text(cv.w_m / 2, cv.h_m * 0.2, 'PHARMACIST ON DUTY', 'helv_bold', 0.022,
                                                       green)), C('#F7F7F2')),
    ]
    build_sign_atlas('T_Sign_Pharmacy.png', signs, bg=(27, 94, 32))


def sign_diner():
    signs = [
        ('poster0', 0.8, 0.6, 220, lambda cv: (cv.rect(0, 0, cv.w_m, cv.h_m, C('#FFF8E1')),
                                               cv.rect(0, cv.h_m * 0.7, cv.w_m, cv.h_m, C('#C62828')),
                                               cv.fit_text(cv.w_m / 2, cv.h_m * 0.85, 'BREAKFAST', 'clarendon', 0.6, 0.12,
                                                           C('#FFFFFF')),
                                               cv.fit_text(cv.w_m / 2, cv.h_m * 0.48, 'ALL DAY', 'clarendon', 0.6, 0.15,
                                                           C('#C62828')),
                                               cv.fit_text(cv.w_m / 2, cv.h_m * 0.18, 'PANCAKES · EGGS · HASH',
                                                           'helv_bold', 0.7, 0.05, C('#4F7D7A'))), C('#FFF8E1')),
        ('poster1', 0.8, 0.6, 220, lambda cv: (cv.rect(0, 0, cv.w_m, cv.h_m, C('#4F7D7A')),
                                               cv.fit_text(cv.w_m / 2, cv.h_m * 0.66, 'FREE', 'clarendon', 0.6, 0.16,
                                                           C('#FFD54F')),
                                               cv.fit_text(cv.w_m / 2, cv.h_m * 0.34, 'COFFEE REFILLS', 'clarendon', 0.7,
                                                           0.09, C('#FFFFFF'))), C('#4F7D7A')),
        ('number', 0.4, 0.26, 300, number_plate('88', bg='#4F7D7A', fg='#F4F1E8', key='clarendon'), C('#4F7D7A')),
        ('fascia_side', 6.0, 0.6, 160, lambda cv: (board(C('#ECEFF1'), inner=C('#C62828'), inner_inset=0.04,
                                                         inner_w=0.03)(cv),
                                                   cv.fit_text(cv.w_m / 2, cv.h_m * 0.5, 'EAT  ·  DRINK  ·  SINCE 1954',
                                                               'clarendon', cv.w_m * 0.75, cv.h_m * 0.45,
                                                               C('#C62828'))), C('#ECEFF1')),
    ]
    build_sign_atlas('T_Sign_Diner.png', signs, bg=(79, 125, 122))


def signs_lit():
    """Emissive sign faces (shared atlas MB_SignsLit). Dark pixels stay dark, so neon only glows on the tubes."""
    red, white, blue, yellow = C('#E53935'), C('#FFFFFF'), C('#1E5AA8'), C('#FFD54F')

    def mart_box(cv):
        w, h = cv.w_m, cv.h_m
        cv.rect(0, 0, w, h, C('#FFFFFF'))
        cv.rect(0, 0, w, h * 0.12, red)
        cv.rect(0, h * 0.88, w, h, red)
        cv.rect(w * 0.03, h * 0.2, w * 0.3, h * 0.8, red)
        cv.fit_text(w * 0.165, h * 0.5, '24/7', 'arial_black', w * 0.25, h * 0.5, white)
        cv.fit_text(w * 0.64, h * 0.5, 'MART', 'arial_black', w * 0.6, h * 0.62, C('#1E3C78'), spacing=0.12)

    def open_led(cv):
        w, h = cv.w_m, cv.h_m
        cv.rect(0, 0, w, h, C('#0A0A0A'))
        cv.rrect(0.03, 0.03, w - 0.03, h - 0.03, 0.04, None, outline=C('#3D7BFF'), w=0.025)
        neon(cv, w / 2, h * 0.5, 'OPEN', 'arial_black', h * 0.5, C('#FF3B3B'), glow_r=6)

    def cross(cv):
        w, h = cv.w_m, cv.h_m
        cv.rect(0, 0, w, h, C('#1FAF4E'))
        cv.glow(w / 2, h / 2, w * 0.6, C('#B9FFCF'), 0.9)
        for k in np.arange(0.06, w, 0.06):
            cv.line([(k, 0), (k, h)], C('#18A045'), 0.006)
            cv.line([(0, k), (w, k)], C('#18A045'), 0.006)

    def diner_main(cv):
        w, h = cv.w_m, cv.h_m
        cv.rect(0, 0, w, h, C('#101418'))
        cv.rrect(0.05, 0.05, w - 0.05, h - 0.05, 0.12, None, outline=C('#29B6F6'), w=0.03)
        neon(cv, w * 0.36, h * 0.52, 'Diner', 'script', h * 0.95, C('#FF4081'), glow_r=8)
        neon(cv, w * 0.79, h * 0.5, 'OPEN 24H', 'arial_round', h * 0.24, C('#40C4FF'), glow_r=6)

    def diner_blade(cv):
        w, h = cv.w_m, cv.h_m
        cv.rect(0, 0, w, h, C('#C62828'))
        cv.rect(0.05, 0.05, w - 0.05, h - 0.05, C('#141414'))
        for k, ch in enumerate('DINER'):
            neon(cv, w / 2, h * (0.86 - k * 0.17), ch, 'clarendon', w * 0.62, C('#FFD54F'), glow_r=6)

    def diner_open(cv):
        w, h = cv.w_m, cv.h_m
        cv.rect(0, 0, w, h, C('#0E1114'))
        neon(cv, w / 2, h * 0.5, 'OPEN 24H', 'arial_round', h * 0.55, C('#FF4081'), glow_r=6)

    def pharm_letters(cv):
        w, h = cv.w_m, cv.h_m
        cv.rect(0, 0, w, h, C('#0F3D17'))
        neon(cv, w / 2, h * 0.5, 'PHARMACY', 'optima_bold', h * 0.62, C('#69F0AE'), spacing=0.08, glow_r=5)

    signs = [
        ('mart_front', 9.4, 1.1, 108, mart_box, white),
        ('mart_side', 7.4, 1.1, 120, mart_box, white),
        ('mart_open', 0.9, 0.38, 260, open_led, C('#0A0A0A')),
        ('pharm_cross', 0.9, 0.9, 256, cross, C('#1FAF4E')),
        ('pharm_letters', 3.2, 0.5, 220, pharm_letters, C('#0F3D17')),
        ('diner_main', 5.6, 1.0, 180, diner_main, C('#101418')),
        ('diner_blade', 0.9, 3.4, 150, diner_blade, C('#C62828')),
        ('diner_open', 1.6, 0.45, 250, diner_open, C('#0E1114')),
        ('open_neon', 0.9, 0.4, 250, open_led, C('#0A0A0A')),
    ]
    build_sign_atlas('T_SignsLit.png', signs, bg=(10, 10, 10))


# ----------------------------------------------------------------------------------------------- details atlas

def details():
    """Small prop faces: AC unit grille, louvre vent, intercom, doors, fan top, meter, skylight. 512 x 512."""
    signs = []

    def ac(cv):
        w, h = cv.w_m, cv.h_m
        cv.rect(0, 0, w, h, C('#DCD8CE'))
        cv.rect(0.03, 0.04, w * 0.72, h - 0.04, C('#B9B4A8'))
        for y in np.arange(0.06, h - 0.05, 0.022):
            cv.rect(0.04, y, w * 0.71, y + 0.009, C('#8F8A7F'))
        cv.rect(w * 0.76, h * 0.55, w * 0.95, h * 0.8, C('#C9C4B9'))
        for k in range(3):
            cv.ellipse(w * 0.8 + k * 0.035, h * 0.3, 0.012, 0.012, C('#6E6A62'))

    def vent(cv):
        w, h = cv.w_m, cv.h_m
        cv.rect(0, 0, w, h, C('#8E949A'))
        for y in np.arange(0.03, h - 0.02, 0.05):
            cv.rect(0.03, y, w - 0.03, y + 0.025, C('#5C6268'))

    def door_res(color):
        def fn(cv):
            w, h = cv.w_m, cv.h_m
            cv.rect(0, 0, w, h, C(color))
            cv.rect(0.12, h * 0.5, w - 0.12, h - 0.12, C('#2A3138'))
            cv.vgrad(0.12, h * 0.5, w - 0.12, h - 0.12, C('#2A3138'), C('#7E8F9D'))
            cv.line([(0.12, h * 0.75), (w - 0.12, h * 0.75)], C(color), 0.03)
            cv.rect(0.12, 0.12, w - 0.12, h * 0.42, shade(C(color), 0.88))
            cv.rect(0.16, 0.16, w - 0.16, h * 0.38, shade(C(color), 1.08))
            cv.rect(w - 0.2, h * 0.45, w - 0.14, h * 0.48, C('#C9A45C'))
            cv.rect(w * 0.35, h * 0.3, w * 0.65, h * 0.32, C('#C9A45C'))
        return fn

    def door_steel(cv):
        w, h = cv.w_m, cv.h_m
        cv.rect(0, 0, w, h, C('#5B6670'))
        cv.rect(0.05, 0.05, w - 0.05, h - 0.05, C('#66727C'))
        cv.rect(w - 0.22, h * 0.45, w - 0.08, h * 0.5, C('#B0B7BC'))
        cv.rect(0.1, h * 0.15, w - 0.1, h * 0.2, C('#59646D'))

    def fan(cv):
        w, h = cv.w_m, cv.h_m
        cv.rect(0, 0, w, h, C('#B7BCC0'))
        cv.ellipse(w / 2, h / 2, w * 0.44, h * 0.44, C('#2E3236'))
        for a in range(0, 360, 20):
            r = w * 0.44
            cv.line([(w / 2, h / 2), (w / 2 + r * math.cos(math.radians(a)), h / 2 + r * math.sin(math.radians(a)))],
                    C('#8D9297'), 0.01)
        for r in (0.15, 0.3, 0.44):
            cv.ellipse(w / 2, h / 2, w * r, h * r, None, outline=C('#8D9297'), w=0.012)
        cv.ellipse(w / 2, h / 2, w * 0.08, h * 0.08, C('#55595E'))

    def intercom(cv):
        w, h = cv.w_m, cv.h_m
        cv.rect(0, 0, w, h, C('#B8B2A2'))
        cv.rect(0.02, h * 0.62, w - 0.02, h - 0.03, C('#4A4F55'))
        for k in range(6):
            y = h * 0.08 + k * h * 0.085
            cv.rect(0.03, y, w * 0.55, y + h * 0.05, C('#EDE8DB'))
            cv.ellipse(w * 0.75, y + h * 0.025, 0.012, 0.012, C('#6B6457'))

    def meter(cv):
        w, h = cv.w_m, cv.h_m
        cv.rect(0, 0, w, h, C('#8B9196'))
        cv.ellipse(w / 2, h * 0.6, w * 0.3, w * 0.3, C('#DDE6EA'))
        cv.rect(w * 0.3, h * 0.2, w * 0.7, h * 0.3, C('#2E3236'))

    def skylight(cv):
        w, h = cv.w_m, cv.h_m
        cv.vgrad(0, 0, w, h, C('#56687A'), C('#AFC2D1'))
        cv.line([(0, h * 0.3), (w, h * 0.9)], C('#E6EEF3'), 0.04)

    signs = [('ac', 0.7, 0.42, 300, ac, C('#DCD8CE')), ('vent', 0.5, 0.5, 220, vent, C('#8E949A')),
             ('door_green', 1.0, 2.25, 110, door_res('#2F4F3F'), C('#2F4F3F')),
             ('door_red', 1.0, 2.25, 110, door_res('#7E2F25'), C('#7E2F25')),
             ('door_blue', 1.0, 2.25, 110, door_res('#2C3E5C'), C('#2C3E5C')),
             ('door_black', 1.0, 2.25, 110, door_res('#22262A'), C('#22262A')),
             ('door_steel', 0.95, 2.1, 110, door_steel, C('#5B6670')),
             ('fan', 0.8, 0.8, 160, fan, C('#B7BCC0')), ('intercom', 0.2, 0.34, 300, intercom, C('#B8B2A2')),
             ('meter', 0.3, 0.45, 200, meter, C('#8B9196')), ('skylight', 1.0, 1.0, 100, skylight, C('#56687A'))]
    build_sign_atlas('T_Details.png', signs, w=512, bg=(128, 128, 128))


def glass_sheen():
    """Optional transparent shop-glass layer (RGBA): faint tint + diagonal streaks."""
    n = 256
    img = Image.new('RGBA', (n, n), (205, 222, 230, 34))
    d = ImageDraw.Draw(img, 'RGBA')
    for x0, w, a in ((30, 40, 40), (90, 14, 30), (170, 55, 34), (240, 10, 26)):
        for ox in (-n, 0, n):
            d.polygon([(x0 + ox, 0), (x0 + w + ox, 0), (x0 + w - 100 + ox, n), (x0 - 100 + ox, n)],
                      fill=(255, 255, 255, a))
    img = img.filter(ImageFilter.GaussianBlur(2))
    save(img, 'T_GlassSheen.png', keep_alpha=True)
    register('T_GlassSheen.png', (n, n), {}, tile_m=2.0)


# ------------------------------------------------------------------------------------------------------- main

def surfaces():
    masonry('T_BrickRed.png', '#9B4A3A', '#B9AB9A', 20, 7, joint=5.0, var=0.07, dark_frac=0.06, light_frac=0.05, seed=1)
    masonry('T_BrickBrown.png', '#7A4B3A', '#A79C8D', 20, 7, joint=5.0, var=0.07, dark_frac=0.07, light_frac=0.04,
            seed=2)
    masonry('T_BrickCommon.png', '#87553F', '#9C968C', 20, 7, joint=5.5, var=0.11, dark_frac=0.12, light_frac=0.06,
            seed=3, hue_var=0.05)
    masonry('T_BrickTeal.png', '#4F7D7A', '#436B68', 20, 7, joint=4.0, var=0.025, bevel=0.07, grain=0.03, wear=0.04,
            seed=4, hue_var=0.0, joint_noise=0.03)
    masonry('T_BrickSlate.png', '#4A4F5A', '#3E424B', 20, 7, joint=4.0, var=0.025, bevel=0.08, grain=0.03, wear=0.04,
            seed=5, hue_var=0.0, joint_noise=0.03)
    masonry('T_StoneCream.png', '#D8CDB5', '#B8AD96', 4, 2, joint=3.0, var=0.035, bevel=0.05, grain=0.06, seed=6,
            hue_var=0.01)
    masonry('T_PanelLight.png', '#D6D3CB', '#8D8C88', 4, 2, joint=3.0, var=0.02, bond=0.0, bevel=0.03, grain=0.025,
            seed=7, hue_var=0.0)
    roof_texture()
    wood_staves()
    shutter()
    grate()
    awnings()
    foliage()


def main():
    global TEX
    out = sys.argv[1] if len(sys.argv) > 1 else 'build_art/opening/buildings'
    TEX = os.path.join(out, 'textures')
    os.makedirs(TEX, exist_ok=True)
    surfaces()
    window_atlases()
    details()
    glass_sheen()
    signs_lit()
    sign_cafe()
    sign_mart()
    sign_pharmacy()
    sign_diner()
    interiors_corner()
    with open(os.path.join(out, 'atlas_layout.json'), 'w') as fh:
        json.dump(LAYOUT, fh, indent=1, sort_keys=True)
    print('layout ->', os.path.join(out, 'atlas_layout.json'), len(LAYOUT), 'textures')


if __name__ == '__main__':
    main()
