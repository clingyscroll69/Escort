#!/usr/bin/env python3
"""Audio for the opening's first beat (GDD §8: "The walk, earbuds and a royalty-free song. A horn rises, the song cuts,
white screen.").

Self-made, so no licences are involved: a short synth-pop song heard in the earbuds; the street, muffled by the earbuds
(Kenney CC0 footsteps, traffic, a crossing signal); the truck, whose horn rises out of the muffle; the ringing after.
The song, street and truck all end on the same sample at CUT, where the song's build would have dropped.

Output: Escort/Assets/_Game/Resources/Audio/syn_open_*.wav (44.1 kHz, 16-bit stereo). Deterministic (fixed seed).
The timing constants must match HS.UI.OpeningView (OpeningTests checks the clip lengths against it).
"""
import os
import subprocess
import tempfile

import numpy as np
from scipy import signal
from scipy.io import wavfile

SR = 44100
BEAT = 0.5                # 120 BPM
BAR = 4 * BEAT
CUT = 8 * BAR             # 16.0 s: the drop that never comes
HORN_AT = 12.5            # the truck layer runs HORN_AT → CUT
STEP = 0.53               # walking pace (113 steps/min, deliberately off the beat so the steps stay audible)
STEP_AT = 0.15            # the first step
STEPS_END = 14.0          # the last step: the walker stops dead
ROOT = '/Users/sapnagoel/Documents/coding/Game'
OUT = f'{ROOT}/Escort/Assets/_Game/Resources/Audio'
KENNEY = f'{ROOT}/ThirdParty/Kenney/kenney_impact-sounds/Audio'
PREVIEW = os.environ.get('OPENING_PREVIEW')  # optional: write the full mix here for QA
rng = np.random.default_rng(2026)
N = int(round(CUT * SR))


# ---------------------------------------------------------------------------------------------------------- helpers
def t_arr(dur):
    return np.arange(int(round(dur * SR))) / SR


def midi(m):
    return 440.0 * 2 ** ((m - 69) / 12)


def lp(x, f, order=2):
    b, a = signal.butter(order, min(f, SR / 2 - 100) / (SR / 2), btype='low')
    return signal.lfilter(b, a, x)


def hp(x, f, order=2):
    b, a = signal.butter(order, f / (SR / 2), btype='high')
    return signal.lfilter(b, a, x)


def bp(x, lo, hi, order=2):
    b, a = signal.butter(order, [lo / (SR / 2), min(hi, SR / 2 - 100) / (SR / 2)], btype='band')
    return signal.lfilter(b, a, x)


def pan_gains(p):
    a = (np.clip(p, -1, 1) + 1) * np.pi / 4
    return np.cos(a), np.sin(a)


def place(buf, x, t0, gain=1.0, pan=0.0):
    """Mix mono x (or stereo (2, n)) into the stereo buffer at t0 seconds, equal-power pan."""
    i0 = int(round(t0 * SR))
    if i0 >= buf.shape[1]:
        return
    if x.ndim == 1:
        gl, gr = pan_gains(pan)
        x = np.stack([x * gl, x * gr])
    x = x[:, : buf.shape[1] - i0]
    buf[:, i0:i0 + x.shape[1]] += x * gain


def fade_edges(x, fin=0.0, fout=0.004):
    n = x.shape[-1]
    if fin > 0:
        k = int(fin * SR)
        x[..., :k] *= np.linspace(0, 1, k)
    if fout > 0:
        k = int(fout * SR)
        x[..., n - k:] *= np.linspace(1, 0, k)
    return x


def write(name, x, peak=0.89):
    x = np.asarray(x, dtype=np.float64)
    x = x / (np.max(np.abs(x)) or 1.0) * peak
    data = (x.T * 32767).astype(np.int16)
    wavfile.write(f'{OUT}/{name}.wav', SR, data)
    print(f'{name}: {x.shape[-1] / SR:.3f}s')
    return x


# ------------------------------------------------------------------------------------------------------ instruments
def ep(f, dur, vel=1.0, decay=1.4):
    """FM electric piano: modulator index decays (bright attack, mellow tail) + a faint metallic tine."""
    t = t_arr(dur + 0.25)
    idx = 1.7 * np.exp(-t / 0.3) + 0.3
    car = np.sin(2 * np.pi * f * t + idx * np.sin(2 * np.pi * f * t))
    tine = np.sin(2 * np.pi * f * 14 * t) * np.exp(-t / 0.015) * 0.12
    amp = np.exp(-t / decay) * np.minimum(1, t / 0.003)
    gate = np.where(t < dur, 1.0, np.exp(-(t - dur) / 0.06))
    return (car + tine) * amp * gate * vel


