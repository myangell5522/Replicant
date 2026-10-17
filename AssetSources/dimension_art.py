"""Phase-2 assets: the dimension (oil-stone tiles, glyph lilies, the remnant 'glyph tree' that the
explosion leaves on the horizon), song orbs, petals, twin boss-map icons and halos."""
import os, math, random
import numpy as np
from PIL import Image
from scipy import ndimage
import art
from art import BLACK, SHEEN, GOLD, EMBER, OUTLINE, GLYPHS

ASSETS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets").replace("\\", "/")
T = ASSETS + "/Textures/"


def save1(arr, path):
    art.save(arr, path, scale=1)


def white(alpha):
    out = np.zeros(alpha.shape + (4,))
    out[..., :3] = 255
    out[..., 3] = np.clip(alpha, 0, 1) * 255
    return out


# ================================================================ oil stone tile atlas
def oil_stone():
    """4x4 atlas of 8x8 art cells (16x16 stored): row0 fills, row1 top caps, row2 edge overlays."""
    C = 8
    atlas = art.new(C * 4, C * 4)
    x, y = art.grid(C, C)
    for v in range(4):
        rng = np.random.default_rng(v + 10)
        n = art.value_noise(C, C, 3, seed=20 + v, octaves=2)
        base = art.ramp(0.12 + n * 0.25, BLACK[:5], dither=True)
        cell = np.zeros((C, C, 4)); cell[..., :3] = base; cell[..., 3] = 255
        # oily specks of sheen and the odd gold fleck
        spk = rng.random((C, C)) > 0.9
        cell[spk, :3] = SHEEN[1] if v % 2 else SHEEN[0]
        if v == 3:
            cell[3, 4, :3] = GOLD[1]
        atlas[0:C, v * C:(v + 1) * C] = cell
        # top cap: glossy rim + thin amber moss with drips
        cap = np.zeros((C, C, 4))
        cap[0, :, :3] = SHEEN[2]; cap[0, :, 3] = 255
        cap[1, :, :3] = SHEEN[1]; cap[1, :, 3] = 255
        cap[2, :, :3] = BLACK[4]; cap[2, :, 3] = 255
        for k in range(C):
            if rng.random() < 0.45:
                cap[1, k, :3] = GOLD[1]
            if rng.random() < 0.18:
                cap[2, k, :3] = GOLD[0]; cap[3, k, :3] = BLACK[3]; cap[3, k, 3] = 255
        atlas[C:2 * C, v * C:(v + 1) * C] = cap
    # edge overlays: left, right, bottom outline + sheen
    left = np.zeros((C, C, 4)); left[:, 0, :3] = OUTLINE; left[:, 0, 3] = 255; left[:, 1, :3] = BLACK[4]; left[:, 1, 3] = 140
    right = np.zeros((C, C, 4)); right[:, -1, :3] = OUTLINE; right[:, -1, 3] = 255
    bottom = np.zeros((C, C, 4)); bottom[-1, :, :3] = OUTLINE; bottom[-1, :, 3] = 255
    gloss = np.zeros((C, C, 4)); gloss[0, 1:6, :3] = (255, 236, 190); gloss[0, 1:6, 3] = 200
    for k, o in enumerate((left, right, bottom, gloss)):
        atlas[2 * C:3 * C, k * C:(k + 1) * C] = o
    art.save(atlas, T + "Tiles/OilStone.png")


# ================================================================ glyph lily (decoration tile)
def lilies():
    W, H = 14, 18
    frames, glows = [], []
    for v in range(3):
        c = art.new(H, W)
        g = art.new(H, W)
        x, y = art.grid(H, W)
        # stem + two leaves (dark oily green-black)
        stem_x = 7 + (v - 1) * 0.6
        stem = (np.abs(x - (stem_x + (y - 17) * 0.05 * (v - 1))) < 0.6) & (y > 8)
        art.put(c, stem, (24, 30, 18))
        leaf1 = (((x - (stem_x - 3)) / 3.2) ** 2 + ((y - 14) / 1.2) ** 2) < 1
        leaf2 = (((x - (stem_x + 3)) / 3.0) ** 2 + ((y - 15.5) / 1.1) ** 2) < 1
        art.put(c, leaf1 | leaf2, (16, 22, 14))
        art.put(c, (leaf1 | leaf2) & (y < 14.5), (34, 44, 24))
        # the bloom: 5 pointed petals, pale gold-white with amber throat
        cx, cy = stem_x, 7 - v * 0.6
        ang = np.arctan2(y - cy, x - cx)
        r = np.hypot(x - cx, y - cy)
        petals = r < (3.2 + 2.6 * np.abs(np.cos(ang * 2.5 + v)) ** 2) * (1 - 0.35 * (y > cy))
        petals &= ~((y > cy + 2.5))
        art.put(c, petals, (236, 226, 196))
        art.put(c, petals & (r > 3.5), (255, 248, 226))
        throat = r < 1.6
        art.put(c, throat, GOLD[2])
        art.put(c, r < 0.8, GOLD[4])
        art.put(c, art.outline(petals | throat) & ~stem & ~(leaf1 | leaf2), (60, 46, 30), 200)
        art.put(g, petals, (255, 230, 170), 120)
        art.put(g, throat, GOLD[4], 255)
        frames.append(c)
        glows.append(g)
    art.save(art.sheet(frames, vertical=False), T + "Tiles/GlyphLily.png")
    art.save(art.sheet(glows, vertical=False), T + "Tiles/GlyphLily_Glow.png")


