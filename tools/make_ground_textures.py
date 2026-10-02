#!/usr/bin/env python3
"""Tileable stylised ground textures (grass, dirt road, flagstone) for the toy-diorama look. Procedural (no AI)."""
import numpy as np
from PIL import Image
OUT = '/Users/sapnagoel/Documents/coding/Game/Escort/Assets/_Game/Art/Environment/Textures'
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

grass(); dirt(); flagstone()
print('ground textures written')
