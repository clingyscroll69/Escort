"""Textures for the opening street kit (spec docs/superpowers/specs/2026-10-01-opening-street-design.md §5).
PIL/numpy only, deterministic. All copy is draft placeholder text (listed in manifest.json "draft_copy").

Usage: /opt/anaconda3/bin/python3 tools/opening_street_textures.py <out_dir>/textures
"""
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

SUP = '/System/Library/Fonts/Supplemental/'
FONTS = {
    'din': (SUP + 'DIN Condensed Bold.ttf', 0),
    'narrow': (SUP + 'Arial Narrow Bold.ttf', 0),
    'arialb': (SUP + 'Arial Bold.ttf', 0),
    'black': (SUP + 'Arial Black.ttf', 0),
    'impact': (SUP + 'Impact.ttf', 0),
    'chalk': (SUP + 'Chalkduster.ttf', 0),
    'futura_b': (SUP + 'Futura.ttc', 2),
    'futura_xb': (SUP + 'Futura.ttc', 4),
    'futura_m': (SUP + 'Futura.ttc', 0),
    'avenir_heavy': ('/System/Library/Fonts/Avenir Next Condensed.ttc', 8),
    'avenir_demi_i': ('/System/Library/Fonts/Avenir Next Condensed.ttc', 3),
    'georgia_b': (SUP + 'Georgia Bold.ttf', 0),
    'rounded': (SUP + 'Arial Rounded Bold.ttf', 0),
}
SS = 2  # supersampling factor


def font(name, size):
    path, idx = FONTS[name]
    return ImageFont.truetype(path, int(size), index=idx)


def hexc(h, a=255):
    h = h.lstrip('#')
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), a)


def canvas(w, h, bg):
    return Image.new('RGBA', (w * SS, h * SS), hexc(bg) if isinstance(bg, str) else bg)


def save(img, out_dir, name, size):
    img = img.resize(size, Image.LANCZOS).convert('RGB')
    path = os.path.join(out_dir, name)
    img.save(path, optimize=True)
    print('[tex]', path, size)


def text_c(d, xy, s, f, fill, anchor='mm', stroke=0, stroke_fill=None, spacing=0):
    """Draw text centred at xy (canvas pixels, already supersampled)."""
    if spacing:
        # manual letter spacing
        widths = [d.textlength(ch, font=f) for ch in s]
        total = sum(widths) + spacing * (len(s) - 1)
        x = xy[0] - total / 2
        for ch, w in zip(s, widths):
            d.text((x + w / 2, xy[1]), ch, font=f, fill=fill, anchor='m' + anchor[1], stroke_width=stroke,
                   stroke_fill=stroke_fill)
            x += w + spacing
        return
    d.text(xy, s, font=f, fill=fill, anchor=anchor, stroke_width=stroke, stroke_fill=stroke_fill)


def fit_font(d, s, name, max_w, max_size):
    size = max_size
    while size > 6:
        f = font(name, size)
        if d.textlength(s, font=f) <= max_w:
            return f
        size -= 2
    return font(name, size)


def rrect(d, box, r, fill=None, outline=None, width=1):
    d.rounded_rectangle(box, radius=r, fill=fill, outline=outline, width=width)


