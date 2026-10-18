"""The twins of phase 2 — v3 (eyeless).

Requiem (elder): long wavy hair with an amber "angel ring", a black lace blindfold embroidered with
gold script, a thin trail of black oil running from beneath it like a tear, trailing blindfold ribbons,
a tall shepherd's-crook staff with a hanging bell.

Refrain (younger): blunt-banged bob with gold clips, NO blindfold — where her eyes should be, the skin is
overwritten by a band of flickering gold glyphs that glitch sideways from frame to frame; a hairline crack
runs down from it. Two long hair ribbons stream behind her. Short lyre-staff with glowing strings.

Both gowns dissolve at the hem into hanging tendrils of black oil (Re's body), with drips that stretch and
fall, glossy specular streaks that slide down the silk, and a scrolling band of glyphs above the hem.
Warm rim light from below (the lily field) on the silhouette.

Front-facing (Empress-of-Light style). Sheet per twin (vertical): idle x8, cast x6, dash x4, sing x4,
hurt x2 = 24 frames. Canvas 72 x 100 art px -> 144 x 200 stored. Body centre ~ (36, 52).
"""
import os
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage
import art

ASSETS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets").replace("\\", "/")
OUT = ASSETS + "/Textures/NPCs/Twins/"

W, H = 72, 100
CX = 36

OUTLINE = (12, 7, 10)
SKIN = [(86, 58, 64), (150, 112, 110), (204, 170, 158), (232, 212, 200), (248, 238, 230)]
HAIR = [(6, 4, 7), (15, 10, 14), (28, 19, 21), (46, 31, 27), (84, 56, 36), (138, 92, 44)]
GOLDH = [(104, 58, 12), (176, 108, 24), (234, 166, 52), (255, 218, 124)]
DRESS = [(5, 4, 7), (11, 9, 13), (19, 15, 21), (29, 24, 30), (44, 37, 42), (64, 54, 54)]
SHEEN = [(70, 58, 44), (118, 96, 60), (182, 152, 96), (236, 214, 160)]
CAPE = [(14, 11, 16), (24, 20, 26), (36, 30, 36), (54, 46, 50)]
LINING = [(70, 30, 8), (126, 62, 12), (190, 110, 24), (232, 162, 56)]
GOLD = [(98, 50, 8), (160, 88, 12), (222, 142, 26), (255, 196, 70), (255, 236, 168)]
STAFF = [(46, 30, 14), (98, 64, 22), (168, 118, 40), (232, 192, 104)]
OIL = [(3, 2, 4), (10, 8, 12), (24, 20, 26), (60, 50, 50)]
MOUTH = (148, 78, 80)
MOUTH_D = (70, 26, 36)
BLUSH = (222, 162, 152)
RIM = np.array((214, 128, 44), float)

POSES = [("idle", 8), ("cast", 6), ("dash", 4), ("sing", 4), ("hurt", 2)]

# ---------------------------------------------------------------- hand-pixeled heads (18 wide)
# o outline · h/H/s hair dark/mid/angel-ring · k bang edge · f/F/d/D skin · b blush · m mouth
# B blindfold · y gold embroidery · r oil tear · z glyph band (filled per frame) · c crack · Y/w gold clip
HEAD_REQUIEM = [
    "......oooooo......",
    "....oohhhhhhoo....",
    "...ohhHHHHHHHHho..",
    "..ohHHssssHHHHHho.",
    ".ohHHsHHHHssHHHHho",
    ".ohHHHHHHHHHHHhhho",
    ".ohHHHHHHHHHhhfhho",
    "ohhHHHHHHhhffffhho",
    "ohhHHHhhffffffhhho",
    "ohnnnnnnnnnnnnnnKo",
    "oBByBBByyBBByBBBKK",
    "oNBBBBBBBBBBBBBNKq",
    "ohhdDrDDDDDDDDdhho",
    "ohhdfrfffffffbdhho",
    ".ohdbrfffffffffdho",
    ".ohhdrffffmfffdhho",
    ".ohhhdffffffddhhho",
    "..ohhhddddddhhho..",
    "..ohh.oddddo.hho..",
    "...oo..oooo...oo..",
]
HEAD_REFRAIN = [
    "......oooooo......",
    "....oohhhhhhoo....",
    "...ohhHHHHHHHHho..",
    "..ohHHHssssHHHHho.",
    ".ohHHssHHHHssHHHho",
    ".ohHHHHHHHHHHHHHho",
    "ohhHHHHHHHHHHHHhho",
    "ohhkkkkkkkkkkkkhho",
    "ohhnnnnnnnnnnnnhho",
    "ohhzzzzzzzzzzzzhKo",
    "ohhzzzzzzzzzzzzKKq",
    "ohhDDDcDDDDDDDDhho",
    "ohhdbffcffffffbdho",
    ".ohhdffcffmfffdhho",
    ".ohhhdffffffffdhho",
    "..ohhhddffddhhho..",
    "..oyYhhoddddohhYyo",
    "...oo...oooo..oo..",
]
for g in (HEAD_REQUIEM, HEAD_REFRAIN):
    assert all(len(r) == 18 for r in g), [len(r) for r in g]

