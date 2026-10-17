"""Re (phase 1) body sheet + glow sheet.

Sheet layout (vertical): 8 idle frames, 6 attack (maw-open) frames.
Each frame: 80 x 84 art px -> 160 x 168 stored.
"""
import os
ASSETS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets").replace("\\", "/")
import numpy as np
from scipy import ndimage
import art
from art import BLACK, SHEEN, GOLD, EMBER, BONE, OUTLINE

W, H = 80, 84
CX, CY = 40, 44
OUT = ASSETS + "/Textures/NPCs/Re/"

IDLE = 8
ATTACK_OPEN = [0.2, 0.5, 0.8, 1.0, 1.0, 0.92]
FRAMES = [("idle", i, 0.0) for i in range(IDLE)] + [("atk", i, o) for i, o in enumerate(ATTACK_OPEN)]

BASE_GLYPHS = art.glyph_field(H, W, cell=(7, 9), seed=11)


def body_frame(kind, i, open_amt):
    p = (i / IDLE) * 2 * np.pi if kind == "idle" else (i / len(ATTACK_OPEN)) * 2 * np.pi
    breath = np.sin(p)
    atk = kind == "atk"
    lunge = open_amt * 2.5 if atk else 0.0

    S = art.sd_ellipse
    sds = [
        S(H, W, CX, CY - 12 - lunge + breath * 0.8, 13.5, 16.5),                  # hood
        S(H, W, CX, CY - 27 - lunge + breath * 1.1, 5.5, 8.5),                    # hood point
        S(H, W, CX - 16, CY - 1 + breath * 0.5, 11, 8, rot=0.5),                   # left shoulder
        S(H, W, CX + 16, CY - 1 - breath * 0.5, 11, 8, rot=-0.5),                  # right shoulder
        S(H, W, CX, CY + 8 + breath * 0.3, 17 + open_amt, 15),                     # chest
        S(H, W, CX, CY + 22 - breath * 0.5, 11, 9),                                # belly
        S(H, W, CX - 25, CY + 4 + np.sin(p + 0.7), 5.5, 7, rot=0.3),               # arm stubs
        S(H, W, CX + 25, CY + 4 + np.sin(p + 3.1), 5.5, 7, rot=-0.3),
        art.sd_capsule(H, W, CX - 9, CY + 24, CX - 11, CY + 33 + np.sin(p + 1) * 1.5, 3.2, 1.6),   # drips
        art.sd_capsule(H, W, CX + 8, CY + 25, CX + 10, CY + 34 + np.sin(p + 2.4) * 1.5, 3.0, 1.4),
        art.sd_capsule(H, W, CX + 1, CY + 27, CX, CY + 37 + np.sin(p + 4) * 1.8, 3.4, 1.8),
    ]
    sd = art.union(sds, 4.0)
    mask = sd <= 0

    # height field for normals: domed + flowing goo bumps
    dist = ndimage.distance_transform_edt(mask)
    dome = np.sqrt(np.clip(dist / 14.0, 0, 1))
    flow = art.value_noise(H, W, 9, seed=5, octaves=3, offset=(0, -i * 1.7))
    hgt = dome * 9 + flow * 2.2 * mask
    n = art.normals_from_height(ndimage.gaussian_filter(hgt, 0.8))
    diff, spec = art.shade(n)

    x, y = art.grid(H, W)
    # face cavity under the hood (ambient occlusion)
    cav = art.ellipse(H, W, CX, CY - 3 - lunge * 0.5, 15, 11)
    ao = np.clip(cav, 0.25, 1.0)
    ao = ao * (0.55 + 0.45 * np.clip(dist / 5, 0, 1))

    val = (0.05 + 0.75 * diff * ao) * mask
    val += 0.08 * art.value_noise(H, W, 4, seed=9, octaves=2, offset=(0, -i)) * mask
    col = art.ramp(val, BLACK)

    c = art.new(H, W)
    art.put(c, mask, col)

    # wet sheen highlights (gold/olive like the reference goo)
    sh = (spec * ao) * mask
    shm = sh > 0.32
    art.put(c, shm, art.ramp(np.clip((sh - 0.32) * 1.6, 0, 1), SHEEN))
    # warm rim light from below-right (reflected glyph light)
    edge = mask & ~ndimage.binary_erosion(mask, iterations=2)
    edge1 = mask & ~ndimage.binary_erosion(mask, iterations=1)
    rim = edge1 & (x > CX + 8) & (y > CY + 2)
    art.put(c, rim, GOLD[1])

    # glyph veins carved into the goo (base layer shows dim engraving)
    inner = ndimage.binary_erosion(mask, iterations=3)
    # glyphs live only in a few curved bands: hood rim arc, chest arc, shoulder stripes
    hood_arc = np.abs(np.sqrt((x - CX) ** 2 + ((y - (CY - 12 - lunge)) * 0.95) ** 2) - 14.5) < 3.2
    hood_arc &= y < CY - 6
    chest = np.abs(y - (CY + 15 + ((x - CX) / 10.0) ** 2 * 1.8)) < 3.2
    shoulders = (np.abs(np.abs(x - CX) - 18 + (y - CY) * 0.35) < 2.6) & (y > CY - 9) & (y < CY + 10)
    sparse = art.value_noise(H, W, 6, seed=21, octaves=2, offset=(0, -i * 0.6)) > 0.42
    band = (hood_arc | chest | shoulders) & sparse
    glyph = BASE_GLYPHS & inner & band & (cav > 1.05)
    art.put(c, glyph, GOLD[1])

    # forehead crest sigil (always lit)
    crest = np.zeros((H, W), bool)
    cg = art.GLYPHS[4]
    cy0, cx0 = int(CY - 27 - lunge + breath * 1.1), CX - 2
    crest[cy0:cy0 + 7, cx0:cx0 + 5] = cg
    crest &= mask
    art.put(c, crest, GOLD[2])
    glyph = glyph | crest

    glow = art.new(H, W)

    # glyph pulse travelling upward
    pulse = 0.5 + 0.5 * np.sin(p * 1.0 + y * 0.16 - x * 0.03)
    if atk:
        pulse = np.clip(pulse * 0.6 + 0.4 + open_amt * 0.3, 0, 1)
    halo = ndimage.binary_dilation(glyph, iterations=1) & ~glyph & mask
    art.put(glow, halo, GOLD[2], (60 + 70 * pulse))
    art.put(glow, glyph, art.ramp(0.35 + 0.65 * pulse, GOLD[2:]), 255)

    # ---------------------------------------------------------------- maw
    my = CY + 6 - lunge * 0.4
    if open_amt > 0:
        rx, ry = 6 + 7 * open_amt, 1.2 + 7.5 * open_amt
        e = art.ellipse(H, W, CX, my, rx, ry)
        maw = (e < 1) & mask
        depth = np.clip(1 - e, 0, 1)
        inside = art.ramp(depth ** 1.4 * (0.6 + 0.4 * open_amt), np.vstack([BLACK[:2], EMBER[:3]]))
        art.put(c, maw, inside)
        # throat glow in glow layer
        gl = maw & (depth > 0.35)
        art.put(glow, gl, art.ramp(np.clip((depth - 0.35) * 1.6, 0, 1) * open_amt, np.vstack([EMBER[1:], GOLD[4:6]])), 220)
        # lip shading
        lip = ndimage.binary_dilation(maw, iterations=1) & ~maw & mask
        art.put(c, lip, BLACK[0])
        # teeth: triangles hanging from upper and lower lip
        teeth = np.zeros((H, W), bool)
        tooth_shade = np.zeros((H, W))
        nt = 5
        for k in range(nt):
            tx = CX - rx + (k + 0.5) * (2 * rx / nt)
            ux = (tx - CX) / rx
            if abs(ux) >= 0.98:
                continue
            ytop = my - ry * np.sqrt(1 - ux * ux)
            ybot = my + ry * np.sqrt(1 - ux * ux)
            ln = (2.0 + 4.6 * open_amt) * (1 - 0.35 * abs(ux)) * (1.25 if k % 2 == 0 else 0.7)
            for (y0, sgn, ofs) in ((ytop, 1, 0.0), (ybot, -1, rx / nt)):
                tx = tx + ofs * (1 if sgn < 0 else 0)
                for dy in range(int(np.ceil(ln)) + 1):
                    half = 1.1 * max(0.0, 1 - dy / ln) ** 0.8
                    yy = int(round(y0 + sgn * dy))
                    for xx in range(int(np.floor(tx - half)), int(np.ceil(tx + half)) + 1):
                        if 0 <= yy < H and 0 <= xx < W and abs(xx - tx) <= half + 0.25:
                            teeth[yy, xx] = True
                            tooth_shade[yy, xx] = 0.9 - 0.6 * dy / max(ln, 1) + (0.15 if xx < tx else -0.1)
        teeth &= ndimage.binary_dilation(maw, iterations=1)
        art.put(c, teeth, art.ramp(np.clip(tooth_shade, 0, 1), BONE))
    else:
        # closed: a jagged seam with glints of teeth
        seam = (np.abs(y - (my + np.sin((x - CX) * 0.5) * 0.6)) < 0.6) & (np.abs(x - CX) < 9) & mask
        art.put(c, seam, BLACK[0])
        glint = seam & ((x.astype(int) + i) % 4 == 0)
        art.put(c, glint, BONE[1])

    # ---------------------------------------------------------------- eyes
    eye_open = 1.0 + (0.6 * open_amt if atk else 0.0)
    blink = (kind == "idle" and i == 5)
    for sx in (-1, 1):
        ex, ey = CX + sx * 7.5, CY - 6 - lunge * 0.6
        e = art.ellipse(H, W, ex, ey, 3.2 * eye_open, (0.5 if blink else 1.25) * eye_open, rot=sx * -0.35)
        em = e < 1
        art.put(c, ndimage.binary_dilation(em, iterations=1) & mask, BLACK[0])
        art.put(c, em, GOLD[3])
        art.put(glow, em, GOLD[6] if not blink else GOLD[4], 255)
        art.put(glow, (e >= 1) & (e < 2.4) & mask, GOLD[3], 110)
        if atk:
            art.put(glow, (e >= 2.4) & (e < 4.2) & mask, EMBER[2], 60)

    # outline
    ol = art.outline(mask)
    art.put(c, ol, OUTLINE)
    return c, glow


def build():
    frames, glows = [], []
    for kind, i, o in FRAMES:
        a, g = body_frame(kind, i, o)
        frames.append(a)
        glows.append(g)
    art.save(art.sheet(frames), OUT + "Re.png")
    art.save(art.sheet(glows), OUT + "Re_Glow.png")
    print("Re frames", len(frames), "frame size", W * 2, H * 2)


if __name__ == "__main__":
    build()
