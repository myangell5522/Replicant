"""Lily field v2: eight hand-shaped flower variants for the dimension, plus glow maps.

Each variant is drawn at 18 x 28 art pixels (stored 2x: 36 x 56), anchored bottom-centre.
Pale cream petals shaded toward amber, near-black stems, gold glyph veins and hearts that glow
through the ground mist. Variants:
 0 open lily      1 spider lily     2 glowing bud       3 twin lily
 4 drooping bell  5 glyph lily      6 wilted lily       7 blossom cluster
"""
import os
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage
import art

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Textures", "Tiles").replace("\\", "/") + "/"
W, H = 18, 28
CX = 9
OUTLINE = (10, 8, 6)
STEM = [(14, 16, 10), (28, 30, 18), (46, 46, 26)]
PETAL = [(150, 112, 70), (204, 170, 124), (234, 214, 180), (250, 242, 222)]
GOLD = [(160, 88, 12), (222, 142, 26), (255, 196, 70), (255, 236, 168)]
DEAD = [(60, 44, 34), (96, 72, 54), (130, 104, 80)]


class C:
    def __init__(self):
        self.rgb = np.zeros((H, W, 3))
        self.a = np.zeros((H, W))
        self.g = np.zeros((H, W, 4))

    def mask(self, fn):
        im = Image.new("L", (W, H), 0)
        fn(ImageDraw.Draw(im))
        return np.asarray(im) > 127

    def paint(self, m, col):
        self.rgb[m] = col
        self.a[m] = 255

    def px(self, x, y, col, glow=None, ga=200):
        x, y = int(round(x)), int(round(y))
        if 0 <= x < W and 0 <= y < H:
            self.rgb[y, x] = col
            self.a[y, x] = 255
            if glow is not None:
                self.g[y, x, :3] = glow
                self.g[y, x, 3] = ga

    def glow(self, m, col, a):
        self.g[m, :3] = col
        self.g[m, 3] = a

    def finish(self):
        sil = self.a > 0
        ol = ndimage.binary_dilation(sil) & ~sil
        self.rgb[ol] = OUTLINE
        self.a[ol] = 255
        out = np.zeros((H, W, 4))
        out[..., :3] = self.rgb
        out[..., 3] = self.a
        return out, self.g


def shade(c, m, pal, cx, cy, light=(-1, -1)):
    ys, xs = np.nonzero(m)
    if len(xs) == 0:
        return
    gx, gy = np.meshgrid(np.arange(W), np.arange(H))
    v = 0.6 - 0.12 * (gx - cx) * light[0] * -1 - 0.1 * (gy - cy) * light[1] * -1
    v = v + (art.bayer(H, W) - 0.5) * 0.25
    idx = np.clip((v * (len(pal) - 0.01)).astype(int), 0, len(pal) - 1)
    out = np.array(pal, float)[idx]
    c.rgb[m] = out[m]
    c.a[m] = 255


def stem(c, pts, w=1):
    m = c.mask(lambda d: d.line(pts, fill=255, width=w))
    c.paint(m, STEM[1])
    # leaf highlight on the left edge
    hl = m & ~ndimage.binary_erosion(m) & (np.roll(m, 1, axis=1) == False)
    c.rgb[hl] = STEM[2]


def leaf(c, x, y, side, size=5):
    m = c.mask(lambda d: d.polygon([(x, y), (x + side * size, y - size * 0.6), (x + side * size * 0.8, y - 1), (x + side * 1, y + 1)], fill=255))
    c.paint(m, STEM[0])
    c.px(x + side * size * 0.6, y - size * 0.4, STEM[2])


def trumpet(c, x, y, r=5, tilt=0.0, pal=PETAL, heart=True):
    """An open lily seen from the front-side: six petals around a gold heart."""
    for k in range(6):
        a = -np.pi / 2 + tilt + (k - 2.5) * 0.55
        tip = (x + np.cos(a) * r, y + np.sin(a) * r * 0.9)
        left = (x + np.cos(a - 0.35) * r * 0.45, y + np.sin(a - 0.35) * r * 0.45)
        right = (x + np.cos(a + 0.35) * r * 0.45, y + np.sin(a + 0.35) * r * 0.45)
        m = c.mask(lambda d: d.polygon([(x, y + 1), left, tip, right], fill=255))
        shade(c, m, pal, x, y - 2)
    # curled petal tips
    for k in (0, 5):
        a = -np.pi / 2 + tilt + (k - 2.5) * 0.55
        c.px(x + np.cos(a) * (r + 0.6), y + np.sin(a) * (r + 0.6) * 0.9 + 1, pal[1])
    if heart:
        for dx, dy in ((0, -1), (-1, -2), (1, -2), (0, -3)):
            c.px(x + dx, y + dy, GOLD[2] if dy < -1 else GOLD[1], glow=GOLD[3], ga=230)