HEAD_COLORS = {
    "o": OUTLINE, "k": HAIR[0], "h": HAIR[1], "H": HAIR[2], "s": HAIR[4],
    "f": SKIN[3], "F": SKIN[4], "d": SKIN[2], "D": SKIN[1], "b": BLUSH,
    "m": MOUTH, "M": MOUTH_D, "B": OIL[1], "y": GOLD[2], "r": OIL[0], "z": SKIN[3],
    "c": SKIN[1], "Y": GOLDH[1], "w": GOLDH[2],
    # the blindfold: highlight, cloth, shadow, knot, frayed gold thread
    "n": CAPE[3], "N": OIL[0], "K": CAPE[2], "q": GOLD[1],
}
HEAD_COLORS["B"] = CAPE[1]
HEAD_COLORS["z"] = CAPE[1]

# micro glyphs for Refrain's eye band (3 x 2)
MICRO = [
    ["#.", "##"], ["##", ".#"], ["#.", ".#"], [".#", "#."], ["##", "#."],
    ["#.", "#."], [".#", "##"], ["##", ".."], ["..", "##"], ["#.", "##"],
]


# ---------------------------------------------------------------- canvas helpers
def mask_from(draw_fn):
    im = Image.new("L", (W, H), 0)
    draw_fn(ImageDraw.Draw(im))
    return np.asarray(im) > 127


def poly(points):
    return mask_from(lambda d: d.polygon([(float(x), float(y)) for x, y in points], fill=255))


def ell(cx, cy, rx, ry):
    return mask_from(lambda d: d.ellipse([cx - rx, cy - ry, cx + rx, cy + ry], fill=255))


def line_mask(points, width=1):
    return mask_from(lambda d: d.line([(float(x), float(y)) for x, y in points], fill=255, width=width))


GX, GY = art.grid(H, W)
BAYER = art.bayer(H, W)


def cel(mask, palette, light_x=-1.0, base=0.5, contrast=0.45, round_=0.18, light_y=0.0):
    """Banded shading: lit from the upper left, rounder toward a region's core."""
    out = np.zeros((H, W, 3))
    if not mask.any():
        return out
    ys, xs = np.nonzero(mask)
    cx, cy = xs.mean(), ys.mean()
    sx = max(1.5, xs.std())
    sy = max(1.5, ys.std())
    v = base - contrast * (GX - cx) / (sx * 2.5) * (-light_x) - light_y * (GY - cy) / (sy * 3.0)
    v += round_ * np.clip(ndimage.distance_transform_edt(mask) / 3.0, 0, 1) - round_ * 0.5
    v += (BAYER - 0.5) * 0.12          # ordered dither between bands
    n = len(palette)
    idx = np.clip(np.round(np.clip(v, 0, 1) * (n - 1)), 0, n - 1).astype(int)
    return np.array(palette, float)[idx]


class Canvas:
    def __init__(self):
        self.rgb = np.zeros((H, W, 3))
        self.a = np.zeros((H, W))
        self.glow = np.zeros((H, W, 4))
        self.norim = np.zeros((H, W), bool)

    def paint(self, mask, colors, alpha=255):
        col = np.asarray(colors, float)
        if col.ndim == 1:
            self.rgb[mask] = col
        else:
            self.rgb[mask] = col[mask]
        self.a[mask] = alpha
        self.glow[mask] = 0     # covered parts stop glowing

    def px(self, x, y, color, glow=None, ga=200):
        x, y = int(round(x)), int(round(y))
        if 0 <= x < W and 0 <= y < H:
            self.rgb[y, x] = color
            self.a[y, x] = 255
            if glow is not None:
                self.glow[y, x, :3] = glow
                self.glow[y, x, 3] = ga
            else:
                self.glow[y, x] = 0

    def glow_mask(self, mask, color, alpha=255):
        self.glow[mask, :3] = color
        self.glow[mask, 3] = alpha

    def edge_line(self, mask, color):
        e = mask & ~ndimage.binary_erosion(mask)
        self.rgb[e] = color

    def finish(self):
        sil = self.a > 0
        # warm rim light from below/right (the glowing field) on the silhouette's inner edge
        below = np.zeros_like(sil)
        below[:-1] = ~sil[1:]
        right = np.zeros_like(sil)
        right[:, :-1] = ~sil[:, 1:]
        rim = sil & below & ~self.norim & (GY > 40)
        k = np.clip((GY - 40) / 45.0, 0, 1)[..., None] * 0.4
        self.rgb = np.where(rim[..., None], self.rgb * (1 - k) + RIM * k, self.rgb)
        # cool top light: 1px soft highlight on top edges of hair/shoulders
        above = np.zeros_like(sil)
        above[1:] = ~sil[:-1]
        top = sil & above & ~self.norim
        self.rgb = np.where(top[..., None], self.rgb * 0.75 + np.array((90, 76, 70)) * 0.25, self.rgb)
        ol = ndimage.binary_dilation(sil) & ~sil
        self.rgb[ol] = OUTLINE
        self.a[ol] = 255
        out = np.zeros((H, W, 4))
        out[..., :3] = self.rgb
        out[..., 3] = self.a
        return out


