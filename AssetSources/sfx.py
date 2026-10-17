"""Procedural SFX for Re + the riddle cutscene. Exports mono/stereo OGG Vorbis 44.1k."""
import os
ASSETS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets").replace("\\", "/")
import numpy as np
import soundfile as sf
from scipy import signal
import os

SR = 44100
OUT = ASSETS + "/Sounds/Re/"
OUT_C = ASSETS + "/Sounds/Cutscene/"
OUT_M = ASSETS + "/Music/"
rng = np.random.default_rng(1234)


# ---------------------------------------------------------------- primitives
def t_(dur):
    return np.arange(int(dur * SR)) / SR


def env(dur, a=0.005, d=0.1, s=0.0, r=0.2, curve=3.0):
    """ADSR-ish with exponential decay. Returns array of len dur."""
    n = int(dur * SR)
    t = np.arange(n) / SR
    e = np.ones(n)
    att = np.clip(t / max(a, 1e-4), 0, 1)
    dec = s + (1 - s) * np.exp(-np.clip(t - a, 0, None) / max(d, 1e-4) * curve / 3)
    e = att * np.where(t < a, 1, dec)
    rel_start = dur - r
    e *= np.where(t > rel_start, np.clip((dur - t) / max(r, 1e-4), 0, 1), 1)
    return e


def noise(dur):
    return rng.standard_normal(int(dur * SR))


def bp(x, lo, hi, order=2):
    lo = max(lo, 10); hi = min(hi, SR / 2 - 100)
    b, a = signal.butter(order, [lo, hi], btype="band", fs=SR)
    return signal.lfilter(b, a, x)


def lp(x, f, order=2):
    b, a = signal.butter(order, min(f, SR / 2 - 100), btype="low", fs=SR)
    return signal.lfilter(b, a, x)


def hp(x, f, order=2):
    b, a = signal.butter(order, f, btype="high", fs=SR)
    return signal.lfilter(b, a, x)


def sweep_filter(x, f0, f1, q=4.0, block=256, kind="band"):
    """Time-varying bandpass by block processing with state carry (resonant sweep)."""
    y = np.zeros_like(x)
    n = len(x)
    nb = n // block + 1
    zi = None
    for k in range(nb):
        s, e = k * block, min(n, (k + 1) * block)
        if s >= e:
            break
        tt = s / max(1, n - 1)
        f = f0 * (f1 / f0) ** tt
        bw = f / q
        b, a = signal.butter(2, [max(20, f - bw / 2), min(SR / 2 - 200, f + bw / 2)], btype="band", fs=SR)
        if zi is None or len(zi) != max(len(a), len(b)) - 1:
            zi = signal.lfilter_zi(b, a) * 0
        y[s:e], zi = signal.lfilter(b, a, x[s:e], zi=zi)
    return y


def osc(freq, dur, shape="sine", phase=0.0):
    """freq can be scalar or array (len = dur*SR)."""
    n = int(dur * SR)
    f = np.broadcast_to(np.asarray(freq, float), (n,)) if np.ndim(freq) == 0 else np.asarray(freq)[:n]
    ph = 2 * np.pi * np.cumsum(f) / SR + phase
    if shape == "sine":
        return np.sin(ph)
    if shape == "saw":
        return 2 * ((ph / (2 * np.pi)) % 1) - 1
    if shape == "square":
        return np.sign(np.sin(ph))
    if shape == "tri":
        return 2 * np.abs(2 * ((ph / (2 * np.pi)) % 1) - 1) - 1


def expsweep(f0, f1, dur, curve=1.0):
    t = np.linspace(0, 1, int(dur * SR)) ** curve
    return f0 * (f1 / f0) ** t


def bitcrush(x, bits=6, down=4):
    q = 2 ** (bits - 1)
    y = np.round(x * q) / q
    if down > 1:
        y = np.repeat(y[::down], down)[: len(x)]
    return y


