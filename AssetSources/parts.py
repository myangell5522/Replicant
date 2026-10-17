"""Re: crown of tendril-antlers, dripping skirt, tentacle segments/tips, boss head icon."""
import os
ASSETS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets").replace("\\", "/")
import numpy as np
from scipy import ndimage
import art
from art import BLACK, SHEEN, GOLD, EMBER, OUTLINE

OUT = ASSETS + "/Textures/NPCs/Re/"


def shade_mask(mask, light=(-0.6, -0.7, 0.4), bump_seed=0, bump=1.5, t=0):
    dist = ndimage.distance_transform_edt(mask)
    h = np.sqrt(np.clip(dist / 4.0, 0, 1)) * 4 + art.value_noise(*mask.shape, 5, seed=bump_seed, octaves=2, offset=(0, -t)) * bump
    n = art.normals_from_height(ndimage.gaussian_filter(h * mask, 0.6))
    return art.shade(n, light)


# ---------------------------------------------------------------- crown
def crown_frame(t, n_frames):
    W, H = 96, 64
    p = t / n_frames * 2 * np.pi
    sds = []
    tips = []
    rng = np.random.default_rng(42)

    def branch(x, y, ang, length, r, depth, phase):
        nonlocal sds
        sway = np.sin(p + phase) * 0.05 * (3 - depth)
        a = ang + sway
        x2, y2 = x + np.cos(a) * length, y + np.sin(a) * length
        sds.append(art.sd_capsule(H, W, x, y, x2, y2, r, r * 0.62))
        if depth >= 2 or length < 5:
            tips.append((x2, y2, r * 0.62, depth))
            # pointed tip
            x3, y3 = x2 + np.cos(a) * 4, y2 + np.sin(a) * 4
            sds.append(art.sd_capsule(H, W, x2, y2, x3, y3, r * 0.62, 0.3))
            tips.append((x3, y3, 0.4, depth))
            return
        kids = 2 if depth == 0 else 1
        for k in range(kids):
            da = (-0.5 + k / max(1, kids - 1)) * 0.95 if kids > 1 else rng.uniform(-0.3, 0.3)
            branch(x2, y2, a + da + rng.uniform(-0.12, 0.12), length * rng.uniform(0.62, 0.78), r * 0.68, depth + 1, phase + k * 1.3 + 0.7)

    base_y = H - 4
    # five main horns fanning out like a crown
    for k, ang in enumerate(np.linspace(-np.pi * 0.84, -np.pi * 0.16, 5)):
        ln = 11 if k in (0, 4) else (14 if k in (1, 3) else 16)
        branch(48 + (k - 2) * 4, base_y, ang, ln, 2.8, 0, k * 0.9)
    sd = art.union(sds, 1.5)
    mask = sd <= 0
    x, y = art.grid(H, W)

    diff, spec = shade_mask(mask, bump_seed=3, t=t)
    # gradient black -> gold toward the tips (distance from base)
    top = np.argmax(mask.any(1))
    height_t = np.clip((base_y - y) / max(1, base_y - top), 0, 1)
    goldness = np.clip((height_t - 0.4) / 0.45, 0, 1)
    c = art.new(H, W)
    dark = art.ramp((0.08 + 0.7 * diff) * mask, BLACK)
    goldcol = art.ramp((0.15 + 0.7 * diff) * mask, GOLD[:6])
    mix = goldness > art.bayer(H, W)
    col = np.where(mix[..., None], goldcol, dark)
    art.put(c, mask, col)
    art.put(c, mask & (spec > 0.35) & ~mix, SHEEN[2])
    art.put(c, mask & (spec > 0.35) & mix, GOLD[5])
    glyph = art.glyph_field(H, W, cell=(6, 8), seed=8) & ndimage.binary_erosion(mask, iterations=1) & (height_t < 0.55) & (height_t > 0.1)
    glyph &= art.value_noise(H, W, 5, seed=4) > 0.5
    art.put(c, glyph, GOLD[1])
    art.put(c, art.outline(mask), OUTLINE)

    g = art.new(H, W)
    pulse = 0.5 + 0.5 * np.sin(p * 2 - height_t * 6)
    art.put(g, glyph, art.ramp(0.4 + 0.6 * pulse, GOLD[2:]), 255)
    tipglow = mask & (goldness > 0.3)
    art.put(g, tipglow, art.ramp(goldness * (0.6 + 0.4 * pulse), GOLD[2:6]), (goldness * 170)[...])
    for (tx, ty, r, d) in tips:
        if r > 0.5:
            continue
        e = art.ellipse(H, W, tx, ty, 1.0, 1.0)
        art.put(g, (e < 1), GOLD[6], 255)
        art.put(g, (e >= 1) & (e < 4), GOLD[4], 70)
    return c, g


