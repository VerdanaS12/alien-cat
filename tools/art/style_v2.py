#!/usr/bin/env python3
"""Experimental art pass: richer face, tasselled tail and two wing styles.

Re-uses every pose from generate_sprites.py and only swaps the part painters,
so nothing in the shipped sprites changes. Writes comparison images to
build/art/v2/. Run from the repository root:  python3 tools/art/style_v2.py
"""
from __future__ import annotations

import math
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, str(Path(__file__).resolve().parent))
import generate_sprites as gs  # noqa: E402
from generate_sprites import hx, ellipsoid, tube, bezier, poly_mask, shade, rot  # noqa: E402

OUT = gs.ROOT / "build" / "art" / "v2"

# ------------------------------------------------------------- palette -----

EYE_RIM = hx("0f1238")
IRIS_TOP = hx("1d5f9e")
IRIS_MID = hx("23a9d6")
IRIS_LOW = hx("5fe6f2")
IRIS_GLOW = hx("b8fdff")
NOSE_LIGHT = hx("ffc0da")
NOSE = hx("ff7fb2")
NOSE_DARK = hx("d24f8c")
BLUSH = hx("ff8fc0", 120)
WHISKER = hx("d8eeff", 200)
GEM = [hx("4fd8f0"), hx("a8fbff"), hx("ffffff")]

TASSEL = [hx("6aa2ec"), hx("a6d6ff"), hx("d3f0ff"), hx("f6fdff")]
TASSEL_FAR = [hx("33509f"), hx("5c8fe6"), hx("98cdfb"), hx("cfeeff")]
TASSEL_TIP = hx("a6fbff")

MEM_DEEP = np.array([96, 80, 226, 205], np.float32)
MEM_PALE = np.array([196, 186, 255, 170], np.float32)
MEM_DEEP_FAR = np.array([70, 58, 176, 205], np.float32)
MEM_PALE_FAR = np.array([140, 128, 230, 175], np.float32)
MEM_RIM = hx("e2dcff", 220)
BONE = [hx("3b2c9c"), hx("5945cc"), hx("8875f0"), hx("c7bcff")]
BONE_FAR = [hx("2c2178"), hx("4434a8"), hx("6656d2"), hx("9a8cf0")]
WING_OUT = hx("2a2170")

FEATHER = [hx("7a7fe6"), hx("a3a9fb"), hx("cdd2ff"), hx("f2f4ff")]
FEATHER_FAR = [hx("5c60c4"), hx("8388e6"), hx("aeb3f6"), hx("d5d8ff")]
FEATHER_EDGE = hx("6a70d6")
FEATHER_OUT = hx("33378c")
FEATHER_TIP = hx("c9f6ff")


# ------------------------------------------------- post-outline overlays ---
# Thin details (whiskers, sparkles) must be drawn after the outline pass,
# otherwise they get a dark contour and look thick.

_orig_outline = gs.Canvas.outline


def _outline_then_post(self):
    _orig_outline(self)
    for x, y, col in getattr(self, "post", []):
        xi, yi = int(math.floor(x)), int(math.floor(y))
        if 0 <= xi < self.w and 0 <= yi < self.h:
            a = col[3] / 255.0
            if self.rgba[yi, xi, 3] == 0:
                self.rgba[yi, xi] = col
            else:
                self.rgba[yi, xi, :3] = np.array(col[:3]) * a + self.rgba[yi, xi, :3] * (1 - a)
                self.rgba[yi, xi, 3] = max(self.rgba[yi, xi, 3], col[3])


def post(c, x, y, col):
    if not hasattr(c, "post"):
        c.post = []
    c.post.append((x, y, col))


# ------------------------------------------------------------------ face ---