def reverb(x, decay=2.0, mix=0.35, pre=0.02, bright=6000, stereo=False):
    n = int(decay * SR)
    t = np.arange(n) / SR
    outs = []
    for ch in range(2 if stereo else 1):
        ir = rng.standard_normal(n) * np.exp(-t / (decay / 6.9))
        ir = lp(ir, bright)
        ir[: int(pre * SR)] = 0
        ir /= np.sqrt(np.sum(ir ** 2)) + 1e-9
        wet = signal.fftconvolve(x, ir)
        dry = np.concatenate([x, np.zeros(len(wet) - len(x))])
        outs.append(dry * (1 - mix) + wet * mix)
    return np.stack(outs, -1) if stereo else outs[0]


def pad(x, dur):
    n = int(dur * SR)
    if len(x) >= n:
        return x[:n]
    return np.concatenate([x, np.zeros(n - len(x))])


def mix(*parts):
    n = max(len(p) for p in parts)
    out = np.zeros(n)
    for p in parts:
        out[: len(p)] += p
    return out


def at(x, start, total):
    out = np.zeros(int(total * SR))
    s = int(start * SR)
    e = min(len(out), s + len(x))
    out[s:e] += x[: e - s]
    return out


def normalize(x, peak=0.89):
    m = np.max(np.abs(x)) + 1e-9
    return x / m * peak


def soft_clip(x, drive=1.5):
    return np.tanh(x * drive) / np.tanh(drive)


def fade_tail(x, ms=30):
    n = int(ms / 1000 * SR)
    if x.ndim == 1:
        x[-n:] *= np.linspace(1, 0, n)
    else:
        x[-n:] *= np.linspace(1, 0, n)[:, None]
    return x


def write(x, path, peak=0.89):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    x = normalize(x, peak)
    x = fade_tail(x.copy())
    sf.write(path, x.astype(np.float32), SR, format="OGG", subtype="VORBIS")


def stereoize(x, width=0.012):
    d = int(width * SR)
    l = x
    r = np.concatenate([np.zeros(d), x])[: len(x)]
    return np.stack([l, r], -1)


def bell(freq, dur, inharm=(1, 2.76, 5.4, 8.93), decays=(1, 0.6, 0.35, 0.2), amp=(1, 0.5, 0.25, 0.12)):
    out = np.zeros(int(dur * SR))
    t = t_(dur)
    for h, dcy, a in zip(inharm, decays, amp):
        out += a * np.sin(2 * np.pi * freq * h * t) * np.exp(-t / (dur * 0.35 * dcy))
    return out * env(dur, a=0.002, d=dur, r=0.05)


def growl(dur, f0=55, f1=40, rough=0.6, formants=((400, 900), (800, 1500)), sweep=None):
    """Monster vocal: saw with jittery pitch + amplitude roughness through formant bands."""
    n = int(dur * SR)
    jitter = lp(rng.standard_normal(n), 30) * 8
    f = expsweep(f0, f1, dur) + jitter
    src = osc(f, dur, "saw")
    am = 1 + rough * lp(rng.standard_normal(n), 60) * 3
    src = src * am + 0.3 * noise(dur)
    out = np.zeros(n)
    for lo, hi in formants:
        out += bp(src, lo, hi)
    if sweep:
        out += 0.8 * sweep_filter(src, sweep[0], sweep[1], q=3)
    return out


# ================================================================ boss sounds
def awaken():
    D = 3.6
    g = growl(D, 70, 38, rough=0.8, formants=((200, 600), (700, 1300), (1800, 2600)), sweep=(1500, 300)) * env(D, a=0.25, d=2.5, s=0.3, r=0.8)
    sub = osc(expsweep(80, 28, D, 0.6), D) * env(D, a=0.05, d=3, r=0.6) * 1.2
    choir = np.zeros(int(D * SR))
    for k, f in enumerate([110, 138.6, 164.8, 207.7, 220 * 1.498]):
        v = osc(f * (1 + 0.004 * np.sin(t_(D) * 5 + k)), D, "saw")
        choir += bp(v, 500, 2400) * 0.35
    choir *= env(D, a=1.2, d=2, s=0.6, r=1.0)
    rev_swell = sweep_filter(noise(1.2), 300, 6000, q=2) * np.linspace(0, 1, int(1.2 * SR)) ** 3
    glitch = bitcrush(bp(noise(D), 2000, 7000) * (np.sin(t_(D) * 2 * np.pi * 17) > 0.6), 4, 8) * env(D, a=0.4, d=1, r=0.3) * 0.25
    x = mix(at(rev_swell, 0, D), at(g, 0.9, D + 1), at(sub, 0.9, D + 1), at(choir, 0.6, D + 1), at(glitch, 1.0, D + 1))
    x = soft_clip(x * 1.2, 2.0)
    return reverb(x, 2.5, 0.3)


