#!/usr/bin/env python3
"""Procedural FX textures: Oath Glyph circle (rigged duel), soft flame/smoke/spark sprites, ping ring. No AI imagery."""
import math, numpy as np
from PIL import Image, ImageDraw, ImageFilter
OUT = '/Users/sapnagoel/Documents/coding/Game/Escort/Assets/_Game/Art/FX'
import os; os.makedirs(OUT, exist_ok=True)

def oath_glyph(n=1024):
    im = Image.new('RGBA', (n, n), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    c = n / 2
    gold = (255, 214, 120, 255)
    for r, w in ((0.47, 10), (0.43, 4), (0.30, 6)):
        R = r * n
        d.ellipse([c - R, c - R, c + R, c + R], outline=gold, width=w)
    # rune marks between the outer rings
    for i in range(36):
        a = 2 * math.pi * i / 36
        r0, r1 = 0.435 * n, 0.465 * n
        x0, y0 = c + r0 * math.cos(a), c + r0 * math.sin(a)
        x1, y1 = c + r1 * math.cos(a + 0.03 * ((i % 3) - 1)), c + r1 * math.sin(a + 0.03 * ((i % 3) - 1))
        d.line([x0, y0, x1, y1], fill=gold, width=6)
        if i % 4 == 0:
            xm, ym = (x0 + x1) / 2, (y0 + y1) / 2
            d.ellipse([xm - 7, ym - 7, xm + 7, ym + 7], outline=gold, width=3)
    # crossed swords sigil at the centre (the oath)
    for ang in (-0.6, 0.6):
        ca, sa = math.cos(ang - math.pi / 2), math.sin(ang - math.pi / 2)
        L = 0.24 * n
        d.line([c - L * ca, c - L * sa, c + L * ca, c + L * sa], fill=gold, width=10)
        gx, gy = c + 0.14 * n * ca, c + 0.14 * n * sa
        d.line([gx - 30 * sa, gy + 30 * ca, gx + 30 * sa, gy - 30 * ca], fill=gold, width=8)
    glow = im.filter(ImageFilter.GaussianBlur(10))
    out = Image.alpha_composite(glow, im)
    out.save(f'{OUT}/T_OathGlyph.png')

def soft(n=128, name='T_Soft', power=1.6):
    y, x = np.mgrid[0:n, 0:n]
    r = np.sqrt((x - n / 2 + 0.5) ** 2 + (y - n / 2 + 0.5) ** 2) / (n / 2)
    a = np.clip(1 - r, 0, 1) ** power
    img = np.dstack([np.ones_like(a), np.ones_like(a), np.ones_like(a), a])
    Image.fromarray((img * 255).astype(np.uint8)).save(f'{OUT}/{name}.png')

def flame(n=128):
    y, x = np.mgrid[0:n, 0:n].astype(float)
    xn = (x - n / 2) / (n / 2)
    yn = (y) / n  # 0 top .. 1 bottom
    width = 0.15 + 0.55 * (yn ** 0.8)
    a = np.clip(1 - (np.abs(xn) / np.maximum(width, 1e-3)) ** 2, 0, 1) * np.clip(yn * 1.6, 0, 1) * np.clip((1 - yn) * 4, 0, 1)
    img = np.dstack([np.ones_like(a), np.ones_like(a), np.ones_like(a), a ** 1.2])
    Image.fromarray((img * 255).astype(np.uint8)).save(f'{OUT}/T_Flame.png')

def ring(n=256):
    im = Image.new('RGBA', (n, n), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.ellipse([12, 12, n - 12, n - 12], outline=(255, 255, 255, 255), width=14)
    im.filter(ImageFilter.GaussianBlur(2)).save(f'{OUT}/T_Ring.png')

oath_glyph(); soft(); soft(64, 'T_Spark', 3.0); flame(); ring()
print('fx textures written')