def fill_hair(c, m, gold_from, glow=True):
    col = cel(m, HAIR[:5], base=0.48, contrast=0.6)
    c.paint(m, col)
    # glyph-lit gold toward the ends (an echo of Re's tentacles)
    k = np.clip((GY - gold_from) / 9.0, 0, 1) ** 1.3
    tip = m & (k > 0) & (BAYER < k * 0.8)
    idx = np.clip((k * 2.99).astype(int), 0, 2)
    c.rgb[tip] = np.array(GOLDH)[idx[tip]]
    if glow:
        c.glow_mask(tip & (k > 0.55), GOLD[2], 70)


# ---------------------------------------------------------------- parts
def ribbons(c, twin, t, bob, drift, front=False):
    """Requiem: blindfold tails from the temples. Refrain: two long hair ribbons from the back."""
    if twin == "requiem":
        roots = [(CX - 9, 18 + bob), (CX + 9, 18 + bob)]
        length, width = 22, 1.8
    else:
        roots = [(CX - 6, 12 + bob), (CX + 6, 12 + bob)]
        length, width = 40, 2.2
    for side, (rx, ry) in zip((-1, 1), roots):
        pts_l, pts_r = [], []
        for i in range(14):
            k = i / 13
            wave = np.sin(k * 5.0 - t * 2 * np.pi + side) * 2.2 * k
            x = rx + side * (3 + 7 * k) + wave + drift * 1.25 * k ** 1.2
            y = ry + k * length - abs(drift) * 0.5 * k
            w = width * (1 - 0.35 * k)
            pts_l.append((x - w, y))
            pts_r.append((x + w, y))
        tail = [(pts_l[-1][0] - 1, pts_l[-1][1] + 3), (pts_r[-1][0] + 1, pts_r[-1][1] + 1)]
        m = poly(pts_l + tail + pts_r[::-1])
        c.paint(m, cel(m, OIL[1:] + [CAPE[3]], base=0.45, contrast=0.6))
        c.norim[m] = True
        # thin gold selvedge on the outer edge only
        edge = m & ~ndimage.binary_erosion(m) & ((GX - rx) * side > 3)
        c.rgb[edge & (BAYER > 0.35)] = GOLD[0]
        c.px(pts_l[-1][0] + width, pts_l[-1][1] + 2, GOLD[2], glow=GOLD[3], ga=150)


def back_hair(c, twin, t, bob, drift):
    top = 9 + bob
    if twin == "requiem":
        L, R = [], []
        for k in range(20):
            kk = k / 19
            y = top + 5 + kk * 58
            wave = np.sin(kk * 7 + t * 2 * np.pi) * 1.8 * kk
            L.append((CX - 9.5 - 8 * kk ** 1.2 + wave + drift * kk ** 1.6, y))
            R.append((CX + 9.5 + 8 * kk ** 1.2 + wave * 0.8 + drift * kk ** 1.6, y))
        Li = [(CX - 4 + drift * (k / 19) ** 1.6 * 0.6, top + 5 + (k / 19) * 50) for k in range(20)]
        Ri = [(CX + 4 + drift * (k / 19) ** 1.6 * 0.6, top + 5 + (k / 19) * 50) for k in range(20)]
        tips_l = [(CX - 16 + drift + np.sin(t * 6.283) * 1.5, top + 66), (CX - 9 + drift * 0.9, top + 60)]
        tips_r = [(CX + 9 + drift * 0.9, top + 60), (CX + 16 + drift + np.sin(t * 6.283 + 1) * 1.5, top + 66)]
        mL = poly(L + tips_l + Li[::-1])
        mR = poly(Ri + tips_r + R[::-1])
        m = mL | mR | ell(CX, top + 7, 9.5, 9)
        fill_hair(c, m, gold_from=top + 48)
        # strands and wave highlights
        for k, x0 in enumerate((-12, -9, -6, 6, 9, 12)):
            s = line_mask([(CX + x0, top + 12), (CX + x0 * 1.25 + drift * 0.7 + np.sin(t * 6.28 + k) * 1.2, top + 56)])
            c.rgb[s & m] = HAIR[0]
        for k, x0 in enumerate((-11, 11)):
            for j in range(4):
                yy = top + 20 + j * 11 + int(np.sin(t * 6.28 + j) * 1)
                c.px(CX + x0 + (j % 2) + drift * (0.3 + j * 0.12), yy, HAIR[4])
    else:
        m = ell(CX, top + 7, 9.6, 9.2)
        m |= poly([(CX - 9.6, top + 7), (CX + 9.6, top + 7), (CX + 10 + drift * 0.25, top + 19), (CX - 10.2 + drift * 0.25, top + 19.5)])
        fill_hair(c, m, gold_from=top + 15)