# ---------------------------------------------------------------- skirt
def skirt_frame(t, n_frames):
    W, H = 64, 46
    p = t / n_frames * 2 * np.pi
    x, y = art.grid(H, W)
    sds = [art.sd_ellipse(H, W, 32, 4, 22, 7)]
    strands = [(-18, 30, 0.0, 3.6), (-11, 38, 1.1, 4.2), (-4, 42, 2.0, 4.6), (4, 40, 2.9, 4.4), (11, 36, 3.7, 4.0), (18, 28, 4.6, 3.4),
               (-23, 20, 5.2, 2.6), (23, 21, 0.6, 2.6)]
    tips = []
    for (ox, ln, ph, r) in strands:
        L = ln * (0.9 + 0.1 * np.sin(p + ph))
        sw = np.sin(p + ph) * 2.2
        ax, ay = 32 + ox, 6
        mx, my = 32 + ox * 1.12 + sw * 0.5, 6 + L * 0.55
        bx, by = 32 + ox * 1.2 + sw, 6 + L
        sds.append(art.sd_capsule(H, W, ax, ay, mx, my, r, r * 0.72))
        sds.append(art.sd_capsule(H, W, mx, my, bx, by, r * 0.72, 0.6))
        tips.append((bx, by))
        # falling droplet detached from the strand
        dp = ((t / n_frames) + ph / 6.28) % 1.0
        dy = by + 2 + dp * 9
        if dy < H - 2 and r > 3.5:
            sds.append(art.sd_ellipse(H, W, bx, dy, 1.2, 1.6 + dp))
    sd = art.union(sds, 2.5)
    mask = sd <= 0
    mask[:2, :] = False
    diff, spec = shade_mask(mask, bump_seed=11, t=t * 2)
    ht = np.clip((y - 6) / 36, 0, 1)
    goldness = np.clip((ht - 0.45) / 0.45, 0, 1)
    mix = goldness > art.bayer(H, W)
    c = art.new(H, W)
    dark = art.ramp((0.06 + 0.65 * diff) * mask, BLACK)
    gold = art.ramp((0.1 + 0.75 * diff) * mask, GOLD[:6])
    col = np.where(mix[..., None], gold, dark)
    alpha = np.where(mix, 215, 255)  # gold ends are slightly translucent
    art.put(c, mask, col, alpha)
    art.put(c, mask & (spec > 0.38) & ~mix, SHEEN[2])
    art.put(c, mask & (spec > 0.38) & mix, GOLD[6])
    glyph = art.glyph_field(H, W, cell=(6, 9), seed=19) & ndimage.binary_erosion(mask, iterations=1) & (ht > 0.1) & (ht < 0.55)
    glyph &= art.value_noise(H, W, 5, seed=2, offset=(0, -t)) > 0.56
    glyph &= (ht > 0.18) & (ht < 0.4)
    art.put(c, glyph, GOLD[1])
    ol = art.outline(mask)
    art.put(c, ol & (y < 30), OUTLINE)
    art.put(c, ol & (y >= 30), GOLD[0], 200)
    g = art.new(H, W)
    pulse = 0.5 + 0.5 * np.sin(p - ht * 5)
    art.put(g, glyph, art.ramp(0.4 + 0.6 * pulse, GOLD[2:]), 255)
    art.put(g, mask & mix, art.ramp(goldness * 0.9, GOLD[1:6]), goldness[...] * 150)
    return c, g


