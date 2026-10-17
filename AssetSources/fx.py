"""Projectiles, VFX textures, glyph atlas, items."""
import os
ASSETS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets").replace("\\", "/")
import numpy as np
from scipy import ndimage
from PIL import Image
import art
from art import BLACK, SHEEN, GOLD, EMBER, OUTLINE, GLYPHS

ROOT = ASSETS + "/Textures/"


def save_full(arr, path):
    """Save smooth (non-pixel) texture at 1x."""
    art.save(arr, path, scale=1)


# ================================================================ projectiles
def glyph_bolt():
    """8 frames (vertical), each a different glowing rune inside a shard halo. 14x16 art."""
    W, H = 14, 16
    frames = []
    for k in range(8):
        c = art.new(H, W)
        x, y = art.grid(H, W)
        # diamond halo
        d = np.abs(x - 6.5) / 6.5 + np.abs(y - 7.5) / 7.5
        art.put(c, d < 1.0, GOLD[1], 90)
        art.put(c, d < 0.75, GOLD[2], 150)
        g = GLYPHS[(k * 5) % len(GLYPHS)]
        m = np.zeros((H, W), bool)
        m[4:11, 4:9] = g
        halo = ndimage.binary_dilation(m, iterations=1) & ~m
        art.put(c, halo, GOLD[3], 230)
        art.put(c, m, GOLD[6], 255)
        frames.append(c)
    art.save(art.sheet(frames), ROOT + "Projectiles/GlyphBolt.png")


def goo_orb():
    W = H = 22
    frames, glows = [], []
    for k in range(4):
        p = k / 4 * 2 * np.pi
        sd = art.union([art.sd_ellipse(H, W, 11, 11, 7.5 + np.sin(p) * 0.6, 7.5 - np.sin(p) * 0.6),
                        art.sd_ellipse(H, W, 11 + np.cos(p) * 6, 11 + np.sin(p) * 6, 2, 2),
                        art.sd_ellipse(H, W, 11 - np.cos(p + 1) * 6.5, 11 - np.sin(p + 1) * 6.5, 1.6, 1.6)], 2.5)
        mask = sd <= 0
        dist = ndimage.distance_transform_edt(mask)
        hh = np.sqrt(np.clip(dist / 6, 0, 1)) * 5
        n = art.normals_from_height(hh)
        diff, spec = art.shade(n)
        c = art.new(H, W)
        art.put(c, mask, art.ramp(0.06 + 0.7 * diff, BLACK))
        art.put(c, mask & (spec > 0.3), SHEEN[3])
        x, y = art.grid(H, W)
        ring = mask & (np.abs(np.sqrt((x - 11) ** 2 + (y - 11) ** 2) - 4.5) < 0.8)
        ang = np.arctan2(y - 11, x - 11)
        ring &= np.sin(ang * 5 + p) > -0.2
        art.put(c, ring, GOLD[3])
        art.put(c, art.outline(mask), GOLD[1])
        g = art.new(H, W)
        art.put(g, ring, GOLD[5], 255)
        art.put(g, art.outline(mask), GOLD[2], 160)
        frames.append(c)
        glows.append(g)
    art.save(art.sheet(frames), ROOT + "Projectiles/GooOrb.png")
    art.save(art.sheet(glows), ROOT + "Projectiles/GooOrb_Glow.png")


def glyph_lance():
    """Vertical lance pointing UP, 12 x 72 art."""
    W, H = 12, 72
    x, y = art.grid(H, W)
    sd = art.union([art.sd_capsule(H, W, 6, 70, 6, 14, 2.6, 3.2),
                    art.sd_capsule(H, W, 6, 16, 6, 0.5, 4.2, 0.2)], 1.0)
    # barbs
    sd = np.minimum(sd, art.sd_capsule(H, W, 6, 22, 1.2, 28, 1.4, 0.2))
    sd = np.minimum(sd, art.sd_capsule(H, W, 6, 22, 10.8, 28, 1.4, 0.2))
    mask = sd <= 0
    dist = ndimage.distance_transform_edt(mask)
    n = art.normals_from_height(np.sqrt(np.clip(dist / 3, 0, 1)) * 3)
    diff, spec = art.shade(n, light=(-0.8, -0.3, 0.5))
    c = art.new(H, W)
    art.put(c, mask, art.ramp(0.05 + 0.75 * diff, BLACK))
    art.put(c, mask & (spec > 0.35), SHEEN[3])
    core = mask & (np.abs(x - 6) < 0.6) & (y > 6) & (y < 66)
    gl = np.zeros((H, W), bool)
    for k, yy in enumerate(range(30, 64, 9)):
        gl[yy:yy + 7, 4:9] |= GLYPHS[(k * 3 + 2) % len(GLYPHS)]
    gl &= ndimage.binary_erosion(mask, iterations=1)
    art.put(c, core | gl, GOLD[3])
    art.put(c, art.outline(mask), OUTLINE)
    g = art.new(H, W)
    art.put(g, core, GOLD[6], 255)
    art.put(g, gl, GOLD[5], 255)
    art.put(g, mask & (y < 16), GOLD[4], 150)
    art.save(c, ROOT + "Projectiles/GlyphLance.png")
    art.save(g, ROOT + "Projectiles/GlyphLance_Glow.png")


