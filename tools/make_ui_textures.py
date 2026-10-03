#!/usr/bin/env python3
"""Procedural UI sprites (white, tinted at runtime): 9-slice rounded panel + border ring, bar fill, pip, soft glow.
Output: Escort/Assets/_Game/Resources/UI/*.png  (imported as Sprites by ArtImportPostprocessor)."""
from PIL import Image, ImageDraw, ImageFilter
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))
OUT = os.path.join(ROOT, 'Escort/Assets/_Game/Resources/UI')
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


def base():
    """The original sprites: panel, panel_border, bar, pip, glow, tail."""
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


# ---------------------------------------------------------------------------------------------------------------------
# Tutorial / HUD sprites (2026-10-03). White (tinted at runtime); grey areas darken the tint (keycap lip, card body).


def _ss(size_w, size_h=None):
    size_h = size_h or size_w
    return Image.new('L', (size_w * SS, size_h * SS), 0), Image.new('L', (size_w * SS, size_h * SS), 0)


def _rgba_from(alpha, grey, size):
    """Combine a supersampled alpha mask and a supersampled grey (luminance) mask into an RGBA sprite."""
    a = alpha.resize(size, Image.LANCZOS)
    g = grey.resize(size, Image.LANCZOS)
    rgba = Image.merge('RGBA', (g, g, g, a))
    return rgba


def smoothstep(e0, e1, x):
    t = min(1.0, max(0.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


def tutorial():
    # keycap: a key with a darker bottom lip (9-slice border 14)
    S0 = 64
    alpha, grey = _ss(S0)
    da, dg = ImageDraw.Draw(alpha), ImageDraw.Draw(grey)
    W = S0 * SS
    da.rounded_rectangle([0, 0, W - 1, W - 1], 12 * SS, fill=255)
    dg.rounded_rectangle([0, 0, W - 1, W - 1], 12 * SS, fill=150)
    dg.rounded_rectangle([0, 0, W - 1, W - 1 - 7 * SS], 12 * SS, fill=255)
    _rgba_from(alpha, grey, (S0, S0)).save(f'{OUT}/keycap.png')

    # slot: a bevelled frame (outer ring + faint inner ring), border 22
    S0 = 96
    alpha, grey = _ss(S0)
    da = ImageDraw.Draw(alpha)
    W = S0 * SS
    da.rounded_rectangle([0, 0, W - 1, W - 1], 18 * SS, fill=255)
    da.rounded_rectangle([3 * SS, 3 * SS, W - 1 - 3 * SS, W - 1 - 3 * SS], 15 * SS, fill=0)
    da.rounded_rectangle([7 * SS, 7 * SS, W - 1 - 7 * SS, W - 1 - 7 * SS], 12 * SS, fill=80)
    da.rounded_rectangle([9 * SS, 9 * SS, W - 1 - 9 * SS, W - 1 - 9 * SS], 10 * SS, fill=0)
    grey.paste(255, [0, 0, W, W])
    _rgba_from(alpha, grey, (S0, S0)).save(f'{OUT}/slot.png')

    # card: a rounded panel with a lighter inner rim (bevel), border 24
    alpha, grey = _ss(S0)
    da, dg = ImageDraw.Draw(alpha), ImageDraw.Draw(grey)
    da.rounded_rectangle([0, 0, W - 1, W - 1], 20 * SS, fill=255)
    dg.rounded_rectangle([0, 0, W - 1, W - 1], 20 * SS, fill=255)
    dg.rounded_rectangle([3 * SS, 3 * SS, W - 1 - 3 * SS, W - 1 - 3 * SS], 17 * SS, fill=205)
    _rgba_from(alpha, grey, (S0, S0)).save(f'{OUT}/card.png')

    # corner: an L bracket (top-left; rotated in the UI for the other corners)
    c = Image.new('RGBA', (32, 32), (255, 255, 255, 0))
    dc = ImageDraw.Draw(c)
    dc.rectangle([0, 0, 27, 4], fill=(255, 255, 255, 255))
    dc.rectangle([0, 0, 4, 27], fill=(255, 255, 255, 255))
    c.save(f'{OUT}/corner.png')

    # ring
    S0 = 128
    alpha, grey = _ss(S0)
    da = ImageDraw.Draw(alpha)
    W = S0 * SS
    da.ellipse([1 * SS, 1 * SS, W - 1 - 1 * SS, W - 1 - 1 * SS], fill=255)
    da.ellipse([8 * SS, 8 * SS, W - 1 - 8 * SS, W - 1 - 8 * SS], fill=0)
    grey.paste(255, [0, 0, W, W])
    _rgba_from(alpha, grey, (S0, S0)).save(f'{OUT}/ring.png')

    # spot: a soft-edged hole (transparent centre → opaque edge) for the focus-lesson spotlight
    S0 = 256
    sp = Image.new('RGBA', (S0, S0), (255, 255, 255, 0))
    px = sp.load()
    for y in range(S0):
        for x in range(S0):
            r = ((x + 0.5 - S0 / 2) ** 2 + (y + 0.5 - S0 / 2) ** 2) ** 0.5 / (S0 / 2)
            px[x, y] = (255, 255, 255, int(round(255 * smoothstep(0.42, 1.0, r))))
    sp.save(f'{OUT}/spot.png')

    # grid: tileable floor grid for the demo stage (minor every 32 px, major every 128 px)
    g = Image.new('RGBA', (S0, S0), (255, 255, 255, 0))
    dg = ImageDraw.Draw(g)
    for k in range(0, S0, 32):
        a = 150 if k % 128 == 0 else 60
        w = 3 if k % 128 == 0 else 2
        dg.rectangle([k, 0, k + w - 1, S0 - 1], fill=(255, 255, 255, a))
        dg.rectangle([0, k, S0 - 1, k + w - 1], fill=(255, 255, 255, a))
    g.save(f'{OUT}/grid.png')

    # hatch: diagonal stripes (locked entries)
    h = Image.new('RGBA', (32, 32), (255, 255, 255, 0))
    hp = h.load()
    for y in range(32):
        for x in range(32):
            if (x + y) % 16 < 5:
                hp[x, y] = (255, 255, 255, 90)
    h.save(f'{OUT}/hatch.png')

    # diamond: rank pip
    S0 = 32
    alpha, grey = _ss(S0)
    W = S0 * SS
    ImageDraw.Draw(alpha).polygon([(W // 2, 2 * SS), (W - 2 * SS, W // 2), (W // 2, W - 2 * SS), (2 * SS, W // 2)], fill=255)
    grey.paste(255, [0, 0, W, W])
    _rgba_from(alpha, grey, (S0, S0)).save(f'{OUT}/diamond.png')

    # line: a soft horizontal rule that fades out at both ends
    ln = Image.new('RGBA', (128, 8), (255, 255, 255, 0))
    lp = ln.load()
    for y in range(8):
        vy = {3: 1.0, 4: 1.0, 2: 0.35, 5: 0.35}.get(y, 0.0)
        for x in range(128):
            fx = smoothstep(0, 32, x) * smoothstep(0, 32, 127 - x)
            lp[x, y] = (255, 255, 255, int(round(255 * vy * fx)))
    ln.save(f'{OUT}/line.png')
    print('tutorial sprites written to', OUT)


if __name__ == '__main__':
    import sys
    which = sys.argv[1] if len(sys.argv) > 1 else 'all'
    if which in ('all', 'base'):
        base()
    if which in ('all', 'tutorial'):
        tutorial()
