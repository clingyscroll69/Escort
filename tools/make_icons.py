#!/usr/bin/env python3
"""Rule/status icons (white glyph, dark outline, transparent) for the hero's rule display and HUD. No AI imagery."""
import math, os
from PIL import Image, ImageDraw, ImageFilter
OUT = '/Users/sapnagoel/Documents/coding/Game/Escort/Assets/_Game/Resources/Icons'
S = 256

def canvas():
    return Image.new('RGBA', (S, S), (0, 0, 0, 0))

def finish(im, name):
    # dark outline via dilated alpha
    a = im.split()[3]
    halo = a.filter(ImageFilter.MaxFilter(15))
    base = Image.new('RGBA', (S, S), (18, 16, 22, 0))
    base.putalpha(halo.point(lambda v: 230 if v > 0 else 0))
    base.alpha_composite(im)
    base.save(os.path.join(OUT, name + '.png'))

W = (255, 255, 255, 255)

def sword(d, cx, cy, ang, length=180, w=18):
    ca, sa = math.cos(ang), math.sin(ang)
    def P(x, y): return (cx + x * ca - y * sa, cy + x * sa + y * ca)
    blade = [P(-w/2, -length/2), P(w/2, -length/2), P(w/2, length/2 - 30), P(0, length/2), P(-w/2, length/2 - 30)]
    d.polygon(blade, fill=W)
    guard = [P(-45, -length/2 - 4), P(45, -length/2 - 4), P(45, -length/2 + 14), P(-45, -length/2 + 14)]
    d.polygon(guard, fill=W)
    grip = [P(-8, -length/2 - 40), P(8, -length/2 - 40), P(8, -length/2), P(-8, -length/2)]
    d.polygon(grip, fill=W)

def icons():
    os.makedirs(OUT, exist_ok=True)
    # fight: crossed swords
    im = canvas(); d = ImageDraw.Draw(im)
    sword(d, 128, 128, math.radians(-40)); sword(d, 128, 128, math.radians(40)); finish(im, 'fight')
    # challenge / salute: single upright sword
    im = canvas(); d = ImageDraw.Draw(im); sword(d, 128, 140, math.radians(180)); finish(im, 'challenge')
    # wait: hourglass
    im = canvas(); d = ImageDraw.Draw(im)
    d.rectangle([60, 30, 196, 50], fill=W); d.rectangle([60, 206, 196, 226], fill=W)
    d.polygon([(76, 50), (180, 50), (136, 128), (180, 206), (76, 206), (120, 128)], fill=W)
    d.polygon([(96, 60), (160, 60), (128, 110)], fill=(0, 0, 0, 0)); finish(im, 'wait')
    # fall back: shield
    im = canvas(); d = ImageDraw.Draw(im)
    d.polygon([(128, 24), (216, 56), (206, 150), (128, 232), (50, 150), (40, 56)], fill=W); finish(im, 'fallback')
    # scold: exclamation
    im = canvas(); d = ImageDraw.Draw(im)
    d.rounded_rectangle([104, 28, 152, 164], 20, fill=W); d.ellipse([102, 184, 154, 236], fill=W); finish(im, 'scold')
    # threshold: doorway arch
    im = canvas(); d = ImageDraw.Draw(im)
    d.rectangle([56, 90, 84, 230], fill=W); d.rectangle([172, 90, 200, 230], fill=W)
    d.pieslice([56, 30, 200, 150], 180, 360, fill=W); d.pieslice([84, 58, 172, 122], 180, 360, fill=(0, 0, 0, 0))
    d.rectangle([84, 90, 172, 230], fill=(0, 0, 0, 0)); finish(im, 'threshold')
    # route: footsteps
    im = canvas(); d = ImageDraw.Draw(im)
    for (x, y) in [(80, 170), (150, 90)]:
        d.ellipse([x, y, x + 40, y + 62], fill=W); d.ellipse([x + 6, y - 30, x + 34, y - 6], fill=W)
    finish(im, 'route')
    # parry / riposte: shield + spark
    im = canvas(); d = ImageDraw.Draw(im)
    d.polygon([(128, 40), (196, 66), (188, 140), (128, 206), (68, 140), (60, 66)], fill=W)
    d.polygon([(128, 70), (140, 110), (180, 118), (140, 128), (128, 170), (116, 128), (76, 118), (116, 110)], fill=(0, 0, 0, 0)); finish(im, 'riposte')
    # recite / honor: laurel-ish star
    im = canvas(); d = ImageDraw.Draw(im)
    pts = []
    for i in range(10):
        r = 104 if i % 2 == 0 else 46
        a = math.radians(-90 + i * 36)
        pts.append((128 + r * math.cos(a), 132 + r * math.sin(a)))
    d.polygon(pts, fill=W); finish(im, 'honor')
    # eye (witness / insight)
    im = canvas(); d = ImageDraw.Draw(im)
    d.ellipse([28, 72, 228, 184], fill=W); d.ellipse([98, 98, 158, 158], fill=(0, 0, 0, 0)); d.ellipse([114, 114, 142, 142], fill=W); finish(im, 'eye')
    # ping marker
    im = canvas(); d = ImageDraw.Draw(im)
    d.polygon([(128, 236), (60, 110), (196, 110)], fill=W); d.ellipse([60, 40, 196, 176], fill=W); d.ellipse([100, 80, 156, 136], fill=(0, 0, 0, 0)); finish(im, 'ping')
    print('icons written to', OUT)

icons()