def noise(w, h, seed, scale=8, octaves=3):
    """Deterministic smooth value noise in [0, 1]."""
    rng = np.random.default_rng(seed)
    out = np.zeros((h, w))
    amp, tot = 1.0, 0.0
    for o in range(octaves):
        s = max(2, scale * (2 ** o))
        g = rng.random((s + 1, s + 1))
        img = Image.fromarray((g * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC)
        out += amp * np.asarray(img, dtype=float) / 255.0
        tot += amp
        amp *= 0.5
    return out / tot


# --------------------------------------------------------------------------------------------------------------
def street_names(out):
    """Two green street-name blades (rows of 1024 x 256): MAPLE AVE, 3RD ST. Each row maps to a 1.07 x 0.27 m blade."""
    W, H = 1024, 512
    img = canvas(W, H, '#0D6B3E')
    d = ImageDraw.Draw(img)
    rows = [('MAPLE', 'AVE', '300'), ('3', 'RD ST', '100')]
    for i, (big, small, block) in enumerate(rows):
        y0 = i * 256 * SS
        rrect(d, (10 * SS, y0 + 10 * SS, (W - 10) * SS, y0 + 246 * SS), 22 * SS, outline=hexc('#F4F6F2'), width=7 * SS)
        fb = font('din', 196 * SS)
        fs = font('din', 112 * SS)
        fblock = font('din', 54 * SS)
        wb = d.textlength(big, font=fb)
        ws = d.textlength(small, font=fs)
        gap = 22 * SS
        x = (W * SS - (wb + gap + ws)) / 2 + 30 * SS
        base = y0 + 206 * SS
        d.text((x, base), big, font=fb, fill=hexc('#F4F6F2'), anchor='ls')
        d.text((x + wb + gap, base), small, font=fs, fill=hexc('#F4F6F2'), anchor='ls')
        d.text((70 * SS, y0 + 128 * SS), block, font=fblock, fill=hexc('#F4F6F2'), anchor='mm')
    save(img, out, 'T_StreetNames.png', (W, H))


def led_icon(mask_fn, color, name, out, res=256, grid=28):
    """LED pedestrian icon: solid silhouette with a subtle LED dot structure on a near-black panel."""
    big = 1024
    m = Image.new('L', (big, big), 0)
    mask_fn(ImageDraw.Draw(m), big)
    m = np.asarray(m.filter(ImageFilter.GaussianBlur(2)), dtype=float) / 255.0
    yy, xx = np.mgrid[0:big, 0:big]
    cell = big / grid
    cx = (np.floor(xx / cell) + 0.5) * cell
    cy = (np.floor(yy / cell) + 0.5) * cell
    r = np.hypot(xx - cx, yy - cy) / (cell / 2)
    dot = np.clip(1.25 - r, 0, 1) ** 0.6           # bright LED cores
    col = np.array(hexc(color)[:3], dtype=float)
    off = np.array([20, 20, 20], dtype=float)
    bg = np.array([9, 9, 10], dtype=float)
    # off-LEDs faintly visible everywhere, lit LEDs inside the icon (gaps between LEDs stay 70 % bright)
    lit = (0.70 + 0.30 * dot)[..., None] * col
    unlit = bg + (off - bg) * dot[..., None]
    img = unlit * (1 - m[..., None]) + lit * m[..., None]
    Image.fromarray(np.clip(img, 0, 255).astype(np.uint8)).resize((res, res), Image.LANCZOS).save(
        os.path.join(out, name), optimize=True)
    print('[tex]', os.path.join(out, name), (res, res))


def hand_mask(d, S):
    # raised hand, palm out, fingers together, thumb out to the left
    u = S / 100.0
    d.rounded_rectangle((30 * u, 42 * u, 72 * u, 86 * u), radius=12 * u, fill=255)      # palm
    for i, (x, top) in enumerate([(31, 17), (41.5, 11), (52, 13), (62.5, 20)]):           # fingers
        d.rounded_rectangle((x * u, top * u, (x + 9.5) * u, 56 * u), radius=4.75 * u, fill=255)
    d.polygon([(30 * u, 66 * u), (17 * u, 46 * u), (14 * u, 38 * u), (20 * u, 35 * u), (27 * u, 43 * u),
               (36 * u, 54 * u)], fill=255)                                             # thumb
    d.ellipse((13 * u, 33 * u, 23 * u, 43 * u), fill=255)
    d.rounded_rectangle((36 * u, 80 * u, 70 * u, 93 * u), radius=5 * u, fill=255)       # wrist


def walk_mask(d, S):
    # walking person in stride, facing left
    u = S / 100.0
    w = int(8.5 * u)
    d.ellipse((44 * u, 8 * u, 58 * u, 22 * u), fill=255)                               # head
    d.line([(51 * u, 27 * u), (47 * u, 55 * u)], fill=255, width=int(12 * u))           # torso
    d.line([(50 * u, 31 * u), (38 * u, 44 * u), (33 * u, 55 * u)], fill=255, width=w, joint='curve')   # front arm
    d.line([(51 * u, 31 * u), (61 * u, 43 * u), (64 * u, 54 * u)], fill=255, width=w, joint='curve')   # back arm
    d.line([(47 * u, 54 * u), (37 * u, 72 * u), (31 * u, 91 * u)], fill=255, width=int(9.5 * u), joint='curve')
    d.line([(47 * u, 54 * u), (57 * u, 72 * u), (66 * u, 89 * u)], fill=255, width=int(9.5 * u), joint='curve')
    d.ellipse((26 * u, 87 * u, 36 * u, 95 * u), fill=255)
    d.ellipse((62 * u, 85 * u, 72 * u, 93 * u), fill=255)
    for (x, y) in [(51, 27), (47, 55), (38, 44), (61, 43)]:
        d.ellipse(((x - 5) * u, (y - 5) * u, (x + 5) * u, (y + 5) * u), fill=255)


def push_button_sign(out):
    """R10-3 style sign above the push button (portrait 9 x 12 in)."""
    W, H = 384, 512
    img = canvas(W, H, '#F3F3EE')
    d = ImageDraw.Draw(img)
    rrect(d, (8 * SS, 8 * SS, (W - 8) * SS, (H - 8) * SS), 22 * SS, outline=hexc('#141414'), width=7 * SS)
    k = hexc('#141414')
    f1 = font('arialb', 58 * SS)
    text_c(d, (W / 2 * SS, 70 * SS), 'PUSH', f1, k)
    text_c(d, (W / 2 * SS, 136 * SS), 'BUTTON', f1, k)
    f2 = font('arialb', 40 * SS)
    text_c(d, (W / 2 * SS, 196 * SS), 'FOR', f2, k)
    text_c(d, (W / 2 * SS, 254 * SS), 'WALK', f1, k)
    text_c(d, (W / 2 * SS, 318 * SS), 'SIGNAL', f1, k)
    # little walking figure + arrow pointing left (toward the crosswalk the button serves)
    fig = Image.new('L', (400, 400), 0)
    walk_mask(ImageDraw.Draw(fig), 400)
    fig = fig.resize((92 * SS, 92 * SS), Image.LANCZOS)
    img.paste(Image.new('RGBA', fig.size, k), (int(W * SS * 0.62), 372 * SS), fig)
    ay = 420 * SS
    d.polygon([(48 * SS, ay), (110 * SS, ay - 42 * SS), (110 * SS, ay - 16 * SS), (210 * SS, ay - 16 * SS),
               (210 * SS, ay + 16 * SS), (110 * SS, ay + 16 * SS), (110 * SS, ay + 42 * SS)], fill=k)
    save(img, out, 'T_PushButtonSign.png', (W, H))


def flower(d, cx, cy, r, petal, centre, n=5, rot=0.0):
    for i in range(n):
        a = rot + 2 * math.pi * i / n
        px, py = cx + math.cos(a) * r * 0.62, cy + math.sin(a) * r * 0.62
        d.ellipse((px - r * 0.48, py - r * 0.48, px + r * 0.48, py + r * 0.48), fill=petal)
    d.ellipse((cx - r * 0.34, cy - r * 0.34, cx + r * 0.34, cy + r * 0.34), fill=centre)


def banner(out):
    """Lamp-post banner (0.6 x 1.2 m): SPRING MARKET · SAT."""
    W, H = 512, 1024
    img = canvas(W, H, '#F2E6C9')
    d = ImageDraw.Draw(img)
    green, ochre, coral, teal = hexc('#2F6B4F'), hexc('#C98A3A'), hexc('#E0705A'), hexc('#4F7D7A')
    # top band and scalloped edge
    d.rectangle((0, 0, W * SS, 210 * SS), fill=teal)
    for i in range(9):
        cx = (i + 0.5) * W / 8 * SS
        d.ellipse((cx - 34 * SS, 176 * SS, cx + 34 * SS, 244 * SS), fill=teal)
    # sun with rays
    cx, cy = W / 2 * SS, 104 * SS
    for i in range(12):
        a = 2 * math.pi * i / 12
        d.polygon([(cx + math.cos(a - 0.12) * 52 * SS, cy + math.sin(a - 0.12) * 52 * SS),
                   (cx + math.cos(a) * 86 * SS, cy + math.sin(a) * 86 * SS),
                   (cx + math.cos(a + 0.12) * 52 * SS, cy + math.sin(a + 0.12) * 52 * SS)], fill=hexc('#F2C14E'))
    d.ellipse((cx - 50 * SS, cy - 50 * SS, cx + 50 * SS, cy + 50 * SS), fill=hexc('#F2C14E'))
    f = font('futura_xb', 128 * SS)
    text_c(d, (W / 2 * SS, 330 * SS), 'SPRING', f, green)
    text_c(d, (W / 2 * SS, 450 * SS), 'MARKET', fit_font(d, 'MARKET', 'futura_xb', 470 * SS, 128 * SS), green)
    d.ellipse((W / 2 * SS - 13 * SS, 527 * SS, W / 2 * SS + 13 * SS, 553 * SS), fill=ochre)
    text_c(d, (W / 2 * SS, 650 * SS), 'SAT', font('futura_xb', 190 * SS), coral)
    # tulips and daisies along the bottom
    d.rectangle((0, 880 * SS, W * SS, H * SS), fill=hexc('#7FAE5E'))
    for i, x in enumerate([60, 150, 250, 350, 450]):
        x *= SS
        d.line([(x, 880 * SS), (x + (i % 2 * 2 - 1) * 6 * SS, 800 * SS)], fill=green, width=8 * SS)
        d.ellipse((x - 30 * SS, 860 * SS, x + 10 * SS, 884 * SS), fill=green)
        col = [coral, ochre, hexc('#D9568B'), coral, ochre][i]
        d.ellipse((x - 26 * SS, 752 * SS, x + 26 * SS, 812 * SS), fill=col)
        d.polygon([(x - 26 * SS, 780 * SS), (x - 22 * SS, 742 * SS), (x - 8 * SS, 768 * SS), (x, 738 * SS),
                   (x + 8 * SS, 768 * SS), (x + 22 * SS, 742 * SS), (x + 26 * SS, 780 * SS)], fill=col)
    for (x, y) in [(105, 930), (205, 960), (300, 925), (400, 955), (40, 975), (470, 920)]:
        flower(d, x * SS, y * SS, 22 * SS, hexc('#FBF7EC'), hexc('#F2C14E'))
    save(img, out, 'T_BannerSpringMarket.png', (W, H))


def newspaper(d, box, title, accent, seed):
    """Front page seen through the vending box window."""
    x0, y0, x1, y1 = box
    rng = np.random.default_rng(seed)
    d.rectangle(box, fill=hexc('#ECE8DD'))
    w = x1 - x0
    f = fit_font(d, title, 'georgia_b', w * 0.86, 64 * SS)
    text_c(d, ((x0 + x1) / 2, y0 + 40 * SS), title, f, hexc('#1A1A1A'))
    d.line([(x0 + 14 * SS, y0 + 74 * SS), (x1 - 14 * SS, y0 + 74 * SS)], fill=hexc('#1A1A1A'), width=3 * SS)
    # headline bars
    for i, frac in enumerate((0.9, 0.62)):
        yy = y0 + (92 + i * 30) * SS
        d.rectangle((x0 + 16 * SS, yy, x0 + 16 * SS + (w - 32 * SS) * frac, yy + 20 * SS), fill=hexc('#262626'))
    # photo
    py = y0 + 160 * SS
    d.rectangle((x0 + 16 * SS, py, x0 + w * 0.58, py + 120 * SS), fill=accent)
    d.polygon([(x0 + 16 * SS, py + 120 * SS), (x0 + 70 * SS, py + 60 * SS), (x0 + 120 * SS, py + 100 * SS),
               (x0 + 170 * SS, py + 50 * SS), (x0 + w * 0.58, py + 120 * SS)], fill=hexc('#3E4A55'))
    d.ellipse((x0 + w * 0.42, py + 16 * SS, x0 + w * 0.42 + 30 * SS, py + 46 * SS), fill=hexc('#F2D27A'))
    # text columns
    for col in range(2):
        cx0 = x0 + w * 0.62 + col * 0 if col == 0 else x0 + 16 * SS
        for r in range(14 if col == 0 else 7):
            yy = py + (r * 18) * SS if col == 0 else py + (136 + r * 18) * SS
            if col == 0:
                ln = (x1 - 14 * SS) - cx0
            else:
                ln = (x1 - 14 * SS) - cx0
            ln *= 0.75 + 0.25 * rng.random()
            d.rectangle((cx0, yy, cx0 + ln, yy + 7 * SS), fill=hexc('#8A877F'))


def news_fronts(out):
    """Front door panels of the two newspaper boxes, side by side (each 512 x 512)."""
    W, H = 1024, 512
    img = canvas(W, H, '#000000')
    d = ImageDraw.Draw(img)
    specs = [('DAILY NEWS', '#2459A8', '#F4F4F0', 'The Daily News', '#8FB8DE', '50¢'),
             ('CITY WEEKLY', '#E3B21C', '#1C1C1C', 'City Weekly', '#D98B5F', 'FREE')]
    for i, (name, body, ink, title, accent, price) in enumerate(specs):
        x0 = i * 512 * SS
        d.rectangle((x0, 0, x0 + 512 * SS, H * SS), fill=hexc(body))
        f = fit_font(d, name, 'impact', 470 * SS, 92 * SS)
        text_c(d, (x0 + 256 * SS, 62 * SS), name, f, hexc(ink))
        # window with frame
        wb = (x0 + 46 * SS, 122 * SS, x0 + 466 * SS, 430 * SS)
        rrect(d, (wb[0] - 10 * SS, wb[1] - 10 * SS, wb[2] + 10 * SS, wb[3] + 10 * SS), 10 * SS, fill=hexc('#2A2C30'))
        newspaper(d, wb, title, hexc(accent), seed=11 + i)
        # glass sheen
        sheen = Image.new('RGBA', img.size, (0, 0, 0, 0))
        sd = ImageDraw.Draw(sheen)
        sd.polygon([(wb[0] + 40 * SS, wb[1]), (wb[0] + 140 * SS, wb[1]), (wb[0] + 40 * SS, wb[3]),
                    (wb[0] - 60 * SS, wb[3])], fill=(255, 255, 255, 46))
        img.alpha_composite(sheen)
        d = ImageDraw.Draw(img)
        # price plate + handle
        rrect(d, (x0 + 46 * SS, 452 * SS, x0 + 200 * SS, 498 * SS), 8 * SS, fill=hexc('#E9E9E4'))
        text_c(d, (x0 + 123 * SS, 476 * SS), price, font('arialb', 32 * SS), hexc('#1C1C1C'))
        rrect(d, (x0 + 300 * SS, 462 * SS, x0 + 466 * SS, 486 * SS), 12 * SS, fill=hexc('#B9BCC0'))
    save(img, out, 'T_NewsBoxFronts.png', (W, H))


def star_poly(cx, cy, r_out, r_in, n=5, rot=-math.pi / 2):
    pts = []
    for i in range(2 * n):
        r = r_out if i % 2 == 0 else r_in
        a = rot + math.pi * i / n
        pts.append((cx + math.cos(a) * r, cy + math.sin(a) * r))
    return pts


def glint(d, cx, cy, r, col):
    d.polygon([(cx, cy - r), (cx + r * 0.18, cy - r * 0.18), (cx + r, cy), (cx + r * 0.18, cy + r * 0.18), (cx, cy + r),
               (cx - r * 0.18, cy + r * 0.18), (cx - r, cy), (cx - r * 0.18, cy - r * 0.18)], fill=col)


def ad_hero_summoner(out):
    """Back-lit bus-shelter ad for the gacha game (1185 x 1750 mm panel -> 704 x 1024 px)."""
    W, H = 704, 1024
    w, h = W * SS, H * SS
    yy, xx = np.mgrid[0:h, 0:w]
    cx, cy = w * 0.5, h * 0.40
    r = np.hypot(xx - cx, (yy - cy) * 0.85) / (h * 0.75)
    c0, c1 = np.array([104, 62, 196], float), np.array([18, 12, 52], float)
    t = np.clip(r, 0, 1)[..., None]
    bg = c0 * (1 - t) + c1 * t
    # light rays from behind the star
    ang = np.arctan2(yy - cy, xx - cx)
    rays = (np.sin(ang * 14) > 0.55) * np.clip(1.0 - r * 1.2, 0, 1)
    bg += rays[..., None] * np.array([60, 50, 90], float)
    img = Image.fromarray(np.clip(bg, 0, 255).astype(np.uint8)).convert('RGBA')
    d = ImageDraw.Draw(img)
    gold, gold_d, white = hexc('#FFD45A'), hexc('#C8861E'), hexc('#FFFFFF')
    # gold star with a darker inner star
    d.polygon(star_poly(cx, cy, 250 * SS, 108 * SS), fill=gold_d)
    d.polygon(star_poly(cx, cy - 6 * SS, 232 * SS, 98 * SS), fill=gold)
    d.polygon(star_poly(cx, cy - 6 * SS, 150 * SS, 64 * SS), fill=hexc('#FFE89A'))
    # sword silhouette (point down), dark outline then silver
    def sword(dd, grow, colb, colg, colh):
        g = grow * SS
        blade = [(cx - 26 * SS - g, cy - 210 * SS), (cx + 26 * SS + g, cy - 210 * SS), (cx + 26 * SS + g, cy + 250 * SS),
                 (cx, cy + 330 * SS + g * 1.5), (cx - 26 * SS - g, cy + 250 * SS)]
        dd.polygon(blade, fill=colb)
        dd.rounded_rectangle((cx - 110 * SS - g, cy - 238 * SS - g, cx + 110 * SS + g, cy - 206 * SS + g), 14 * SS,
                             fill=colg)
        dd.rounded_rectangle((cx - 17 * SS - g, cy - 350 * SS - g, cx + 17 * SS + g, cy - 236 * SS + g), 10 * SS,
                             fill=colh)
        dd.ellipse((cx - 30 * SS - g, cy - 392 * SS - g, cx + 30 * SS + g, cy - 332 * SS + g), fill=colg)
    sword(d, 9, hexc('#160C30'), hexc('#160C30'), hexc('#160C30'))
    sword(d, 0, hexc('#E4E8F2'), gold, hexc('#7A3B2A'))
    d.polygon([(cx, cy - 206 * SS), (cx + 26 * SS, cy - 206 * SS), (cx + 26 * SS, cy + 250 * SS), (cx, cy + 330 * SS)],
              fill=hexc('#B9C0D2'))                                        # shaded half of the blade
    d.ellipse((cx - 14 * SS, cy - 236 * SS, cx + 14 * SS, cy - 208 * SS), fill=hexc('#E0305A'))   # gem
    # sparkles
    rng = np.random.default_rng(5)
    for i in range(46):
        x, y = rng.uniform(0.04, 0.96) * w, rng.uniform(0.03, 0.97) * h
        if abs(x - cx) < 90 * SS and abs(y - cy) < 330 * SS:
            continue
        rr = rng.uniform(6, 22) * SS
        glint(d, x, y, rr, (255, 246, 210, int(rng.uniform(140, 255))))
    # title
    f_hero = font('futura_xb', 150 * SS)
    for off, col in ((10, hexc('#160C30')), (0, gold)):
        text_c(d, (w / 2 + off * 0.3, 118 * SS + off), 'HERO', f_hero, col, stroke=8 * SS if off else 0,
               stroke_fill=hexc('#160C30'))
    f_sum = fit_font(d, 'SUMMONER', 'futura_xb', 640 * SS, 112 * SS)
    text_c(d, (w / 2, 236 * SS), 'SUMMONER', f_sum, white, stroke=7 * SS, stroke_fill=hexc('#160C30'))
    # tagline + call to action
    f_tag = fit_font(d, 'Will today be the day', 'avenir_demi_i', 600 * SS, 54 * SS)
    text_c(d, (w / 2, 812 * SS), 'Will today be the day', f_tag, white, stroke=3 * SS, stroke_fill=hexc('#160C30'))
    text_c(d, (w / 2, 866 * SS), "you're chosen?", f_tag, white, stroke=3 * SS, stroke_fill=hexc('#160C30'))
    pill = (w / 2 - 290 * SS, 912 * SS, w / 2 + 290 * SS, 992 * SS)
    rrect(d, (pill[0] - 6 * SS, pill[1] - 6 * SS, pill[2] + 6 * SS, pill[3] + 6 * SS), 46 * SS, fill=hexc('#160C30'))
    rrect(d, pill, 40 * SS, fill=hexc('#FF9F2E'))
    rrect(d, (pill[0] + 10 * SS, pill[1] + 8 * SS, pill[2] - 10 * SS, (pill[1] + pill[3]) / 2), 30 * SS,
          fill=hexc('#FFC15C'))
    f_cta = fit_font(d, 'DAILY SUMMON READY', 'futura_xb', 520 * SS, 50 * SS)
    text_c(d, (w / 2, 953 * SS), 'DAILY SUMMON READY', f_cta, hexc('#2A1250'))
    glint(d, pill[2] - 20 * SS, pill[1] + 4 * SS, 26 * SS, white)
    save(img, out, 'T_AdHeroSummoner.png', (W, H))


def bus_stop_sign(out):
    """Bus stop sign plate (0.3 x 0.6 m): bus pictogram, BUS STOP, routes 12 · 47."""
    W, H = 256, 512
    img = canvas(W, H, '#1E4FA3')
    d = ImageDraw.Draw(img)
    white, blue = hexc('#F5F6F2'), hexc('#1E4FA3')
    rrect(d, (8 * SS, 8 * SS, (W - 8) * SS, (H - 8) * SS), 16 * SS, outline=white, width=5 * SS)
    # bus pictogram
    bx, by = 48 * SS, 44 * SS
    rrect(d, (bx, by, bx + 160 * SS, by + 120 * SS), 18 * SS, fill=white)
    d.rectangle((bx + 14 * SS, by + 18 * SS, bx + 146 * SS, by + 62 * SS), fill=blue)
    d.line([(bx + 80 * SS, by + 18 * SS), (bx + 80 * SS, by + 62 * SS)], fill=white, width=4 * SS)
    for wx in (bx + 34 * SS, bx + 126 * SS):
        d.ellipse((wx - 16 * SS, by + 106 * SS, wx + 16 * SS, by + 138 * SS), fill=white)
        d.ellipse((wx - 7 * SS, by + 115 * SS, wx + 7 * SS, by + 129 * SS), fill=blue)
    d.rectangle((bx + 18 * SS, by + 80 * SS, bx + 40 * SS, by + 92 * SS), fill=blue)
    d.rectangle((bx + 120 * SS, by + 80 * SS, bx + 142 * SS, by + 92 * SS), fill=blue)
    f = font('din', 74 * SS)
    text_c(d, (W / 2 * SS, 236 * SS), 'BUS', f, white)
    text_c(d, (W / 2 * SS, 304 * SS), 'STOP', f, white)
    # route tiles 12 · 47
    for i, rt in enumerate(('12', '47')):
        x0 = (34 + i * 104) * SS
        rrect(d, (x0, 360 * SS, x0 + 84 * SS, 452 * SS), 10 * SS, fill=white)
        text_c(d, (x0 + 42 * SS, 410 * SS), rt, font('din', 78 * SS), hexc('#141414'))
    d.ellipse((124 * SS, 398 * SS, 132 * SS, 406 * SS), fill=white)
    text_c(d, (W / 2 * SS, 480 * SS), 'STOP 3417', font('din', 28 * SS), white)
    save(img, out, 'T_BusStopSign.png', (W, H))


def no_parking(out):
    """R7-1 style NO PARKING sign (12 x 18 in)."""
    W, H = 256, 384
    img = canvas(W, H, '#F4F4EF')
    d = ImageDraw.Draw(img)
    red, k = hexc('#C4262E'), hexc('#141414')
    rrect(d, (6 * SS, 6 * SS, (W - 6) * SS, (H - 6) * SS), 16 * SS, outline=red, width=6 * SS)
    cx, cy, r = W / 2 * SS, 110 * SS, 78 * SS
    text_c(d, (cx, cy + 4 * SS), 'P', font('arialb', 124 * SS), k)
    d.ellipse((cx - r, cy - r, cx + r, cy + r), outline=red, width=14 * SS)
    a = math.radians(45)
    d.line([(cx - math.cos(a) * r, cy - math.sin(a) * r), (cx + math.cos(a) * r, cy + math.sin(a) * r)], fill=red,
           width=14 * SS)
    f = font('arialb', 56 * SS)
    text_c(d, (cx, 236 * SS), 'NO', f, red)
    text_c(d, (cx, 296 * SS), 'PARKING', fit_font(d, 'PARKING', 'arialb', 220 * SS, 50 * SS), red)
    d.polygon([(40 * SS, 346 * SS), (70 * SS, 326 * SS), (70 * SS, 338 * SS), (186 * SS, 338 * SS), (186 * SS, 326 * SS),
               (216 * SS, 346 * SS), (186 * SS, 366 * SS), (186 * SS, 354 * SS), (70 * SS, 354 * SS), (70 * SS, 366 * SS)],
              fill=red)
    save(img, out, 'T_NoParking.png', (W, H))


def manhole(out):
    """Cast-iron manhole cover pattern (planar map over the 0.65 m disc)."""
    S = 512 * SS
    base = np.full((S, S, 3), [66, 66, 68], float)
    yy, xx = np.mgrid[0:S, 0:S]
    c = S / 2
    r = np.hypot(xx - c, yy - c) / c
    ang = np.arctan2(yy - c, xx - c)
    raised = np.zeros((S, S), bool)
    raised |= (r > 0.93) & (r < 0.985)                                  # rim
    raised |= (r > 0.66) & (r < 0.70)
    raised |= (r < 0.20)
    # diamond waffle in the centre field
    u, v = (xx - c) / c, (yy - c) / c
    a = np.abs(((u + v) * 9) % 1 - 0.5) < 0.12
    b = np.abs(((u - v) * 9) % 1 - 0.5) < 0.12
    raised |= (r > 0.22) & (r < 0.64) & (a | b)
    # radial ribs in the outer band
    raised |= (r > 0.70) & (r < 0.75) & (np.abs((ang / (2 * np.pi) * 48) % 1 - 0.5) < 0.18)
    col = np.where(raised[..., None], np.array([104, 103, 101], float), base)
    n = noise(S, S, 3, scale=12)
    rust = noise(S, S, 4, scale=5)
    col *= (0.88 + 0.24 * n)[..., None]
    col += (np.clip(rust - 0.62, 0, 1) * 160)[..., None] * np.array([0.55, 0.28, 0.12])
    col = np.where((r > 1.0)[..., None], np.array([80, 79, 77], float), col)
    img = Image.fromarray(np.clip(col, 0, 255).astype(np.uint8)).convert('RGBA')
    d = ImageDraw.Draw(img)
    # lettering around the outer band (raised)
    word = 'CITY  SEWER  ·  1926  ·  '
    f = font('arialb', 30 * SS)
    for i, ch in enumerate(word * 2):
        th = -math.pi / 2 + 2 * math.pi * i / (len(word) * 2)
        chimg = Image.new('RGBA', (60 * SS, 60 * SS), (0, 0, 0, 0))
        ImageDraw.Draw(chimg).text((30 * SS, 30 * SS), ch, font=f, fill=(112, 110, 106, 255), anchor='mm')
        chimg = chimg.rotate(-math.degrees(th) - 90, resample=Image.BICUBIC)
        rr = 0.835 * c
        img.alpha_composite(chimg, (int(c + math.cos(th) * rr - 30 * SS), int(c + math.sin(th) * rr - 30 * SS)))
    text_c(ImageDraw.Draw(img), (c, c), 'S', font('georgia_b', 56 * SS), (52, 52, 54, 255))
    save(img, out, 'T_ManholeCover.png', (512, 512))


def chalkboard(out):
    """Café A-frame board (0.6 x 0.9 m face): FRESH COFFEE + a cup doodle."""
    W, H = 512, 768
    S = (W * SS, H * SS)
    n = noise(S[0], S[1], 9, scale=6)
    smudge = noise(S[0], S[1], 10, scale=3)
    base = np.array([40, 48, 45], float)
    col = base[None, None, :] * (0.92 + 0.16 * n[..., None]) + (np.clip(smudge - 0.55, 0, 1) * 60)[..., None]
    img = Image.fromarray(np.clip(col, 0, 255).astype(np.uint8)).convert('RGBA')
    chalk = Image.new('RGBA', img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(chalk)
    cw, cy_, cp = (244, 240, 228, 255), (246, 214, 120, 255), (240, 160, 160, 255)
    f1 = font('chalk', 104 * SS)
    text_c(d, (W / 2 * SS, 116 * SS), 'FRESH', f1, cw)
    text_c(d, (W / 2 * SS, 236 * SS), 'COFFEE', fit_font(d, 'COFFEE', 'chalk', 470 * SS, 104 * SS), cy_)
    # wavy underline
    pts = [((60 + i * 8) * SS, (300 + 6 * math.sin(i * 0.6)) * SS) for i in range(50)]
    d.line(pts, fill=cw, width=5 * SS)
    # cup doodle with steam
    cx, top = W / 2 * SS, 420 * SS
    d.arc((cx - 110 * SS, top - 30 * SS, cx + 110 * SS, top + 30 * SS), 0, 360, fill=cw, width=6 * SS)
    d.line([(cx - 110 * SS, top), (cx - 84 * SS, top + 170 * SS)], fill=cw, width=6 * SS)
    d.line([(cx + 110 * SS, top), (cx + 84 * SS, top + 170 * SS)], fill=cw, width=6 * SS)
    d.arc((cx - 84 * SS, top + 150 * SS, cx + 84 * SS, top + 190 * SS), 0, 180, fill=cw, width=6 * SS)
    d.arc((cx + 70 * SS, top + 30 * SS, cx + 160 * SS, top + 130 * SS), -90, 90, fill=cw, width=6 * SS)
    d.arc((cx - 160 * SS, top + 190 * SS, cx + 160 * SS, top + 240 * SS), 0, 180, fill=cw, width=5 * SS)
    for k, dx in enumerate((-50, 0, 50)):
        pts = [((cx + dx * SS + 14 * SS * math.sin(i * 0.5 + k)), top - 40 * SS - i * 9 * SS) for i in range(12)]
        d.line(pts, fill=cw, width=5 * SS)
    # little heart on the cup
    hx, hy = cx, top + 90 * SS
    d.polygon([(hx, hy + 30 * SS), (hx - 30 * SS, hy), (hx - 22 * SS, hy - 18 * SS), (hx, hy - 6 * SS),
               (hx + 22 * SS, hy - 18 * SS), (hx + 30 * SS, hy)], fill=cp)
    f2 = font('chalk', 38 * SS)
    text_c(d, (W / 2 * SS, 700 * SS), 'come on in!', f2, cw)
    for (x, y) in [(60, 380), (450, 360), (420, 520), (80, 560)]:
        glint(d, x * SS, y * SS, 14 * SS, cw)
    # chalk grain: knock holes into the strokes
    grain = (noise(S[0], S[1], 12, scale=90, octaves=1) > 0.38).astype(np.uint8) * 255
    a = np.asarray(chalk.split()[3], dtype=np.uint8) * (grain // 255)
    chalk.putalpha(Image.fromarray((a * 0.92).astype(np.uint8)))
    img.alpha_composite(chalk)
    save(img, out, 'T_AFrameChalk.png', (W, H))


def glass_reflection(out):
    """Dark tinted bus-shelter glass with soft sky/skyline reflections (opaque toon material)."""
    W, H = 512, 512
    yy, xx = np.mgrid[0:H, 0:W] / float(H)
    top, bot = np.array([62, 84, 100], float), np.array([28, 38, 46], float)
    col = top * (1 - yy[..., None]) + bot * yy[..., None]
    # reflected skyline silhouettes in the lower half
    rng = np.random.default_rng(21)
    sky = np.zeros((H, W))
    x = 0
    while x < W:
        bw = int(rng.uniform(40, 110))
        bh = rng.uniform(0.38, 0.62)
        sky[int(bh * H):, x:x + bw] = 1
        x += bw + int(rng.uniform(2, 10))
    col = col * (1 - 0.22 * sky[..., None])
    # diagonal light streaks
    for (o, wdt, a) in [(0.15, 0.05, 0.22), (0.30, 0.015, 0.18), (0.62, 0.08, 0.12)]:
        dist = np.abs((xx - yy * 0.55) - o)
        col += (np.clip(1 - dist / wdt, 0, 1) * a * 255)[..., None] * np.array([0.9, 0.95, 1.0])
    Image.fromarray(np.clip(col, 0, 255).astype(np.uint8)).save(os.path.join(out, 'T_GlassReflection.png'),
                                                                 optimize=True)
    print('[tex]', os.path.join(out, 'T_GlassReflection.png'), (W, H))


def drain_marker(out):
    """Small round curb plaque next to the storm drain."""
    W = 256
    img = canvas(W, W, '#B9B4AA')   # sidewalk grey around the round plaque
    d = ImageDraw.Draw(img)
    c = W / 2 * SS
    d.ellipse((6 * SS, 6 * SS, (W - 6) * SS, (W - 6) * SS), fill=hexc('#2D6E8E'), outline=hexc('#E8EEF0'), width=6 * SS)
    f = font('arialb', 30 * SS)
    text_c(d, (c, 58 * SS), 'NO DUMPING', f, hexc('#F2F4F2'))
    # fish
    d.ellipse((c - 62 * SS, c - 30 * SS, c + 40 * SS, c + 30 * SS), fill=hexc('#F2F4F2'))
    d.polygon([(c + 30 * SS, c), (c + 76 * SS, c - 32 * SS), (c + 76 * SS, c + 32 * SS)], fill=hexc('#F2F4F2'))
    d.ellipse((c - 44 * SS, c - 10 * SS, c - 30 * SS, c + 4 * SS), fill=hexc('#2D6E8E'))
    text_c(d, (c, 190 * SS), 'DRAINS TO', font('arialb', 26 * SS), hexc('#F2F4F2'))
    text_c(d, (c, 222 * SS), 'RIVER', font('arialb', 26 * SS), hexc('#F2F4F2'))
    save(img, out, 'T_DrainMarker.png', (W, W))


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else 'build_art/opening/street/textures'
    os.makedirs(out, exist_ok=True)
    street_names(out)
    led_icon(hand_mask, '#FF7A1A', 'T_PedHand.png', out)
    led_icon(walk_mask, '#F2F5EE', 'T_PedWalk.png', out)
    push_button_sign(out)
    banner(out)
    news_fronts(out)
    ad_hero_summoner(out)
    bus_stop_sign(out)
    no_parking(out)
    manhole(out)
    chalkboard(out)
    glass_reflection(out)
    drain_marker(out)


if __name__ == '__main__':
    main()
