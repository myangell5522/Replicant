"""Refrain's music stand: an ornate gold stand holding a blank sheet with five staff lines (the arrows are
drawn onto the sheet in code). Art 34 x 48 -> stored 68 x 96, plus a glow map."""
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage
import art

W, H = 34, 48
OUT = "../Assets/Textures/Projectiles/"
OUTLINE = (12, 8, 6)
GOLD = [(98, 50, 8), (160, 88, 12), (222, 142, 26), (255, 196, 70), (255, 236, 168)]
PAPER = [(150, 120, 86), (196, 170, 130), (226, 206, 168), (242, 230, 204)]


def mask(fn):
    im = Image.new("L", (W, H), 0)
    fn(ImageDraw.Draw(im))
    return np.asarray(im) > 127


rgb = np.zeros((H, W, 3))
a = np.zeros((H, W))
glow = np.zeros((H, W, 4))
gx, gy = np.meshgrid(np.arange(W), np.arange(H))
bay = art.bayer(H, W)


def paint(m, pal, cx=None):
    ys, xs = np.nonzero(m)
    if len(xs) == 0:
        return
    c = xs.mean() if cx is None else cx
    v = 0.6 - (gx - c) * 0.08 + (bay - 0.5) * 0.3
    idx = np.clip((v * (len(pal) - 0.01)).astype(int), 0, len(pal) - 1)
    rgb[m] = np.array(pal, float)[idx][m]
    a[m] = 255


cx = W // 2
# tripod legs and stem
legs = mask(lambda d: (d.line([(cx, 30), (cx - 9, 47)], fill=255, width=2), d.line([(cx, 30), (cx + 9, 47)], fill=255, width=2), d.line([(cx, 30), (cx, 47)], fill=255, width=1)))
paint(legs, GOLD[:4])
stem = mask(lambda d: d.line([(cx, 14), (cx, 31)], fill=255, width=2))
paint(stem, GOLD[1:])
knob = mask(lambda d: d.ellipse([cx - 2, 28, cx + 2, 32], fill=255))
paint(knob, GOLD[2:])
# the tray: a curved gold ledge with scrolls at each end
tray = mask(lambda d: d.polygon([(1, 14), (W - 2, 14), (W - 4, 17), (3, 17)], fill=255))
paint(tray, GOLD)
for sx in (1, W - 3):
    scroll = mask(lambda d: d.ellipse([sx - 1, 12, sx + 2, 16], fill=255))
    paint(scroll, GOLD[2:])
# the sheet (tilted back): parchment with a scorched edge
sheet = mask(lambda d: d.polygon([(3, 1), (W - 4, 0), (W - 3, 14), (2, 14)], fill=255))
paint(sheet, PAPER)
edge = sheet & ~ndimage.binary_erosion(sheet)
rgb[edge] = PAPER[0]
# five staff lines
for k in range(5):
    y = 3 + k * 2
    ln = mask(lambda d: d.line([(5, y), (W - 6, y - 1)], fill=255)) & sheet
    rgb[ln] = (120, 92, 60)
# a burning clef at the left
clef = [(5, 4), (5, 5), (6, 3), (6, 6), (5, 7), (6, 8), (5, 9), (6, 10)]
for (x, y) in clef:
    rgb[y, x] = GOLD[3]
    glow[y, x] = (*GOLD[4], 230)
# gold glints on the stand
for (x, y) in ((cx, 15), (cx - 6, 15), (cx + 6, 15), (cx, 29)):
    glow[y, x] = (*GOLD[3], 170)
sil = a > 0
ol = ndimage.binary_dilation(sil) & ~sil
rgb[ol] = OUTLINE
a[ol] = 255
out = np.zeros((H, W, 4))
out[..., :3] = rgb
out[..., 3] = a
art.save(out, OUT + "MusicStand.png")
art.save(glow, OUT + "MusicStand_Glow.png")
print("stand ok")
