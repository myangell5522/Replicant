"""Phase 3 — RECURSION, the centipede that devours the song.

Black lacquered chitin plates with gold-inscribed seams, ember eyes, bone-and-gold mandibles and
jointed legs. All facing RIGHT (rotation = velocity angle in code).

Writes (art pixels x2):
  NPCs/Recursion/RecursionHead.png (+_Glow)   84 x 64 per frame, 4 frames (mandibles closed -> wide)
  NPCs/Recursion/RecursionBody.png (+_Glow)   48 x 60 per frame, 4 frames (leg cycle)
  NPCs/Recursion/RecursionTail.png (+_Glow)   60 x 44 per frame, 2 frames (stinger pulse)
  NPCs/Recursion/RecursionHead_Head_Boss.png  map / boss bar icon
"""
import numpy as np
from PIL import Image
import art

OUT = "../Assets/Textures/NPCs/Recursion/"
CHITIN = np.array([(2, 1, 3), (7, 5, 8), (13, 10, 14), (21, 16, 20), (31, 24, 27), (44, 34, 34), (60, 48, 42)], float)
SHEEN = np.array([(70, 56, 34), (112, 88, 44), (160, 124, 58), (214, 176, 96)], float)
GOLD = art.GOLD
EMBER = art.EMBER
BONE = np.array([(70, 58, 40), (128, 108, 72), (190, 170, 124), (236, 224, 188), (255, 248, 222)], float)
OUTLINE = np.array((2, 1, 3), float)


def smooth01(x):
    x = np.clip(x, 0, 1)
    return x * x * (3 - 2 * x)


def carapace(canvas, sd, light=(-0.45, -0.75, 0.5), depth=7.0, base=0.1):
    """Paint a lacquered chitin volume from a signed distance field (negative inside)."""
    inside = sd < 0
    hgt = smooth01(-sd / depth) ** 0.7 * depth
    n = art.normals_from_height(hgt)
    diff, spec = art.shade(n, light)
    v = base + diff * 0.62
    col = art.ramp(v, CHITIN)
    # wet gold sheen on the highlights
    sheen = np.clip(spec * 1.6 - 0.15, 0, 1)
    sh = art.ramp(sheen, SHEEN)
    mix = (sheen > art.bayer(*sd.shape) * 0.9)[..., None]
    col = np.where(mix, sh, col)
    art.put(canvas, inside, col)
    return inside


def stroke(canvas, mask, pal, v=0.6):
    if mask.any():
        art.put(canvas, mask, art.ramp(np.full(mask.shape, v), pal))


def line_mask(h, w, pts, r=0.7):
    m = np.zeros((h, w), bool)
    for (ax, ay), (bx, by) in zip(pts[:-1], pts[1:]):
        m |= art.sd_capsule(h, w, ax, ay, bx, by, r) < 0
    return m


def glyph_stamp(h, w, x0, y0, idx):
    g = art.GLYPHS[idx % len(art.GLYPHS)]
    m = np.zeros((h, w), bool)
    gh, gw = g.shape
    for yy in range(gh):
        for xx in range(gw):
            if g[yy, xx] and 0 <= y0 + yy < h and 0 <= x0 + xx < w:
                m[y0 + yy, x0 + xx] = True
    return m


def glow_put(glow, mask, rgb, a):
    glow[mask, :3] = rgb
    glow[mask, 3] = np.maximum(glow[mask, 3], a)


HEAD_FRAMES, BODY_FRAMES, TAIL_FRAMES = 6, 8, 4


def veins(h, w, inside, seed, count=5, length=10):
    """Glowing ember cracks wandering across chitin."""
    r = np.random.default_rng(seed)
    m = np.zeros((h, w), bool)
    ys, xs = np.nonzero(inside)
    if len(xs) == 0:
        return m
    for _ in range(count):
        i = r.integers(len(xs))
        x, y = float(xs[i]), float(ys[i])
        ang = r.uniform(0, 2 * np.pi)
        for _ in range(length):
            ang += r.uniform(-0.9, 0.9)
            nx, ny = x + np.cos(ang) * 1.6, y + np.sin(ang) * 1.6
            m |= art.sd_capsule(h, w, x, y, nx, ny, 0.45) < 0
            x, y = nx, ny
    return m & inside


# ---------------------------------------------------------------- head
HW, HH = 100, 72


def bezier(p0, p1, p2, n=12):
    out = []
    for k in range(n + 1):
        t = k / n
        out.append(((1 - t) ** 2 * p0[0] + 2 * (1 - t) * t * p1[0] + t * t * p2[0],
                    (1 - t) ** 2 * p0[1] + 2 * (1 - t) * t * p1[1] + t * t * p2[1]))
    return out