# ---------------------------------------------------------------- tentacle
def tentacle_parts():
    """8 cells (vertical): 3 dark segments, 3 gold segments (glyph variants), tip-dark, tip-gold. Cell 16x24 art."""
    CW, CH = 16, 24
    cells, glows = [], []
    combos = [("seg", False, 0), ("seg", False, 1), ("seg", False, 2), ("seg", True, 0), ("seg", True, 1), ("seg", True, 2), ("tip", False, 0), ("tip", True, 0)]
    for kind, gold, var in combos:
        if True:
            x, y = art.grid(CH, CW)
            if kind == "seg":
                sd = art.union([art.sd_ellipse(CH, CW, 8, 12, 6.2, 7.5), art.sd_ellipse(CH, CW, 8, 5, 4.6, 4.5), art.sd_ellipse(CH, CW, 8, 19, 4.6, 4.5)], 2.5)
            else:
                sd = art.union([art.sd_ellipse(CH, CW, 8, 6, 5.6, 5.6), art.sd_capsule(CH, CW, 8, 7, 8.5, 22.5, 4.6, 0.3),
                                art.sd_capsule(CH, CW, 7, 12, 2.5, 17, 1.6, 0.3)], 2.0)
            mask = sd <= 0
            diff, spec = shade_mask(mask, bump_seed=7, bump=0.8)
            c = art.new(CH, CW)
            if gold:
                art.put(c, mask, art.ramp(0.12 + 0.75 * diff, GOLD[:6]), 210)
                art.put(c, mask & (spec > 0.3), GOLD[6], 240)
                art.put(c, art.outline(mask), GOLD[0], 220)
            else:
                art.put(c, mask, art.ramp(0.06 + 0.7 * diff, BLACK))
                art.put(c, mask & (spec > 0.3), SHEEN[2])
                art.put(c, art.outline(mask), OUTLINE)
            g = art.new(CH, CW)
            # glyph band around the segment
            band = np.zeros((CH, CW), bool)
            if kind == "seg":
                gl = art.GLYPHS[(9, 22, 31)[var]]
                band[9:16, 5:10] = gl
            else:
                gl = art.GLYPHS[17]
                band[3:10, 6:11] = gl
            band &= ndimage.binary_erosion(mask, iterations=1)
            art.put(c, band, GOLD[1] if not gold else GOLD[5])
            art.put(g, band, GOLD[5] if not gold else GOLD[6], 255)
            if gold:
                art.put(g, mask & ~band, art.ramp(0.3 + 0.5 * diff, GOLD[2:6]), 140)
            cells.append(c)
            glows.append(g)
    return art.sheet(cells), art.sheet(glows)


# ---------------------------------------------------------------- boss icon
def head_icon():
    W = H = 18
    sd = art.union([art.sd_ellipse(H, W, 9, 10, 6.5, 6.5), art.sd_ellipse(H, W, 9, 4, 3, 4)], 2)
    mask = sd <= 0
    diff, spec = shade_mask(mask, bump=0.2)
    c = art.new(H, W)
    art.put(c, mask, art.ramp(0.1 + 0.7 * diff, BLACK))
    art.put(c, mask & (spec > 0.4), SHEEN[2])
    for ex in (6, 12):
        c[9, ex - 1:ex + 1] = list(GOLD[5]) + [255]
    c[12, 7:12] = list(EMBER[2]) + [255]
    c[3, 9] = list(GOLD[4]) + [255]
    c[5, 8] = list(GOLD[3]) + [255]
    art.put(c, art.outline(mask), OUTLINE)
    return c


def build():
    n = 6
    cr = [crown_frame(t, n) for t in range(n)]
    art.save(art.sheet([a for a, _ in cr]), OUT + "ReCrown.png")
    art.save(art.sheet([g for _, g in cr]), OUT + "ReCrown_Glow.png")
    sk = [skirt_frame(t, 8) for t in range(8)]
    art.save(art.sheet([a for a, _ in sk]), OUT + "ReSkirt.png")
    art.save(art.sheet([g for _, g in sk]), OUT + "ReSkirt_Glow.png")
    a, g = tentacle_parts()
    art.save(a, OUT + "ReTentacle.png")
    art.save(g, OUT + "ReTentacle_Glow.png")
    art.save(head_icon(), OUT + "Re_Head_Boss.png", scale=2)
    print("parts done")


if __name__ == "__main__":
    build()