def oil_hem(c, twin, t, hem_pts, drift, gown_mask):
    """Tendrils of black oil hanging from the hem; drips stretch and fall over the loop."""
    rng = np.random.default_rng(5 if twin == "requiem" else 9)
    n = len(hem_pts)
    for j in range(1, n - 1, 1):
        x, y = hem_pts[j]
        base_len = rng.uniform(3, 10) * (1.25 if twin == "requiem" else 0.95)
        phase = rng.uniform(0, 1)
        k = (t + phase) % 1.0
        length = base_len * (0.75 + 0.35 * np.sin(k * 2 * np.pi))
        w = rng.uniform(0.9, 1.6)
        sway = np.sin(t * 2 * np.pi + j) * 0.6 + drift * 0.18
        m = poly([(x - w, y - 1), (x + w, y - 1), (x + w * 0.45 + sway, y + length), (x - w * 0.45 + sway, y + length)])
        m |= ell(x + sway, y + length, w * 0.7, w * 0.8)
        c.paint(m, cel(m, OIL, base=0.4, contrast=0.7))
        c.norim[m] = True
        # specular glint on each tendril
        c.px(x - w * 0.3 + sway * 0.5, y + length * 0.45, OIL[3])
        # a falling drop (detached), on some tendrils
        if j % 3 == int(phase * 3):
            dy = y + length + 2 + k * 9
            if dy < H - 1:
                d = ell(x + sway, dy, 0.8, 1.1)
                c.paint(d, OIL[1])
                c.norim[d] = True
                c.px(x + sway - 0.3, dy - 0.4, OIL[3])
        # gold glint at the root, like light in the oil
        if j % 4 == 1:
            c.px(x, y + 1, GOLD[1], glow=GOLD[2], ga=120)


def gown(c, twin, t, bob, drift):
    waist = 45 + bob
    hem = 84 if twin == "requiem" else 80
    top_w, hem_w = 5.0, (19 if twin == "requiem" else 16)
    L, R = [], []
    for i in range(15):
        k = i / 14
        y = waist + (hem - waist) * k
        w = top_w + (hem_w - top_w) * (k ** 0.7)
        wave = np.sin(t * 2 * np.pi + k * 3.4) * 1.2 * k
        L.append((CX - w + wave + drift * k ** 1.5, y))
        R.append((CX + w + wave + drift * k ** 1.5, y))
    hemp = []
    for j in range(17):
        k = j / 16
        x = L[-1][0] + (R[-1][0] - L[-1][0]) * k
        y = hem + np.sin(k * 11 + t * 2 * np.pi) * 1.2 + (1.4 if j % 2 else 0)
        hemp.append((x, y))
    m = poly(L + hemp + R[::-1])
    c.paint(m, cel(m, DRESS, base=0.46, contrast=0.55))
    # deep folds
    for fx in (-13, -8, -4, 0, 4, 8, 13):
        f = line_mask([(CX + fx * 0.3, waist + 5), (CX + fx + drift * 0.8 + np.sin(t * 6.28 + fx) * 0.6, hem - 1)])
        c.rgb[f & m] = DRESS[0]
    # oily silk: specular streaks that slide down over the loop
    for fx, off in ((-9, 0.0), (-4, 0.37), (3, 0.71)):
        y0 = waist + 4 + ((t + off) % 1.0) * (hem - waist - 10)
        s = line_mask([(CX + fx * 0.6 - 0.5 + drift * 0.3, y0), (CX + fx * 0.8 - 0.5 + drift * 0.5, y0 + 9)])
        s &= m
        c.rgb[s] = SHEEN[1] if fx < 0 else SHEEN[0]
        core = s & (BAYER > 0.6)
        c.rgb[core] = SHEEN[2] if fx == -9 else SHEEN[1]
    if twin == "requiem":
        # front slit showing the amber lining
        panel = poly([(CX - 0.5, waist + 5), (CX + 0.5, waist + 5), (CX + 3.5 + drift, hem - 3), (CX - 1.5 + drift, hem - 3)]) & m
        c.paint(panel, cel(panel, LINING[:2] + [DRESS[3]], base=0.25, contrast=0.6))
        c.edge_line(panel, DRESS[0])
    else:
        # a second, shorter over-skirt tier with a gold-trimmed scalloped edge
        tier_y = waist + 20
        tl = [(x, y) for x, y in L if y <= tier_y] + [(CX - 12 + drift * 0.6, tier_y)]
        tr = [(CX + 12 + drift * 0.6, tier_y)] + [(x, y) for x, y in R if y <= tier_y][::-1]
        sc = []
        for j in range(9):
            k = j / 8
            sc.append((CX - 12 + 24 * k + drift * 0.6, tier_y + (2.2 if j % 2 else 0)))
        tm = poly(tl + sc + tr)
        c.paint(tm, cel(tm, CAPE, base=0.5, contrast=0.6))
        edge = tm & ~ndimage.binary_erosion(tm) & (GY > tier_y - 3)
        c.rgb[edge] = GOLD[1]
        c.glow_mask(edge, GOLD[2], 80)
        under = m & (GY > hem - 3)
        c.rgb[under] = LINING[1]
    # gold hem line + a band of glyphs above it that scrolls with the animation
    hemline = m & (GY >= hem - 6) & (GY < hem - 5)
    c.rgb[hemline] = GOLD[1]
    big = art.glyph_field(H, W * 2, cell=(6, 8), seed=33 if twin == "requiem" else 41)
    shift = int(t * 12) % W
    gl = big[:, shift:shift + W]
    marks = m & gl & (GY > hem - 14) & (GY < hem - 7)
    c.rgb[marks] = GOLD[1]
    c.glow_mask(marks, GOLD[3], 130)
    c.glow_mask(hemline, GOLD[3], 140)
    oil_hem(c, twin, t, hemp, drift, m)
    return m