def head(frame):
    c = art.new(HH, HW)
    g = art.new(HH, HW)
    cy = HH / 2
    x, y = art.grid(HH, HW)
    open_ = frame / (HEAD_FRAMES - 1)
    # the maw: a furnace between the jaws, only when they part
    if open_ > 0.2:
        maw = art.ellipse(HH, HW, 70, cy, 8 + open_ * 5, 2 + open_ * 9) < 1
        art.put(c, maw, art.ramp(np.full((HH, HW), 0.5), EMBER))
        glow_put(g, maw, (255, 110, 30), 210)
        core = art.ellipse(HH, HW, 68, cy, 5 + open_ * 2, 1 + open_ * 4) < 1
        art.put(c, core, art.ramp(np.full((HH, HW), 0.92), GOLD))
        glow_put(g, core, (255, 236, 180), 255)
    # forcipules: long sickle jaws curving forward and hooking in
    for side in (-1, 1):
        p0 = (60, cy + side * 9)
        p1 = (84 + open_ * 2, cy + side * (19 + open_ * 12))
        p2 = (97, cy + side * (2.5 + open_ * 14))
        pts = bezier(p0, p1, p2, 14)
        m = np.zeros((HH, HW), bool)
        for k in range(14):
            rad = 4.2 * (1 - k / 15) ** 0.8 + 0.5
            m |= art.sd_capsule(HH, HW, pts[k][0], pts[k][1], pts[k + 1][0], pts[k + 1][1], rad) < 0
        # serrations on the inner edge
        for k in (4, 7, 10):
            px_, py_ = pts[k]
            m |= art.sd_capsule(HH, HW, px_, py_, px_ + 3, py_ - side * 4, 1.0, 0.3) < 0
        v = 0.2 + 0.55 * np.clip((x - 60) / 36, 0, 1) + 0.15 * (side * (y - cy) > 12)
        art.put(c, m, art.ramp(v, BONE))
        tip = m & (x > 88)
        art.put(c, tip, art.ramp(np.full((HH, HW), 0.8), GOLD))
        glow_put(g, tip, (255, 210, 120), 140)
    # carapace: a long armoured skull with a crest of back-swept spines
    sd = art.union([
        art.sd_ellipse(HH, HW, 36, cy, 27, 22),
        art.sd_ellipse(HH, HW, 58, cy, 14, 15),
        art.sd_ellipse(HH, HW, 14, cy, 11, 20),
    ] + [art.sd_capsule(HH, HW, 20 + k * 9, cy + side * (18 - abs(k - 2) * 1.2), 4 + k * 8, cy + side * (33 - abs(k - 1.5) * 2.5), 3.6, 0.5)
         for k in range(5) for side in (-1, 1)], k=3.0)
    inside = carapace(c, sd)
    # ember veins under the lacquer
    vm = veins(HH, HW, inside & (x < 50), 21, 7, 9)
    stroke(c, vm, EMBER, 0.6)
    glow_put(g, vm, (255, 120, 40), 170)
    # bone tips on the crown spines
    tips = inside & ((np.abs(y - cy) > 27))
    art.put(c, tips, art.ramp(np.full((HH, HW), 0.55), BONE))
    glow_put(g, tips & (np.abs(y - cy) > 30), (255, 200, 110), 120)
    # face plate: a darker beveled mask on the front
    face = inside & (art.ellipse(HH, HW, 60, cy, 11, 12) < 1)
    fh = art.ramp(0.08 + 0.25 * np.clip((x - 50) / 18, 0, 1), CHITIN)
    art.put(c, face, fh)
    # plate seams inlaid with gold
    for sx, rx in ((22, 8), (40, 8)):
        arc = (np.abs(art.ellipse(HH, HW, sx + rx, cy, rx + 2, 23) - 1.0) < 0.07) & inside & (x < sx + rx)
        stroke(c, arc, GOLD, 0.55)
        glow_put(g, arc, (255, 190, 80), 110)
    fr = (np.abs(art.ellipse(HH, HW, 60, cy, 11, 12) - 1) < 0.12) & inside
    stroke(c, fr, GOLD, 0.7)
    glow_put(g, fr, (255, 200, 100), 150)
    # inscription on the dome
    for i, (gx0, gy0) in enumerate(((26, cy - 11), (26, cy + 3), (33, cy - 4), (17, cy - 8), (17, cy + 1))):
        m = glyph_stamp(HH, HW, int(gx0), int(gy0), 11 + i * 5) & inside
        stroke(c, m, GOLD, 0.8)
        glow_put(g, m, (255, 214, 120), 200)
    # eyes: a cluster of ember points on the face, the largest pair forward
    for side in (-1, 1):
        for k, (ex, ey, r) in enumerate(((65, 4.5, 2.6), (60, 8, 1.8), (55, 5.5, 1.5), (61, 11.5, 1.3), (56, 10, 1.0), (67, 9, 1.0))):
            e = art.ellipse(HH, HW, ex, cy + side * ey, r, r * 0.8) < 1
            art.put(c, e, art.ramp(np.full((HH, HW), 0.9), EMBER))
            glow_put(g, e, (255, 214, 140), 255)
            hal = (art.ellipse(HH, HW, ex, cy + side * ey, r + 2, r + 1.6) < 1) & ~e
            glow_put(g, hal, (255, 110, 30), 90)
        # slit pupils on the great eyes
        sl = (art.ellipse(HH, HW, 65, cy + side * 4.5, 0.5, 2.0) < 1)
        art.put(c, sl, OUTLINE)
    # teeth along the face plate's front edge, between the jaws
    for k in range(-3, 4):
        t_ = art.sd_capsule(HH, HW, 70, cy + k * 2.6, 74 + (2 if k % 2 else 0), cy + k * 2.6, 0.9, 0.3) < 0
        art.put(c, t_, art.ramp(np.full((HH, HW), 0.85), BONE))
    sil = c[..., 3] > 0
    art.put(c, art.outline(sil), OUTLINE)
    return c, g


