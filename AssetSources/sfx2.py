"""Phase-2 sounds (twins, orbs, dimension) — same DSP helpers as sfx.py."""
import numpy as np
from sfx import (SR, t_, env, noise, bp, lp, hp, sweep_filter, osc, expsweep, bitcrush, reverb, pad, mix, at,
                 soft_clip, write, bell, stereoize, ASSETS, rng)

OUT = ASSETS + "/Sounds/Twins/"
OUT_M = ASSETS + "/Music/"


def orb_shatter(k):
    D = 1.0
    glass = np.zeros(int(D * SR))
    for j in range(14):
        f = 2500 + rng.random() * 6000
        st = rng.random() * 0.08
        glass = mix(glass, at(bell(f, 0.5, inharm=(1, 2.32, 4.1), decays=(1, .5, .3), amp=(1, .5, .3)) * (0.3 + rng.random() * 0.3), st, D))
    crack = hp(noise(D), 3000) * env(D, a=0.0005, d=0.04, r=0.05)
    tone = bell(1046.5 * (1 + k * 0.12), 0.9) * 0.35
    return reverb(mix(glass, crack * 0.8, tone), 1.2, 0.3)


def orb_clink():
    D = 0.5
    ring = bell(3135, 0.45, inharm=(1, 2.76, 5.4), decays=(1, .4, .2), amp=(1, .4, .2))
    tick = hp(noise(D), 4000) * env(D, a=0.0005, d=0.015, r=0.02)
    return reverb(mix(ring * 0.6, tick), 0.6, 0.2)


def bell_toll():
    D = 4.0
    b = bell(98, D, inharm=(1, 2.0, 2.4, 3.0, 4.2, 5.4), decays=(1, .8, .7, .5, .35, .25), amp=(1, .6, .5, .35, .25, .15))
    hum = osc(49, D) * env(D, a=0.01, d=2.5, r=1.0) * 0.4
    strike = lp(noise(0.1), 1500) * env(0.1, a=0.001, d=0.03, r=0.05)
    return reverb(mix(b, hum, strike), 3.5, 0.45, stereo=True)


def note(i):
    D = 1.4
    scale = [523.25, 587.33, 659.25, 783.99, 880.0]
    f = scale[i]
    return reverb(bell(f, 1.2, inharm=(1, 2.0, 3.0, 4.1), decays=(1, .6, .4, .2), amp=(1, .3, .15, .08)) * 0.6, 1.8, 0.4)


def twin_dash():
    D = 0.8
    wh = sweep_filter(noise(D), 600, 5000, q=2.5) * np.sin(np.linspace(0, np.pi, int(D * SR))) ** 2
    sh = bell(1567, 0.6) * 0.25
    return reverb(mix(wh, sh), 0.8, 0.25)


def twin_cast():
    D = 1.6
    sparkle = np.zeros(int(D * SR))
    for j in range(40):
        st = rng.random() * 0.9
        sparkle = mix(sparkle, at(osc(2000 + rng.random() * 5000, 0.04) * np.hanning(int(0.04 * SR)), st, D))
    chord = mix(bell(659.25, 1.4) * 0.4, at(bell(987.77, 1.3) * 0.3, 0.06, D), at(bell(1318.5, 1.2) * 0.2, 0.12, D))
    return reverb(mix(sparkle * 0.3, chord), 2.0, 0.4)


def twin_hurt(k):
    D = 0.6
    f = 880 * (1 + 0.07 * k)
    tone = osc(expsweep(f, f * 0.7, D), D, "tri") * env(D, a=0.005, d=0.15, r=0.2) * 0.4
    glass = bell(f * 2.5, 0.4) * 0.25
    return reverb(mix(tone, glass, hp(noise(D), 3000) * env(D, a=0.001, d=0.03, r=0.03) * 0.2), 0.9, 0.3)


def chain_form():
    D = 1.4
    hum = sum(osc(f, D, "saw") for f in (220, 221.5, 330))
    hum = sweep_filter(hum, 300, 3000, q=2) * np.linspace(0, 1, int(D * SR)) ** 1.5
    links = np.zeros(int(D * SR))
    for j in range(10):
        links = mix(links, at(bell(2800 + j * 90, 0.2) * 0.2, 0.1 + j * 0.11, D))
    return reverb(mix(hum * 0.4, links), 1.2, 0.3)


