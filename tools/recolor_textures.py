#!/usr/bin/env python3
"""Palette recolours for the character cast (GDD §2: shared palette unifies mixed sources).

Outfit atlases are painted, but cloth separates from leather/metal by hue or by (low saturation, high value), so each
recipe rule selects pixels with a soft mask and remaps hue/saturation/value. Also emits skin-tone variants and the
shared hair/eye textures. Output: Escort/Assets/_Game/Art/Characters/Textures/.
"""
import json, os, sys
import numpy as np
from PIL import Image

# The checkout this script lives in (works from any git worktree). The Quaternius source kits are not in git, so a
# worktree falls back to the main checkout's copy.
ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))
Q = ROOT + '/ThirdParty/Quaternius'
if not os.path.isdir(Q):
    Q = '/Users/sapnagoel/Documents/coding/Game/ThirdParty/Quaternius'
OUT = ROOT + '/Escort/Assets/_Game/Art/Characters/Textures'
OUTFIT_TEX = Q + '/Outfits_Fantasy/Modular Character Outfits - Fantasy[Standard]/Textures'
UBC_TEX = Q + '/UBC/Universal Base Characters[Standard]/Base Characters/Textures'
HAIR_TEX = Q + '/UBC/Universal Base Characters[Standard]/Hairstyles/Textures'
SIZE = 1024

SKIN = {  # value multiplier, saturation multiplier, hue shift (deg)
    'fair': (1.14, 0.78, 2),
    'medium': (1.0, 1.0, 0),
    'tan': (0.86, 1.05, -2),
    'deep': (0.58, 1.08, -4),
    'pale': (1.12, 0.55, 6),
}


def rgb_to_hsv(rgb):
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    mx = rgb.max(-1)
    mn = rgb.min(-1)
    d = mx - mn
    h = np.zeros_like(mx)
    m = d > 1e-6
    rm = m & (mx == r)
    gm = m & (mx == g) & ~rm
    bm = m & ~rm & ~gm
    h[rm] = ((g - b)[rm] / d[rm]) % 6
    h[gm] = ((b - r)[gm] / d[gm]) + 2
    h[bm] = ((r - g)[bm] / d[bm]) + 4
    h = h * 60.0
    s = np.where(mx > 1e-6, d / np.maximum(mx, 1e-6), 0)
    return np.stack([h, s, mx], -1)


def hsv_to_rgb(hsv):
    h, s, v = hsv[..., 0] % 360, hsv[..., 1], hsv[..., 2]
    c = v * s
    x = c * (1 - np.abs((h / 60) % 2 - 1))
    m = v - c
    z = np.zeros_like(h)
    conds = [(h < 60), (h < 120), (h < 180), (h < 240), (h < 300), (h >= 300)]
    rs = [c, x, z, z, x, c]
    gs = [x, c, c, x, z, z]
    bs = [z, z, x, c, c, x]
    r = np.select(conds, rs)
    g = np.select(conds, gs)
    b = np.select(conds, bs)
    return np.stack([r + m, g + m, b + m], -1)


def smooth(edge0, edge1, x):
    t = np.clip((x - edge0) / (edge1 - edge0 + 1e-9), 0, 1)
    return t * t * (3 - 2 * t)


def hue_mask(h, lo, hi, feather=12):
    # inside [lo, hi] with feathered edges; handles wrap-around
    if lo <= hi:
        return smooth(lo - feather, lo, h) * (1 - smooth(hi, hi + feather, h))
    return np.maximum(hue_mask(h, lo, 360, feather), hue_mask(h, 0, hi, feather))