def bodice(c, twin, bob):
    top, waist = 30 + bob, 45 + bob
    m = poly([(CX - 6.5, top), (CX + 6.5, top), (CX + 5, waist), (CX - 5, waist)])
    c.paint(m, cel(m, DRESS, base=0.56, contrast=0.5))
    # neckline with skin + collarbone shade
    neck = poly([(CX - 3.5, top), (CX + 3.5, top), (CX, top + 5)])
    c.paint(neck, SKIN[3])
    c.px(CX, top + 4, SKIN[2])
    c.px(CX - 2, top + 1, SKIN[2])
    c.px(CX + 2, top + 1, SKIN[2])
    # gold filigree: lacing and two curling side scrolls
    for k in range(5):
        yy = top + 6 + k * 1.8
        c.px(CX - 1, yy, GOLD[2], glow=GOLD[3], ga=90)
        c.px(CX + 1, yy, GOLD[2], glow=GOLD[3], ga=90)
        c.px(CX, yy + 0.9, GOLD[1])
    for side in (-1, 1):
        for k, (dx, dy) in enumerate(((3, 6), (4, 7), (4.5, 8.5), (4, 10), (3, 10.6))):
            c.px(CX + side * dx, top + dy, GOLD[1] if k % 2 else GOLD[2])
    # waist sash
    belt = m & (GY >= waist - 2.5)
    c.rgb[belt] = GOLD[1] if twin == "requiem" else LINING[1]
    c.rgb[belt & (BAYER > 0.7)] = GOLD[2] if twin == "requiem" else LINING[2]
    c.px(CX, waist - 1, GOLD[3], glow=GOLD[4], ga=200)
    c.glow_mask(belt, GOLD[2], 60)
    # neck
    nk = poly([(CX - 1.6, top - 3.5), (CX + 1.6, top - 3.5), (CX + 1.6, top), (CX - 1.6, top)])
    c.paint(nk, SKIN[2])


def sash_tails(c, twin, t, bob, drift):
    """Hanging sash tails at the waist (Requiem: gold tassels, Refrain: a bow's tails)."""
    waist = 45 + bob
    for side in (-1, 1):
        x0 = CX + side * 4
        pts = []
        for i in range(8):
            k = i / 7
            pts.append((x0 + side * k * 2 + np.sin(t * 6.28 + k * 3) * 0.8 * k + drift * 0.6 * k, waist + k * (14 if twin == "requiem" else 11)))
        s = line_mask(pts, width=2 if twin == "refrain" else 1)
        c.paint(s, LINING[1] if twin == "refrain" else GOLD[1])
        tip = pts[-1]
        if twin == "requiem":
            tm = ell(tip[0], tip[1] + 1, 1.0, 1.6)
            c.paint(tm, GOLD[2])
            c.glow_mask(tm, GOLD[3], 140)
        else:
            c.px(tip[0], tip[1] + 1, LINING[2])
    if twin == "refrain":
        bow = poly([(CX - 5, waist - 3), (CX - 1, waist - 1), (CX - 5, waist + 1)]) | poly([(CX + 5, waist - 3), (CX + 1, waist - 1), (CX + 5, waist + 1)])
        c.paint(bow, cel(bow, LINING, base=0.5))
        c.px(CX, waist - 1, GOLD[3], glow=GOLD[4], ga=180)


