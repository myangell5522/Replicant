"""Second-pass VFX textures: hi-res smooth glows (so point sampling never shows),
the multi-layer sigil, fountain-burst textures, gloom overlays.

Smooth textures are authored at 1:1 and are large on purpose — the game draws them
scaled DOWN, which keeps them soft even with Terraria's point sampler.
"""
import os
import numpy as np
from scipy import ndimage
import art
from art import GOLD, GLYPHS

ASSETS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets").replace("\\", "/")
ROOT = ASSETS + "/Textures/"
V = ROOT + "VFX/"


def save1(arr, path):
    art.save(arr, path, scale=1)


def white(alpha):
    out = np.zeros(alpha.shape + (4,))
    out[..., :3] = 255
    out[..., 3] = np.clip(alpha, 0, 1) * 255
    return out


def radial(size):
    x, y = art.grid(size, size)
    c = (size - 1) / 2
    return np.sqrt((x - c) ** 2 + (y - c) ** 2) / c


# ================================================================ smooth primitives
def smooth_set():
    d = radial(512)
    # bloom: gaussian core + long soft tail, exactly 0 at the rim
    a = (np.exp(-(d / 0.28) ** 2) * 0.75 + np.exp(-(d / 0.6) ** 2) * 0.35) * np.clip(1 - d, 0, 1) ** 0.6
    save1(white(a / a.max()), V + "Bloom.png")
    d = radial(256)
    save1(white(np.exp(-(d / 0.3) ** 2) * np.clip(1 - d, 0, 1)), V + "BloomSharp.png")
    # ring
    d = radial(512)
    a = np.exp(-((d - 0.9) / 0.035) ** 2) + 0.35 * np.exp(-((d - 0.84) / 0.1) ** 2)
    save1(white(a * (d < 1)), V + "Ring.png")
    # streak 256x64
    x, y = art.grid(64, 256)
    u = (x - 127.5) / 127.5
    v = (y - 31.5) / 31.5
    a = np.clip(1 - np.abs(u), 0, 1) ** 1.5 * np.exp(-(v / 0.22) ** 2)
    save1(white(a), V + "Streak.png")
    # flare 256
    x, y = art.grid(256, 256)
    dx, dy = (x - 127.5) / 127.5, (y - 127.5) / 127.5
    rr = np.sqrt(dx * dx + dy * dy)
    a = np.exp(-(dy / 0.025) ** 2) * np.clip(1 - np.abs(dx), 0, 1) ** 2.5 + np.exp(-(dx / 0.025) ** 2) * np.clip(1 - np.abs(dy), 0, 1) ** 2.5
    a += np.exp(-(rr / 0.12) ** 2)
    save1(white(a), V + "Flare.png")
    # trail 512x64, horizontally tileable
    x, y = art.grid(64, 512)
    v = (y - 31.5) / 31.5
    n = art.value_noise(64, 512, 48, seed=4, octaves=4, periodic=(512, 64))
    st = art.value_noise(64, 512, 10, seed=9, octaves=2, periodic=(512, 10))
    a = np.exp(-(v / 0.12) ** 2) + np.exp(-(v / 0.45) ** 2) * (0.25 + 0.55 * n) * (0.5 + 0.6 * st)
    save1(white(a), V + "Trail.png")
    # thin filament 512x16
    x, y = art.grid(16, 512)
    v = (y - 7.5) / 7.5
    save1(white(np.exp(-(v / 0.18) ** 2) + 0.25 * np.exp(-(v / 0.6) ** 2)), V + "Thread.png")
    # smoke 256
    d = radial(256)
    n = art.value_noise(256, 256, 40, seed=3, octaves=5)
    a = np.clip(1 - d - (n - 0.5) * 0.7, 0, 1) ** 1.6
    save1(white(a), V + "Smoke.png")
    # fog bank 512x256 (wide, soft, lumpy)
    x, y = art.grid(256, 512)
    u, v = (x - 255.5) / 255.5, (y - 127.5) / 127.5
    n = art.value_noise(256, 512, 60, seed=21, octaves=5)
    a = np.clip(1 - (u * u + v * v * 1.0) ** 0.5 - (n - 0.5) * 0.9, 0, 1) ** 1.3
    save1(white(a), V + "Fog.png")
    # god ray 64x512 (origin at top)
    x, y = art.grid(512, 64)
    u = (x - 31.5) / 31.5
    a = np.exp(-(u / 0.35) ** 2) * (1 - y / 512) ** 1.4 * np.clip(y / 20, 0, 1)
    save1(white(a), V + "GodRay.png")
    # vignette: transparent centre, black edges
    x, y = art.grid(256, 256)
    dx, dy = (x - 127.5) / 127.5, (y - 127.5) / 127.5
    r = np.sqrt(dx * dx * 0.8 + dy * dy)
    a = np.clip((r - 0.35) / 0.75, 0, 1) ** 1.6
    vg = np.zeros((256, 256, 4)); vg[..., 3] = a * 255
    save1(vg, V + "Vignette.png")
    # film grain: white speckles with random low alpha
    rng = np.random.default_rng(5)
    g = rng.random((256, 256))
    a = np.where(g > 0.82, (g - 0.82) / 0.18, 0) * 0.9
    save1(white(a), V + "Grain.png")
    # ash flake
    x, y = art.grid(24, 24)
    e = ((x - 11.5) / 9) ** 2 + ((y - 11.5) / 5) ** 2
    save1(white(np.clip(1 - e, 0, 1) ** 1.5), V + "Ash.png")
    # tileable noise (opaque grayscale)
    n = art.value_noise(256, 256, 64, seed=12, octaves=5, periodic=(256, 256))
    n = (n - n.min()) / (n.max() - n.min())
    nz = np.zeros((256, 256, 4)); nz[..., 0] = nz[..., 1] = nz[..., 2] = n * 255; nz[..., 3] = 255
    art.save(nz, V + "Noise.png", scale=1)