# ================================================================ orbs
def orbs():
    # gold (breakable): bright faceted sphere with a glyph core, 4 frames of shimmer
    S = 14
    frames = []
    for k in range(4):
        x, y = art.grid(S, S)
        r = np.hypot(x - 6.5, y - 6.5)
        m = r < 6.2
        v = np.clip(1 - r / 6.4, 0, 1) ** 0.6 + 0.25 * np.clip(-(x - 6.5 + y - 6.5) / 9, -1, 1)
        c = art.new(S, S)
        art.put(c, m, art.ramp(np.clip(v, 0, 1) * 0.9 + 0.05, GOLD))
        gl = np.zeros((S, S), bool)
        gl[3:10, 4:9] = GLYPHS[(k * 9) % len(GLYPHS)]
        art.put(c, gl & m, (255, 255, 240))
        art.put(c, (np.abs(r - 5.6) < 0.6) & ((x + y + k) % 4 == 0), (255, 250, 220))
        art.put(c, art.outline(m), (120, 64, 10))
        frames.append(c)
    art.save(art.sheet(frames), T + "Projectiles/SongOrbGold.png")
    # dark (unbreakable): glossy black sphere, gold rim, inner swirl
    S = 18
    frames = []
    for k in range(4):
        x, y = art.grid(S, S)
        r = np.hypot(x - 8.5, y - 8.5)
        m = r < 8.2
        c = art.new(S, S)
        art.put(c, m, art.ramp(np.clip(0.05 + 0.3 * (1 - r / 8.2), 0, 1), BLACK))
        ang = np.arctan2(y - 8.5, x - 8.5)
        swirl = m & (np.abs(np.sin(ang * 2 + r * 0.7 + k * np.pi / 2)) < 0.22) & (r < 6.5) & (r > 1.5)
        art.put(c, swirl, EMBER[1])
        spec = ((x - 5.5) ** 2 + (y - 5) ** 2) < 3
        art.put(c, spec & m, SHEEN[2])
        art.put(c, ((x - 5) ** 2 + (y - 4.5) ** 2) < 0.8, (255, 240, 210))
        rim = m & (r > 7.1)
        art.put(c, rim, GOLD[2])
        art.put(c, art.outline(m), OUTLINE)
        frames.append(c)
    art.save(art.sheet(frames), T + "Projectiles/SongOrbDark.png")
    # lily petal
    W, H = 10, 6
    x, y = art.grid(H, W)
    m = (((x - 4.5) / 4.5) ** 2 + ((y - 3) / 2.2) ** 2) < 1
    m &= ~((x > 7.5) & (np.abs(y - 3) > 0.8))
    c = art.new(H, W)
    art.put(c, m, (240, 230, 200))
    art.put(c, m & (y < 2.5), (255, 250, 232))
    art.put(c, m & (x < 2), GOLD[3])
    art.put(c, art.outline(m), (90, 66, 30))
    art.save(c, T + "Projectiles/LilyPetal.png")


# ================================================================ halo (awakened twins)
def halo():
    S = 160
    x, y = art.grid(S, S)
    c0 = (S - 1) / 2
    r = np.hypot(x - c0, y - c0)
    ang = np.arctan2(y - c0, x - c0)
    m = (np.abs(r - 70) < 1.6) | ((np.abs(r - 62) < 0.9) & (np.sin(ang * 24) > 0.2))
    # glyph ticks around the ring
    for k in range(12):
        a = k / 12 * 2 * np.pi
        px, py = c0 + np.cos(a) * 66, c0 + np.sin(a) * 66
        m |= (np.hypot(x - px, y - py) < 2.2)
    big = m.astype(float)
    glow = ndimage.gaussian_filter(big, 3) * 2 + ndimage.gaussian_filter(big, 8) * 2
    save1(white(np.clip(np.maximum(big, glow), 0, 1)), T + "NPCs/Twins/Halo.png")


