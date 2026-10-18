"""Recursion's segment-volley needle: an obsidian thorn with a gold edge and an ember pulse running to its tip.
4 frames, 26 x 9 art px (stored x2). Writes Projectiles/VerseNeedle.png + _Glow."""
import numpy as np
import art
from centipede import CHITIN, GOLD, EMBER, OUTLINE, glow_put

NW, NH = 26, 9
OUT = "../Assets/Textures/Projectiles/"


def needle(frame):
    c = art.new(NH, NW)
    g = art.new(NH, NW)
    x, y = art.grid(NH, NW)
    cy = NH / 2 - 0.5
    half = np.clip((x - 1) / 24, 0, 1)
    w = 3.4 * (1 - half) ** 0.8 * np.clip(x / 3, 0, 1)
    m = np.abs(y - cy) <= w
    art.put(c, m, art.ramp(0.15 + 0.3 * np.clip(1 - np.abs(y - cy) / 3.5, 0, 1), CHITIN))
    edge = m & art.outline(~m) & (y < cy)
    art.put(c, edge, art.ramp(np.full((NH, NW), 0.7), GOLD))
    core = m & (np.abs(y - cy) < 0.8) & (x > 2)
    art.put(c, core, art.ramp(np.full((NH, NW), 0.6), EMBER))
    hot = core & (np.abs(x - (4 + frame * 5)) < 2.5)
    art.put(c, hot, art.ramp(np.full((NH, NW), 0.95), GOLD))
    glow_put(g, core, (255, 120, 40), 140)
    glow_put(g, hot, (255, 230, 170), 255)
    glow_put(g, m & (x > 21), (255, 240, 200), 200)
    art.put(c, art.outline(c[..., 3] > 0), OUTLINE)
    return c, g


if __name__ == "__main__":
    fr = [needle(i) for i in range(4)]
    art.save(art.sheet([f[0] for f in fr]), OUT + "VerseNeedle.png")
    art.save(art.sheet([f[1] for f in fr]), OUT + "VerseNeedle_Glow.png")