def eye_v2(c, ex, ey, rx, ry, state, look, outer):
    lx, ly = look
    if state == "closed":       # soft smiling-down lash curve + one lash
        for i in range(-3, 4):
            x = ex + i * rx / 3.2
            y = ey + 1.2 - 0.9 * (1 - (i / 3) ** 2)
            c.px(x, y, EYE_RIM)
        c.px(ex + outer * (rx + 0.6), ey + 0.2, EYE_RIM)
        return
    if state == "happy":        # thick ^ arc
        for i in range(-3, 4):
            x = ex + i * rx / 3.2
            y = ey + 1.4 - 2.2 * (1 - (i / 3) ** 2)
            c.px(x, y, EYE_RIM)
            c.px(x, y + 1, EYE_RIM)
        return

    m, *_ = ellipsoid(c, ex, ey, rx, ry)
    c.rgba[m] = EYE_RIM
    inner, *_ = ellipsoid(c, ex, ey + 0.3, rx - 0.75, ry - 0.8)
    inner &= m
    t = (c.Y - (ey - ry)) / (2 * ry)
    for th, col in ((-9, IRIS_TOP), (0.42, IRIS_MID), (0.66, IRIS_LOW), (0.84, IRIS_GLOW)):
        c.rgba[inner & (t >= th)] = col
    # pupil: big and round (cute), shifted by gaze
    pr = 0.5 if state == "wide" else 0.42
    pm, *_ = ellipsoid(c, ex + lx * 0.7, ey - 0.2 + ly * 0.5, rx * pr, ry * (pr + 0.12))
    c.rgba[pm & inner] = EYE_RIM
    # heavier upper lid + outer lash
    top = m & ~np.roll(m, 1, axis=0)
    ys, xs = np.nonzero(top)
    for y, x in zip(ys, xs):
        if abs(x + 0.5 - ex) < rx * 0.8 and y - 1 >= 0:
            c.rgba[y - 1, x] = EYE_RIM
            if c.part[y - 1, x] < 0:
                c.part[y - 1, x] = c.paint_dummy()
    c.px(ex + outer * (rx + 0.35), ey - ry * 0.62, EYE_RIM)
    # highlights: big soft one top-left, small one bottom-right, tiny sparkle
    hx0, hy0 = ex - rx * 0.42 + lx * 0.3, ey - ry * 0.5
    for dx, dy in ((0, 0), (1, 0), (0, 1), (1, 1)):
        c.px(hx0 + dx, hy0 + dy, gs.WHITE)
    c.px(ex + rx * 0.38, ey + ry * 0.32, gs.WHITE)
    c.px(ex + rx * 0.05, ey + ry * 0.55, hx("ffffff", 150))
    if state == "half":
        lid = m & (c.Y < ey + 0.3)
        c.rgba[lid] = gs.FUR[1]
        row = m & (np.abs(c.Y - (ey + 0.8)) < 0.5)
        c.rgba[row] = EYE_RIM


def face_v2(c, hx_, hy_, eyes, mouth, blush, look, tongue):
    big = eyes == "wide"
    eye_v2(c, hx_ - 3.6, hy_ + 0.6, 3.6 + big * 0.4, 4.4 + big * 0.4, eyes, look, outer=-1)
    eye_v2(c, hx_ + 6.6, hy_ + 0.1, 2.9 + big * 0.3, 4.0 + big * 0.3, eyes, look, outer=1)

    if blush:
        bm, *_ = ellipsoid(c, hx_ - 8.2, hy_ + 5.4, 2.3, 1.1)
        c.paint(bm & (c.rgba[..., 3] > 0), BLUSH)
        bm2, *_ = ellipsoid(c, hx_ + 11, hy_ + 4.9, 1.3, 0.9)
        c.paint(bm2 & (c.rgba[..., 3] > 0), BLUSH)

    # tiny forehead gem — the alien touch
    gx, gy = hx_ + 1.5, hy_ - 6.3
    c.px(gx, gy - 1, GEM[0])
    c.px(gx - 1, gy, GEM[0])
    c.px(gx + 1, gy, GEM[0])
    c.px(gx, gy + 1, GEM[0])
    c.px(gx, gy, GEM[2])

    # nose: little rounded triangle with a shine
    for dx in (0, 1, 2, 3):
        c.px(hx_ + 0.5 + dx, hy_ + 3.4, NOSE)
    c.px(hx_ + 1.5, hy_ + 4.4, NOSE)
    c.px(hx_ + 2.5, hy_ + 4.4, NOSE_DARK)
    c.px(hx_ + 1, hy_ + 3.4, NOSE_LIGHT)

    y = hy_ + 5.6
    if mouth == "w":
        c.px(hx_ + 2, y, gs.MOUTH)
        for dx, dy in ((-0.2, 0.9), (0.8, 1.6), (2, 0.9), (3.2, 1.6), (4.2, 0.9)):
            c.px(hx_ + dx, y + dy, gs.MOUTH)
        if tongue:
            c.px(hx_ + 2, y + 1.9, gs.TONGUE)
            c.px(hx_ + 2, y + 2.9, gs.TONGUE)
            c.px(hx_ + 3, y + 2.4, gs.TONGUE)
    elif mouth == "smile":
        for dx, dy in ((-0.6, 0.4), (0.4, 1.4), (1.5, 1.9), (2.5, 1.9), (3.6, 1.4), (4.6, 0.4)):
            c.px(hx_ + dx, y + dy, gs.MOUTH)
        c.px(hx_ + 1.5, y + 2.6, gs.TONGUE)
        c.px(hx_ + 2.5, y + 2.6, gs.TONGUE)
    else:
        gs_mouth(c, hx_, hy_, mouth, tongue)

    # whiskers (light, after outline so they stay 1px thin)
    for a, b in (((hx_ - 9.5, hy_ + 5.0), (hx_ - 15.5, hy_ + 3.8)),
                 ((hx_ - 9.5, hy_ + 6.4), (hx_ - 15, hy_ + 7.8)),
                 ((hx_ + 12.0, hy_ + 4.8), (hx_ + 16.5, hy_ + 3.9)),
                 ((hx_ + 12.0, hy_ + 6.1), (hx_ + 16, hy_ + 7.2))):
        steps = int(abs(b[0] - a[0])) + 1
        for s in range(steps + 1):
            t = s / steps
            post(c, a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, WHISKER)


