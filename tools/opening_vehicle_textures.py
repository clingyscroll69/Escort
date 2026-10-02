#!/usr/bin/env python3
"""Procedural textures for the opening vehicles kit (PIL + numpy, deterministic, no downloads).

  T_V_TruckLivery.png  1024x512  box-side livery (UV 0-1 per side) + the round "DF" emblem reused by the cab-door badges
  T_V_GlassReflect.png  512x512  RGBA glass: sky gradient + one soft diagonal highlight streak, alpha = opacity
  T_V_Plates.png       1024x1024 licence-plate atlas, 2 columns x 4 rows of 512x256 cells (see PLATES)
  T_V_Badge.png        1024x256  "TENSEI" chrome letters on a dark plate (truck grille badge)
  T_V_TaxiSign.png      512x128  lit "TAXI" roof sign face
  T_V_Checker.png       256x128  taxi checker band, tiles in U (4 squares x 2 rows per tile)

Usage: /opt/anaconda3/bin/python3 tools/opening_vehicle_textures.py <out_dir>/textures
The UV constants (EMBLEM, PLATES cells) are mirrored in tools/blender/opening_vehicles.py; keep them in sync.
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

SUP = '/System/Library/Fonts/Supplemental/'
SYS = '/System/Library/Fonts/'

NAVY = (31, 45, 92)
RED = (217, 68, 46)
WHITE = (255, 255, 255)

# Livery canvas is drawn at the box side's physical aspect (5.14 m x 2.33 m panel UV span) and resized to 1024x512,
# so circles and letters come out undistorted on the mesh.
LIV_W, LIV_H = 1134, 512
EMBLEM = (150, 238, 96)          # cx, cy, r in livery canvas pixels (cab-door badges sample this disc)
PLATES = ['TK 4649', 'KN 3021', 'MR 1188', 'TX 7012', 'VN 5530', 'SV 9047', 'HB 2290', 'SD 6408']


def font(path, size, index=0):
    return ImageFont.truetype(path, size, index=index)


def text_size(fnt, s):
    l, t, r, b = fnt.getbbox(s)
    return r - l, b - t, l, t


def shear_layer(img, k):
    """Italic-style horizontal shear of an RGBA layer (k = dx per dy, positive leans right)."""
    w, h = img.size
    return img.transform((w, h), Image.AFFINE, (1, k, -k * h * 0.5, 0, 1, 0), resample=Image.BICUBIC)


def draw_text_layer(size, s, fnt, fill, pos, shear=0.0, stroke=0, stroke_fill=None):
    layer = Image.new('RGBA', size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    d.text(pos, s, font=fnt, fill=fill, stroke_width=stroke, stroke_fill=stroke_fill)
    if shear:
        layer = shear_layer(layer, shear)
    return layer


def emblem(img, cx, cy, r, ss=1):
    d = ImageDraw.Draw(img)
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=RED)
    r2 = r * 0.86
    d.ellipse([cx - r2, cy - r2, cx + r2, cy + r2], fill=WHITE)
    r3 = r * 0.80
    d.ellipse([cx - r3, cy - r3, cx + r3, cy + r3], fill=NAVY)
    # white swoosh arc under the letters
    sw = Image.new('L', img.size, 0)
    sd = ImageDraw.Draw(sw)
    sd.ellipse([cx - r3 * 0.95, cy - r3 * 0.15, cx + r3 * 0.95, cy + r3 * 0.95], fill=255)
    sd.ellipse([cx - r3 * 1.05, cy - r3 * 0.30, cx + r3 * 0.85, cy + r3 * 0.80], fill=0)
    mask = Image.new('L', img.size, 0)
    ImageDraw.Draw(mask).ellipse([cx - r3, cy - r3, cx + r3, cy + r3], fill=255)
    sw = Image.fromarray(np.minimum(np.array(sw), np.array(mask)))
    img.paste(Image.new('RGBA', img.size, RED + (255,)), (0, 0), sw)
    f = font(SUP + 'Futura.ttc', int(r * 0.95), index=4)
    w, h, l, t = text_size(f, 'DF')
    layer = draw_text_layer(img.size, 'DF', f, WHITE + (255,), (cx - w / 2 - l, cy - h / 2 - t - r * 0.12), shear=-0.18)
    img.alpha_composite(layer)


def livery(out):
    S = 2  # supersample
    W, H = LIV_W * S, LIV_H * S
    img = Image.new('RGBA', (W, H), WHITE + (255,))
    d = ImageDraw.Draw(img)
    # bottom pinstripes (just above the rub rail, which covers v < 0.06)
    d.rectangle([0, int(H * 0.885), W, int(H * 0.905)], fill=NAVY)
    d.rectangle([0, int(H * 0.915), W, int(H * 0.925)], fill=RED)
    # emblem at the front end
    cx, cy, r = (v * S for v in EMBLEM)
    emblem(img, cx, cy, r)
    # main wordmark, fitted between x0 and x1
    x0, x1 = 290 * S, 1085 * S
    f = font(SUP + 'Futura.ttc', 200 * S, index=4)       # Futura Condensed ExtraBold
    s = 'DESTINY FREIGHT'
    w, h, l, t = text_size(f, s)
    scale = (x1 - x0) / w
    f = font(SUP + 'Futura.ttc', int(200 * S * scale), index=4)
    w, h, l, t = text_size(f, s)
    ty = 120 * S
    layer = draw_text_layer((W, H), s, f, NAVY + (255,), (x0 - l, ty - t), shear=-0.16)
    img.alpha_composite(layer)
    base = ty + h
    # red swoosh underline: tapered band, thin at the front, thick and kicking up at the rear
    pts_top, pts_bot = [], []
    for i in range(61):
        a = i / 60
        x = x0 - 10 * S + (x1 - x0 + 30 * S) * a
        mid = base + 34 * S - 22 * S * a ** 2.2
        th = (4 + 22 * a ** 1.4) * S
        pts_top.append((x, mid - th / 2))
        pts_bot.append((x, mid + th / 2))
    d.polygon(pts_top + pts_bot[::-1], fill=RED)
    # tagline + fleet number
    f2 = font(SYS + 'Avenir Next.ttc', 46 * S, index=3)  # Demi Bold Italic
    tag = 'Always on time.'
    w2, h2, l2, t2 = text_size(f2, tag)
    d.text((x0 + 18 * S - l2, base + 70 * S - t2), tag, font=f2, fill=NAVY)
    f3 = font(SUP + 'DIN Alternate Bold.ttf', 30 * S)
    no = 'No. 4649'
    w3, h3, l3, t3 = text_size(f3, no)
    d.text((x1 - w3 - l3, int(H * 0.80) - t3), no, font=f3, fill=NAVY)
    img = img.convert('RGB').resize((1024, 512), Image.LANCZOS)
    img.save(os.path.join(out, 'T_V_TruckLivery.png'))


def glass(out):
    N = 512
    y = np.linspace(0, 1, N)[:, None] * np.ones((1, N))           # 0 top .. 1 bottom
    x = np.ones((N, 1)) * np.linspace(0, 1, N)[None, :]
    top = np.array([168, 186, 200], float)
    mid = np.array([78, 93, 112], float)
    low = np.array([38, 46, 58], float)
    t1 = np.clip(y / 0.42, 0, 1)[..., None]
    t2 = np.clip((y - 0.42) / 0.58, 0, 1)[..., None]
    col = np.where(y[..., None] < 0.42, top * (1 - t1) + mid * t1, mid * (1 - t2) + low * t2)
    alpha = 0.66 - 0.26 * np.clip(y / 0.7, 0, 1)
    # one soft diagonal streak (lower-left to upper-right) + a faint thin companion
    dist = (x * 0.8 + y * 0.6) - 0.70
    streak = np.exp(-(dist / 0.085) ** 2) * 0.55 + np.exp(-((dist - 0.17) / 0.018) ** 2) * 0.35
    col = col * (1 - streak[..., None]) + np.array([235, 242, 248]) * streak[..., None]
    alpha = np.clip(alpha + streak * 0.40, 0, 0.92)
    rgba = np.dstack([np.clip(col, 0, 255), alpha * 255]).astype(np.uint8)
    Image.fromarray(rgba).save(os.path.join(out, 'T_V_GlassReflect.png'))


def plates(out):
    W, H = 1024, 1024
    S = 2
    img = Image.new('RGB', (W * S, H * S), (90, 90, 90))
    d = ImageDraw.Draw(img)
    f = font(SUP + 'DIN Alternate Bold.ttf', 112 * S)
    fs = font(SUP + 'DIN Alternate Bold.ttf', 34 * S)
    for i, s in enumerate(PLATES):
        col, row = i % 2, i // 2
        x0, y0 = col * 512 * S, row * 256 * S
        cw, ch = 512 * S, 256 * S
        d.rounded_rectangle([x0 + 2 * S, y0 + 2 * S, x0 + cw - 2 * S, y0 + ch - 2 * S], 22 * S, fill=(244, 244, 236))
        d.rounded_rectangle([x0 + 12 * S, y0 + 12 * S, x0 + cw - 12 * S, y0 + ch - 12 * S], 16 * S,
                            outline=(30, 58, 46), width=6 * S)
        for bx in (0.18, 0.82):  # bolt heads
            cx, cy = x0 + cw * bx, y0 + 34 * S
            d.ellipse([cx - 9 * S, cy - 9 * S, cx + 9 * S, cy + 9 * S], fill=(170, 172, 168))
        top = 'CITY' if i != 3 else 'CITY  TAXI'
        w, h, l, t = text_size(fs, top)
        d.text((x0 + cw / 2 - w / 2 - l, y0 + 26 * S - t), top, font=fs, fill=(30, 58, 46))
        w, h, l, t = text_size(f, s)
        d.text((x0 + cw / 2 - w / 2 - l, y0 + 150 * S - h / 2 - t), s, font=f, fill=(30, 58, 46))
    img.resize((W, H), Image.LANCZOS).save(os.path.join(out, 'T_V_Plates.png'))


def badge(out):
    # physical badge 0.57 x 0.112 m -> drawn at that aspect, resized to 1024x256
    S = 2
    W, H = 1303 * S, 256 * S
    img = Image.new('RGBA', (W, H), (0, 0, 0, 255))
    # chrome rim: vertical gradient ring
    g = np.linspace(0, 1, H)[:, None]
    chrome = np.clip(np.where(g < 0.5, 235 - 120 * g, 120 + 150 * (g - 0.5)), 0, 255)
    rim = np.repeat(np.repeat(chrome, W, axis=1)[..., None], 3, axis=2).astype(np.uint8)
    rim_img = Image.fromarray(rim).convert('RGBA')
    mask = Image.new('L', (W, H), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, W - 1, H - 1], 60 * S, fill=255)
    img.paste(rim_img, (0, 0), mask)
    inner = Image.new('L', (W, H), 0)
    ImageDraw.Draw(inner).rounded_rectangle([16 * S, 16 * S, W - 16 * S, H - 16 * S], 48 * S, fill=255)
    img.paste(Image.new('RGBA', (W, H), (26, 29, 34, 255)), (0, 0), inner)
    # chrome letters with spacing
    f = font(SUP + 'Futura.ttc', 168 * S, index=2)     # Futura Bold
    s = 'TENSEI'
    spacing = 46 * S
    widths = [text_size(f, ch)[0] for ch in s]
    total = sum(widths) + spacing * (len(s) - 1)
    x = (W - total) / 2
    letters = Image.new('L', (W, H), 0)
    ld = ImageDraw.Draw(letters)
    w, h, l, t = text_size(f, 'TENSEI')
    ty = (H - h) / 2 - t
    for ch, cw in zip(s, widths):
        cl = text_size(f, ch)[2]
        ld.text((x - cl, ty), ch, font=f, fill=255)
        x += cw + spacing
    # dark drop shadow, then chrome fill with a horizon band
    shadow = letters.filter(ImageFilter.MaxFilter(9)).filter(ImageFilter.GaussianBlur(3 * S))
    img.paste(Image.new('RGBA', (W, H), (5, 6, 8, 255)), (0, 0), shadow)
    gy = np.linspace(0, 1, H)[:, None]
    lum = np.where(gy < 0.52, 250 - 90 * (np.clip(gy, 0, 0.52) / 0.52) ** 1.5,
                   120 + 150 * (np.clip(gy - 0.52, 0, 1) / 0.48) ** 0.8)
    lum = np.repeat(lum, W, axis=1)
    cr = np.dstack([lum * 0.96, lum * 0.98, lum * 1.0, np.full_like(lum, 255)]).clip(0, 255).astype(np.uint8)
    img.paste(Image.fromarray(cr), (0, 0), letters)
    # thin top highlight on the letters
    hl = np.array(letters, float)
    shifted = np.roll(hl, 4 * S, axis=0)
    edge = np.clip(hl - shifted, 0, 255).astype(np.uint8)
    img.paste(Image.new('RGBA', (W, H), (255, 255, 255, 255)), (0, 0), Image.fromarray(edge))
    img.convert('RGB').resize((1024, 256), Image.LANCZOS).save(os.path.join(out, 'T_V_Badge.png'))


def taxi_sign(out):
    S = 2
    W, H = 512 * S, 128 * S
    img = Image.new('RGB', (W, H), (255, 243, 190))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, W - 1, H - 1], outline=(40, 36, 30), width=8 * S)
    f = font(SUP + 'Futura.ttc', 104 * S, index=4)
    s = 'TAXI'
    w, h, l, t = text_size(f, s)
    d.text(((W - w) / 2 - l, (H - h) / 2 - t), s, font=f, fill=(25, 24, 22))
    img.resize((512, 128), Image.LANCZOS).save(os.path.join(out, 'T_V_TaxiSign.png'))


def checker(out):
    img = Image.new('RGB', (256, 128), (244, 244, 238))
    d = ImageDraw.Draw(img)
    for i in range(4):
        for j in range(2):
            if (i + j) % 2 == 0:
                d.rectangle([i * 64, j * 64, i * 64 + 63, j * 64 + 63], fill=(28, 28, 30))
    img.save(os.path.join(out, 'T_V_Checker.png'))


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
        os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'build_art', 'opening', 'vehicles', 'textures')
    os.makedirs(out, exist_ok=True)
    livery(out)
    glass(out)
    plates(out)
    badge(out)
    taxi_sign(out)
    checker(out)
    print('[vehicle textures] wrote to', out)


if __name__ == '__main__':
    main()