def cape(c, twin, t, bob, drift):
    top = 29 + bob
    fl = np.sin(t * 2 * np.pi + 1.2) * 0.8
    if twin == "requiem":
        pts = [(CX - 7.5, top), (CX + 7.5, top), (CX + 13 + fl + drift * 0.4, top + 14), (CX + 8, top + 12), (CX + 4.5, top + 14.5),
               (CX - 4.5, top + 14.5), (CX - 8, top + 12), (CX - 13 + fl + drift * 0.4, top + 14)]
    else:
        pts = [(CX - 7.5, top), (CX + 7.5, top), (CX + 12 + fl + drift * 0.4, top + 9), (CX + 4, top + 8.5), (CX - 4, top + 8.5),
               (CX - 12 + fl + drift * 0.4, top + 9)]
    m = poly(pts)
    m &= ~poly([(CX - 2.8, top), (CX + 2.8, top), (CX + 3.8, top + 16), (CX - 3.8, top + 16)])
    c.paint(m, cel(m, CAPE, base=0.5, contrast=0.6, light_y=0.3))
    trim = m & ~ndimage.binary_erosion(m) & (GY > top + 4)
    c.rgb[trim] = GOLD[1]
    c.glow_mask(trim, GOLD[2], 90)
    # high collar
    collar = poly([(CX - 6.5, top - 3), (CX - 3.5, top + 1), (CX + 3.5, top + 1), (CX + 6.5, top - 3), (CX + 4.5, top + 0.5), (CX - 4.5, top + 0.5)])
    c.paint(collar, CAPE[2])
    c.edge_line(collar, CAPE[3])
    brooch = ell(CX, top + 1.8, 1.5, 1.5)
    c.paint(brooch, GOLD[3])
    c.px(CX - 0.5, top + 1.2, GOLD[4], glow=GOLD[4], ga=255)
    c.glow_mask(brooch, GOLD[4], 200)


def arm(c, side, hand, bob, t, far=False):
    """Bell sleeve: narrow at the shoulder, flaring wide at the wrist with a hanging point."""
    sh = np.array((CX + side * 7, 31 + bob), float)
    hd = np.array(hand, float)
    d = hd - sh
    L = np.hypot(*d) + 1e-6
    u = d / L
    n = np.array([-u[1], u[0]])
    w0, w1 = 2.0, 4.6
    drape = np.array((side * 0.6 + np.sin(t * 6.28) * 0.5, 5.5))
    p = [sh + n * w0, hd + n * w1, hd + n * w1 * 0.3 + drape, hd - n * w1, sh - n * w0]
    m = poly([tuple(q) for q in p])
    pal = DRESS if not far else [tuple(int(v * 0.75) for v in col) for col in DRESS]
    c.paint(m, cel(m, pal, base=0.52, contrast=0.45))
    cuff = poly([tuple(hd + n * w1), tuple(hd - n * w1), tuple(hd - n * w1 - u * 1.4), tuple(hd + n * w1 - u * 1.4)])
    c.paint(cuff, GOLD[1])
    c.glow_mask(cuff & (BAYER > 0.5), GOLD[2], 70)
    tipp = hd + n * w1 * 0.3 + drape
    c.px(tipp[0], tipp[1], GOLD[2], glow=GOLD[3], ga=120)
    hp = hd + u * 1.8
    hm = ell(hp[0], hp[1], 1.6, 1.5)
    c.paint(hm, SKIN[3])
    c.px(hp[0] + 0.7, hp[1] + 0.6, SKIN[2])
    return (hp[0], hp[1])