# ---------------------------------------------------------------- body segment
BW, BH = 54, 72


def body(frame):
    c = art.new(BH, BW)
    g = art.new(BH, BW)
    cy = BH / 2
    ph = frame / BODY_FRAMES * 2 * np.pi
    # dorsal spines: one great back-swept thorn above and below each plate
    for side in (-1, 1):
        sp = art.sd_capsule(BH, BW, 30, cy + side * 14, 8, cy + side * 33, 3.2, 0.4) < 0
        art.put(c, sp, art.ramp(np.full((BH, BW), 0.3), CHITIN))
        edge = sp & art.outline(~sp)
        sp_tip = sp & (np.abs(art.grid(BH, BW)[1] - cy) > 29)
        art.put(c, sp_tip, art.ramp(np.full((BH, BW), 0.6), BONE))
        glow_put(g, sp_tip, (255, 190, 90), 90)
    # legs (behind the plate): two a side, three joints, a gait that ripples
    for side in (-1, 1):
        for k, lx in enumerate((19, 35)):
            a = ph + k * np.pi + (0 if side < 0 else np.pi * 0.5)
            sw = np.sin(a) * 6
            lift = max(0.0, np.cos(a)) * 3
            hip = (lx, cy + side * 12)
            knee = (lx + sw * 0.5 + 1, cy + side * (23 - lift))
            ankle = (lx + sw * 0.9 - 4, cy + side * (30 - lift))
            foot = (lx + sw - 9, cy + side * (34 - lift * 0.5))
            m = line_mask(BH, BW, [hip, knee], 1.7) | line_mask(BH, BW, [knee, ankle], 1.2) | line_mask(BH, BW, [ankle, foot], 0.8)
            art.put(c, m, art.ramp(np.full((BH, BW), 0.32), BONE))
            for j, r in ((knee, 1.4), (ankle, 1.1)):
                jn = art.ellipse(BH, BW, j[0], j[1], r, r) < 1
                art.put(c, jn, art.ramp(np.full((BH, BW), 0.55), GOLD))
            tip = art.ellipse(BH, BW, foot[0], foot[1], 1.3, 1.3) < 1
            art.put(c, tip, art.ramp(np.full((BH, BW), 0.85), GOLD))
            glow_put(g, tip, (255, 200, 110), 110)
    # the plate: a rounded shield, overlapping the next one (rear edge left)
    sd = art.union([
        art.sd_ellipse(BH, BW, 27, cy, 20, 18),
        art.sd_ellipse(BH, BW, 34, cy, 13, 15),
    ], k=5.0)
    inside = carapace(c, sd, depth=6.0)
    x, y = art.grid(BH, BW)
    # gold rim on the rear lip (overlap edge)
    rim = inside & (sd > -2.2) & (x < 22)
    stroke(c, rim, GOLD, 0.55)
    glow_put(g, rim, (255, 180, 70), 90)
    # dorsal keel and the ember seam that pulses (glow driven in code)
    keel = inside & (np.abs(y - cy) < 1.0) & (x > 12) & (x < 44)
    stroke(c, keel, CHITIN, 0.95)
    seam = inside & (art.ellipse(BH, BW, 28, cy, 1.7, 10) < 1)
    stroke(c, seam, EMBER, 0.75)
    glow_put(g, seam, (255, 170, 70), 255)
    lip = inside & (np.abs(art.ellipse(BH, BW, 28, cy, 3.4, 11.5) - 1) < 0.22)
    stroke(c, lip, GOLD, 0.45)
    glow_put(g, lip, (255, 140, 40), 90)
    # a small engraved glyph either side of the keel
    for s_, gi in ((-1, 3), (1, 9)):
        m = glyph_stamp(BH, BW, 33, int(cy + (-10 if s_ < 0 else 3)), gi) & inside
        stroke(c, m, GOLD, 0.7)
        glow_put(g, m, (255, 210, 120), 170)
    # a band of script across the plate's front and ember veins in the lacquer
    band = inside & (np.abs(x - 41) < 0.8) & (np.abs(y - cy) < 13) & ((y.astype(int) % 3) != 0)
    stroke(c, band, GOLD, 0.6)
    glow_put(g, band, (255, 200, 100), 130)
    vm = veins(BH, BW, inside & (x < 24), 5, 3, 7)
    stroke(c, vm, EMBER, 0.6)
    glow_put(g, vm, (255, 120, 40), 140)
    sil = c[..., 3] > 0
    art.put(c, art.outline(sil), OUTLINE)
    return c, g


