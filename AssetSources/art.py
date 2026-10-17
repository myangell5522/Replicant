"""Shared pixel-art helpers for the Replicant boss assets.

Everything is authored at 1x "art pixel" resolution and upscaled 2x (nearest)
on save, matching how Terraria / Calamity textures are stored.
"""
import numpy as np
from PIL import Image
from scipy import ndimage

# ---------------------------------------------------------------- palettes
BLACK = np.array([
    (3, 2, 5), (8, 6, 11), (14, 11, 17), (22, 17, 25), (32, 25, 34), (45, 36, 44), (62, 51, 56),
], float)
SHEEN = np.array([  # olive/gold wet sheen on black goo
    (40, 33, 22), (66, 54, 30), (104, 84, 42), (150, 122, 60), (198, 168, 92),
], float)
GOLD = np.array([
    (52, 26, 6), (98, 50, 8), (160, 88, 12), (222, 142, 26), (255, 196, 70), (255, 236, 168), (255, 252, 230),
], float)
EMBER = np.array([(70, 10, 6), (140, 30, 8), (220, 80, 16), (255, 140, 40)], float)
BLUE = np.array([(10, 16, 40), (22, 44, 98), (40, 92, 172), (88, 160, 232), (170, 214, 255), (236, 248, 255)], float)
BONE = np.array([(84, 70, 44), (150, 130, 86), (210, 192, 140), (246, 236, 200)], float)
OUTLINE = (2, 1, 3)

BAYER4 = np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]], float) / 16.0 + 1 / 32.0


