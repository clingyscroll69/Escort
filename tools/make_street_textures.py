#!/usr/bin/env python3
"""Tileable street-surface textures for the opening (asphalt, sidewalk slabs, curb, tactile paving, road paint).

Self-made (numpy/PIL), deterministic. Surfaces are UV'd at 1 unit = 2 m (docs/superpowers/specs/2026-10-01-opening-street-design.md
§5), except paint and tactile paving, which are mapped per piece.

Output: Escort/Assets/_Game/Art/Opening/Textures/T_Street_*.png (1024², sRGB).
"""
import os

import numpy as np
from PIL import Image

ROOT = '/Users/sapnagoel/Documents/coding/Game'
OUT = f'{ROOT}/Escort/Assets/_Game/Art/Opening/Textures'
N = 1024
rng = np.random.default_rng(3303)


def tile_noise(n, scale, octaves=4, persistence=0.5):
    """Tileable fractal noise in [0, 1] via filtered white noise in the frequency domain."""
    out = np.zeros((n, n))
    amp, total = 1.0, 0.0
    fy = np.fft.fftfreq(n)[:, None]
    fx = np.fft.fftfreq(n)[None, :]
    f = np.sqrt(fx * fx + fy * fy)
    for o in range(octaves):
        cutoff = (2 ** o) / scale
        white = rng.standard_normal((n, n))
        spec = np.fft.fft2(white) * np.exp(-(f / cutoff) ** 2)
        layer = np.real(np.fft.ifft2(spec))
        layer = (layer - layer.mean()) / (layer.std() + 1e-9)
        out += layer * amp
        total += amp
        amp *= persistence
    out /= total
    out = (out - out.min()) / (out.max() - out.min())
    return out


def hexrgb(h):
    h = h.lstrip('#')
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], dtype=np.float64) / 255.0


def save(name, rgb):
    os.makedirs(OUT, exist_ok=True)
    img = Image.fromarray((np.clip(rgb, 0, 1) * 255).astype(np.uint8))
    img.save(os.path.join(OUT, name))
    print('wrote', name)


def crack_mask(n, count, length, width):
    """Thin wandering dark cracks (wrapping at the edges so the tile repeats)."""
    m = np.zeros((n, n))
    for _ in range(count):
        x, y = rng.uniform(0, n, 2)
        ang = rng.uniform(0, 2 * np.pi)
        for _s in range(length):
            ang += rng.normal(0, 0.35)
            x = (x + np.cos(ang) * 2) % n
            y = (y + np.sin(ang) * 2) % n
            w = max(1, int(width * rng.uniform(0.5, 1.2)))
            xi, yi = int(x), int(y)
            for dx in range(-w, w + 1):
                for dy in range(-w, w + 1):
                    if dx * dx + dy * dy <= w * w:
                        m[(yi + dy) % n, (xi + dx) % n] = 1.0
            if rng.random() < 0.02:  # branch
                ang += rng.choice([-1, 1]) * rng.uniform(0.6, 1.2)
    return m


def asphalt():
    base = hexrgb('#3A3F47')
    large = tile_noise(N, 220, 3)
    mid = tile_noise(N, 40, 3)
    fine = rng.random((N, N))
    v = 0.92 + 0.16 * large + 0.10 * mid
    # aggregate: sparse light and dark specks
    v += np.where(fine > 0.985, 0.35, 0) + np.where(fine < 0.02, -0.25, 0)
    v += (rng.random((N, N)) - 0.5) * 0.08
    rgb = base[None, None, :] * v[..., None]
    # patches (repairs): slightly darker, smoother rectangles
    for _ in range(3):
        x0, y0 = rng.integers(0, N, 2)
        w, h = rng.integers(120, 320, 2)
        ys = (np.arange(y0, y0 + h) % N)[:, None]
        xs = (np.arange(x0, x0 + w) % N)[None, :]
        rgb[ys, xs] = rgb[ys, xs] * 0.82 + base * 0.1
    # oil stains
    for _ in range(5):
        cx, cy = rng.uniform(0, N, 2)
        r = rng.uniform(25, 70)
        yy, xx = np.mgrid[0:N, 0:N]
        dx = np.minimum(np.abs(xx - cx), N - np.abs(xx - cx))
        dy = np.minimum(np.abs(yy - cy), N - np.abs(yy - cy))
        d = np.sqrt(dx * dx + dy * dy) / r
        rgb *= (1 - 0.28 * np.clip(1 - d, 0, 1) ** 1.5)[..., None]
    cr = crack_mask(N, 4, 160, 0.9)
    rgb *= (1 - 0.35 * cr)[..., None]
    save('T_Street_Asphalt.png', rgb)