def gs_mouth(c, hx_, hy_, mouth, tongue):
    """Re-use the original mouths for o/open/flat by drawing only the mouth part."""
    y = hy_ + 5.8
    if mouth == "o":
        m, *_ = ellipsoid(c, hx_ + 2, y + 1.4, 1.5, 1.7)
        c.rgba[m] = gs.MOUTH_IN
        c.px(hx_ + 2, y + 2, gs.TONGUE)
    elif mouth.startswith("open"):
        size = int(mouth[-1])
        m, *_ = ellipsoid(c, hx_ + 2, y + 1 + size * 0.5, 1.6 + size * 0.6, 0.9 + size * 0.9)
        c.rgba[m] = gs.MOUTH_IN
        t, *_ = ellipsoid(c, hx_ + 2, y + 1.6 + size, 1.2 + size * 0.4, 0.7 + size * 0.3)
        c.rgba[t & m] = gs.TONGUE
        if size >= 2:   # two tiny fangs
            c.px(hx_ + 2 - (1 + size * 0.5), y + 0.6, gs.WHITE)
            c.px(hx_ + 2 + (size * 0.5), y + 0.6, gs.WHITE)
    elif mouth == "flat":
        for dx in range(0, 4):
            c.px(hx_ + dx + 0.5, y + 0.9, gs.MOUTH)


# ------------------------------------------------------------------ tail ---


def tail_v2(c, root, ctrl, tip, glow=0.4, far=False):
    pts = bezier(root, ctrl, tip, 16)
    radii = [2.9 - 1.5 * (i / 16) for i in range(17)]
    m, nx, ny, nz = tube(c, pts, radii)
    c.paint(m, shade(nx, ny, nz, gs.FUR), edge=gs.EDGE_FUR)

    # direction at the tip and its normal
    (ax, ay), (bx, by) = pts[-4], pts[-1]
    d = np.array([bx - ax, by - ay])
    d /= np.linalg.norm(d) or 1
    n = np.array([-d[1], d[0]])
    tx, ty = tip

    # tassel: one soft flame-shaped tuft that curls a little at the end
    base = np.array([tx, ty]) - d * 0.8
    c1 = base + d * 5.0 + n * 0.5
    end = base + d * 10.0 + n * 1.8
    cl = bezier(tuple(base), tuple(c1), tuple(end), 12)
    prof = [2.0, 2.7, 3.2, 3.5, 3.5, 3.3, 3.0, 2.6, 2.1, 1.6, 1.1, 0.7, 0.35]
    tm, tnx, tny, tnz = tube(c, cl, prof)
    col = shade(tnx, tny, tnz, TASSEL)
    # strand texture: two darker grooves running toward the tip
    for side in (-1.3, 1.3):
        g = bezier(tuple(base + d * 2.5 + n * side), tuple(c1 + n * side * 0.8), tuple(end - d * 2.5 + n * side * 0.25), 10)
        for gx, gy in g:
            xi, yi = int(gx), int(gy)
            if 0 <= xi < c.w and 0 <= yi < c.h and tm[yi, xi]:
                col[yi, xi] = hx("7fb4f0")
    # glowing tip
    along = (c.X - base[0]) * d[0] + (c.Y - base[1]) * d[1]
    col[tm & (along > 7.6)] = TASSEL_TIP
    c.paint(tm, col, outline=gs.OUTLINE, edge=hx("4f7fd6"))
    # darker cuff where fur turns into the tuft
    cm, cnx, cny, cnz = ellipsoid(c, base[0], base[1], 1.9, 1.9)
    c.paint(cm, shade(cnx, cny, cnz, gs.FUR))

    # sparkle just past the tip
    sx, sy = end + d * 2.0 + n * 0.8
    post(c, sx, sy, gs.STAR)
    for ox, oy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        post(c, sx + ox, sy + oy, gs.STAR_DIM)