def build():
    frames, glows = [], []
    rng = np.random.default_rng(4)
    for v in range(8):
        c = C()
        base = H - 1
        if v == 0:      # open lily
            stem(c, [(CX, base), (CX, base - 10), (CX + 1, base - 17)])
            leaf(c, CX, base - 5, -1)
            leaf(c, CX, base - 8, 1, 4)
            trumpet(c, CX + 1, base - 19, 6)
        elif v == 1:    # spider lily: long curled filaments
            stem(c, [(CX, base), (CX, base - 16)])
            leaf(c, CX, base - 4, 1, 4)
            hx, hy = CX, base - 18
            for k in range(7):
                a = -np.pi / 2 + (k - 3) * 0.42
                pts = []
                for j in range(7):
                    rr = j * 1.2
                    curl = (j / 6) ** 2 * 1.4 * np.sign(np.cos(a) + 1e-3)
                    pts.append((hx + np.cos(a) * rr + curl, hy + np.sin(a) * rr + (j / 6) ** 2 * 2.5))
                m = c.mask(lambda d: d.line(pts, fill=255, width=1))
                c.paint(m, PETAL[2] if k % 2 else PETAL[3])
                c.px(pts[-1][0], pts[-1][1], GOLD[2], glow=GOLD[3], ga=200)
            c.px(hx, hy, GOLD[3], glow=GOLD[3], ga=255)
        elif v == 2:    # bud, glowing from inside
            stem(c, [(CX, base), (CX - 1, base - 9), (CX, base - 14)])
            leaf(c, CX - 1, base - 6, -1, 5)
            m = c.mask(lambda d: d.polygon([(CX - 2, base - 14), (CX + 2, base - 14), (CX + 1, base - 21), (CX, base - 23), (CX - 1, base - 21)], fill=255))
            shade(c, m, PETAL, CX, base - 18)
            seam = c.mask(lambda d: d.line([(CX, base - 15), (CX, base - 22)], fill=255))
            c.paint(seam, GOLD[1])
            c.glow(seam | m & (art.bayer(H, W) > 0.7), GOLD[2], 150)
        elif v == 3:    # twin lily
            stem(c, [(CX, base), (CX, base - 9), (CX - 4, base - 15)])
            stem(c, [(CX, base - 9), (CX + 4, base - 19)])
            leaf(c, CX, base - 4, 1)
            trumpet(c, CX - 4, base - 16, 4, tilt=-0.3)
            trumpet(c, CX + 4, base - 21, 4, tilt=0.25)
        elif v == 4:    # drooping bell
            stem(c, [(CX - 2, base), (CX - 1, base - 14), (CX + 3, base - 19), (CX + 5, base - 16)])
            leaf(c, CX - 2, base - 6, -1)
            m = c.mask(lambda d: d.polygon([(CX + 3, base - 15), (CX + 7, base - 15), (CX + 8, base - 9), (CX + 5, base - 10), (CX + 2, base - 9)], fill=255))
            shade(c, m, PETAL, CX + 5, base - 13)
            c.px(CX + 5, base - 8, GOLD[2], glow=GOLD[3], ga=255)
            c.px(CX + 5, base - 9, GOLD[1], glow=GOLD[2], ga=180)
        elif v == 5:    # glyph lily: veins of script on the petals
            stem(c, [(CX, base), (CX + 1, base - 12), (CX, base - 18)])
            leaf(c, CX + 1, base - 7, 1, 5)
            leaf(c, CX, base - 3, -1, 4)
            trumpet(c, CX, base - 20, 7)
            for (dx, dy) in ((-3, -3), (-2, -5), (2, -4), (3, -2), (0, -6)):
                c.px(CX + dx, base - 20 + dy, GOLD[1], glow=GOLD[2], ga=170)
        elif v == 6:    # wilted lily, head bowed and dark
            stem(c, [(CX - 1, base), (CX, base - 12), (CX + 3, base - 16), (CX + 5, base - 13)])
            leaf(c, CX - 1, base - 5, -1)
            m = c.mask(lambda d: d.polygon([(CX + 3, base - 13), (CX + 7, base - 13), (CX + 6, base - 8), (CX + 4, base - 7), (CX + 3, base - 9)], fill=255))
            shade(c, m, DEAD, CX + 5, base - 10)
            c.px(CX + 4, base - 6, DEAD[0])
            c.px(CX + 5, base - 5, GOLD[0], glow=GOLD[1], ga=90)
        else:           # a cluster of small blossoms
            for (x0, h, s) in ((CX - 4, 11, 3), (CX + 1, 15, 3), (CX + 4, 9, 2), (CX - 1, 7, 2)):
                stem(c, [(CX, base), (x0, base - h)])
                trumpet(c, x0, base - h - 1, s, heart=True)
        f, g = c.finish()
        frames.append(f)
        glows.append(g)
    art.save(art.sheet(frames, vertical=False), OUT + "LilyField.png")
    art.save(art.sheet(glows, vertical=False), OUT + "LilyField_Glow.png")
    print("lily field", len(frames), W * 2, H * 2)


if __name__ == "__main__":
    build()