def rune_sigil():
    """Circular sigil, white (tinted in code), 72x72 art, pixel style."""
    S = 72
    x, y = art.grid(S, S)
    c0 = (S - 1) / 2
    r = np.sqrt((x - c0) ** 2 + (y - c0) ** 2)
    ang = np.arctan2(y - c0, x - c0)
    m = np.zeros((S, S), bool)
    m |= np.abs(r - 34) < 0.7
    m |= np.abs(r - 31) < 0.6
    m |= np.abs(r - 20) < 0.6
    m |= (np.abs(r - 17.5) < 0.6) & (np.sin(ang * 12) > 0)
    # hexagram-ish spokes
    for k in range(6):
        a0 = k * np.pi / 3
        a1 = a0 + 2 * np.pi / 3
        for t in np.linspace(0, 1, 120):
            px = c0 + np.cos(a0) * 20 * (1 - t) + np.cos(a1) * 20 * t
            py = c0 + np.sin(a0) * 20 * (1 - t) + np.sin(a1) * 20 * t
            m[int(round(py)), int(round(px))] = True
    # glyph ring between 22 and 30
    for k in range(14):
        a = k / 14 * 2 * np.pi
        gx, gy = c0 + np.cos(a) * 25.5 - 2, c0 + np.sin(a) * 25.5 - 3
        g = GLYPHS[(k * 7) % len(GLYPHS)]
        gi, gj = int(round(gy)), int(round(gx))
        m[gi:gi + 7, gj:gj + 5] |= g
    # center eye
    e = art.ellipse(S, S, c0, c0, 7, 3.2)
    m |= (e < 1) & (e > 0.55)
    m |= art.ellipse(S, S, c0, c0, 1.6, 1.6) < 1
    c = art.new(S, S)
    art.put(c, ndimage.binary_dilation(m, iterations=1) & ~m, (255, 255, 255), 70)
    art.put(c, m, (255, 255, 255), 255)
    art.save(c, ROOT + "Projectiles/RuneSigil.png")


