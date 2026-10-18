"""Cutscene pixel eyes: 6 corruption levels (columns) x 10 animation rows.

Rows: 0 centre, 1 look left, 2 look right, 3 look down, 4 half-blink,
      5..9 closing (lids lowering until shut).
Frame: 80 x 28 art -> 160 x 56 stored. Base sheet + glow sheet.
"""
import os
ASSETS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets").replace("\\", "/")
import numpy as np
from scipy import ndimage
import art
from art import BLUE, GOLD, EMBER, GLYPHS

FW, FH = 80, 28
EYES = [(20, 14, 1), (60, 14, -1)]  # cx, cy, side (outer corner direction)
ROWS = [
    dict(g=(0, 0), o=1.0), dict(g=(-3.5, 0), o=1.0), dict(g=(3.5, 0), o=1.0), dict(g=(0, 1.6), o=0.9),
    dict(g=(0, 0.5), o=0.45),
    dict(g=(0, 1.0), o=0.8), dict(g=(0, 1.4), o=0.55), dict(g=(0, 1.8), o=0.3), dict(g=(0, 2), o=0.1), dict(g=(0, 2), o=0.0),
]
LEVELS = 6
OUT = ASSETS + "/Textures/Cutscene/"

RED = np.array([(40, 4, 4), (110, 12, 8), (190, 30, 14), (255, 70, 30), (255, 150, 80), (255, 230, 190)], float)


def lerp(a, b, t):
    return np.asarray(a, float) * (1 - t) + np.asarray(b, float) * t


def iris_palette(L):
    if L == 0:
        return BLUE
    if L == 1:
        return np.array([lerp(BLUE[i], GOLD[i], 0.12) for i in range(6)])
    if L == 2:
        return np.array([lerp(BLUE[i], GOLD[i], 0.45) for i in range(6)])
    if L == 3:
        return GOLD[:6]
    if L == 4:
        return np.array([lerp(GOLD[i], RED[i], 0.5) for i in range(6)])
    return RED


