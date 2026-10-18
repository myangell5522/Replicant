"""Phase-3 sounds: Recursion, the centipede — same DSP helpers as sfx.py."""
import numpy as np
from sfx import (SR, t_, env, noise, bp, lp, hp, sweep_filter, osc, expsweep, bitcrush, reverb, pad, mix, at,
                 soft_clip, write, bell, stereoize, growl, ASSETS, rng)

OUT = ASSETS + "/Sounds/Recursion/"


def chitter(dur, rate=38, seed=0):
    """Rapid chitin clicks (legs on stone / mandibles)."""
    n = int(dur * SR)
    out = np.zeros(n)
    r = np.random.default_rng(seed)
    t = 0.0
    while t < dur - 0.02:
        click = hp(r.standard_normal(int(0.012 * SR)), 2500) * env(0.012, a=0.0003, d=0.004, r=0.004)
        out = mix(out, at(click * (0.4 + r.random() * 0.6), t, dur))
        t += (1 / rate) * (0.6 + r.random() * 0.8)
    return out[:n]


def roar():
    D = 3.2
    g = growl(D, 62, 34, rough=0.9, formants=((180, 520), (650, 1250), (1700, 2900)), sweep=(2600, 260)) * env(D, a=0.12, d=2.2, s=0.35, r=0.9)
    scream = growl(D, 300, 180, rough=0.5, formants=((1800, 3600), (3800, 6000))) * env(D, a=0.3, d=1.2, s=0.2, r=1.0) * 0.35
    sub = osc(expsweep(70, 24, D, 0.7), D) * env(D, a=0.04, d=2.8, r=0.6) * 1.1
    cl = chitter(D, 46, 3) * env(D, a=0.6, d=2, r=0.6) * 0.5
    return reverb(soft_clip(mix(g, scream, sub, cl), 2.2), 2.8, 0.35, stereo=True)


def screech():
    D = 1.1
    s = growl(D, 420, 160, rough=0.7, formants=((1200, 2600), (3000, 5200)), sweep=(5000, 900)) * env(D, a=0.01, d=0.5, s=0.2, r=0.4)
    crunch = bitcrush(hp(noise(D), 800), 5, 6) * env(D, a=0.001, d=0.15, r=0.1) * 0.5
    return reverb(soft_clip(mix(s, crunch), 2.0), 1.4, 0.3, stereo=True)


def rush():
    D = 1.8
    w = sweep_filter(noise(D), 180, 3200, q=1.6) * env(D, a=0.5, d=0.9, s=0.0, r=0.5)
    w2 = sweep_filter(noise(D), 3200, 200, q=2.0) * env(D, a=0.9, d=0.6, r=0.4) * 0.6
    rumble = lp(noise(D), 90) * env(D, a=0.3, d=1.2, r=0.4) * 2.5
    legs = chitter(D, 70, 5) * env(D, a=0.4, d=1.0, r=0.4) * 0.6
    return reverb(mix(w, w2, rumble, legs), 1.5, 0.25, stereo=True)


def bite():
    D = 0.8
    snap = hp(noise(D), 1500) * env(D, a=0.0005, d=0.03, r=0.05) * 1.2
    crunch = bitcrush(bp(noise(D), 200, 2400), 4, 8) * env(D, a=0.002, d=0.18, r=0.15)
    thud = osc(expsweep(140, 40, D), D) * env(D, a=0.001, d=0.2, r=0.2) * 1.2
    wet = lp(noise(D), 700) * env(D, a=0.02, d=0.3, r=0.2) * 0.5
    return reverb(soft_clip(mix(snap, crunch, thud, wet), 2.4), 1.0, 0.25, stereo=True)


def telegraph():
    D = 1.0
    f = expsweep(300, 2400, D, 2.0)
    tone = (osc(f, D, "saw") * 0.4 + osc(f * 1.5, D) * 0.4) * env(D, a=0.05, d=0.9, s=0.6, r=0.06)
    ring = bell(1800, D, inharm=(1, 2.4, 3.9), decays=(1, .6, .3), amp=(1, .4, .2)) * 0.3
    return reverb(mix(bp(tone, 300, 6000), ring), 0.9, 0.25)


def portal():
    D = 1.6
    suck = sweep_filter(noise(D), 4000, 120, q=2.5) * env(D, a=0.6, d=0.8, r=0.3)
    sub = osc(expsweep(30, 110, D, 2.0), D) * env(D, a=0.5, d=0.9, r=0.3) * 1.4
    shimmer = sweep_filter(noise(D), 900, 7000, q=8) * env(D, a=0.3, d=1.0, r=0.4) * 0.5
    return reverb(soft_clip(mix(suck, sub, shimmer), 1.6), 2.2, 0.4, stereo=True)