def apply_rules(rgb, rules):
    hsv = rgb_to_hsv(rgb)
    h, s, v = hsv[..., 0], hsv[..., 1], hsv[..., 2]
    out = rgb.copy()
    for rule in rules:
        mask = np.ones_like(h)
        if 'hue_min' in rule:
            mask *= hue_mask(h, rule['hue_min'], rule['hue_max'])
            mask *= smooth(0.08, 0.2, s)  # ignore greys (metal) inside the hue band
        if 'sat_max' in rule:
            mask *= 1 - smooth(rule['sat_max'], rule['sat_max'] + 0.1, s)
        if 'val_min' in rule:
            mask *= smooth(rule['val_min'] - 0.08, rule['val_min'], v)
        nh = np.full_like(h, rule['to_hue'])
        ns = np.clip(rule['to_sat'] * (0.55 + 0.45 * np.clip(s / 0.6, 0, 1.5)), 0, 1)
        nv = np.clip(v * rule.get('val_mul', 1.0), 0, 1)
        rec = hsv_to_rgb(np.stack([nh, ns, nv], -1))
        out = out * (1 - mask[..., None]) + rec * mask[..., None]
    return np.clip(out, 0, 1)


def load(path, size=SIZE):
    im = Image.open(path).convert('RGB').resize((size, size), Image.LANCZOS)
    return np.asarray(im).astype(np.float32) / 255.0


def save(arr, name):
    os.makedirs(OUT, exist_ok=True)
    Image.fromarray((np.clip(arr, 0, 1) * 255 + 0.5).astype(np.uint8)).save(os.path.join(OUT, name))


def find_outfit(name):
    for sub in ('Peasant', 'Ranger'):
        p = os.path.join(OUTFIT_TEX, sub, name + '.png')
        if os.path.exists(p):
            return p
    raise FileNotFoundError(name)


def skin_variant(rgb, variant):
    vm, sm, hs = SKIN[variant]
    hsv = rgb_to_hsv(rgb)
    # only touch skin-like pixels (warm hues); leaves underwear/lips mostly intact
    mask = hue_mask(hsv[..., 0], 0, 50, 10) * smooth(0.15, 0.3, hsv[..., 1])
    nh = hsv[..., 0] + hs
    ns = np.clip(hsv[..., 1] * sm, 0, 1)
    nv = np.clip(hsv[..., 2] * vm, 0, 1)
    rec = hsv_to_rgb(np.stack([nh, ns, nv], -1))
    return rgb * (1 - mask[..., None]) + rec * mask[..., None]


def main():
    recipes = json.load(open(ROOT + '/tools/blender/recipes.json'))
    only = sys.argv[1] if len(sys.argv) > 1 else None
    heads = {'Male': load(UBC_TEX + '/T_Superhero_Male_Ligh.png'), 'Female': load(UBC_TEX + '/T_Superhero_Female_Light_BaseColor.png')}
    hands = {'Male': load(OUTFIT_TEX + '/Base/T_Regular_Male_Dark_BaseColor.png'), 'Female': load(OUTFIT_TEX + '/Base/T_Regular_Female_Dark_BaseColor.png')}
    for name, r in recipes.items():
        if name.startswith('_') or (only and name != only):
            continue
        base = load(find_outfit(r['outfit_tex']))
        save(apply_rules(base, r.get('recolor', [])), f'T_{name}_Outfit.png')
        variant = r.get('skin_variant', 'medium')
        save(skin_variant(heads[r['sex']], variant), f'T_{name}_Head.png')
        save(skin_variant(hands[r['sex']], variant), f'T_{name}_Hands.png')
        hc = np.array(r.get('hair_color', [0.3, 0.2, 0.12]), dtype=np.float32)
        hair = load(HAIR_TEX + '/T_Hair_1_BaseColor.png', 512)
        # hair base is near-white greyscale: multiply, then lift contrast slightly so strands still read
        save(np.clip(hair * hc[None, None, :] * 1.15, 0, 1), f'T_{name}_Hair.png')
        print('recoloured', name, variant)
    save(load(HAIR_TEX + '/T_Hair_1_BaseColor.png', 512), 'T_Hair.png')
    save(load(UBC_TEX + '/T_Eye_Brown.png', 256), 'T_Eyes.png')


if __name__ == '__main__':
    main()