def lead_voice(f, dur, detune, swell):
    """Band-limited pulse (duty 0.3) by additive synthesis, delayed vibrato, gentle top-end tilt."""
    t = t_arr(dur + 0.1)
    vib = 1 + 0.0045 * np.sin(2 * np.pi * 5.6 * t) * np.clip((t - 0.18) / 0.2, 0, 1)
    ph = 2 * np.pi * np.cumsum(f * detune * vib) / SR
    x = np.zeros_like(t)
    for k in range(1, int(9000 // f) + 1):
        x += np.sin(np.pi * k * 0.3) / k * np.sin(k * ph) / (1 + (k * f / 3200) ** 2)
    a = np.minimum(1, t / 0.008)
    d = 0.78 + 0.22 * np.exp(-t / 0.12)
    rel = np.where(t < dur, 1.0, np.exp(-(t - dur) / 0.05))
    env = a * d * rel
    if swell:
        env *= np.linspace(0.55, 1.3, len(t))
    return x * env


def lead(f, dur, vel=1.0, swell=False):
    return np.stack([lead_voice(f, dur, 0.9985, swell), lead_voice(f, dur, 1.0015, swell)]) * vel


def bass(f, dur):
    t = t_arr(dur)
    saw = signal.sawtooth(2 * np.pi * f * t + 0.3)
    x = lp(saw, 650, 2) * 0.55 + np.sin(2 * np.pi * f * t) * 0.75
    env = np.minimum(1, t / 0.004) * np.exp(-t / 0.3)
    return fade_edges(x * env, 0, 0.01)


def kick():
    t = t_arr(0.38)
    f = 48 + 115 * np.exp(-t / 0.028)
    body = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.19)
    click = hp(rng.standard_normal(len(t)), 2500) * np.exp(-t / 0.002) * 0.25
    return body + click


def clap():
    t = t_arr(0.32)
    n = bp(rng.standard_normal(len(t)), 900, 3800)
    env = sum(np.where(t >= d, np.exp(-(t - d) / 0.006), 0) for d in (0, 0.011, 0.022))
    env = env + np.where(t >= 0.03, np.exp(-(t - 0.03) / 0.09), 0) * 0.8
    return n * env + np.sin(2 * np.pi * 185 * t) * np.exp(-t / 0.05) * 0.3


def hat(open_=False):
    t = t_arr(0.35 if open_ else 0.06)
    return hp(rng.standard_normal(len(t)), 7000) * np.exp(-t / (0.11 if open_ else 0.016))


def snare(vel):
    t = t_arr(0.16)
    n = bp(rng.standard_normal(len(t)), 1500, 7000) * np.exp(-t / 0.06)
    return (n + np.sin(2 * np.pi * 205 * t) * np.exp(-t / 0.03) * 0.5) * vel


def crash():
    t = t_arr(1.6)
    return hp(rng.standard_normal(len(t)), 4000) * np.exp(-t / 0.55)


def pluck(f, dur=0.2):
    t = t_arr(dur)
    return (np.sin(2 * np.pi * f * t) + 0.35 * np.sin(4 * np.pi * f * t)) * np.exp(-t / 0.07) * np.minimum(1, t / 0.002)


def riser(dur):
    """Noise swept upward through a bank of bands (crossfaded, so no time-varying filter clicks)."""
    t = t_arr(dur)
    noise = rng.standard_normal(len(t))
    centers = np.geomspace(350, 9000, 12)
    pos = (t / dur) ** 1.4 * (len(centers) - 1)
    x = np.zeros_like(t)
    for i, c in enumerate(centers):
        x += bp(noise, c / 1.45, c * 1.45) * np.clip(1 - np.abs(pos - i), 0, 1)
    return x * (t / dur) ** 2


def reverb(send, dur=1.1):
    t = t_arr(dur)
    ir = np.stack([lp(rng.standard_normal(len(t)) * np.exp(-t / 0.26), 5200) for _ in range(2)])
    ir[:, : int(0.012 * SR)] = 0
    ir /= np.sqrt(np.sum(ir ** 2, axis=1, keepdims=True))
    wet = np.stack([signal.fftconvolve(send[c], ir[c])[: send.shape[1]] for c in range(2)])
    return wet


# ----------------------------------------------------------------------------------------------------------- song
def make_song():
    chords = ['D', 'A', 'Bm', 'G', 'D', 'A', 'G', 'A']
    voicing = {'D': [62, 66, 69], 'A': [61, 64, 69], 'Bm': [62, 66, 71], 'G': [62, 67, 71]}
    root = {'D': 38, 'A': 33, 'Bm': 35, 'G': 31}
    drums, keys, low, ld, arp, fx = (np.zeros((2, N)) for _ in range(6))
    kicks = []

    for b, ch in enumerate(chords):
        t0 = b * BAR
        notes = voicing[ch]
        # electric piano: held chords in the intro, offbeat stabs once the band is in, straight eighths in the build
        if b < 2:
            for at, d in ((0, 1.5), (1.5, 2.5)):
                for i, m in enumerate(notes):
                    place(keys, ep(midi(m), d * BEAT, 0.55, 1.6), t0 + at * BEAT, pan=(i - 1) * 0.25)
        elif b < 6:
            for at in (0.5, 1.5, 2.5, 3.5):
                for i, m in enumerate(notes):
                    place(keys, ep(midi(m), 0.16, 0.5, 0.22), t0 + at * BEAT, pan=(i - 1) * 0.3)
        else:
            for s in range(8):
                v = 0.4 + 0.5 * ((b - 6) * 8 + s) / 15
                for i, m in enumerate(notes):
                    place(keys, ep(midi(m), 0.14, v, 0.2), t0 + s * BEAT / 2, pan=(i - 1) * 0.3)
        # bass: disco octaves from bar 3; the build's last half-bar drops out
        if b >= 2:
            for s in range(8):
                if b == 7 and s >= 4:
                    break
                m = root[ch] + (12 if s % 2 else 0)
                place(low, bass(midi(m), 0.22), t0 + s * BEAT / 2, 0.9)
        # drums
        for s in range(8):
            at = t0 + s * BEAT / 2
            if b < 2:
                if s % 2:
                    place(drums, hat(), at, 0.22, 0.3)
                continue
            if b < 7:
                if s % 2 == 0:
                    place(drums, kick(), at, 1.0)
                    kicks.append(at)
                if s in (2, 6):
                    place(drums, clap(), at, 0.55, -0.05)
                if s % 2:
                    place(drums, hat(open_=(s == 7 and b % 2 == 1)), at, 0.3, 0.3)
                if b == 6 and s % 2 == 0:
                    place(drums, hat(), at + BEAT / 4, 0.18, 0.35)
        if b == 2:
            place(drums, crash(), t0, 0.35, -0.3)
        if b == 7:  # snare roll: eighths, sixteenths, thirty-seconds, crescendo
            hits = [i * BEAT / 2 for i in range(4)] + [2 * BEAT + i * BEAT / 4 for i in range(4)] + \
                   [3 * BEAT + i * BEAT / 8 for i in range(8)]
            for i, h in enumerate(hits):
                place(drums, snare(0.3 + 0.7 * i / (len(hits) - 1)), t0 + h, 0.6)
        # sparkle arpeggio (sixteenths) bars 5–7
        if 4 <= b <= 6:
            seq = [m + 12 for m in notes] + [notes[1] + 24]
            for s in range(16):
                place(arp, pluck(midi(seq[s % 4])), t0 + s * BEAT / 4, 0.22 * (0.7 if s % 4 else 1.0), 0.45)

    # the hook: (bar, beat, beats, midi) — D major; ends hanging on C#6, the leading tone into the drop that never comes
    melody = [
        (2, 0, .5, 78), (2, .5, .5, 78), (2, 1, .5, 78), (2, 1.5, .5, 76), (2, 2, 1, 78), (2, 3, 1, 81),
        (3, 0, 1, 83), (3, 1, .5, 81), (3, 1.5, .5, 78), (3, 2, 1, 76), (3, 3, 1, 74),
        (4, 0, .5, 78), (4, .5, .5, 78), (4, 1, .5, 78), (4, 1.5, .5, 76), (4, 2, 1, 78), (4, 3, 1, 81),
        (5, 0, .5, 83), (5, .5, .5, 81), (5, 1, .5, 78), (5, 1.5, 2, 76),
        (6, 0, .5, 74), (6, .5, .5, 76), (6, 1, .5, 78), (6, 1.5, .5, 79), (6, 2, 2, 81),
        (7, 0, 1, 81), (7, 1, 1, 83), (7, 2, 2, 85),
    ]
    for bar, beat, beats, m in melody:
        swell = bar == 7 and beat == 2
        place(ld, lead(midi(m), beats * BEAT * 0.92, 0.42 if not swell else 0.5, swell), bar * BAR + beat * BEAT)
    # dotted-eighth ping-pong delay on the lead
    dl = int(0.375 * SR)
    echo = np.zeros_like(ld)
    echo[1, dl:] += ld[0, :-dl] * 0.28
    echo[0, 2 * dl:] += ld[1, :-2 * dl] * 0.18
    ld += lp(echo, 3000)

    place(fx, riser(3.0), CUT - 3.0, 0.55)

    # sidechain: everything but the drums ducks under each kick (the pumping 'walking' feel)
    t = np.arange(N) / SR
    duck = np.ones(N)
    for k in kicks:
        i = int(k * SR)
        tt = t[i:] - k
        duck[i:] = np.minimum(duck[i:], 1 - 0.38 * np.exp(-tt / 0.11))
    music = (keys * 0.55 + low * 0.7 + ld * 0.62 + arp * 0.5) * duck
    wet = reverb(keys * 0.4 + ld * 0.45 + drums * 0.08)
    mix = drums * 0.85 + music + wet * 0.22 + fx
    mix = np.tanh(mix / np.max(np.abs(mix)) * 1.15)   # gentle limiter
    mix = np.stack([hp(mix[c], 25) for c in range(2)])  # no DC
    return fade_edges(mix, fin=0.25, fout=0.004)


# --------------------------------------------------------------------------------------------------------- street
def load_steps():
    steps = []
    with tempfile.TemporaryDirectory() as d:
        for i in range(5):
            out = f'{d}/fs{i}.wav'
            subprocess.run(['ffmpeg', '-loglevel', 'error', '-y', '-i', f'{KENNEY}/footstep_concrete_00{i}.ogg',
                            '-ar', str(SR), '-ac', '1', out], check=True)
            _, x = wavfile.read(out)
            x = x.astype(np.float64)
            steps.append(x / (np.max(np.abs(x)) or 1.0))
    return steps


def make_street():
    """Everything outside the earbuds: low-passed (muffled) and quiet."""
    st = np.zeros((2, N))
    t = np.arange(N) / SR
    # distant traffic bed (two decorrelated brown noises)
    for c in range(2):
        brown = np.cumsum(rng.standard_normal(N))
        brown = lp(brown - lp(brown, 8), 240)
        st[c] += brown / np.max(np.abs(brown)) * 0.2 * (0.75 + 0.25 * np.sin(2 * np.pi * t / 5.3 + c))
    # cars passing (pan sweeps)
    for tc, p0, p1, dur in ((2.4, -0.9, 0.9, 3.2), (6.6, 0.9, -0.9, 2.8), (10.2, -0.7, 0.8, 3.0)):
        tt = t_arr(dur)
        x = bp(rng.standard_normal(len(tt)), 90, 900) * np.exp(-((tt - dur / 2) / (dur / 5)) ** 2)
        gl, gr = pan_gains(np.linspace(p0, p1, len(tt)))
        place(st, np.stack([x * gl, x * gr]), tc - dur / 2, 0.6)
    # footsteps, a touch to either side
    steps = load_steps()
    k = 0
    while STEP_AT + k * STEP <= STEPS_END + 1e-6:
        x = steps[int(rng.integers(0, 5))] * rng.uniform(0.85, 1.0)
        place(st, x, STEP_AT + k * STEP, 1.1, -0.12 if k % 2 else 0.12)
        k += 1
    # crossing signal: slow locator ticks while waiting, then the rapid 'walk' ticking
    def tick(f, dur):
        tt = t_arr(dur)
        return np.sin(2 * np.pi * f * tt) * np.exp(-tt / (dur / 3))
    for at in np.arange(2.0, 7.0, 1.0):
        place(st, tick(1450, 0.03), at, 0.4, -0.4)
    for at in np.arange(7.0, CUT, 1 / 9):
        place(st, tick(1900, 0.014), at, 0.28, -0.4)
    st = np.stack([lp(st[c], 1500) for c in range(2)])   # the earbuds muffle the world
    return fade_edges(st, fin=0.1, fout=0.004), k


# ----------------------------------------------------------------------------------------------------------- truck
def horn_tone(f, t, rise):
    ph = 2 * np.pi * np.cumsum(f * (1 + rise * t / t[-1])) / SR
    x = np.zeros_like(t)
    for k in range(1, int(7000 // f) + 1):
        x += np.sin(k * ph) / k ** 0.85
    return x


def blast(t, on, off, a=0.03, r=0.04):
    e = np.clip((t - on) / a, 0, 1)
    if off is not None:
        e *= np.clip((off - t) / r, 0, 1)
    return e


def make_truck(dur, short=False):
    """Engine and tyres closing in, the air horn (two blasts, then held), brakes at the very end. Starts muffled and
    far off; opens up as it closes in, until it drowns out the song."""
    t = t_arr(dur)
    p = np.ones_like(t) if short else (t / dur) ** 2.2
    noise = rng.standard_normal(len(t))
    ph = 2 * np.pi * np.cumsum(31 * (1 + 0.06 * t / dur)) / SR
    unit = lambda y: y / (np.max(np.abs(y)) or 1.0)
    eng = unit(lp(sum(np.sin(k * ph) / k ** 0.9 for k in range(1, 17)), 420) + lp(noise, 300) * 0.5)
    road = unit(bp(noise, 400, 2200))
    chord = unit(sum(horn_tone(f, t, 0.0 if short else 0.02) for f in (277.18, 349.23, 415.30)))
    chord *= 1 + 0.05 * np.sin(2 * np.pi * 31 * t)
    env = blast(t, 0.0, None, 0.02) if short else blast(t, 0.45, 0.85) + blast(t, 1.25, None, 0.04)
    raw = eng * 0.55 + road * 0.3 * p + chord * env
    x = lp(raw, 450) * (1 - p) + raw * p
    x *= 0.04 + 0.96 * p
    if not short:
        s0 = dur - 0.7
        se = np.clip((t - s0) / 0.25, 0, 1) * (t > s0)
        sph = 2 * np.pi * np.cumsum(2150 + 40 * np.sin(2 * np.pi * 6.5 * t)) / SR
        x += se * (np.sin(sph) + 0.4 * np.sin(2 * sph) + unit(bp(noise, 2500, 6000)) * 0.5) * 0.3
    gl, gr = pan_gains(np.zeros_like(t) if short else np.linspace(0.7, 0.0, len(t)))
    return fade_edges(np.stack([x * gl, x * gr]), fin=0.0 if short else 0.05, fout=0.004)


def make_ring(dur=3.2):
    t = t_arr(dur)
    env = np.minimum(1, t / 0.03) * np.exp(-t / 1.0) * np.clip((dur - t) / 0.5, 0, 1)
    return np.stack([np.sin(2 * np.pi * 3900 * t), np.sin(2 * np.pi * 3926 * t)]) * env


# ------------------------------------------------------------------------------------------------------------ main
if __name__ == '__main__':
    song = write('syn_open_song', make_song())
    street, n_steps = make_street()
    street = write('syn_open_street', street)
    truck = write('syn_open_horn', make_truck(CUT - HORN_AT))
    write('syn_open_sting', make_truck(0.6, short=True))
    ring = write('syn_open_ring', make_ring())
    print(f'steps: {n_steps} (from {STEP_AT}s every {STEP}s up to {STEPS_END}s)')
    if PREVIEW:
        # the mix as OpeningView plays it (volumes must match its constants)
        total = int((CUT + 3.4) * SR)
        mix = np.zeros((2, total))
        t = np.arange(N) / SR
        duck = np.interp(t, [0, 14.2, CUT], [0.8, 0.8, 0.25])   # OpeningView: the horn drowns the earbuds out
        mix[:, :N] += song * duck + street * 0.55
        place(mix, truck, HORN_AT, 0.9)
        place(mix, ring, CUT, 0.12)
        wavfile.write(PREVIEW, SR, (np.clip(mix, -1, 1).T * 32767).astype(np.int16))
        print('preview', PREVIEW, f'peak {np.max(np.abs(mix)):.2f}')
