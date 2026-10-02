#!/usr/bin/env python3
"""Self-made (synthesized) SFX and loops for the slice — no third-party licences involved (GDD §9 Audio).
Output: Escort/Assets/_Game/Resources/Audio/syn_*.wav (44.1 kHz, 16-bit). Deterministic (fixed seeds)."""
import numpy as np
from scipy import signal
from scipy.io import wavfile
import os

SR = 44100
OUT = '/Users/sapnagoel/Documents/coding/Game/Escort/Assets/_Game/Resources/Audio'
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(1234)


def write(name, x, stereo=False):
    x = np.asarray(x, dtype=np.float64)
    peak = np.max(np.abs(x)) or 1.0
    x = x / peak * 0.89
    data = (x * 32767).astype(np.int16)
    wavfile.write(f'{OUT}/{name}.wav', SR, data)
    print(name, f'{len(x) / SR:.2f}s')


def env(n, a, d, sustain=0.0):
    t = np.arange(n) / SR
    e = np.minimum(1.0, t / max(a, 1e-4)) * np.exp(-np.maximum(0, t - a) / max(d, 1e-4))
    return e * (1 - sustain) + sustain


def bandpass(x, lo, hi, order=2):
    b, a = signal.butter(order, [lo / (SR / 2), hi / (SR / 2)], btype='band')
    return signal.lfilter(b, a, x)


def lowpass(x, f, order=2):
    b, a = signal.butter(order, f / (SR / 2), btype='low')
    return signal.lfilter(b, a, x)


def seamless(x, fade=1.0):
    """Crossfade the tail into the head so the loop has no seam."""
    n = int(fade * SR)
    head, tail = x[:n].copy(), x[-n:].copy()
    w = np.linspace(0, 1, n)
    x = x[:-n].copy()
    x[:n] = head * w + tail * (1 - w)
    return x


# --- whoosh (dodge, thrown sand): two noise bands cross-faded low→high→low (no filter-state seams) ---
for i, (dur, lo, hi) in enumerate([(0.32, (350, 900), (1800, 5000)), (0.42, (250, 700), (1200, 3500))]):
    n = int(dur * SR)
    noise = rng.standard_normal(n)
    low, high = bandpass(noise, *lo), bandpass(noise, *hi)
    k = np.sin(np.linspace(0, np.pi, n))          # 0 → 1 → 0
    out = (low * (1 - k) + high * k) * k ** 1.2
    write(f'syn_whoosh_{i}', out)

# --- crossbow release: plucked string (Karplus-Strong) + wood click ---
def pluck(freq, dur, damp=0.996):
    n = int(dur * SR)
    p = int(SR / freq)
    buf = rng.uniform(-1, 1, p)
    out = np.zeros(n)
    for i in range(n):
        out[i] = buf[i % p]
        buf[i % p] = damp * 0.5 * (buf[i % p] + buf[(i + 1) % p])
    return out

tw = pluck(98, 0.45) * env(int(0.45 * SR), 0.002, 0.12)
click = bandpass(rng.standard_normal(int(0.03 * SR)), 1500, 6000) * env(int(0.03 * SR), 0.0005, 0.006)
tw[:len(click)] += click * 0.8
write('syn_twang', tw)

# --- horn (the gallery's signal / opening): brass-ish saw stack with vibrato and a swell ---
dur = 1.5
t = np.arange(int(dur * SR)) / SR
vib = 1 + 0.006 * np.sin(2 * np.pi * 5.2 * t)
f = 174.6  # F3
horn = sum(signal.sawtooth(2 * np.pi * f * k * vib * t) / k ** 1.3 for k in (1, 2, 3))
horn = lowpass(horn, 1400, 3) * np.minimum(1, t / 0.18) * np.exp(-np.maximum(0, t - 1.0) / 0.25)
write('syn_horn', horn)

# --- fire crackle loop (8 s): brown-noise bed + decaying crackles ---
dur = 9.0
n = int(dur * SR)
bed = lowpass(np.cumsum(rng.standard_normal(n)) * 0.002, 400)
bed -= lowpass(bed, 20)
crk = np.zeros(n)
for _ in range(int(dur * 14)):
    p = rng.integers(0, n - 2000)
    L = rng.integers(200, 1600)
    burst = bandpass(rng.standard_normal(L), 1200, 7000) * np.exp(-np.arange(L) / (L * 0.18))
    crk[p:p + L] += burst * rng.uniform(0.2, 1.0)
fire = seamless(bed * 2.5 + crk * 0.7, 1.0)
write('syn_fire_loop', fire)

# --- forest ambience loop (20 s): wind + occasional bird chirps ---
dur = 21.0
n = int(dur * SR)
t = np.arange(n) / SR
wind = lowpass(rng.standard_normal(n), 700, 2)
wind *= 0.55 + 0.45 * np.sin(2 * np.pi * t / 7.3) * np.sin(2 * np.pi * t / 3.1 + 1)
birds = np.zeros(n)
for _ in range(9):
    start = rng.uniform(0.5, dur - 2)
    for c in range(rng.integers(2, 5)):
        s0 = int((start + c * rng.uniform(0.09, 0.16)) * SR)
        L = int(rng.uniform(0.05, 0.11) * SR)
        if s0 + L >= n: break
        tt = np.arange(L) / SR
        f0, f1 = rng.uniform(2800, 4200), rng.uniform(2000, 5200)
        ph = 2 * np.pi * np.cumsum(np.linspace(f0, f1, L)) / SR
        birds[s0:s0 + L] += (np.sin(ph) + 0.3 * np.sin(2 * ph)) * np.sin(np.pi * tt / tt[-1]) * 0.35
forest = seamless(wind * 0.5 + birds * 0.6, 1.0)
write('syn_forest_loop', forest)

# --- pads: duel tension (low, minor, pulsing) and camp (warm, major-ish) ---
def pad(freqs, dur, cutoff, pulse=0.0, detune=0.004):
    n = int(dur * SR)
    t = np.arange(n) / SR
    x = np.zeros(n)
    for f in freqs:
        for d in (-detune, 0, detune):
            x += signal.sawtooth(2 * np.pi * f * (1 + d) * t + rng.uniform(0, 6.28))
    x = lowpass(x, cutoff, 3)
    if pulse > 0: x *= 0.7 + 0.3 * np.sin(2 * np.pi * pulse * t) ** 2
    return seamless(x * np.minimum(1, t / 1.5), 1.5)

write('syn_duel_pad', pad([55.0, 82.41, 130.81], 17.0, 480, pulse=0.5))      # A1, E2, C3 (A minor colour)
write('syn_camp_pad', pad([98.0, 146.83, 196.0, 246.94], 17.0, 700))          # G2, D3, G3, B3 (G major)