def sidewalk():
    """Concrete slabs: 1 m squares → 2 x 2 slabs per 2 m tile, with joints, stains and gum spots."""
    base = hexrgb('#B9B4AA')
    rgb = np.ones((N, N, 3)) * base
    slab = N // 2
    yy, xx = np.mgrid[0:N, 0:N]
    for sy in range(2):
        for sx in range(2):
            tone = 1.0 + rng.uniform(-0.05, 0.05)
            rgb[sy * slab:(sy + 1) * slab, sx * slab:(sx + 1) * slab] *= tone
    rgb *= (0.92 + 0.12 * tile_noise(N, 90, 3))[..., None]
    rgb *= (0.96 + 0.06 * tile_noise(N, 14, 2))[..., None]
    # joints: dark grooves with a lighter lip
    for k in range(0, N, slab):
        for off, f in ((0, 0.55), (1, 0.6), (2, 0.75), (3, 1.08), (-1, 0.65), (-2, 0.85)):
            rgb[(k + off) % N, :] *= f
            rgb[:, (k + off) % N] *= f
    # a scored mid-line in each slab (control joints are shallower)
    for k in range(slab // 2, N, slab):
        rgb[k % N, :] *= 0.86
        rgb[:, k % N] *= 0.86
    # stains and gum
    for _ in range(14):
        cx, cy = rng.uniform(0, N, 2)
        r = rng.uniform(10, 60)
        d = np.sqrt(np.minimum(np.abs(xx - cx), N - np.abs(xx - cx)) ** 2 + np.minimum(np.abs(yy - cy), N - np.abs(yy - cy)) ** 2) / r
        rgb *= (1 - 0.12 * np.clip(1 - d, 0, 1))[..., None]
    for _ in range(40):
        cx, cy = rng.integers(0, N, 2)
        r = rng.integers(3, 7)
        d = (xx - cx) ** 2 + (yy - cy) ** 2
        rgb[d <= r * r] *= 0.55
    rgb += (rng.random((N, N, 1)) - 0.5) * 0.04
    save('T_Street_Sidewalk.png', rgb)


def curb():
    base = hexrgb('#CFCAC0')
    v = 0.9 + 0.12 * tile_noise(N, 60, 3) + (rng.random((N, N)) - 0.5) * 0.12
    rgb = base[None, None, :] * v[..., None]
    rgb *= np.where(rng.random((N, N)) > 0.992, 0.7, 1.0)[..., None]
    save('T_Street_Curb.png', rgb)


def tactile():
    """Yellow truncated domes (detectable warning surface), 16 per metre-wide tile."""
    base = hexrgb('#E2AE2A')
    rgb = np.ones((N, N, 3)) * base
    yy, xx = np.mgrid[0:N, 0:N]
    pitch = N / 16
    cx = (xx % pitch) - pitch / 2
    cy = (yy % pitch) - pitch / 2
    d = np.sqrt(cx * cx + cy * cy) / (pitch * 0.36)
    dome = np.clip(1 - d, 0, 1)
    light = np.clip(0.5 - (cx + cy) / (pitch * 0.9), 0, 1)
    rgb *= (0.86 + 0.1 * tile_noise(N, 70, 2))[..., None]
    rgb = rgb * (1 - (dome > 0)[..., None] * 0.0) + (dome > 0)[..., None] * (0.12 * light[..., None] - 0.04)
    ring = (d > 0.92) & (d < 1.08)
    rgb[ring] *= 0.72
    rgb *= (0.94 + (rng.random((N, N, 1)) - 0.5) * 0.06)
    save('T_Street_Tactile.png', rgb)


def paint():
    """Road paint wear: white, scuffed by tyres (asphalt showing through), as a 1-unit tile."""
    asph = hexrgb('#3A3F47')
    wear = tile_noise(N, 26, 4)
    scuff = tile_noise(N, 6, 2)
    # mostly intact paint; tyres have scuffed patches and the odd bare fleck
    keep = 1 - 0.75 * np.clip((0.32 - wear) * 5, 0, 1) - 0.35 * np.clip((0.22 - scuff) * 6, 0, 1)
    keep -= np.where(rng.random((N, N)) > 0.985, 0.5, 0)
    keep = np.clip(keep, 0, 1)
    white = np.array([0.93, 0.93, 0.9])
    rgb = white * keep[..., None] + asph * (1 - keep[..., None]) * 1.1
    rgb *= (0.95 + 0.05 * rng.random((N, N, 1)))
    save('T_Street_Paint.png', rgb)


if __name__ == '__main__':
    asphalt()
    sidewalk()
    curb()
    tactile()
    paint()