# ----------------------------------------------------------------- wings ---


def _scallop(a, b, pull_to, depth=0.32, n=4):
    mid = ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2)
    ctrl = (mid[0] + (pull_to[0] - mid[0]) * depth, mid[1] + (pull_to[1] - mid[1]) * depth)
    return bezier(a, ctrl, b, n)[1:-1]


MEM_ROOT, MEM_SHOULDER, MEM_WRIST, MEM_TOP = (0, 0), (-1.5, -5.5), (-6.5, -10.5), (-11.5, -15)
MEM_FINGERS = [(-16.5, -10), (-17.5, -4.5), (-12, 0.5)]
MEM_TRAIL = (-5, 2)


def wing_membrane(c, root, ang_deg, scale=1.0, far=False):
    ang = math.radians(ang_deg)
    W = lambda pts: rot(pts, ang, root, scale)   # noqa: E731
    wrist = W([MEM_WRIST])[0]
    edge = [MEM_TOP] + MEM_FINGERS + [MEM_TRAIL]
    poly = [MEM_ROOT, MEM_SHOULDER, MEM_WRIST, MEM_TOP]
    for a, b in zip(edge, edge[1:]):
        poly += _scallop(a, b, MEM_WRIST)
        poly.append(b)
    pts = W(poly)
    m = poly_mask(c, pts)

    # membrane: deep near the bones, pale and more see-through toward the edge
    dist = np.hypot(c.X - wrist[0], c.Y - wrist[1]) / (12 * scale)
    t = np.clip(dist, 0, 1)[..., None]
    deep, pale = (MEM_DEEP_FAR, MEM_PALE_FAR) if far else (MEM_DEEP, MEM_PALE)
    col = deep * (1 - t) + pale * t
    # rim highlight just inside the scalloped edge
    inside = m & np.roll(m, 1, 0) & np.roll(m, -1, 0) & np.roll(m, 1, 1) & np.roll(m, -1, 1)
    arm_zone = np.hypot(c.X - wrist[0], c.Y - wrist[1]) < 5 * scale
    rim = m & ~inside & ~arm_zone
    col[rim] = MEM_RIM if not far else hx("b2a8f0", 215)
    c.paint(m, col, outline=WING_OUT)

    # bones: arm (shoulder → wrist → top claw) and three fingers
    ramp = BONE_FAR if far else BONE
    arm = W([MEM_ROOT, MEM_SHOULDER, MEM_WRIST, MEM_TOP])
    am, anx, any_, anz = tube(c, arm, [1.5 * scale, 1.3 * scale, 1.1 * scale, 0.6 * scale])
    c.paint(am, shade(anx, any_, anz, ramp), outline=WING_OUT)
    for f in MEM_FINGERS:
        fw = W([MEM_WRIST, ((MEM_WRIST[0] + f[0]) / 2 + 0.6, (MEM_WRIST[1] + f[1]) / 2 - 0.4), f])
        fpts = bezier(fw[0], fw[1], fw[2], 8)
        fm, fnx, fny, fnz = tube(c, fpts, [0.8 * scale - 0.45 * scale * i / 8 for i in range(9)])
        c.paint(fm & m, shade(fnx, fny, fnz, ramp))
    # tiny claw and two star specks in the membrane
    top = W([MEM_TOP])[0]
    c.px(top[0], top[1], hx("f3edff"))


# feathered variant: rounded fairy feathers ---------------------------------

FEATHERS = [   # (base on the arm 0..1, direction degrees, length)
    (1.00, 190, 9.5), (0.74, 208, 9.0), (0.48, 228, 7.6), (0.22, 250, 5.8),
]
COVERTS = [(0.85, 205, 4.6), (0.50, 230, 4.0)]