def erupt():
    D = 2.0
    blast = lp(noise(D), 400) * env(D, a=0.002, d=0.5, r=0.6) * 2.0
    crack = hp(noise(D), 2000) * env(D, a=0.001, d=0.06, r=0.1)
    sub = osc(expsweep(90, 26, D, 0.5), D) * env(D, a=0.002, d=1.2, r=0.6) * 1.5
    sc = screech() [: int(D * SR)] if False else 0
    return reverb(soft_clip(mix(blast, crack, sub), 2.0), 2.0, 0.35, stereo=True)


def chain_lock():
    D = 1.2
    links = np.zeros(int(D * SR))
    for k in range(6):
        b = bell(900 + k * 230, 0.6, inharm=(1, 2.6, 4.3), decays=(1, .5, .3), amp=(1, .5, .3)) * (0.5 - k * 0.05)
        links = mix(links, at(b, k * 0.035, D))
    clank = bp(noise(D), 300, 3000) * env(D, a=0.001, d=0.06, r=0.1) * 1.4
    thud = osc(expsweep(120, 50, D), D) * env(D, a=0.001, d=0.15, r=0.2)
    return reverb(soft_clip(mix(links, clank, thud), 1.8), 1.6, 0.35, stereo=True)


def needle():
    D = 0.35
    z = sweep_filter(noise(D), 6000, 1500, q=5) * env(D, a=0.001, d=0.12, r=0.1)
    t = osc(expsweep(2400, 900, D), D) * env(D, a=0.001, d=0.08, r=0.08) * 0.4
    return mix(z, t)


def death():
    D = 5.0
    g = growl(D, 70, 22, rough=1.0, formants=((150, 500), (600, 1200), (1600, 2600)), sweep=(2400, 120)) * env(D, a=0.05, d=3.5, s=0.2, r=1.2)
    cracks = np.zeros(int(D * SR))
    for k in range(18):
        c = bitcrush(hp(noise(0.25), 900), 5, 4) * env(0.25, a=0.001, d=0.06, r=0.1) * (0.4 + rng.random() * 0.5)
        cracks = mix(cracks, at(c, 0.4 + k * 0.17 + rng.random() * 0.05, D))
    sub = osc(expsweep(60, 18, D, 0.6), D) * env(D, a=0.02, d=4, r=1.0) * 1.2
    shatter = np.zeros(int(D * SR))
    for j in range(20):
        f = 1800 + rng.random() * 5000
        shatter = mix(shatter, at(bell(f, 1.2, inharm=(1, 2.3, 4.1), decays=(1, .5, .3), amp=(1, .5, .3)) * 0.25, 3.4 + rng.random() * 0.2, D))
    return reverb(soft_clip(mix(g, cracks, sub, shatter), 1.8), 3.5, 0.4, stereo=True)


def ominous_drone():
    """The eyes open in the sky: a sub swell, a dissonant cluster that never resolves, breathing noise."""
    D = 7.0
    t = t_(D)
    cluster = np.zeros(int(D * SR))
    for f, a in ((55, 1.0), (58.3, 0.8), (82.4, 0.6), (116.5, 0.4), (164.8, 0.25), (233.1, 0.15)):
        wob = 1 + 0.004 * np.sin(2 * np.pi * (0.1 + f * 0.001) * t)
        cluster += a * osc(f * wob, D, "saw")
    cluster = lp(cluster, 900) * env(D, a=2.5, d=4, s=0.7, r=1.6)
    sub = osc(expsweep(28, 36, D), D) * env(D, a=3, d=4, s=0.8, r=1.5) * 1.4
    breath = sweep_filter(noise(D), 300, 1800, q=3) * (0.5 + 0.5 * np.sin(2 * np.pi * 0.35 * t)) * env(D, a=2, d=4, s=0.5, r=1.5) * 0.4
    whisper = bp(noise(D), 3000, 7000) * (np.sin(2 * np.pi * 1.7 * t) > 0.6) * env(D, a=3, d=3, s=0.3, r=1) * 0.12
    return reverb(soft_clip(mix(cluster, sub, breath, whisper), 1.3), 4.0, 0.5, stereo=True)


def sonic_boom():
    D = 1.4
    crack = hp(noise(D), 1200) * env(D, a=0.0005, d=0.03, r=0.05) * 1.6
    boom = lp(noise(D), 160) * env(D, a=0.002, d=0.35, r=0.3) * 2.4
    sub = osc(expsweep(70, 30, D), D) * env(D, a=0.001, d=0.5, r=0.4) * 1.3
    tail = sweep_filter(noise(D), 4000, 300, q=1.5) * env(D, a=0.01, d=0.7, r=0.4) * 0.6
    return reverb(soft_clip(mix(crack, boom, sub, tail), 2.0), 2.0, 0.35, stereo=True)