def hurt(k):
    D = 0.35
    squelch = sweep_filter(noise(D), 1200 + k * 200, 250, q=5) * env(D, a=0.002, d=0.12, r=0.1) * 1.6
    blub = osc(expsweep(220 + k * 30, 90, D), D) * env(D, a=0.001, d=0.08, r=0.1) * 0.6
    tick = bell(1760 * (1 + k * 0.06), 0.3) * 0.25
    return reverb(mix(squelch, blub, tick), 0.6, 0.15)


def glyph_shoot(k):
    D = 0.5
    f = expsweep(2600 + 300 * k, 900, D, 0.4)
    tone = (osc(f, D) + 0.4 * osc(f * 1.5, D, "tri")) * env(D, a=0.001, d=0.15, r=0.2)
    zap = bitcrush(bp(noise(D), 3000, 9000), 5, 3) * env(D, a=0.001, d=0.04, r=0.05) * 0.5
    shimmer = bell(3520 + 200 * k, 0.45) * 0.3
    return reverb(mix(tone * 0.7, zap, shimmer), 0.9, 0.25)


def goo_spit():
    D = 0.6
    splat = sweep_filter(noise(D), 2500, 300, q=3) * env(D, a=0.002, d=0.12, r=0.15) * 1.4
    thump = osc(expsweep(120, 45, D), D) * env(D, a=0.002, d=0.15, r=0.2)
    bubbles = np.zeros(int(D * SR))
    for j in range(5):
        b = osc(expsweep(500 + rng.random() * 600, 900 + rng.random() * 400, 0.06), 0.06) * env(0.06, a=0.001, d=0.03, r=0.02)
        bubbles = mix(bubbles, at(b, 0.05 + j * 0.06 + rng.random() * 0.03, D))
    return reverb(mix(splat, thump, bubbles * 0.4), 0.7, 0.2)


def dash():
    D = 0.9
    wh = sweep_filter(noise(D), 300, 3000, q=2.5) * np.sin(np.linspace(0, np.pi, int(D * SR))) ** 2
    rum = lp(noise(D), 120) * 3 * env(D, a=0.05, d=0.6, r=0.3)
    tone = osc(expsweep(180, 60, D), D, "saw")
    tone = lp(tone, 800) * env(D, a=0.02, d=0.4, r=0.3) * 0.4
    return reverb(mix(wh * 1.3, rum, tone), 0.8, 0.2)


def charge():
    D = 1.6
    f = expsweep(200, 1800, D, 1.6)
    riser = (osc(f, D, "saw") * 0.3 + osc(f * 1.01, D, "saw") * 0.3)
    riser = sweep_filter(riser, 400, 6000, q=2)
    grains = np.zeros(int(D * SR))
    for j in range(60):
        st = rng.random() * D * 0.95
        fr = 2000 + rng.random() * 5000 * (st / D)
        g = osc(fr, 0.03) * np.hanning(int(0.03 * SR))
        grains = mix(grains, at(g, st, D))
    e = np.linspace(0, 1, int(D * SR)) ** 2
    return reverb(mix(riser * e, grains * 0.35 * e), 0.8, 0.25)


def lance_telegraph():
    D = 0.9
    f = 1318.5
    tone = (osc(f, D) + 0.5 * osc(f * 2.01, D) + 0.3 * osc(f * 0.5, D, "tri"))
    trem = 0.6 + 0.4 * np.sin(2 * np.pi * 14 * t_(D))
    return reverb(tone * trem * env(D, a=0.05, d=0.6, s=0.3, r=0.25) * 0.5, 1.2, 0.35)


