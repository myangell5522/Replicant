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
    spec = ROWS[row]
    o = spec["o"]
    gx, gy = spec["g"]
    c = art.new(FH, FW)
    g = art.new(FH, FW)
    x, y = art.grid(FH, FW)
    rng = np.random.default_rng(100 + L)
    tw = L / (LEVELS - 1)
    for (cx, cy, side) in EYES:
        rx, up, lo = 15.0, 7.5, 5.5
        u = (x - cx) / rx
        # slight almond asymmetry: outer corner lifted
        lift = side * 0 + (x - cx) * 0.0
        prof = np.clip(1 - u * u, 0, 1) ** 0.75
        top = cy - up * prof * o + (1 - o) * 1.5 - np.where(np.sign(x - cx) == -side, 0, 0)
        bot = cy + lo * prof * (0.35 + 0.65 * o) + (1 - o) * 1.5 * 0
        if o <= 0.01:
            eye = np.zeros_like(x, bool)
        else:
            eye = (y >= top) & (y <= bot) & (np.abs(u) < 1)
        # ------------------------------------------------ sclera
        sclera_light = lerp((226, 228, 236), (16, 10, 14), min(1, max(0, (L - 2.5) / 1.8)))
        sclera_dark = lerp((150, 152, 172), (6, 4, 8), min(1, max(0, (L - 2.5) / 1.8)))
        shadow = np.clip((y - top) / 3.0, 0, 1)  # upper lid casts shadow
        scl = sclera_dark[None, None, :] * (1 - shadow[..., None]) + sclera_light[None, None, :] * shadow[..., None]
        edge = np.clip(np.abs(u) * 1.3 - 0.3, 0, 1)
        scl = scl * (1 - 0.35 * edge[..., None])
        art.put(c, eye, scl)
        # sclera glyph veins (L>=1)
        if L >= 1:
            vein = art.glyph_field(FH, FW, cell=(6, 8), seed=30 + L) & eye
            vein &= rng.random((FH, FW)) < [0, 0.08, 0.2, 0.32, 0.4, 0.45][L]
            vcol = GOLD[2] if L < 4 else (GOLD[3] if L == 4 else EMBER[2])
            art.put(c, vein, vcol)
            art.put(g, vein, GOLD[3] if L < 5 else EMBER[2], 35 + 10 * L)
        # ------------------------------------------------ iris
        icx, icy = cx + gx, cy + gy - 0.5
        rI = 5.6
        d = np.sqrt((x - icx) ** 2 + (y - icy) ** 2)
        iris = (d < rI) & eye
        pal = iris_palette(L)
        ang = np.arctan2(y - icy, x - icx)
        radial = d / rI
        fibers = 0.5 + 0.5 * np.sin(ang * 9 + d * 0.8)
        v = 0.25 + 0.55 * np.sin(np.clip(radial, 0, 1) * np.pi) + 0.15 * fibers - 0.25 * np.clip((y - top) < 2.5, 0, 1)
        if L >= 4:  # concentric rings like the red sigil eyes
            v = 0.3 + 0.7 * (0.5 + 0.5 * np.cos(radial * np.pi * (5 + L)))
        icol = art.ramp(np.clip(v, 0, 1), pal)
        art.put(c, iris, icol)
        limbal = iris & (radial > 0.82)
        art.put(c, limbal, pal[0])
        if L == 2:  # half corrupted iris (gold invades from one side)
            half = iris & ((x - icx) * side > -1) & (radial > 0.3)
            art.put(c, half, art.ramp(np.clip(v, 0, 1), GOLD[:6]))
        # ------------------------------------------------ pupil
        if L <= 1:
            pup = d < 2.0
        elif L == 2:
            pup = ((x - icx) / 1.6) ** 2 + ((y - icy) / 2.4) ** 2 < 1
        elif L == 3:
            pup = (np.abs(x - icx) < 0.6) & (np.abs(y - icy) < 3.6)
        else:
            pup = (np.abs(d - 2.2) < 0.7)
        pup &= eye
        art.put(c, pup, (4, 3, 6))
        if L >= 4:
            core = (d < 1.0) & eye
            art.put(c, core, GOLD[6] if L == 4 else (255, 250, 240))
        # specular highlight
        hl = (np.abs(x - (icx - 2)) < 1) & (np.abs(y - (icy - 2)) < 1) & eye
        hl &= ((x - (icx - 2)) <= 0) & ((y - (icy - 2)) <= 0) | ((x - (icx - 2)) == 0) & ((y - (icy - 2)) == 0)
        art.put(c, hl, (255, 255, 255))
        art.put(c, (np.abs(x - (icx + 2.5)) < 0.5) & (np.abs(y - (icy + 2.0)) < 0.5) & eye, (255, 255, 255))
        # emissive iris
        glow_a = [22, 26, 40, 70, 90, 105][L]
        gpal = pal
        art.put(g, iris & ~pup, art.ramp(np.clip(v, 0, 1), gpal), glow_a)
        if L >= 4:
            art.put(g, pup | ((d < 1.0) & eye), (255, 220, 170), 170)
        # ------------------------------------------------ lids (lid rim, lashes)
        rim_upper = (np.abs(y - top) < 0.75) & (np.abs(u) < 1.02)
        rim_lower = (np.abs(y - bot) < 0.6) & (np.abs(u) < 0.95)
        if o <= 0.01:
            rim_upper = (np.abs(y - (cy + 1.5)) < 0.6) & (np.abs(u) < 0.98)
            rim_lower = np.zeros_like(rim_upper)
        lid_col = lerp((70, 56, 66), GOLD[3], min(1, L / 4)) if L < 5 else EMBER[2]
        art.put(c, rim_upper, lid_col)
        art.put(c, rim_lower, lid_col * 0.7)
        art.put(g, rim_upper, lid_col, 15 + 18 * L)
        # lashes at the outer corner
        for k in range(3):
            lx = cx + side * (rx - 2 - k * 2.4)
            ux = (lx - cx) / rx
            ty = cy - up * max(0, 1 - ux * ux) ** 0.75 * o + (1 - o) * 1.5 if o > 0.01 else cy + 1.5
            for s in range(2):
                px, py = int(round(lx + side * (s + 1) * 0.8)), int(round(ty - s - 1))
                if 0 <= px < FW and 0 <= py < FH:
                    c[py, px] = list(lid_col) + [255]
        # glyph scribbles around the eye at high corruption
        if L >= 3:
            ring = (np.sqrt(((x - cx) / (rx + 3)) ** 2 + ((y - cy) / (up + 4)) ** 2) < 1.25) & ~eye
            ring &= ~rim_upper & ~rim_lower
            marks = art.glyph_field(FH, FW, cell=(6, 8), seed=50 + L * 3) & ring
            marks &= rng.random((FH, FW)) < (0.12 + 0.07 * (L - 3))
            art.put(c, marks, GOLD[3] if L < 5 else EMBER[3])
            art.put(g, marks, GOLD[3] if L < 5 else EMBER[2], 90)
        # tear of light under closed eye at max corruption
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


if __name__ == "__main__":
    build()