def wing_feather(c, root, ang_deg, scale=1.0, far=False):
    ang = math.radians(ang_deg)
    arm_a, arm_b = (-0.5, -1.5), (-7.5, -11.5)
    ramp = FEATHER_FAR if far else FEATHER

    def feather(base_t, deg, length, r0):
        bx = arm_a[0] + (arm_b[0] - arm_a[0]) * base_t
        by = arm_a[1] + (arm_b[1] - arm_a[1]) * base_t
        d = math.radians(deg)
        end = (bx + math.cos(d) * length, by - math.sin(d) * length)
        mid = (bx + math.cos(d) * length * 0.5 - 0.6, by - math.sin(d) * length * 0.5 - 0.6)
        pts = rot([(bx, by), mid, end], ang, root, scale)
        sp = bezier(pts[0], pts[1], pts[2], 10)
        rr = [r0 * scale * (1 - 0.15 * i / 10) for i in range(11)]
        m, nx, ny, nz = tube(c, sp, rr)
        col = shade(nx, ny, nz, ramp)
        tip = np.hypot(c.X - pts[2][0], c.Y - pts[2][1]) < 1.6 * scale
        col[tip & m] = FEATHER_TIP if not far else hx("a9c8f2")
        c.paint(m, col, outline=FEATHER_OUT, edge=FEATHER_EDGE)

    for base_t, deg, length in FEATHERS:
        feather(base_t, deg, length, 2.3)
    for base_t, deg, length in COVERTS:
        feather(base_t, deg, length, 1.9)
    # leading edge
    arm = rot([arm_a, arm_b], ang, root, scale)
    am, anx, any_, anz = tube(c, arm, [1.5 * scale, 1.1 * scale])
    c.paint(am, shade(anx, any_, anz, ramp), outline=FEATHER_OUT)


# ------------------------------------------------------------- previews ----


def build(wing_fn=None, new_face=True, new_tail=True):
    """Builds every cat clip with the chosen painters; restores the originals afterwards."""
    saved = (gs.draw_wing, gs.draw_tail, gs.draw_face, gs.Canvas.outline)
    try:
        gs.Canvas.outline = _outline_then_post
        if wing_fn is not None:
            gs.draw_wing = wing_fn
        if new_tail:
            gs.draw_tail = tail_v2
        if new_face:
            gs.draw_face = face_v2
        clips, ms, _ = gs.build_cat_frames()
        return clips, ms
    finally:
        gs.draw_wing, gs.draw_tail, gs.draw_face, gs.Canvas.outline = saved


def font(size):
    try:
        return ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", size)
    except OSError:
        return ImageFont.load_default()


BG = (236, 240, 250, 255)
INK = (40, 50, 90, 255)


def comparison(rows, picks, scale, path):
    label_w = 210
    cell = 64 * scale
    img = Image.new("RGBA", (label_w + cell * len(picks), 40 + cell * len(rows)), BG)
    d = ImageDraw.Draw(img)
    for i, (clip, frame, title) in enumerate(picks):
        d.text((label_w + i * cell + 10, 10), title, fill=INK, font=font(18))
    for r, (name, clips) in enumerate(rows):
        d.text((14, 40 + r * cell + cell // 2 - 12), name, fill=INK, font=font(22))
        for i, (clip, frame, _) in enumerate(picks):
            fr = clips[clip][frame].resize((cell, cell), Image.NEAREST)
            img.alpha_composite(fr, (label_w + i * cell, 40 + r * cell))
    img.convert("RGB").save(path)


def portrait(clips, path, scale=10):
    fr = clips["sit"][0].crop((12, 2, 64, 54))
    big = fr.resize((fr.width * scale, fr.height * scale), Image.NEAREST)
    img = Image.new("RGBA", big.size, BG)
    img.alpha_composite(big)
    img.convert("RGB").save(path)


def gif(clips, ms, names, path, scale=5):
    frames, durations = [], []
    for name in names:
        for _ in range(3 if len(clips[name]) <= 4 else 2):
            for fr in clips[name]:
                img = Image.new("RGBA", (64 * scale + 40, 64 * scale + 40), BG)
                img.alpha_composite(fr.resize((64 * scale, 64 * scale), Image.NEAREST), (20, 28))
                frames.append(img.convert("RGB"))
                durations.append(ms[name])
    frames[0].save(path, save_all=True, append_images=frames[1:], duration=durations, loop=0, disposal=2)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    old, _ = build(wing_fn=None, new_face=False, new_tail=False)
    mem, mem_ms = build(wing_fn=wing_membrane)
    fea, fea_ms = build(wing_fn=wing_feather)

    picks = [("idle", 0, "стоит"), ("sit", 0, "сидит"), ("walk", 2, "идёт"), ("fly", 1, "летит"),
             ("purr", 0, "мурлычет"), ("sleep", 0, "спит")]
    comparison([("Было", old), ("Вариант A:\nкрылья\nдракончика", mem), ("Вариант B:\nкрылья\nс пёрышками", fea)],
               picks, 5, OUT / "compare.png")
    portrait(mem, OUT / "portrait_A.png")
    portrait(fea, OUT / "portrait_B.png")
    gif(mem, mem_ms, ["idle", "walk", "fly", "sit", "purr"], OUT / "anim_A.gif")
    gif(fea, fea_ms, ["idle", "walk", "fly", "sit", "purr"], OUT / "anim_B.gif")
    print("written to", OUT)


if __name__ == "__main__":
    main()
