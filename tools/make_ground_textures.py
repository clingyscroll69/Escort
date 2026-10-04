#!/usr/bin/env python3
"""Tileable stylised ground textures (grass, dirt road, flagstone) for the toy-diorama look. Procedural (no AI)."""
import numpy as np
from PIL import Image
import os, sys
OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'Escort/Assets/_Game/Art/Environment/Textures')
N = 512
rng = np.random.default_rng(7)

def tile_noise(scale, octaves=4):
    """Tileable value noise via FFT-filtered white noise."""
    acc = np.zeros((N, N))
    amp = 1.0
    for o in range(octaves):
        f = scale * (2 ** o)
        white = rng.standard_normal((N, N))
        fx = np.fft.fftfreq(N)[:, None]
        fy = np.fft.fftfreq(N)[None, :]
        r = np.sqrt(fx ** 2 + fy ** 2)
        filt = np.exp(-(r * N / f) ** 2)
        n = np.real(np.fft.ifft2(np.fft.fft2(white) * filt))
        n = (n - n.mean()) / (n.std() + 1e-9)
        acc += n * amp
        amp *= 0.5
    acc = (acc - acc.min()) / (acc.max() - acc.min())
    return acc

def save(arr, name):
    Image.fromarray((np.clip(arr, 0, 1) * 255).astype(np.uint8)).save(f'{OUT}/{name}.png')

def grass():
    n = tile_noise(6)
    fine = tile_noise(40, 2)
    base = np.array([0.40, 0.57, 0.28])
    dark = np.array([0.30, 0.46, 0.22])
    light = np.array([0.52, 0.66, 0.33])
    t = n[..., None]
    col = dark * (1 - t) + light * t
    col = col * (0.9 + 0.2 * fine[..., None])
    # tiny flecks of dry grass
    fleck = (rng.random((N, N)) > 0.996)[..., None]
    col = np.where(fleck, np.array([0.72, 0.68, 0.4]), col)
    save(col, 'T_Ground_Grass')

def dirt():
    n = tile_noise(5)
    fine = tile_noise(48, 2)
    a = np.array([0.46, 0.35, 0.24])
    b = np.array([0.58, 0.46, 0.31])
    t = n[..., None]
    col = a * (1 - t) + b * t
    col = col * (0.88 + 0.24 * fine[..., None])
    pebbles = (tile_noise(90, 1) > 0.82)[..., None]
    col = np.where(pebbles, col * 0.78 + 0.1, col)
    save(col, 'T_Ground_Dirt')

def flagstone():
    n = tile_noise(8)
    col = np.array([0.55, 0.53, 0.5]) * (0.85 + 0.3 * n[..., None])
    grid = np.zeros((N, N))
    for k in range(0, N, 128):
        grid[k:k + 4, :] = 1
        grid[:, (k + 64) % N:(k + 64) % N + 4] = 1 if (k // 128) % 2 else grid[:, (k + 64) % N:(k + 64) % N + 4]
    col = np.where(grid[..., None] > 0, col * 0.55, col)
    save(col, 'T_Ground_Flagstone')

def forest():
    """Whisperwood floor: moss and leaf litter, darker and browner than the Old Road's grass."""
    global rng
    rng = np.random.default_rng(21)
    n = tile_noise(5)
    fine = tile_noise(36, 2)
    moss = np.array([0.24, 0.36, 0.17])
    litter = np.array([0.36, 0.30, 0.18])
    t = np.clip(n * 1.3 - 0.2, 0, 1)[..., None]
    col = moss * (1 - t) + litter * t
    col = col * (0.86 + 0.26 * fine[..., None])
    leaves = (rng.random((N, N)) > 0.992)[..., None]
    col = np.where(leaves, np.array([0.55, 0.36, 0.14]), col)
    save(col, 'T_Ground_Forest')

def forest_path():
    """A trodden trail: darker, damper dirt with roots."""
    global rng
    rng = np.random.default_rng(22)
    n = tile_noise(5)
    fine = tile_noise(44, 2)
    a = np.array([0.30, 0.23, 0.15])
    b = np.array([0.42, 0.33, 0.22])
    col = a * (1 - n[..., None]) + b * n[..., None]
    col = col * (0.88 + 0.22 * fine[..., None])
    roots = (np.abs(tile_noise(12, 2) - 0.5) < 0.012)[..., None]
    col = np.where(roots, col * 0.6, col)
    save(col, 'T_Ground_ForestPath')

def bog():
    """Mire: black-green water and mud, glossy patches."""
    global rng
    rng = np.random.default_rng(23)
    n = tile_noise(4)
    fine = tile_noise(30, 2)
    mud = np.array([0.17, 0.15, 0.09])
    water = np.array([0.12, 0.17, 0.13])
    t = (n > 0.55)[..., None]
    col = np.where(t, water, mud) * (0.85 + 0.3 * fine[..., None])
    gloss = (tile_noise(60, 1) > 0.86)[..., None]
    col = np.where(gloss & t, col + 0.12, col)
    save(col, 'T_Ground_Bog')

which = sys.argv[1] if len(sys.argv) > 1 else 'road'
if which in ('all', 'road'):
    grass(); dirt(); flagstone()
if which in ('all', 'forest'):
    forest(); forest_path(); bog()
print('ground textures written:', which)