def beam_fire():
    D = 1.8
    zap = sweep_filter(noise(D), 6000, 800, q=3) * env(D, a=0.001, d=0.3, r=0.6)
    tone = sum(osc(f, D, "saw") for f in (110, 165, 220)) * env(D, a=0.01, d=1.2, s=0.3, r=0.4)
    return reverb(soft_clip(mix(zap, lp(tone, 1800) * 0.4), 1.5), 1.5, 0.3)


def transform():
    D = 5.0
    rise = sweep_filter(noise(2.5), 200, 9000, q=2) * np.linspace(0, 1, int(2.5 * SR)) ** 3
    hit_sub = osc(expsweep(80, 25, 2.5, 0.5), 2.5) * env(2.5, a=0.001, d=1.2, r=0.8) * 1.5
    choir = np.zeros(int(3.0 * SR))
    for f in (220, 261.6, 329.6, 440, 523.25):
        v = osc(f * (1 + 0.003 * np.sin(t_(3.0) * 5)), 3.0, "saw")
        choir += bp(v, 400, 2600) * 0.25
    choir *= env(3.0, a=0.05, d=2.5, s=0.3, r=0.6)
    x = mix(at(rise, 0, D), at(hit_sub, 2.5, D), at(choir, 2.5, D), at(bell(1318.5, 2.4) * 0.4, 2.5, D))
    return reverb(soft_clip(x, 1.6), 3.0, 0.4, stereo=True)


def twin_death():
    D = 5.0
    fall = osc(expsweep(880, 110, 3.0, 0.6), 3.0, "tri") * env(3.0, a=0.01, d=2.0, r=1.0) * 0.3
    shards = np.zeros(int(D * SR))
    for j in range(40):
        st = rng.random() ** 1.5 * 3.5
        shards = mix(shards, at(bell(2000 + rng.random() * 5000, 0.4) * 0.15, st, D))
    sigh = bp(noise(D), 500, 1500) * env(D, a=0.6, d=2.0, r=1.5) * 0.1
    return reverb(mix(pad(fall, D), shards, sigh), 3.5, 0.5, stereo=True)


def rebirth_swell():
    """Music-folder track that carries the burst through the dimension travel (music isn't stopped by it)."""
    D = 22.0
    t = t_(D)
    pad_ = np.zeros(int(D * SR))
    for f, a in ((55, .5), (82.4, .35), (110, .3), (164.8, .2), (220, .15), (329.6, .08)):
        pad_ += osc(f * (1 + 0.002 * np.sin(t * 0.7 + f)), D, "saw") * a
    pad_ = lp(pad_, 1600) * env(D, a=0.5, d=D, r=6.0)
    choir = np.zeros(int(D * SR))
    for f in (220, 277.2, 329.6, 440):
        v = osc(f * (1 - 0.04 * (1 - np.exp(-t / 3))), D, "saw")
        choir += bp(v, 500, 2400) * 0.12
    choir *= env(D, a=2.0, d=D, r=8.0)
    shimmer = np.zeros(int(D * SR))
    for j in range(500):
        st = rng.random() * (D - 1)
        g = osc(1800 + rng.random() * 6000, 0.05) * np.hanning(int(0.05 * SR)) * (1 - st / D)
        shimmer = mix(shimmer, at(g, st, D))
    x = mix(pad_ * 0.5, choir, shimmer[: int(D * SR)] * 0.15)
    return reverb(x, 4.0, 0.45, stereo=True)


def build():
    for k in range(3):
        write(orb_shatter(k), OUT + f"OrbShatter{k + 1}.ogg", 0.6)
    write(orb_clink(), OUT + "OrbClink.ogg", 0.5)
    write(bell_toll(), OUT + "BellToll.ogg", 0.85)
    for i in range(5):
        write(note(i), OUT + f"Note{i + 1}.ogg", 0.5)
    write(twin_dash(), OUT + "TwinDash.ogg", 0.7)
    write(twin_cast(), OUT + "TwinCast.ogg", 0.6)
    for k in range(3):
        write(twin_hurt(k), OUT + f"TwinHurt{k + 1}.ogg", 0.5)
    write(chain_form(), OUT + "ChainForm.ogg", 0.6)
    write(beam_fire(), OUT + "BeamFire.ogg", 0.8)
    write(transform(), OUT + "Transform.ogg", 0.95)
    write(twin_death(), OUT + "TwinDeath.ogg", 0.85)
    write(rebirth_swell(), OUT_M + "RebirthSwell.ogg", 0.7)
    print("sfx2 done")


if __name__ == "__main__":
    build()