def lance_fire():
    D = 1.2
    crack = hp(noise(D), 1500) * env(D, a=0.0005, d=0.03, r=0.05) * 2
    body = sweep_filter(noise(D), 5000, 400, q=3) * env(D, a=0.001, d=0.25, r=0.3)
    thud = osc(expsweep(150, 40, D), D) * env(D, a=0.001, d=0.2, r=0.3)
    ring = bell(880, 1.0) * 0.3
    return reverb(soft_clip(mix(crack, body, thud, ring), 1.5), 1.4, 0.3)


def sigil():
    D = 1.6
    chord = [523.25, 659.25, 783.99, 987.77]
    x = np.zeros(int(D * SR))
    for k, f in enumerate(chord):
        x = mix(x, at(bell(f, D - k * 0.08) * 0.4, k * 0.07, D))
    swell = bp(noise(D), 3000, 8000) * np.linspace(0, 1, int(D * SR)) ** 2 * env(D, a=0.01, d=D, r=0.2) * 0.25
    return reverb(mix(x, swell), 2.0, 0.4)


def explode():
    D = 3.0
    sub = osc(expsweep(90, 22, D, 0.5), D) * env(D, a=0.002, d=1.0, r=1.0) * 1.4
    burst = lp(noise(D), 3500) * env(D, a=0.001, d=0.5, r=1.2)
    crackle = np.zeros(int(D * SR))
    for j in range(140):
        st = rng.random() ** 1.6 * D * 0.8
        c = hp(noise(0.01), 2000) * np.hanning(int(0.01 * SR)) * (1 - st / D)
        crackle = mix(crackle, at(c, st, D))
    glass = mix(bell(1975, 2.0) * 0.2, bell(2637, 1.8) * 0.15)
    x = soft_clip(mix(sub, burst, crackle * 0.6, at(glass, 0.05, D)), 2.2)
    return reverb(x, 2.5, 0.3)


def death_buildup():
    D = 4.5
    f = expsweep(60, 240, D, 1.4)
    drone = sum(osc(f * m * (1 + 0.003 * k), D, "saw") * a for k, (m, a) in enumerate([(1, 0.4), (1.5, 0.25), (2, 0.2), (3.01, 0.1)]))
    drone = sweep_filter(drone, 200, 3500, q=1.6)
    stutter = (np.sin(2 * np.pi * expsweep(3, 24, D, 1.5).cumsum() / SR) > 0).astype(float)
    glitch = bitcrush(bp(noise(D), 1500, 8000), 4, 6) * stutter * np.linspace(0, 1, int(D * SR)) ** 2 * 0.6
    e = np.linspace(0.1, 1, int(D * SR)) ** 1.5
    return reverb(soft_clip(mix(drone * e, glitch), 1.6), 2.0, 0.35)


# ================================================================ cutscene sounds
def eyes_open():
    D = 3.0
    breath = bp(noise(D), 400, 2200) * np.sin(np.linspace(0, np.pi, int(D * SR))) ** 2 * 0.25
    chime = mix(at(bell(659.25, 2.4) * 0.5, 0.5, D), at(bell(987.77, 2.2) * 0.35, 0.75, D), at(bell(1318.5, 2.0) * 0.2, 1.0, D))
    pad_ = sum(osc(f, D, "tri") for f in (164.8, 246.9)) * env(D, a=1.2, d=2, s=0.4, r=1) * 0.12
    return reverb(stereoize(mix(breath, chime, pad_))[:, 0], 3.0, 0.5, stereo=True)


