"""Parry barrier: a curved plate of hexagonal glyph lattice (white, premultiplied; tinted in code)."""
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
W, H = 96, 192
im = Image.new("L", (W, H), 0)
d = ImageDraw.Draw(im)
# curved plate: points on an arc bulging to the right (the side facing the hit)
cx = -140
for ring in range(3):
    r = 200 + ring * 14
    pts = []
    for k in range(60):
        a = -0.42 + 0.84 * k / 59
        pts.append((cx + np.cos(a) * r, H / 2 + np.sin(a) * r))
    d.line(pts, fill=255 if ring != 1 else 150, width=3 if ring == 1 else 2)
# hex lattice between the rims
for row in range(-6, 7):
    for col in range(2):
        a = row * 0.065
        r = 206 + col * 14 + (row % 2) * 7
        x, y = cx + np.cos(a) * r, H / 2 + np.sin(a) * r
        hexp = [(x + 6 * np.cos(t), y + 6 * np.sin(t)) for t in np.linspace(0, 2 * np.pi, 7)]
        d.line(hexp, fill=190, width=1)
# glyph ticks
rng = np.random.default_rng(3)
for k in range(14):
    a = -0.4 + 0.8 * k / 13
    x, y = cx + np.cos(a) * 222, H / 2 + np.sin(a) * 222
    for j in range(3):
        if rng.random() < 0.7:
            d.rectangle([x - 2 + j * 2, y - 3, x - 1 + j * 2, y + 3 - rng.integers(0, 4)], fill=255)
glow = im.filter(ImageFilter.GaussianBlur(5))
a = np.clip(np.asarray(im, float) * 1.0 + np.asarray(glow, float) * 1.4, 0, 255)
# fade the ends of the plate
yy = np.arange(H)[:, None]
a *= np.clip(np.sin(np.pi * yy / H) * 1.6, 0, 1)
out = np.zeros((H, W, 4), np.uint8)
out[..., 0] = out[..., 1] = out[..., 2] = out[..., 3] = a.astype(np.uint8)
Image.fromarray(out, "RGBA").save("../Assets/Textures/VFX/BlockShield.png")
print("shield ok")