def staff(c, twin, hand, pose, t):
    hx, hy = hand
    tall = twin == "requiem"
    L = 60 if tall else 36
    ang = -np.pi / 2 + (0.16 if pose == "idle" else 0.34 if pose == "cast" else 0.6 if pose == "dash" else 0.25)
    grip = 0.40 if pose == "cast" else 0.64
    top = np.array((hx + np.cos(ang) * L * grip, hy + np.sin(ang) * L * grip))
    bot = np.array((hx - np.cos(ang) * L * (1 - grip), hy - np.sin(ang) * L * (1 - grip)))
    u = (top - bot) / np.linalg.norm(top - bot)
    n = np.array([-u[1], u[0]])
    shaft = line_mask([tuple(bot), tuple(top)], width=2)
    c.paint(shaft, STAFF[1])
    hl = line_mask([tuple(bot - n * 0.5), tuple(top - n * 0.5)])
    c.rgb[hl & shaft] = STAFF[2]
    # gold rings along the shaft + pointed ferrule
    for k in (0.15, 0.5, 0.85):
        p = bot + (top - bot) * k
        c.px(p[0], p[1], GOLD[2])
        c.px(p[0] + n[0], p[1] + n[1], GOLD[1])
    c.px(bot[0] - u[0], bot[1] - u[1], GOLD[2])
    if tall:
        # shepherd's crook curling over, with a bell hanging from it
        cx_, cy_ = top + n * 4.0 * -1 + u * 1.0
        r = 4.0
        ring = (np.hypot(GX - cx_, GY - cy_) <= r + 0.7) & (np.hypot(GX - cx_, GY - cy_) >= r - 0.9)
        ring &= ~((GY > cy_ + 0.5) & (GX < cx_))   # open toward the lower inside
        c.paint(ring, cel(ring, STAFF, base=0.6))
        c.glow_mask(ring & (BAYER > 0.6), GOLD[3], 90)
        # bell
        swing = np.sin(t * 2 * np.pi) * (1.4 if pose != "cast" else 0.4)
        bx, by = cx_ - r + 0.5 + swing * 0.4, cy_ + 2.5
        c.px(bx, by, GOLD[1])
        c.px(bx, by + 1, GOLD[1])
        bell = poly([(bx - 1.5 + swing * 0.3, by + 2), (bx + 1.5 + swing * 0.3, by + 2), (bx + 3 + swing, by + 7), (bx - 3 + swing, by + 7)])
        c.paint(bell, cel(bell, GOLDH, base=0.55, contrast=0.6))
        c.glow_mask(bell, GOLD[3], 150 if pose != "cast" else 230)
        clap = ell(bx + swing * 1.2, by + 7.6, 0.8, 0.8)
        c.paint(clap, GOLD[4])
        c.glow_mask(clap, GOLD[4], 255)
    else:
        # lyre: two curved horns with three glowing strings
        horn_l = [tuple(top + n * (2 + 2.5 * np.sin(k * 1.4)) + u * (k * 6.5)) for k in np.linspace(0, 1, 6)]
        horn_r = [tuple(top - n * (2 + 2.5 * np.sin(k * 1.4)) + u * (k * 6.5)) for k in np.linspace(0, 1, 6)]
        for h in (horn_l, horn_r):
            hm = line_mask(h, width=1)
            c.paint(hm, STAFF[2])
            c.px(h[-1][0], h[-1][1], GOLD[3], glow=GOLD[4], ga=200)
        bar = line_mask([horn_l[-1], horn_r[-1]])
        c.paint(bar, GOLD[1])
        for s in (-1, 0, 1):
            a = top + n * s * 1.2 + u * 0.5
            b = top + n * s * 1.6 + u * 6.0
            sm = line_mask([tuple(a), tuple(b)])
            c.paint(sm, GOLD[3])
            c.glow_mask(sm, GOLD[4], 220 if pose == "cast" else 150)
        gem = ell(top[0], top[1], 1.3, 1.3)
        c.paint(gem, GOLD[3])
        c.glow_mask(gem, GOLD[4], 255)
    # tassel
    tp = top - u * 3 + n * 2.5
    ts = line_mask([tuple(tp), (tp[0] + 0.5, tp[1] + 6)])
    c.paint(ts, LINING[2])
    c.px(tp[0] + 0.5, tp[1] + 6.5, GOLD[3], glow=GOLD[3], ga=150)