# ---------------------------------------------------------------- tail
TW, TH = 60, 44


def tail(frame):
    c = art.new(TH, TW)
    g = art.new(TH, TW)
    cy = TH / 2
    # cerci: two long prongs trailing back (left)
    sway = np.sin(frame / TAIL_FRAMES * 2 * np.pi) * 2.5
    pulse = 0.5 + 0.5 * np.cos(frame / TAIL_FRAMES * 2 * np.pi)
    for side in (-1, 1):
        m = line_mask(TH, TW, [(24, cy + side * 6), (12, cy + side * 12 + sway), (2, cy + side * 15 + sway * 1.6)], 1.2)
        art.put(c, m, art.ramp(np.full((TH, TW), 0.45), BONE))
        tip = art.ellipse(TH, TW, 2.5, cy + side * 15 + sway * 1.6, 1.6, 1.6) < 1
        art.put(c, tip, art.ramp(np.full((TH, TW), 0.8), GOLD))
        glow_put(g, tip, (255, 200, 110), int(120 + 120 * pulse))
    sd = art.union([
        art.sd_ellipse(TH, TW, 40, cy, 17, 15),
        art.sd_capsule(TH, TW, 40, cy, 14, cy, 11, 3),
    ], k=4.0)
    inside = carapace(c, sd, depth=5.0)
    x, y = art.grid(TH, TW)
    rings = inside & (np.abs(np.sin((x - 10) * 0.42)) < 0.18) & (x < 34)
    stroke(c, rings, GOLD, 0.5)
    glow_put(g, rings, (255, 170, 60), 80)
    # stinger of light
    st = line_mask(TH, TW, [(16, cy), (0, cy)], 1.0)
    art.put(c, st, art.ramp(np.full((TH, TW), 0.7 + 0.25 * pulse), GOLD))
    glow_put(g, st, (255, 230, 160), int(150 + 105 * pulse))
    seam = inside & (art.ellipse(TH, TW, 42, cy, 1.4, 7) < 1)
    stroke(c, seam, EMBER, 0.7)
    glow_put(g, seam, (255, 150, 50), 220)
    sil = c[..., 3] > 0
    art.put(c, art.outline(sil), OUTLINE)
    return c, g


def build():
    hs = [head(i) for i in range(HEAD_FRAMES)]
    art.save(art.sheet([h[0] for h in hs]), OUT + "RecursionHead.png")
    art.save(art.sheet([h[1] for h in hs]), OUT + "RecursionHead_Glow.png")
    bs = [body(i) for i in range(BODY_FRAMES)]
    art.save(art.sheet([b[0] for b in bs]), OUT + "RecursionBody.png")
    art.save(art.sheet([b[1] for b in bs]), OUT + "RecursionBody_Glow.png")
    ts = [tail(i) for i in range(TAIL_FRAMES)]
    art.save(art.sheet([t[0] for t in ts]), OUT + "RecursionTail.png")
    art.save(art.sheet([t[1] for t in ts]), OUT + "RecursionTail_Glow.png")
    # boss icon: the head, closed, downscaled into a 30 x 24 badge
    im = art.to_image(hs[2][0], scale=1)
    im = im.crop((2, 4, 100, 68)).resize((36, 24), Image.NEAREST)
    im.save(OUT + "RecursionHead_Head_Boss.png")


if __name__ == "__main__":
    build()
    # preview strip for checking
    prev = Image.new("RGBA", (900, 300), (40, 38, 52, 255))
    x = 10
    for name in ("RecursionHead", "RecursionBody", "RecursionTail"):
        im = Image.open(OUT + name + ".png")
        gl = Image.open(OUT + name + "_Glow.png")
        prev.alpha_composite(im, (x, 10))
        prev.alpha_composite(gl, (x + im.width + 6, 10))
        x += im.width * 2 + 24
    prev.save("preview_centipede.png")