def frame(L, row):
    """A woman's eyes, drawn in pixels: tilted almonds with a winged lash line, curled lashes, arched brows that
    tighten as the corruption rises, a soft crease, wet catch-lights, and a glyph mark beneath each like a tear."""
    spec = ROWS[row]
    o = spec["o"]
    gx, gy = spec["g"]
    c = art.new(FH, FW)
    g = art.new(FH, FW)
    x, y = art.grid(FH, FW)
    rng = np.random.default_rng(100 + L)
    anger = max(0.0, (L - 2) / 3.0)        # brows draw down and in as she turns
    for (cx, cy, side) in EYES:
        rx, up, lo = 13.5, 7.6, 4.3
        u = (x - cx) / rx
        so = u * side                        # +1 at the outer corner, -1 at the inner
        tilt = np.clip(so, 0, 1) ** 1.5 * 3.6 - np.clip(-so, 0, 1) ** 2 * 1.2  # outer corner lifts, inner dips
        prof_up = np.clip(1 - u * u, 0, 1) ** 0.62
        prof_lo = np.clip(1 - u * u, 0, 1) ** 0.9
        top = cy + 0.5 - up * prof_up * o - tilt * o + (1 - o) * 1.5
        bot = cy + 0.5 + lo * prof_lo * (0.3 + 0.7 * o) - tilt * 0.6
        if o <= 0.01:
            eye = np.zeros_like(x, bool)
        else:
            eye = (y >= top) & (y <= bot) & (np.abs(u) < 1)
        # ------------------------------------------------ sclera: warm, shaded under the lid, pink at the inner corner
        k_dark = min(1, max(0, (L - 2.5) / 1.8))
        sclera_light = lerp((238, 232, 230), (16, 10, 14), k_dark)
        sclera_dark = lerp((160, 146, 160), (6, 4, 8), k_dark)
        shadow = np.clip((y - top) / 3.2, 0, 1)
        scl = sclera_dark[None, None, :] * (1 - shadow[..., None]) + sclera_light[None, None, :] * shadow[..., None]
        edge = np.clip(np.abs(u) * 1.3 - 0.3, 0, 1)
        scl = scl * (1 - 0.3 * edge[..., None])
        art.put(c, eye, scl)
        caruncle = eye & (so < -0.78)
        art.put(c, caruncle, lerp((214, 150, 150), (120, 30, 20), k_dark))
        if L >= 1:
            vein = art.glyph_field(FH, FW, cell=(6, 8), seed=30 + L) & eye
            vein &= rng.random((FH, FW)) < [0, 0.06, 0.16, 0.28, 0.36, 0.42][L]
            vcol = GOLD[2] if L < 4 else (GOLD[3] if L == 4 else EMBER[2])
            art.put(c, vein, vcol)
            art.put(g, vein, GOLD[3] if L < 5 else EMBER[2], 35 + 10 * L)
        # ------------------------------------------------ iris: large, layered, a dark limbal ring and a lit lower half
        icx, icy = cx + gx, cy + gy
        rI = 6.0
        d = np.sqrt((x - icx) ** 2 + ((y - icy) * 1.05) ** 2)
        iris = (d < rI) & eye
        pal = iris_palette(L)
        ang = np.arctan2(y - icy, x - icx)
        radial = d / rI
        fibers = 0.5 + 0.5 * np.sin(ang * 11 + d * 0.9)
        lower_lit = np.clip((y - icy) / rI, 0, 1) * 0.35
        v = 0.2 + 0.45 * np.sin(np.clip(radial, 0, 1) * np.pi) + 0.15 * fibers + lower_lit - 0.3 * ((y - top) < 2.5)
        if L >= 4:
            v = 0.3 + 0.7 * (0.5 + 0.5 * np.cos(radial * np.pi * (5 + L)))
        icol = art.ramp(np.clip(v, 0, 1), pal)
        art.put(c, iris, icol)
        art.put(c, iris & (radial > 0.84), pal[0])
        if L == 2:
            half = iris & ((x - icx) * side > -1) & (radial > 0.3)
            art.put(c, half, art.ramp(np.clip(v, 0, 1), GOLD[:6]))
        # ------------------------------------------------ pupil
        if L <= 1:
            pup = d < 2.2
        elif L == 2:
            pup = ((x - icx) / 1.6) ** 2 + ((y - icy) / 2.5) ** 2 < 1
        elif L == 3:
            pup = (np.abs(x - icx) < 0.6) & (np.abs(y - icy) < 3.8)
        else:
            pup = (np.abs(d - 2.3) < 0.7)
        pup &= eye
        art.put(c, pup, (4, 3, 6))
        if L >= 4:
            core = (d < 1.0) & eye
            art.put(c, core, GOLD[6] if L == 4 else (255, 250, 240))
        # wet catch-lights: a bold one up-left, a small one low-right
        hl1 = eye & ((x - (icx - 2.2)) ** 2 + (y - (icy - 2.2)) ** 2 < 1.6)
        art.put(c, hl1, (255, 255, 255))
        art.put(c, eye & (np.abs(x - (icx + 2.4)) < 0.5) & (np.abs(y - (icy + 2.2)) < 0.5), (255, 250, 240))
        glow_a = [24, 28, 42, 72, 92, 108][L]
        art.put(g, iris & ~pup, art.ramp(np.clip(v, 0, 1), pal), glow_a)
        art.put(g, hl1, (255, 255, 255), 160)
        if L >= 4:
            art.put(g, pup | ((d < 1.0) & eye), (255, 220, 170), 170)
        # ------------------------------------------------ lids: a heavy upper lash line with a wing, a fine lower line
        lid_col = lerp((150, 104, 70), GOLD[3], min(1, L / 5)) if L < 5 else EMBER[2]
        lash_col = lerp((66, 40, 44), GOLD[2], min(1, max(0, (L - 2) / 3))) if L < 5 else EMBER[1]
        if o > 0.01:
            rim_upper = (y >= top - 1.4) & (y < top + 0.4) & (np.abs(u) < 1.0)
            rim_lower = (np.abs(y - bot) < 0.55) & (np.abs(u) < 0.92) & (so > -0.7)
        else:
            closed_y = cy + 1.5 - tilt * 0.5
            rim_upper = (np.abs(y - closed_y) < 0.8) & (np.abs(u) < 0.98)
            rim_lower = np.zeros_like(rim_upper)
        # the wing: a flick from the outer corner, up and out
        wing = np.zeros_like(rim_upper)
        wx0 = cx + side * rx * 0.95
        wy0 = (cy + 0.5 - 2.6 * o - (0 if o > 0.01 else -1.0)) if o > 0.01 else cy + 0.5
        for t_ in np.linspace(0, 1, 6):
            px, py = wx0 + side * t_ * 4.5, wy0 - t_ * 2.6
            wing |= (np.abs(x - px) < 0.6) & (np.abs(y - py) < 0.6)
        art.put(c, rim_upper | wing, lash_col)
        # a warm light catching the top of the lash line (reads on a dark sky)
        if o > 0.01:
            sheen = (np.abs(y - (top - 1.9)) < 0.5) & (np.abs(u) < 0.85) & ~rim_upper
            art.put(c, sheen, lid_col)
            art.put(g, sheen, lid_col, 50 + 12 * L)
        art.put(c, rim_lower, lid_col * 0.8)
        art.put(g, rim_upper | wing, lid_col, 30 + 16 * L)
        # curled lashes along the outer half of the upper lid
        for k in range(5):
            f = 0.15 + k * 0.17                       # 0 = centre, 1 = outer corner
            lx = cx + side * rx * f
            uu = (lx - cx) / rx
            base_y = (cy + 0.5 - up * max(0, 1 - uu * uu) ** 0.62 * o - (max(0, f) ** 1.6) * 2.6 * o + (1 - o) * 1.5) if o > 0.01 else cy + 1.5
            length = 2 + (k >= 1) + (k >= 3) + (k >= 4)
            for s_ in range(length):
                px = int(round(lx + side * (s_ * 0.6 + s_ * s_ * 0.3)))
                py = int(round(base_y - 1.5 - s_ * 0.9))
                if 0 <= px < FW and 0 <= py < FH:
                    tipc = lid_col if s_ == length - 1 else lash_col
                    c[py, px] = list(tipc) + [255]
                    g[py, px] = list(lid_col) + [70 if s_ == length - 1 else 25]
        # two little lower lashes at the outer corner
        if o > 0.2:
            for k in range(2):
                lx = cx + side * rx * (0.55 + k * 0.2)
                uu = (lx - cx) / rx
                by = cy + 0.5 + lo * max(0, 1 - uu * uu) ** 0.9 * (0.3 + 0.7 * o) - (0.55 + k * 0.2) ** 1.6 * 2.6 * 0.6
                px, py = int(round(lx + side * 0.6)), int(round(by + 1.2))
                if 0 <= px < FW and 0 <= py < FH:
                    c[py, px] = list(lid_col) + [255]
        # the crease above the lid
        if o > 0.3:
            crease = (np.abs(y - (top - 2.6 - 0.6 * prof_up)) < 0.45) & (np.abs(u) < 0.7) & (so > -0.4)
            art.put(c, crease, lerp((90, 70, 76), GOLD[1], min(1, L / 4)))
        # ------------------------------------------------ brows: soft arches that draw down and in with anger
        bx = np.linspace(-0.85, 1.05, 48)
        brow = np.zeros_like(rim_upper)
        for b in bx:
            px = cx + side * b * rx
            arch = (1 - ((b - 0.3) / 1.2) ** 2) * 3.2
            py = cy - 9.5 - arch + anger * (2.6 - 3.0 * (b + 0.85) / 1.9) + (1 - o) * 1.2
            thick = 0.95 if b < 0.2 else (0.7 if b < 0.7 else 0.45)
            brow |= (np.abs(x - px) < 0.55) & (np.abs(y - py) < thick)
        art.put(c, brow, lerp((96, 64, 52), GOLD[2], min(1, L / 4)) if L < 5 else EMBER[1])
        art.put(g, brow, lid_col, 25 + 10 * L)
        # ------------------------------------------------ the glyph beauty mark beneath the outer corner
        mx, my = int(round(cx + side * 9)), int(round(cy + 8))
        for (dx, dy) in ((0, -1), (0, 0), (-1, 1), (0, 1), (1, 1), (0, 2)):
            if 0 <= my + dy < FH and 0 <= mx + dx < FW:
                c[my + dy, mx + dx] = list(GOLD[4] if (dx, dy) == (0, 0) else GOLD[3] if L < 5 else EMBER[3]) + [255]
                g[my + dy, mx + dx] = list(GOLD[5] if L < 5 else EMBER[3]) + [150 + 15 * L]
        # glyph scribbles around the eye at high corruption
        if L >= 3:
            ring = (np.sqrt(((x - cx) / (rx + 3)) ** 2 + ((y - cy) / (up + 4)) ** 2) < 1.25) & ~eye
            ring &= ~rim_upper & ~rim_lower & ~brow
            marks = art.glyph_field(FH, FW, cell=(6, 8), seed=50 + L * 3) & ring
            marks &= rng.random((FH, FW)) < (0.1 + 0.06 * (L - 3))
            art.put(c, marks, GOLD[3] if L < 5 else EMBER[3])
            art.put(g, marks, GOLD[3] if L < 5 else EMBER[2], 90)
        if o <= 0.01 and L >= 4:
            tear = (np.abs(x - (cx + side * 3)) < 0.5) & (y > cy + 2) & (y < cy + 2 + (L - 2) * 2)
            art.put(c, tear, GOLD[5])
            art.put(g, tear, GOLD[6], 220)
    return c, g


