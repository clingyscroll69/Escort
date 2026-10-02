#!/usr/bin/env python3
"""Procedural UI sprites (white, tinted at runtime): 9-slice rounded panel + border ring, bar fill, pip, soft glow.
Output: Escort/Assets/_Game/Resources/UI/*.png  (imported as Sprites by ArtImportPostprocessor)."""
from PIL import Image, ImageDraw, ImageFilter
import os

OUT = '/Users/sapnagoel/Documents/coding/Game/Escort/Assets/_Game/Resources/UI'
os.makedirs(OUT, exist_ok=True)
SS = 4  # supersample for anti-aliasing


def rounded(size, radius, outline=0, fill=True):
    W = size * SS
    img = Image.new('L', (W, W), 0)
    d = ImageDraw.Draw(img)
    r = radius * SS
    if fill:
        d.rounded_rectangle([0, 0, W - 1, W - 1], r, fill=255)
    else:
        d.rounded_rectangle([0, 0, W - 1, W - 1], r, fill=255)
        o = outline * SS
        d.rounded_rectangle([o, o, W - 1 - o, W - 1 - o], max(1, r - o), fill=0)
    img = img.resize((size, size), Image.LANCZOS)
    rgba = Image.new('RGBA', (size, size), (255, 255, 255, 0))
    rgba.putalpha(img)
    return rgba


rounded(64, 16).save(f'{OUT}/panel.png')
rounded(64, 16, outline=3, fill=False).save(f'{OUT}/panel_border.png')
rounded(32, 6).save(f'{OUT}/bar.png')

# pip: filled circle
W = 32 * SS
c = Image.new('L', (W, W), 0)
ImageDraw.Draw(c).ellipse([2 * SS, 2 * SS, W - 2 * SS, W - 2 * SS], fill=255)
c = c.resize((32, 32), Image.LANCZOS)
pip = Image.new('RGBA', (32, 32), (255, 255, 255, 0))
pip.putalpha(c)
pip.save(f'{OUT}/pip.png')

# soft glow: radial falloff
g = Image.new('L', (128, 128), 0)
ImageDraw.Draw(g).ellipse([24, 24, 104, 104], fill=255)
g = g.filter(ImageFilter.GaussianBlur(18))
# window the blur's tail to exactly 0 at the inscribed circle: scaled up, a glow must never show its square edge
px = g.load()
for y in range(128):
    for x in range(128):
        r = ((x + 0.5 - 64) ** 2 + (y + 0.5 - 64) ** 2) ** 0.5 / 64.0
        w = min(1.0, max(0.0, (1.0 - r) / 0.35))
        px[x, y] = int(round(px[x, y] * w * w * (3 - 2 * w)))
glow = Image.new('RGBA', (128, 128), (255, 255, 255, 0))
glow.putalpha(g)
glow.save(f'{OUT}/glow.png')

# bubble tail: small downward triangle
W = 32 * SS
t = Image.new('L', (W, W), 0)
ImageDraw.Draw(t).polygon([(4 * SS, 0), (W - 4 * SS, 0), (W // 2, W - 6 * SS)], fill=255)
t = t.resize((32, 32), Image.LANCZOS)
tail = Image.new('RGBA', (32, 32), (255, 255, 255, 0))
tail.putalpha(t)
tail.save(f'{OUT}/tail.png')
print('ok', sorted(os.listdir(OUT)))