# ================================================================ glyph textures
def glyph_atlas():
    n = len(GLYPHS)
    c = art.new(9, 7 * n)
    for i, g in enumerate(GLYPHS):
        m = np.zeros((9, 7 * n), bool)
        m[1:8, i * 7 + 1:i * 7 + 6] = g
        art.put(c, m, (255, 255, 255), 255)
    art.save(c, ROOT + "VFX/GlyphAtlas.png")
    # glow version: same glyphs with a SOFT blurred halo (no hard pixel ring)
    big = np.kron(c[..., 3] / 255.0, np.ones((2, 2)))
    halo = ndimage.gaussian_filter(big, 1.6) * 0.55
    a = np.maximum(big, halo)
    save1(white(a), ROOT + "VFX/GlyphAtlas_Glow.png")


def glyph_strip():
    """A single line of glyphs written ALONG the strip (rotated so they read upright when the strip
    points up), chunky enough to stay legible when the strip is drawn ~20-30 px wide.
    1024 x 32, tileable horizontally."""
    W, H, P = 1024, 32, 4          # P = screen px per glyph pixel
    rng = np.random.default_rng(77)
    m = np.zeros((H, W))
    x = 2
    while x < W - 7 * P:
        g = GLYPHS[rng.integers(0, len(GLYPHS))]
        patch = g[::-1, :].T.astype(float)            # 5 x 7: rows across the strip, columns along it
        patch = np.kron(patch, np.ones((P, P)))      # 20 x 28
        y0 = (H - patch.shape[0]) // 2 + rng.integers(-1, 2)
        bright = rng.uniform(0.55, 1.0)
        m[y0:y0 + patch.shape[0], x:x + patch.shape[1]] = np.maximum(m[y0:y0 + patch.shape[0], x:x + patch.shape[1]], patch * bright)
        x += patch.shape[1] + P + (P * rng.integers(1, 3) if rng.random() < 0.2 else 0)
    pad = np.concatenate([m[:, -40:], m, m[:, :40]], 1)
    glow = ndimage.gaussian_filter(pad, 1.4)[:, 40:-40]
    glow2 = ndimage.gaussian_filter(pad, 5.0)[:, 40:-40]
    a = np.maximum(m, glow * 0.7 + glow2 * 0.45)
    y = np.arange(H)[:, None]
    edge = np.clip(np.minimum(y, H - 1 - y) / 4.0, 0, 1)
    save1(white(np.clip(a, 0, 1) * edge), V + "GlyphStrip.png")


def glyph_bolt():
    """Rune projectile: just the glyph with a 1px dim rim — the glow comes from a soft bloom in code."""
    W, H = 10, 12
    frames = []
    for k in range(8):
        c = art.new(H, W)
        g = GLYPHS[(k * 5) % len(GLYPHS)]
        m = np.zeros((H, W), bool)
        m[2:9, 2:7] = g
        halo = ndimage.binary_dilation(m, iterations=1) & ~m
        art.put(c, halo, GOLD[2], 150)
        art.put(c, m, GOLD[6], 255)
        frames.append(c)
    art.save(art.sheet(frames), ROOT + "Projectiles/GlyphBolt.png")


# ================================================================ sigil (3 layers + blurred glows)
def _rasterize_line(m, p0, p1, w=0.6):
    h, wd = m.shape
    x, y = art.grid(h, wd)
    ax, ay = p0
    bx, by = p1
    pax, pay = x - ax, y - ay
    bax, bay = bx - ax, by - ay
    t = np.clip((pax * bax + pay * bay) / (bax * bax + bay * bay + 1e-9), 0, 1)
    d = np.hypot(pax - bax * t, pay - bay * t)
    m |= d < w