def riddle_select(level):
    """level 1..5: each pick is more corrupted."""
    D = 1.6
    base = [783.99, 739.99, 698.46, 622.25, 554.37][level - 1]
    tone = bell(base, 1.4, inharm=(1, 2.0, 3.0, 4.2 + level * 0.3), decays=(1, 0.7, 0.4, 0.3))
    if level >= 2:  # detune chorus
        tone = tone + bell(base * (1 + 0.006 * level), 1.4) * 0.6
    if level >= 3:  # bitcrush & stutter
        st = (np.sin(2 * np.pi * (6 + level * 3) * t_(1.4)) > -0.3 + 0.12 * level)
        tone = bitcrush(tone, 9 - level, 1 + level // 2) * st
    glitch = np.zeros(int(D * SR))
    for j in range(level * level * 2):
        st = rng.random() * 0.9
        g = bitcrush(bp(noise(0.04), 1000 + rng.random() * 6000, 9000), 3, 4) * np.hanning(int(0.04 * SR))
        glitch = mix(glitch, at(g * (0.15 + 0.06 * level), st, D))
    low = np.zeros(int(D * SR))
    if level >= 4:
        low = osc(expsweep(110, 55, D), D, "saw")
        low = lp(low, 600) * env(D, a=0.01, d=0.8, r=0.3) * (0.3 * (level - 3))
    whisper = bp(noise(D), 2500, 6000) * env(D, a=0.2, d=0.8, r=0.5) * 0.04 * level
    x = mix(pad(tone, D), glitch, low, whisper)
    return reverb(soft_clip(x, 1 + level * 0.3), 2.2, 0.45, stereo=True)


def eyes_close():
    D = 2.2
    tone = osc(expsweep(330, 110, D, 0.7), D, "tri") * env(D, a=0.02, d=1.2, r=0.6) * 0.5
    sub = osc(expsweep(70, 35, D), D) * env(D, a=0.01, d=1.4, r=0.5)
    rustle = lp(noise(0.5), 1500) * env(0.5, a=0.05, d=0.3, r=0.2) * 0.2
    return reverb(mix(tone, sub, rustle), 2.5, 0.4, stereo=True)


def heartbeat():
    D = 1.0
    def thump(f):
        return osc(expsweep(f, 35, 0.18), 0.18) * env(0.18, a=0.002, d=0.07, r=0.08)
    return reverb(mix(at(thump(70), 0, D), at(thump(60) * 0.7, 0.24, D)), 0.6, 0.15)


def burst():
    """Big slow-motion transformation: suck-in, impact, slowed choir, glyph rain, long tail."""
    D = 8.0
    suck = sweep_filter(noise(1.3), 200, 9000, q=2) * np.linspace(0, 1, int(1.3 * SR)) ** 4 * 1.2
    suck_tone = osc(expsweep(60, 900, 1.3, 2.5), 1.3, "saw")
    suck_tone = lp(suck_tone, 4000) * np.linspace(0, 1, int(1.3 * SR)) ** 3 * 0.4
    imp_sub = osc(expsweep(70, 18, 4.0, 0.4), 4.0) * env(4.0, a=0.001, d=2.2, r=1.5) * 1.6
    imp_noise = lp(noise(3.0), 5000) * env(3.0, a=0.001, d=0.6, r=1.5) * 1.3
    crack = hp(noise(0.2), 1000) * env(0.2, a=0.0005, d=0.03, r=0.1) * 2
    # slowed, pitched-down choir chord (A minor add9) that swells after the hit
    choir = np.zeros(int(6 * SR))
    t6 = t_(6)
    for k, f in enumerate([110, 130.8, 164.8, 220, 246.9, 329.6]):
        drift = 1 - 0.06 * (1 - np.exp(-t6 / 1.5))  # slow-mo pitch sag
        v = osc(f * drift * (1 + 0.003 * np.sin(t6 * 4 + k)), 6, "saw")
        choir += (bp(v, 300, 1100) + 0.6 * bp(v, 1800, 2800)) * 0.25
    choir *= env(6, a=0.5, d=4, s=0.4, r=2.0)
    shimmer = np.zeros(int(6 * SR))
    for j in range(260):
        st = rng.random() ** 1.3 * 5.5
        fr = 1500 + rng.random() * 6000
        g = osc(fr, 0.05) * np.hanning(int(0.05 * SR)) * (1 - st / 6) ** 1.5
        shimmer = mix(shimmer, at(g, st, 6))
    hit_t = 1.3
    x = mix(at(suck, 0, D), at(suck_tone, 0, D), at(imp_sub, hit_t, D), at(imp_noise, hit_t, D), at(crack, hit_t, D),
            at(choir, hit_t + 0.15, D), at(shimmer * 0.3, hit_t + 0.1, D))
    x = soft_clip(x * 1.1, 2.0)
    return reverb(x, 4.0, 0.42, stereo=True, bright=7000)


def glyph_stream_loop():
    D = 4.0
    t = t_(D)
    whisper = bp(noise(D), 1800, 6500) * (0.6 + 0.4 * np.sin(2 * np.pi * t / D * 4))
    ticks = np.zeros(int(D * SR))
    for j in range(180):
        st = rng.random() * (D - 0.05)
        g = osc(2000 + rng.random() * 5000, 0.02) * np.hanning(int(0.02 * SR))
        ticks = mix(ticks, at(g, st, D))
    drone = sum(osc(f * 4 / D * round(D * f / 4) / f if False else f, D) for f in (220, 330, 440.0)) * 0.08
    x = mix(whisper * 0.5, ticks[: int(D * SR)] * 0.3, drone)
    # make loop seamless: crossfade end into start
    n = int(0.3 * SR)
    x[:n] = x[:n] * np.linspace(0, 1, n) + x[-n:] * np.linspace(1, 0, n)
    x = x[:-n]
    return x


# ================================================================ music-folder tracks
def silence():
    return np.zeros(int(4 * SR)) + rng.standard_normal(int(4 * SR)) * 1e-5


def riddle_ambience():
    """Seamless 24s drone: low pad, distant bells, breath. Loopable."""
    D = 24.0
    t = t_(D)
    pad_ = np.zeros(int(D * SR))
    for f, a in [(55, 0.5), (82.4, 0.35), (110, 0.3), (164.8, 0.15), (196, 0.08)]:
        cyc = round(f * D) / D  # integer cycles -> seamless
        pad_ += osc(cyc, D, "tri") * a
    pad_ = lp(pad_, 900) * (0.75 + 0.25 * np.sin(2 * np.pi * t / D * 2))
    air = bp(noise(D), 300, 1600) * (0.5 + 0.5 * np.sin(2 * np.pi * t / D * 3) ** 2) * 0.08
    bells = np.zeros(int(D * SR))
    notes = [659.25, 587.33, 493.88, 440.0, 392.0, 493.88]
    for k, f in enumerate(notes):
        bells = mix(bells, at(bell(f, 3.5) * 0.12, 1.5 + k * 3.8, D + 4))
    tailb = bells[int(D * SR):]; bells = bells[: int(D * SR)].copy(); bells[: len(tailb)] += tailb
    x = mix(pad_ * 0.6, air, bells)
    x = reverb(x, 3.0, 0.35)
    # fold tail into beginning for seamless loop
    head = x[: int(D * SR)].copy()
    tail = x[int(D * SR):]
    head[: len(tail)] += tail
    return stereoize(head, 0.018)


def build():
    write(awaken(), OUT + "ReAwaken.ogg")
    for k in range(3):
        write(hurt(k), OUT + f"ReHurt{k + 1}.ogg", 0.7)
    for k in range(2):
        write(glyph_shoot(k), OUT + f"ReGlyphShoot{k + 1}.ogg", 0.7)
    write(goo_spit(), OUT + "ReGooSpit.ogg", 0.8)
    write(dash(), OUT + "ReDash.ogg")
    write(charge(), OUT + "ReCharge.ogg", 0.75)
    write(lance_telegraph(), OUT + "ReLanceTelegraph.ogg", 0.6)
    write(lance_fire(), OUT + "ReLanceFire.ogg", 0.78)
    write(sigil(), OUT + "ReSigil.ogg", 0.75)
    write(explode(), OUT + "ReExplode.ogg", 0.95)
    write(death_buildup(), OUT + "ReDeathBuildup.ogg")
    write(eyes_open(), OUT_C + "EyesOpen.ogg", 0.7)
    for lv in range(1, 6):
        write(riddle_select(lv), OUT_C + f"RiddleSelect{lv}.ogg", 0.75 + lv * 0.04)
    write(eyes_close(), OUT_C + "EyesClose.ogg", 0.8)
    write(heartbeat(), OUT_C + "Heartbeat.ogg", 0.8)
    write(burst(), OUT_C + "Burst.ogg", 0.97)
    write(glyph_stream_loop(), OUT_C + "GlyphStreamLoop.ogg", 0.5)
    write(silence(), OUT_M + "Silence.ogg", 1e-4)
    write(riddle_ambience(), OUT_M + "RiddleAmbience.ogg", 0.6)
    print("sfx done")


if __name__ == "__main__":
    build()