# ================================================================ glyph tree (remnant of the burst on the horizon)
def glyph_tree():
    """Pre-rendered still of the glyph fountain, used far away in the dimension's sky."""
    strip = np.asarray(Image.open(T + "VFX/GlyphStrip.png").convert("RGBA")).astype(np.float32) / 255
    thread = np.asarray(Image.open(T + "VFX/Thread.png").convert("RGBA")).astype(np.float32) / 255
    W, H = 1024, 640
    acc = np.zeros((H, W, 3), np.float32)
    o = np.array([W / 2, H - 40])
    rnd = random.Random(5)

    def sample(tex, u, v):
        h, w = tex.shape[:2]
        xi = ((u % 1.0) * (w - 1)).astype(int)
        yi = (np.clip(v, 0, 1) * (h - 1)).astype(int)
        return tex[yi, xi, 3]

    def draw(pts, tex, wfun, col, urep):
        N = len(pts)
        for i in range(N - 1):
            p0, p1 = pts[i], pts[i + 1]
            d = p1 - p0
            L = np.linalg.norm(d) + 1e-6
            nrm = np.array([-d[1], d[0]]) / L
            for j in range(int(max(2, L))):
                f = j / max(2, L)
                t = (i + f) / (N - 1)
                p = p0 + d * f
                w = wfun(t)
                vs = np.linspace(0, 1, max(2, int(w)))
                pos = p[None, :] + nrm[None, :] * ((vs - 0.5) * w)[:, None]
                a = sample(tex, np.full_like(vs, t * urep), vs)
                xi = pos[:, 0].astype(int); yi = pos[:, 1].astype(int)
                ok = (xi >= 0) & (xi < W) & (yi >= 0) & (yi < H)
                acc[yi[ok], xi[ok]] += a[ok, None] * col(t)[None, :]

    def point(ang, bend, L, a):
        r = L * a
        an = ang + bend * a * a
        p = o + np.array([math.cos(an), math.sin(an)]) * r
        if p[1] > o[1]:
            p[1] = o[1] + (p[1] - o[1]) * 0.08
        return p

    for i in range(44):
        u = i / 43 * 2 - 1
        uu = math.copysign(abs(u) ** 1.25, u)
        ang = -math.pi / 2 + uu * 1.4
        bend = uu * rnd.uniform(0.7, 1.3)
        L = H * (0.9 - 0.42 * abs(uu)) * rnd.uniform(0.85, 1.05)
        w = rnd.uniform(14, 22)
        pts = [point(ang, bend, L, k / 35) for k in range(36)]
        draw(pts, strip, lambda t, w=w: w * (0.45 + 0.55 * math.sin(min(1, t * 1.3 + 0.15) * math.pi)),
             lambda t: (np.array([1, .97, .9]) * (1 - min(1, t * 2.2)) + np.array([1, .77, .3]) * min(1, t * 2.2)) * (1 - max(0, (t - 0.75) / 0.25)),
             L / (1024 * (w / 32)))
    for i in range(60):
        u = rnd.uniform(-1, 1)
        L = H * rnd.uniform(0.4, 1.0)
        pts = [point(-math.pi / 2 + u * 1.5, u * rnd.uniform(0.8, 1.9), L, k / 35) for k in range(36)]
        draw(pts, thread, lambda t: 2.5 * (1 - t * 0.6), lambda t: np.array([1, .85, .5]) * 0.5 * (1 - t), 2)
    # luminance -> premultiplied white-gold texture
    lum = np.clip(acc, 0, 1.5)
    out = np.zeros((H, W, 4))
    out[..., :3] = np.clip(lum, 0, 1) * 255
    out[..., 3] = np.clip(lum.max(-1), 0, 1) * 255
    # already premultiplied-looking: save without extra premultiply
    Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), "RGBA").save(T + "VFX/GlyphTree.png")


# ================================================================ boss map icons (from the head grids)
def head_icons():
    import twins
    for name, grid in (("Requiem", twins.HEAD_REQUIEM), ("Refrain", twins.HEAD_REFRAIN)):
        c = art.new(20, 20)
        for y, row in enumerate(grid):
            for x, ch in enumerate(row):
                if ch != "." and y < 20:
                    col = twins.HEAD_COLORS[ch]
                    if ch == "z":   # Refrain's written-over eyes
                        col = twins.GOLD[3] if (x + y) % 2 else twins.SKIN[1]
                    c[y, x + 1, :3] = col
                    c[y, x + 1, 3] = 255
        art.save(c, T + "NPCs/Twins/" + name + "_Head_Boss.png", scale=2)


def build():
    oil_stone(); lilies(); orbs(); halo(); head_icons(); glyph_tree()
    print("dimension art done")


if __name__ == "__main__":
    build()