def build():
    rows_c, rows_g = [], []
    for r in range(len(ROWS)):
        fc, fg = [], []
        for L in range(LEVELS):
            a, b = frame(L, r)
            fc.append(a)
            fg.append(b)
        rows_c.append(np.concatenate(fc, 1))
        rows_g.append(np.concatenate(fg, 1))
    art.save(np.concatenate(rows_c, 0), OUT + "Eyes.png")
    art.save(np.concatenate(rows_g, 0), OUT + "Eyes_Glow.png")
    print("eyes", FW * 2, FH * 2, "cols", LEVELS, "rows", len(ROWS))
    # the sky copy (dimension background): rows closed/half/open x levels 1/3/4, pre-scaled 4x so it stays
    # crisp under the sky's linear sampling
    from PIL import Image
    for src, dst in (("Eyes.png", "SkyEyes.png"), ("Eyes_Glow.png", "SkyEyes_Glow.png")):
        im = Image.open(OUT + src)
        fw, fh = FW * 2, FH * 2
        out = Image.new("RGBA", (fw * 4 * 3, fh * 4 * 3))
        for r, rr in enumerate((7, 4, 0)):
            for c_, lv in enumerate((1, 3, 4)):
                out.paste(im.crop((lv * fw, rr * fh, (lv + 1) * fw, (rr + 1) * fh)).resize((fw * 4, fh * 4), Image.NEAREST), (c_ * fw * 4, r * fh * 4))
        out.save(ASSETS + "/Textures/Sky/" + dst)


if __name__ == "__main__":
    build()