# ================================================================ smooth VFX
def vfx():
    V = ROOT + "VFX/"
    save_full(art.soft_glow_texture(128, 2.2), V + "Bloom.png")
    save_full(art.soft_glow_texture(64, 4.0), V + "BloomSharp.png")
    # ring
    S = 256
    x, y = art.grid(S, S)
    c0 = (S - 1) / 2
    r = np.sqrt((x - c0) ** 2 + (y - c0) ** 2) / c0
    a = np.exp(-((r - 0.86) / 0.06) ** 2) + 0.35 * np.exp(-((r - 0.8) / 0.14) ** 2)
    ring = np.zeros((S, S, 4)); ring[..., :3] = 255; ring[..., 3] = np.clip(a, 0, 1) * 255 * (r < 1)
    save_full(ring, V + "Ring.png")
    # streak
    W, H = 128, 32
    x, y = art.grid(H, W)
    u = (x - (W - 1) / 2) / ((W - 1) / 2); v = (y - (H - 1) / 2) / ((H - 1) / 2)
    a = np.clip(1 - np.abs(u), 0, 1) ** 1.5 * np.exp(-(v / 0.25) ** 2)
    st = np.zeros((H, W, 4)); st[..., :3] = 255; st[..., 3] = a * 255
    save_full(st, V + "Streak.png")
    # 4-point flare
    S = 128
    x, y = art.grid(S, S)
    c0 = (S - 1) / 2
    dx, dy = (x - c0) / c0, (y - c0) / c0
    rr = np.sqrt(dx * dx + dy * dy)
    a = np.exp(-(dy / 0.04) ** 2) * np.clip(1 - np.abs(dx), 0, 1) ** 2 + np.exp(-(dx / 0.04) ** 2) * np.clip(1 - np.abs(dy), 0, 1) ** 2
    a += np.clip(1 - rr, 0, 1) ** 6 * 0.9
    fl = np.zeros((S, S, 4)); fl[..., :3] = 255; fl[..., 3] = np.clip(a, 0, 1) * 255
    save_full(fl, V + "Flare.png")
    # trail texture (horizontally tileable): bright core + flowing streaks
    W, H = 256, 64
    x, y = art.grid(H, W)
    v = (y - (H - 1) / 2) / ((H - 1) / 2)
    n = art.value_noise(H, W, 32, seed=4, octaves=4, periodic=(W, H))
    streaks = art.value_noise(H, W, 8, seed=9, octaves=2, periodic=(W, 8))
    a = np.exp(-(v / 0.18) ** 2) * 1.0 + np.exp(-(v / 0.55) ** 2) * (0.35 + 0.65 * n) * (0.6 + 0.6 * streaks)
    tr = np.zeros((H, W, 4)); tr[..., :3] = 255; tr[..., 3] = np.clip(a, 0, 1) * 255
    save_full(tr, V + "Trail.png")
    # tileable noise
    n = art.value_noise(256, 256, 64, seed=12, octaves=5, periodic=(256, 256))
    n = (n - n.min()) / (n.max() - n.min())
    nz = np.zeros((256, 256, 4)); nz[..., 0] = nz[..., 1] = nz[..., 2] = n * 255; nz[..., 3] = 255
    save_full(nz, V + "Noise.png")
    # smoke puff (alpha soft, noisy edge)
    S = 96
    x, y = art.grid(S, S)
    c0 = (S - 1) / 2
    rr = np.sqrt((x - c0) ** 2 + (y - c0) ** 2) / c0
    n = art.value_noise(S, S, 18, seed=3, octaves=4)
    a = np.clip(1 - rr - (n - 0.5) * 0.6, 0, 1) ** 1.4
    sm = np.zeros((S, S, 4)); sm[..., :3] = 255; sm[..., 3] = a * 255
    save_full(sm, V + "Smoke.png")
    # god ray (for burst)
    W, H = 32, 256
    x, y = art.grid(H, W)
    u = (x - (W - 1) / 2) / ((W - 1) / 2)
    a = np.exp(-(u / 0.3) ** 2) * (1 - y / H) ** 1.2
    gr = np.zeros((H, W, 4)); gr[..., :3] = 255; gr[..., 3] = a * 255
    save_full(gr, V + "GodRay.png")


def glyph_atlas():
    """All glyphs in one row, white with soft halo: cell 7x9 art -> 14x18 stored."""
    n = len(GLYPHS)
    c = art.new(9, 7 * n)
    for i, g in enumerate(GLYPHS):
        m = np.zeros((9, 7 * n), bool)
        m[1:8, i * 7 + 1:i * 7 + 6] = g
        art.put(c, m, (255, 255, 255), 255)
    art.save(c, ROOT + "VFX/GlyphAtlas.png")
    # glow version (halo only) for additive passes
    h = art.new(9, 7 * n)
    full = c[..., 3] > 0
    halo = ndimage.binary_dilation(full, iterations=1) & ~full
    art.put(h, halo, (255, 255, 255), 110)
    art.put(h, full, (255, 255, 255), 255)
    art.save(h, ROOT + "VFX/GlyphAtlas_Glow.png")


def glyph_strip():
    """Horizontally tileable strip of glyph 'writing' for energy streams. 256x24 art -> 512x48."""
    W, H = 256, 24
    rng = np.random.default_rng(77)
    m = np.zeros((H, W), bool)
    xx = 0
    while xx < W - 6:
        g = GLYPHS[rng.integers(0, len(GLYPHS))]
        yy = 8 + rng.integers(-2, 3)
        m[yy:yy + 7, xx:xx + 5] |= g
        xx += 6 + (rng.integers(0, 4) if rng.random() < 0.2 else 0)
    # thin scanlines/threads above and below
    x, y = art.grid(H, W)
    thread = ((np.abs(y - 4) < 0.5) | (np.abs(y - 19) < 0.5)) & (art.value_noise(H, W, 6, seed=5, periodic=(W, H)) > 0.5)
    c = art.new(H, W)
    halo = ndimage.binary_dilation(m, iterations=2) & ~m
    art.put(c, halo, (255, 255, 255), 60)
    art.put(c, ndimage.binary_dilation(m, iterations=1) & ~m, (255, 255, 255), 130)
    art.put(c, thread, (255, 255, 255), 150)
    art.put(c, m, (255, 255, 255), 255)
    art.save(c, ROOT + "VFX/GlyphStrip.png")