def put_head(c, twin, pose, bob, i, tilt):
    g = [list(r) for r in (HEAD_REQUIEM if twin == "requiem" else HEAD_REFRAIN)]
    if pose == "sing":
        for y in range(len(g)):
            for x in range(18):
                if g[y][x] == "m":
                    g[y][x] = "M"
                    if y + 1 < len(g) and g[y + 1][x] in "fd":
                        g[y + 1][x] = "m"
    if pose == "hurt":
        for y in range(len(g)):
            for x in range(18):
                if g[y][x] == "m":
                    g[y][x] = "M"
                    if x + 1 < 18:
                        g[y][x + 1] = "M"
    ox = CX - 9 + tilt
    oy = 8 + bob + (-1 if pose == "sing" else 0)
    glow = np.zeros((H, W), bool)
    band = []
    for y, row in enumerate(g):
        for x, ch in enumerate(row):
            if ch == ".":
                continue
            px, py = ox + x, oy + y
            if 0 <= px < W and 0 <= py < H:
                c.rgb[py, px] = HEAD_COLORS[ch]
                c.a[py, px] = 255
                c.glow[py, px] = 0
                c.norim[py, px] = True
                if ch in "ywq":
                    glow[py, px] = True
                if ch == "z":
                    band.append((px, py))
    strength = 255 if pose == "cast" else 180
    c.glow_mask(glow, GOLD[3], strength)
    if twin == "requiem":
        # the oil tear catches the light
        for y, row in enumerate(g):
            for x, ch in enumerate(row):
                if ch == "r" and (y + i) % 4 == 0:
                    c.px(ox + x, oy + y, OIL[3])
    if band:
        # Refrain: her eyes are written over. Micro-glyphs flicker and the band tears sideways.
        rng = np.random.default_rng(1000 + i * 7 + {"idle": 0, "cast": 11, "dash": 23, "sing": 37, "hurt": 51}[pose])
        xs = [p[0] for p in band]
        ys = [p[1] for p in band]
        x0, y0 = min(xs), min(ys)
        bw = max(xs) - x0 + 1
        shift = int(rng.integers(-1, 2)) if pose != "hurt" else int(rng.integers(-2, 3))
        cells = bw // 3
        # the band itself: an ink smear where the eyes were
        for (px, py) in band:
            c.rgb[py, px] = CAPE[1] if (px + py + i) % 3 else CAPE[0]
        gm = np.zeros((H, W), bool)
        for k in range(cells):
            gph = MICRO[int(rng.integers(0, len(MICRO)))]
            for yy, rowg in enumerate(gph):
                for xx, ch in enumerate(rowg):
                    if ch == "#":
                        px = x0 + k * 3 + 1 + xx + (shift if yy == 0 else 0)
                        py = y0 + yy
                        if x0 - 1 <= px <= x0 + bw and 0 <= py < H:
                            gm[py, px] = True
        c.rgb[gm] = GOLD[3]
        c.rgb[gm & (BAYER > 0.7)] = GOLD[4]
        c.a[gm] = 255
        c.glow_mask(gm, GOLD[4], 255 if pose in ("cast", "hurt") else 210)
        # a stray glyph pixel that escaped the band (glitch) on some frames
        if rng.random() < 0.6:
            sx = x0 + int(rng.integers(0, bw))
            c.px(sx, y0 - 1 - int(rng.integers(0, 2)), GOLD[2], glow=GOLD[3], ga=200)


# ---------------------------------------------------------------- frame assembly
def frame(twin, pose, i, n):
    t = i / n
    c = Canvas()
    bob = int(round(np.sin(t * 2 * np.pi) * 1.0)) if pose in ("idle", "sing", "cast") else 0
    drift = {"dash": -10.0, "hurt": 2.5}.get(pose, np.sin(t * 2 * np.pi) * 0.9)
    tilt = -1 if pose == "dash" else (1 if pose == "hurt" else 0)

    ribbons(c, twin, t, bob, drift)
    back_hair(c, twin, t, bob, drift)
    if pose == "cast":
        left_hand = (CX - 15, 34 + bob)
    elif pose == "sing":
        left_hand = (CX - 2, 39 + bob)
    elif pose == "dash":
        left_hand = (CX - 15, 42)
    elif pose == "hurt":
        left_hand = (CX - 13, 35)
    else:
        left_hand = (CX - 11, 47 + bob + np.sin(t * 6.28) * 0.5)
    gown(c, twin, t, bob, drift)
    bodice(c, twin, bob)
    sash_tails(c, twin, t, bob, drift)
    lh = arm(c, -1, left_hand, bob, t, far=True)
    cape(c, twin, t, bob, drift)
    if pose == "cast":
        right_hand = (CX + 12, 21 + bob)
    elif pose == "sing":
        right_hand = (CX + 2, 39 + bob)
    elif pose == "dash":
        right_hand = (CX + 9, 43)
    elif pose == "hurt":
        right_hand = (CX + 13, 35)
    else:
        right_hand = (CX + 11, 46 + bob)
    rh = arm(c, 1, right_hand, bob, t)
    put_head(c, twin, pose, bob, i, tilt)
    if pose != "sing":
        staff(c, twin, rh, pose, t)
    if pose == "cast":
        palm = ell(lh[0], lh[1], 2.4, 2.4)
        c.glow_mask(palm, GOLD[3], 230)
    if pose == "sing":
        # a small glyph rising from the folded hands
        c.px(CX, 36 + bob - (i % 4), GOLD[3], glow=GOLD[4], ga=220)
    return c.finish(), c.glow


def build():
    os.makedirs(OUT, exist_ok=True)
    for twin in ("requiem", "refrain"):
        frames, glows = [], []
        for pose, n in POSES:
            for i in range(n):
                f, g = frame(twin, pose, i, n)
                frames.append(f)
                glows.append(g)
        name = "Requiem" if twin == "requiem" else "Refrain"
        art.save(art.sheet(frames), OUT + name + ".png")
        art.save(art.sheet(glows), OUT + name + "_Glow.png")
    print("twins", W * 2, H * 2, sum(n for _, n in POSES), "frames")


if __name__ == "__main__":
    build()