def _save_layer(mask, name, glow_sigma=6.0):
    mask = np.pad(mask, 24)
    big = np.kron(mask.astype(float), np.ones((2, 2)))
    save1(white(big), ROOT + "Projectiles/" + name + ".png")
    glow = ndimage.gaussian_filter(big, glow_sigma) * 2.2 + ndimage.gaussian_filter(big, glow_sigma * 3) * 2.5
    save1(white(np.clip(glow, 0, 1)), ROOT + "Projectiles/" + name + "_Glow.png")


def sigil():
    # ---- outer: R = 170 art (340 px drawn at scale 1)
    S = 344
    c0 = (S - 1) / 2
    x, y = art.grid(S, S)
    r = np.hypot(x - c0, y - c0)
    ang = np.arctan2(y - c0, x - c0)
    m = np.zeros((S, S), bool)
    m |= np.abs(r - 170) < 0.55
    m |= np.abs(r - 166) < 0.5
    m |= np.abs(r - 140) < 0.5
    m |= (np.abs(r - 136) < 0.5) & (np.sin(ang * 48) > -0.3)
    # tick marks between the rings (major/minor)
    for k in range(96):
        a = k / 96 * 2 * np.pi
        ln = 7 if k % 4 == 0 else 3
        _rasterize_line(m, (c0 + np.cos(a) * 166, c0 + np.sin(a) * 166), (c0 + np.cos(a) * (166 - ln), c0 + np.sin(a) * (166 - ln)), 0.5)
    # small diamonds at 8 points on the outer ring
    for k in range(8):
        a = k / 8 * 2 * np.pi + np.pi / 8
        px, py = c0 + np.cos(a) * 168, c0 + np.sin(a) * 168
        dd = np.abs(x - px) + np.abs(y - py)
        m |= (dd < 4.5) & (dd > 3.4)
    _save_layer(m, "SigilOuter")

    # ---- inner: pentagram in a circle, R = 120 art
    S = 248
    c0 = (S - 1) / 2
    x, y = art.grid(S, S)
    r = np.hypot(x - c0, y - c0)
    m = np.zeros((S, S), bool)
    m |= np.abs(r - 120) < 0.55
    m |= np.abs(r - 116) < 0.45
    pts = [(c0 + np.cos(-np.pi / 2 + k * 2 * np.pi / 5) * 116, c0 + np.sin(-np.pi / 2 + k * 2 * np.pi / 5) * 116) for k in range(5)]
    for k in range(5):
        _rasterize_line(m, pts[k], pts[(k + 2) % 5], 0.6)
    # inner pentagon (from the star's crossings)
    ri = 116 * np.cos(2 * np.pi / 5) / np.cos(np.pi / 5)
    ipts = [(c0 + np.cos(np.pi / 2 + k * 2 * np.pi / 5) * ri, c0 + np.sin(np.pi / 2 + k * 2 * np.pi / 5) * ri) for k in range(5)]
    for k in range(5):
        _rasterize_line(m, ipts[k], ipts[(k + 1) % 5], 0.45)
    # vertex circles
    for (px, py) in pts:
        rr = np.hypot(x - px, y - py)
        m |= np.abs(rr - 7) < 0.55
        m |= rr < 2.2
    m |= np.abs(r - 62) < 0.45
    _save_layer(m, "SigilInner")

    # ---- core eye, R = 30 art
    S = 72
    c0 = (S - 1) / 2
    x, y = art.grid(S, S)
    m = np.zeros((S, S), bool)
    up = np.abs(y - (c0 - 13 * np.sqrt(np.clip(1 - ((x - c0) / 26) ** 2, 0, 1)) + 1)) < 0.6
    lo = np.abs(y - (c0 + 13 * np.sqrt(np.clip(1 - ((x - c0) / 26) ** 2, 0, 1)) - 1)) < 0.6
    m |= (up | lo) & (np.abs(x - c0) <= 26)
    rr = np.hypot(x - c0, y - c0)
    m |= np.abs(rr - 8) < 0.6
    m |= rr < 3
    for k in range(12):
        a = k / 12 * 2 * np.pi
        _rasterize_line(m, (c0 + np.cos(a) * 30, c0 + np.sin(a) * 30), (c0 + np.cos(a) * 34, c0 + np.sin(a) * 34), 0.45)
    _save_layer(m, "SigilCore", glow_sigma=4)


def build():
    smooth_set()
    glyph_atlas()
    glyph_strip()
    glyph_bolt()
    sigil()
    print("fx2 done")


if __name__ == "__main__":
    build()