def ground_skim():
    D = 1.6
    grit = bp(noise(D), 150, 2200) * (0.6 + 0.4 * lp(np.abs(noise(D)), 25) * 6) * env(D, a=0.05, d=1.1, r=0.4)
    rumble = lp(noise(D), 80) * env(D, a=0.05, d=1.2, r=0.4) * 2.5
    cl = chitter(D, 90, 7) * env(D, a=0.05, d=1.0, r=0.4) * 0.5
    return reverb(soft_clip(mix(grit, rumble, cl), 1.6), 1.2, 0.25, stereo=True)


def constrict():
    D = 2.4
    creak = sweep_filter(osc(expsweep(70, 50, D), D, "saw") * (1 + 0.6 * lp(noise(D), 18) * 8), 300, 900, q=6) * env(D, a=0.3, d=1.6, r=0.5)
    plates = np.zeros(int(D * SR))
    for k in range(10):
        plates = mix(plates, at(bitcrush(hp(noise(0.08), 1500), 5, 3) * env(0.08, a=0.001, d=0.02, r=0.03) * 0.6, 0.15 + k * 0.2, D))
    return reverb(soft_clip(mix(creak, plates), 1.5), 1.8, 0.35, stereo=True)


def rewind_tape():
    D = 1.8
    f = expsweep(1800, 120, D, 0.6)
    warble = osc(f * (1 + 0.05 * np.sin(2 * np.pi * 9 * t_(D))), D, "saw") * 0.3
    hiss = hp(noise(D), 3000) * 0.25
    rev = sweep_filter(noise(D), 200, 5000, q=2) * env(D, a=1.4, d=0.2, r=0.15)   # reverse swell
    clunk = lp(noise(D), 400) * env(D, a=0.001, d=0.05, r=0.05)
    out = mix(bitcrush(warble, 6, 3), hiss * env(D, a=0.01, d=1.6, r=0.1), rev * 0.8, at(clunk, D - 0.12, D))
    return reverb(out, 1.0, 0.2, stereo=True)


def execute_glitch():
    D = 2.2
    out = np.zeros(int(D * SR))
    r = np.random.default_rng(42)
    t = 0.0
    while t < D - 0.1:
        seg = r.uniform(0.03, 0.12)
        f = r.choice([110, 220, 440, 880, 1760]) * r.uniform(0.9, 1.1)
        tone = bitcrush(osc(f, seg, "square"), 3, int(r.integers(2, 10))) * env(seg, a=0.001, d=seg, r=0.01) * r.uniform(0.3, 0.8)
        out = mix(out, at(tone, t, D))
        t += seg * r.uniform(0.6, 1.4)
    sub = osc(expsweep(90, 40, D), D) * env(D, a=0.01, d=1.5, r=0.5)
    return reverb(soft_clip(mix(out, sub, screech()[:int(D * SR), 0] * 0.6), 1.8), 1.6, 0.3, stereo=True)


def build_extra():
    write(ominous_drone(), OUT + "OminousDrone.ogg")
    write(sonic_boom(), OUT + "SonicBoom.ogg")
    write(ground_skim(), OUT + "GroundSkim.ogg")
    write(constrict(), OUT + "Constrict.ogg")
    write(rewind_tape(), OUT + "RewindTape.ogg")
    write(execute_glitch(), OUT + "ExecuteGlitch.ogg")


def build():
    write(roar(), OUT + "Roar.ogg")
    write(screech(), OUT + "Screech.ogg")
    write(rush(), OUT + "Rush.ogg")
    write(bite(), OUT + "Bite.ogg")
    write(telegraph(), OUT + "Telegraph.ogg")
    write(portal(), OUT + "Portal.ogg")
    write(erupt(), OUT + "Erupt.ogg")
    write(chain_lock(), OUT + "ChainLock.ogg")
    write(needle(), OUT + "Needle.ogg", peak=0.7)
    write(death(), OUT + "Death.ogg")
    for k in range(2):
        write(reverb(chitter(0.6, 50, 10 + k) * env(0.6, a=0.02, d=0.4, r=0.1), 0.6, 0.2), OUT + f"Chitter{k + 1}.ogg", peak=0.6)


if __name__ == "__main__":
    import sys
    if "extra" in sys.argv:
        build_extra()
    else:
        build()
        build_extra()