def goo_chunks():
    W = H = 10
    frames = []
    for k in range(3):
        rng = np.random.default_rng(k + 3)
        sds = [art.sd_ellipse(H, W, 5, 5, 2.6 + rng.random(), 2.2 + rng.random())]
        for _ in range(2):
            sds.append(art.sd_ellipse(H, W, 5 + rng.uniform(-2.5, 2.5), 5 + rng.uniform(-2.5, 2.5), 1.2, 1.2))
        mask = art.union(sds, 1.5) <= 0
        c = art.new(H, W)
        art.put(c, mask, BLACK[2])
        art.put(c, mask & ~ndimage.binary_erosion(mask), BLACK[0])
        hl = np.zeros((H, W), bool)
        ys, xs = np.nonzero(mask)
        hl[ys.min() + 1, xs[ys == ys.min() + 1].min() + 1 if (ys == ys.min() + 1).any() else xs.min()] = True
        art.put(c, hl & mask, SHEEN[3])
        art.put(c, mask & (np.random.default_rng(k).random((H, W)) > 0.85), GOLD[2])
        frames.append(c)
    art.save(art.sheet(frames), ROOT + "VFX/GooChunk.png")


def divider():
    W, H = 96, 9
    x, y = art.grid(H, W)
    m = (np.abs(y - 4) < 0.5) & (np.abs(x - 47.5) < 46) & ~(np.abs(x - 47.5) < 7)
    m |= (np.abs(y - 4) < 0.5) & (np.abs(x - 47.5) > 40) & False
    d = np.abs(x - 47.5) / 4 + np.abs(y - 4) / 4
    m |= (d < 1) & (d > 0.5)
    m |= (np.abs(x - 47.5) < 0.6) & (np.abs(y - 4) < 0.6)
    for side in (-1, 1):
        cx = 47.5 + side * 14
        m |= (np.abs(y - 4) < 1.6) & (np.abs(x - cx) < 0.5)
        cx = 47.5 + side * 24
        m |= (np.abs(y - 4) < 2.6) & (np.abs(x - cx) < 0.5)
    c = art.new(H, W)
    a = np.clip(1 - np.abs(x - 47.5) / 48, 0, 1) ** 0.6 * 255
    art.put(c, m, (255, 255, 255), a)
    art.save(c, ROOT + "Cutscene/Divider.png")


# ================================================================ items
def items():
    S = 16
    for name, cracked in (("SealedVerse", False), ("CrackedVerse", True)):
        x, y = art.grid(S, S)
        # dark tablet, slightly tilted, with dripping goo
        sd = art.sd_ellipse(S, S, 8, 7.5, 5.2, 6.2)
        box = (np.abs(x - 8) < 5) & (np.abs(y - 7.5) < 6.2)
        mask = box | (sd < -1.5)
        mask &= ~((x <= 3) & (y <= 1.5)) & ~((x >= 12) & (y <= 1.5))
        drips = ((x == 5) & (y >= 13) & (y <= 14)) | ((x == 10) & (y >= 13) & (y <= 15)) | ((x == 7) & (y == 13))
        mask |= drips
        c = art.new(S, S)
        shadev = np.clip(0.55 - (x - 3) * 0.04 - (y - 1) * 0.02, 0, 1)
        art.put(c, mask, art.ramp(shadev, BLACK, dither=False))
        g = np.zeros((S, S), bool)
        g[4:11, 6:11] = GLYPHS[4]
        g &= mask
        art.put(c, g, GOLD[4] if not cracked else EMBER[3])
        art.put(c, drips, GOLD[2])
        if cracked:
            crack = ((x - 3) == (y - 2) * 0.7) | ((x == 11) & (y > 8)) | ((x == 12) & (y == 8))
            art.put(c, crack & mask, EMBER[2])
        art.put(c, art.outline(mask), OUTLINE)
        art.save(c, ROOT + "Items/" + name + ".png")


def build():
    import fx2
    goo_orb(); glyph_lance(); goo_chunks(); divider(); items()
    fx2.build()  # smooth glows, glyph atlas/strip, glyph bolt, sigil layers
    print("fx done")


if __name__ == "__main__":
    build()
