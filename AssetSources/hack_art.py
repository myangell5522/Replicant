"""Intrusion minigame art: the shell blocks that guard the core, and the player's ship.

HackShell.png (+_Glow): 4 columns (animation) x 3 rows (gold intact, gold cracked, black unbreakable),
    each 32 x 14 art px. Curved like a slice of the ring; drawn tangent to it in code.
HackShip.png (+_Glow): 4 frames (engine flicker), 13 x 15 art px, nose pointing up.
"""
import numpy as np
import art

OUT = "../Assets/Textures/Cutscene/"
GOLD = art.GOLD
EMBER = art.EMBER
CHIT = np.array([(4, 3, 6), (10, 8, 12), (18, 14, 20), (30, 24, 30), (46, 38, 42)], float)
SW, SH = 32, 14


def block_mask():
    x, y = art.grid(SH, SW)
    # a curved slab: the outer edge arcs a little (it is a piece of a ring)
    arc = (x - SW / 2) ** 2 * 0.012
    m = (y > 2 + arc) & (y < SH - 2 + arc * 0.6) & (x > 1) & (x < SW - 2)
    # bevelled corners
    m &= ~(((x < 3) | (x > SW - 4)) & ((y < 4 + arc) | (y > SH - 4 + arc * 0.6)))
    return m, arc


def shell(kind, frame):
    c = art.new(SH, SW)
    g = art.new(SH, SW)
    x, y = art.grid(SH, SW)
    m, arc = block_mask()
    top = 2 + arc
    shade = np.clip((y - top) / (SH - 4), 0, 1)
    if kind in (0, 1):
        v = 0.85 - shade * 0.5 + 0.08 * np.sin(x * 0.7)
        art.put(c, m, art.ramp(v, GOLD))
        # engraved script running along the slab, scrolling with the frame
        gf = art.glyph_field(SH, SW + 8, cell=(6, 8), seed=4)[:, frame * 2: frame * 2 + SW]
        eng = gf & m & (y > top + 2) & (y < SH - 3)
        art.put(c, eng, GOLD[1])
        art.put(g, eng, (255, 230, 150), 140 + 30 * (frame % 2))
        # a bright top lip
        lip = m & (y < top + 1.5)
        art.put(c, lip, GOLD[6])
        art.put(g, lip, (255, 240, 190), 120)
        if kind == 1:
            # cracked: a jagged fracture with ember leaking out
            r = np.random.default_rng(9)
            cx = SW / 2 + 3
            crack = np.zeros_like(m)
            px, py = cx, 2.5
            while py < SH - 2:
                nx = px + r.uniform(-2.2, 2.2)
                ny = py + r.uniform(1.0, 2.2)
                crack |= art.sd_capsule(SH, SW, px, py, nx, ny, 0.55) < 0
                if r.random() < 0.35:
                    bx, by = nx + r.choice([-1, 1]) * r.uniform(2, 5), ny + r.uniform(-1, 1)
                    crack |= art.sd_capsule(SH, SW, nx, ny, bx, by, 0.45) < 0
                px, py = nx, ny
            crack &= m
            art.put(c, crack, EMBER[1])
            art.put(g, crack, (255, 120, 40), 180 + 25 * (frame % 2))
            chip = m & (x > SW - 9) & (y < top + 4)
            c[chip] = 0
    else:
        v = 0.15 + (1 - shade) * 0.35
        art.put(c, m, art.ramp(v, CHIT))
        # a vein of ember breathing through the obsidian
        phase = frame / 4 * 2 * np.pi
        vein = m & (np.abs(y - (SH / 2 + arc * 0.8 + np.sin(x * 0.45 + phase) * 1.6)) < 0.6)
        art.put(c, vein, EMBER[2])
        art.put(g, vein, (255, 110, 30), int(150 + 70 * (0.5 + 0.5 * np.sin(phase))))
        rim = m & art.outline(~m)
        art.put(c, rim & (y < SH / 2), (90, 60, 50))
    sil = c[..., 3] > 0
    art.put(c, art.outline(sil), (2, 1, 3))
    return c, g


SHW, SHH = 13, 15


def ship(frame):
    c = art.new(SHH, SHW)
    g = art.new(SHH, SHW)
    x, y = art.grid(SHH, SHW)
    cx = SHW // 2
    # an arrowhead of light with swept wings and a dark canopy
    body = (np.abs(x - cx) <= (y - 1) * 0.45) & (y >= 1) & (y <= 10)
    wings = (np.abs(x - cx) <= (y - 4) * 0.9) & (y >= 6) & (y <= 11) & ~((y > 9) & (np.abs(x - cx) < 2))
    m = body | wings
    v = 0.95 - np.abs(x - cx) * 0.08 - y * 0.02
    art.put(c, m, art.ramp(v, GOLD[2:]))
    canopy = (np.abs(x - cx) <= 0.6) & (y >= 4) & (y <= 6)
    art.put(c, canopy, (30, 16, 10))
    tips = wings & (np.abs(x - cx) >= 4)
    art.put(c, tips, GOLD[6])
    art.put(g, tips, (255, 240, 200), 200)
    art.put(g, body, (255, 230, 160), 90)
    # engine: a flickering flame below
    fl = [3, 4, 2, 4][frame]
    flame = (np.abs(x - cx) <= 1 - (y - 11) * 0.25) & (y >= 11) & (y < 11 + fl)
    art.put(c, flame, EMBER[3])
    art.put(g, flame, (255, 150, 60), 230)
    sil = c[..., 3] > 0
    art.put(c, art.outline(sil), (2, 1, 3))
    return c, g


def build():
    rows_c, rows_g = [], []
    for kind in range(3):
        fr = [shell(kind, f) for f in range(4)]
        rows_c.append(np.concatenate([a for a, _ in fr], 1))
        rows_g.append(np.concatenate([b for _, b in fr], 1))
    art.save(np.concatenate(rows_c, 0), OUT + "HackShell.png")
    art.save(np.concatenate(rows_g, 0), OUT + "HackShell_Glow.png")
    sh = [ship(f) for f in range(4)]
    art.save(np.concatenate([a for a, _ in sh], 1), OUT + "HackShip.png")
    art.save(np.concatenate([b for _, b in sh], 1), OUT + "HackShip_Glow.png")


if __name__ == "__main__":
    build()