def bayer(h, w):
    return np.tile(BAYER4, (h // 4 + 1, w // 4 + 1))[:h, :w]


def ramp(values, palette, dither=True):
    """Map scalar field 0..1 -> palette colours using ordered dithering."""
    h, w = values.shape
    n = len(palette)
    v = np.clip(values, 0, 1) * (n - 1)
    lo = np.floor(v).astype(int)
    frac = v - lo
    if dither:
        lo = lo + (frac > bayer(h, w)).astype(int)
    else:
        lo = np.rint(v).astype(int)
    lo = np.clip(lo, 0, n - 1)
    return palette[lo]


def grid(h, w):
    y, x = np.mgrid[0:h, 0:w].astype(float)
    return x, y


# ---------------------------------------------------------------- noise
def value_noise(h, w, scale, seed=0, octaves=4, offset=(0.0, 0.0), periodic=None):
    rng = np.random.default_rng(seed)
    out = np.zeros((h, w))
    amp, tot = 1.0, 0.0
    x, y = grid(h, w)
    for o in range(octaves):
        s = scale / (2 ** o)
        gw = int(np.ceil(w / s)) + 3
        gh = int(np.ceil(h / s)) + 3
        if periodic:
            gw = max(2, int(round(periodic[0] / s)))
            gh = max(2, int(round(periodic[1] / s)))
        g = rng.random((gh, gw))
        fx = (x + offset[0] * (o + 1)) / s
        fy = (y + offset[1] * (o + 1)) / s
        x0 = np.floor(fx).astype(int)
        y0 = np.floor(fy).astype(int)
        tx = fx - x0
        ty = fy - y0
        tx = tx * tx * (3 - 2 * tx)
        ty = ty * ty * (3 - 2 * ty)
        x0m, y0m = x0 % gw, y0 % gh
        x1m, y1m = (x0 + 1) % gw, (y0 + 1) % gh
        a = g[y0m, x0m] * (1 - tx) + g[y0m, x1m] * tx
        b = g[y1m, x0m] * (1 - tx) + g[y1m, x1m] * tx
        out += (a * (1 - ty) + b * ty) * amp
        tot += amp
        amp *= 0.5
    return out / tot


# ---------------------------------------------------------------- shapes
def metaball(h, w, balls):
    """balls: list of (cx, cy, r). Returns field where >=1 is inside."""
    x, y = grid(h, w)
    f = np.zeros((h, w))
    for cx, cy, r in balls:
        d2 = (x - cx) ** 2 + (y - cy) ** 2 + 1e-3
        f += (r * r) / d2
    return f


def ellipse(h, w, cx, cy, rx, ry, rot=0.0):
    x, y = grid(h, w)
    c, s = np.cos(rot), np.sin(rot)
    dx, dy = x - cx, y - cy
    u = (dx * c + dy * s) / rx
    v = (-dx * s + dy * c) / ry
    return u * u + v * v


def outline(mask, thickness=1):
    st = ndimage.generate_binary_structure(2, 1 if thickness == 1 else 2)
    grown = ndimage.binary_dilation(mask, st, iterations=thickness)
    return grown & ~mask


def normals_from_height(hgt):
    gy, gx = np.gradient(hgt)
    nz = np.ones_like(hgt)
    n = np.stack([-gx, -gy, nz], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n


def shade(normals, light=(-0.55, -0.7, 0.45)):
    l = np.array(light, float)
    l /= np.linalg.norm(l)
    diff = np.clip((normals * l).sum(-1), 0, 1)
    # blinn specular with viewer (0,0,1)
    hv = l + np.array([0, 0, 1.0])
    hv /= np.linalg.norm(hv)
    spec = np.clip((normals * hv).sum(-1), 0, 1) ** 24
    return diff, spec


# ---------------------------------------------------------------- glyphs
def make_glyphs(count=32, gw=5, gh=7, seed=7):
    """Procedural blocky rune alphabet (meander / key-pattern style).
    Each glyph is a bool array gh x gw built from orthogonal strokes."""
    rng = np.random.default_rng(seed)
    glyphs = []
    tries = 0
    while len(glyphs) < count and tries < 5000:
        tries += 1
        g = np.zeros((gh, gw), bool)
        # start with a spine
        style = rng.integers(0, 4)
        if style == 0:
            g[:, rng.integers(1, gw - 1)] = True
        elif style == 1:
            g[0, :] = True
            g[:, 0] = True
        elif style == 2:
            g[gh // 2, :] = True
        else:
            g[:, gw - 1] = True
            g[gh - 1, :] = True
        # random walk strokes
        for _ in range(rng.integers(2, 5)):
            yy, xx = rng.integers(0, gh), rng.integers(0, gw)
            if rng.random() < 0.5:
                yy = rng.integers(0, gh // 2 + 1) * 2
                yy = min(yy, gh - 1)
            for _ in range(rng.integers(2, 6)):
                d = rng.integers(0, 4)
                ln = rng.integers(1, 4)
                for _ in range(ln):
                    if d == 0: xx += 1
                    elif d == 1: xx -= 1
                    elif d == 2: yy += 1
                    else: yy -= 1
                    xx = int(np.clip(xx, 0, gw - 1)); yy = int(np.clip(yy, 0, gh - 1))
                    g[yy, xx] = True
        dens = g.mean()
        if dens < 0.32 or dens > 0.6:
            continue
        # reject 2x2 solid blocks (keeps strokes 1px thin)
        blk = g[:-1, :-1] & g[1:, :-1] & g[:-1, 1:] & g[1:, 1:]
        if blk.sum() > 1:
            continue
        if any((g == o).all() for o in glyphs):
            continue
        glyphs.append(g)
    return glyphs


GLYPHS = make_glyphs(40)


def glyph_field(h, w, cell=(7, 9), seed=3, jitter=True, glyphs=None):
    """Tile glyphs over an area. Returns bool mask."""
    glyphs = glyphs or GLYPHS
    rng = np.random.default_rng(seed)
    m = np.zeros((h, w), bool)
    cw, ch = cell
    for gy in range(0, h, ch):
        off = rng.integers(0, cw) if jitter else 0
        for gx in range(-off, w, cw):
            g = glyphs[rng.integers(0, len(glyphs))]
            gh, gw = g.shape
            for yy in range(gh):
                for xx in range(gw):
                    if g[yy, xx]:
                        px, py = gx + xx, gy + yy
                        if 0 <= px < w and 0 <= py < h:
                            m[py, px] = True
    return m


# ---------------------------------------------------------------- canvas
def new(h, w):
    return np.zeros((h, w, 4), float)


def put(canvas, mask, rgb, alpha=255):
    rgb = np.asarray(rgb, float)
    if rgb.ndim == 1:
        canvas[mask, :3] = rgb
    else:
        canvas[mask, :3] = rgb[mask]
    canvas[mask, 3] = alpha if np.isscalar(alpha) else np.asarray(alpha)[mask]


def premultiply(canvas):
    """tModLoader packs PNGs as raw, NON-premultiplied data, while SpriteBatch blends as if
    premultiplied. Premultiply here so soft edges and additive glows render correctly."""
    out = canvas.copy()
    a = np.clip(out[..., 3:4], 0, 255) / 255.0
    out[..., :3] = out[..., :3] * a
    return out


def to_image(canvas, scale=2, premul=True):
    if premul:
        canvas = premultiply(canvas)
    arr = np.clip(canvas, 0, 255).astype(np.uint8)
    im = Image.fromarray(arr, "RGBA")
    if scale != 1:
        im = im.resize((im.width * scale, im.height * scale), Image.NEAREST)
    return im


def sheet(frames, vertical=True):
    if vertical:
        return np.concatenate(frames, 0)
    return np.concatenate(frames, 1)


def save(canvas, path, scale=2):
    import os
    os.makedirs(os.path.dirname(path), exist_ok=True)
    to_image(canvas, scale).save(path)


def soft_glow_texture(size, falloff=2.0, color=(255, 255, 255)):
    """Smooth (non-pixel) radial glow for additive bloom; stored at full res."""
    x, y = grid(size, size)
    c = (size - 1) / 2
    d = np.sqrt((x - c) ** 2 + (y - c) ** 2) / c
    a = np.clip(1 - d, 0, 1) ** falloff
    out = np.zeros((size, size, 4))
    out[..., 0], out[..., 1], out[..., 2] = color
    out[..., 3] = a * 255
    return out


def preview(path_in, path_out, bg=(40, 38, 52), scale=1):
    im = Image.open(path_in).convert("RGBA")
    b = Image.new("RGBA", im.size, bg + (255,))
    b.alpha_composite(im)
    if scale != 1:
        b = b.resize((im.width * scale, im.height * scale), Image.NEAREST)
    b.save(path_out)


# ---------------------------------------------------------------- SDF helpers
def sd_ellipse(h, w, cx, cy, rx, ry, rot=0.0):
    """Approximate signed distance (px) to an ellipse."""
    x, y = grid(h, w)
    c, s = np.cos(rot), np.sin(rot)
    dx, dy = x - cx, y - cy
    u = (dx * c + dy * s)
    v = (-dx * s + dy * c)
    k = np.sqrt((u / rx) ** 2 + (v / ry) ** 2) + 1e-6
    return (k - 1) * min(rx, ry)


def smin(a, b, k=6.0):
    hh = np.clip(0.5 + 0.5 * (b - a) / k, 0, 1)
    return b * (1 - hh) + a * hh - k * hh * (1 - hh)


def union(sds, k=6.0):
    d = sds[0]
    for s in sds[1:]:
        d = smin(d, s, k)
    return d


def sd_capsule(h, w, ax, ay, bx, by, r0, r1=None):
    r1 = r0 if r1 is None else r1
    x, y = grid(h, w)
    pax, pay = x - ax, y - ay
    bax, bay = bx - ax, by - ay
    t = np.clip((pax * bax + pay * bay) / (bax * bax + bay * bay + 1e-9), 0, 1)
    dx, dy = pax - bax * t, pay - bay * t
    return np.sqrt(dx * dx + dy * dy) - (r0 + (r1 - r0) * t)
