#!/usr/bin/env python3
"""Procedural pixel-art generator for the alien cat desktop pet.

Draws every frame at 64x64 from shaded primitives (ellipsoids, tubes, polygons),
then applies selective outlines. Output:
  src/AlienCat.App/Assets/cat.png, toys.png, fx.png, icon.ico, animations.json
  build/art/preview.png, build/art/preview.gif
Run from the repository root: python3 tools/art/generate_sprites.py
"""
from __future__ import annotations

import json
import math
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

# new_poses.py imports this module by name; make sure it gets *this* copy when run as a script
sys.modules.setdefault("generate_sprites", sys.modules[__name__])

ROOT = Path(__file__).resolve().parents[2]
OUTFIT = "none"   # none | astronaut | pilot | winter — set by main() for each sprite sheet
ASSETS = ROOT / "src" / "AlienCat.App" / "Assets"
PREVIEW = ROOT / "build" / "art"

# ---------------------------------------------------------------- palette ---


def hx(s: str, a: int = 255) -> tuple[int, int, int, int]:
    s = s.lstrip("#")
    return (int(s[0:2], 16), int(s[2:4], 16), int(s[4:6], 16), a)


OUTLINE = hx("1c2152")
FUR = [hx("2c4aa0"), hx("3d6fd6"), hx("5c9bf2"), hx("94cfff")]
FUR_FAR = [hx("223a85"), hx("2c4aa0"), hx("3d6fd6"), hx("5c9bf2")]
BELLY = [hx("5f98e6"), hx("86bdf5"), hx("b3dcff"), hx("ddf2ff")]
EDGE_FUR = hx("24357e")
EAR_IN = hx("f39bc6")
EAR_IN_DARK = hx("c96fa6")
HORN = [hx("6650c4"), hx("9278ff"), hx("c3b0ff"), hx("f3edff")]
HORN_OUT = hx("3a2a86")
GLOW = (127, 247, 255)
WING = hx("8f7dff", 175)
WING_LIGHT = hx("b7a9ff", 190)
WING_BONE = hx("6a55d8")
WING_OUT = hx("33287e")
EYE = hx("121635")
IRIS = hx("27c6e0")
IRIS_LIGHT = hx("8ff7ff")
WHITE = hx("ffffff")
NOSE = hx("ff78ae")
NOSE_DARK = hx("c94d86")
MOUTH = hx("1c2152")
BLUSH = hx("ff8fc0", 150)
TONGUE = hx("ff8fb5")
MOUTH_IN = hx("5a1f4a")
STAR = hx("c8fbff")
STAR_DIM = hx("8fe3ff")
PAPER = hx("fff3d2")
PAPER_DARK = hx("e5cf9c")
PAPER_OUT = hx("7a5a2e")
ALERT = hx("ff5d7a")

# v2 detail palette (face, tassel, dragon wings)
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

W = H = 64
LIGHT = np.array([-0.55, -0.68, 0.48])
LIGHT = LIGHT / np.linalg.norm(LIGHT)


# ---------------------------------------------------------------- canvas ----


class Canvas:
    def __init__(self, w: int = W, h: int = H):
        self.w, self.h = w, h
        self.rgba = np.zeros((h, w, 4), dtype=np.float32)
        self.part = np.full((h, w), -1, dtype=np.int32)
        self.outlines: list[tuple[int, int, int, int]] = []
        yy, xx = np.mgrid[0:h, 0:w]
        self.X = xx + 0.5
        self.Y = yy + 0.5

    # painting ---------------------------------------------------------------
    def paint(self, mask, colors, outline=OUTLINE, edge=None):
        """Paint `colors` (HxWx4 array or tuple) where mask; edge colours the
        part's border pixels that touch previously painted parts."""
        if not mask.any():
            return
        prev = self.rgba[..., 3] > 0
        pid = len(self.outlines)
        self.outlines.append(outline)
        if isinstance(colors, tuple):
            col = np.zeros((self.h, self.w, 4), dtype=np.float32)
            col[...] = colors
        else:
            col = colors.astype(np.float32)
        a = col[..., 3:4] / 255.0
        m = mask[..., None]
        blended = col[..., :3] * a + self.rgba[..., :3] * (1 - a)
        new_alpha = np.maximum(self.rgba[..., 3:4], col[..., 3:4])
        self.rgba[..., :3] = np.where(m, blended, self.rgba[..., :3])
        self.rgba[..., 3:4] = np.where(m, new_alpha, self.rgba[..., 3:4])
        self.part[mask] = pid
        if edge is not None:
            border = mask & ~self._all_neighbors(mask)
            touching = self._any_neighbor(prev & ~mask)
            e = border & touching
            self.rgba[e] = edge
        return pid

    def px(self, x: float, y: float, color):
        xi, yi = int(math.floor(x)), int(math.floor(y))
        if 0 <= xi < self.w and 0 <= yi < self.h:
            a = color[3] / 255.0
            if a >= 1 or self.rgba[yi, xi, 3] == 0:
                self.rgba[yi, xi] = color
            else:
                self.rgba[yi, xi, :3] = np.array(color[:3]) * a + self.rgba[yi, xi, :3] * (1 - a)
            if self.part[yi, xi] < 0:
                self.part[yi, xi] = self.paint_dummy()

    def paint_dummy(self) -> int:
        self.outlines.append(OUTLINE)
        return len(self.outlines) - 1

    @staticmethod
    def _shift(m, dx, dy):
        out = np.zeros_like(m)
        h, w = m.shape
        xs = slice(max(dx, 0), w + min(dx, 0))
        xd = slice(max(-dx, 0), w + min(-dx, 0))
        ys = slice(max(dy, 0), h + min(dy, 0))
        yd = slice(max(-dy, 0), h + min(-dy, 0))
        out[yd, xd] = m[ys, xs]
        return out

    def _any_neighbor(self, m):
        return (self._shift(m, 1, 0) | self._shift(m, -1, 0) |
                self._shift(m, 0, 1) | self._shift(m, 0, -1))

    def _all_neighbors(self, m):
        return (self._shift(m, 1, 0) & self._shift(m, -1, 0) &
                self._shift(m, 0, 1) & self._shift(m, 0, -1))

    # finishing ----------------------------------------------------------------
    def outline(self):
        filled = self.rgba[..., 3] > 0
        empty = ~filled
        result = self.rgba.copy()
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            nb_filled = self._shift(filled, dx, dy)
            nb_part = self._shift_int(self.part, dx, dy)
            target = empty & nb_filled & (result[..., 3] == 0)
            ys, xs = np.nonzero(target)
            for y, x in zip(ys, xs):
                pid = nb_part[y, x]
                result[y, x] = self.outlines[pid] if pid >= 0 else OUTLINE
        self.rgba = result
        # thin details (whiskers, sparkles) go on after the outline so they stay 1px
        for px_, py_, col in getattr(self, "post", []):
            xi, yi = int(math.floor(px_)), int(math.floor(py_))
            if 0 <= xi < self.w and 0 <= yi < self.h:
                a = col[3] / 255.0
                if self.rgba[yi, xi, 3] == 0:
                    self.rgba[yi, xi] = col
                else:
                    self.rgba[yi, xi, :3] = np.array(col[:3]) * a + self.rgba[yi, xi, :3] * (1 - a)
                    self.rgba[yi, xi, 3] = max(self.rgba[yi, xi, 3], col[3])

    @staticmethod
    def _shift_int(m, dx, dy):
        out = np.full_like(m, -1)
        h, w = m.shape
        xs = slice(max(dx, 0), w + min(dx, 0))
        xd = slice(max(-dx, 0), w + min(-dx, 0))
        ys = slice(max(dy, 0), h + min(dy, 0))
        yd = slice(max(-dy, 0), h + min(-dy, 0))
        out[yd, xd] = m[ys, xs]
        return out

    def glow(self, mask, strength: float, radius: float = 2.2):
        """Soft halo in empty pixels near mask."""
        if strength <= 0 or not mask.any():
            return
        ys, xs = np.nonzero(mask)
        dist = np.full((self.h, self.w), 99.0)
        for y, x in zip(ys, xs):
            d = np.hypot(self.X - (x + 0.5), self.Y - (y + 0.5))
            dist = np.minimum(dist, d)
        halo = (dist <= radius) & (self.rgba[..., 3] == 0)
        alpha = np.clip((1 - (dist - 1) / radius), 0, 1) * 150 * strength
        self.rgba[halo, :3] = GLOW
        self.rgba[halo, 3] = alpha[halo]

    def image(self, mirror: bool = False) -> Image.Image:
        img = Image.fromarray(np.clip(self.rgba, 0, 255).astype(np.uint8), "RGBA")
        return img.transpose(Image.FLIP_LEFT_RIGHT) if mirror else img


# ------------------------------------------------------------- primitives ---


def ellipsoid(c: Canvas, cx, cy, rx, ry, ang=0.0):
    dx, dy = c.X - cx, c.Y - cy
    ca, sa = math.cos(-ang), math.sin(-ang)
    u = (dx * ca - dy * sa) / rx
    v = (dx * sa + dy * ca) / ry
    d = u * u + v * v
    mask = d <= 1.0
    nz = np.sqrt(np.clip(1 - d, 0, 1))
    ca2, sa2 = math.cos(ang), math.sin(ang)
    nx = u * ca2 - v * sa2
    ny = u * sa2 + v * ca2
    return mask, nx, ny, nz


def tube(c: Canvas, pts, radii):
    best = np.full((c.h, c.w), 99.0)
    bx = np.zeros((c.h, c.w))
    by = np.zeros((c.h, c.w))
    for i in range(len(pts) - 1):
        (ax, ay), (bx2, by2) = pts[i], pts[i + 1]
        ra, rb = radii[i], radii[i + 1]
        vx, vy = bx2 - ax, by2 - ay
        ll = vx * vx + vy * vy or 1e-6
        t = np.clip(((c.X - ax) * vx + (c.Y - ay) * vy) / ll, 0, 1)
        qx, qy = ax + t * vx, ay + t * vy
        r = ra + (rb - ra) * t
        ox, oy = (c.X - qx) / r, (c.Y - qy) / r
        d = np.hypot(ox, oy)
        better = d < best
        best = np.where(better, d, best)
        bx = np.where(better, ox, bx)
        by = np.where(better, oy, by)
    mask = best <= 1.0
    nz = np.sqrt(np.clip(1 - best * best, 0, 1))
    return mask, bx, by, nz


def bezier(p0, p1, p2, n=14):
    out = []
    for i in range(n + 1):
        t = i / n
        x = (1 - t) ** 2 * p0[0] + 2 * (1 - t) * t * p1[0] + t * t * p2[0]
        y = (1 - t) ** 2 * p0[1] + 2 * (1 - t) * t * p1[1] + t * t * p2[1]
        out.append((x, y))
    return out


def poly_mask(c: Canvas, pts):
    img = Image.new("1", (c.w, c.h), 0)
    ImageDraw.Draw(img).polygon([(x, y) for x, y in pts], fill=1)
    return np.array(img, dtype=bool)


def shade(nx, ny, nz, ramp, belly=None, belly_from=0.45):
    i = nx * LIGHT[0] + ny * LIGHT[1] + nz * LIGHT[2]
    out = np.zeros(nx.shape + (4,), dtype=np.float32)
    levels = [(-9, ramp[0]), (0.02, ramp[1]), (0.42, ramp[2]), (0.80, ramp[3])]
    for th, col in levels:
        out[i >= th] = col
    if belly is not None:
        bm = ny > belly_from
        bi = np.where(bm, i, -9)
        for th, col in [(-9, belly[0]), (-0.05, belly[1]), (0.3, belly[2]), (0.7, belly[3])]:
            sel = bm & (bi >= th)
            out[sel] = col
    return out


def rot(pts, ang, origin=(0, 0), scale=1.0, flipx=False):
    ca, sa = math.cos(ang), math.sin(ang)
    ox, oy = origin
    res = []
    for x, y in pts:
        x *= scale
        y *= scale
        if flipx:
            x = -x
        res.append((ox + x * ca - y * sa, oy + x * sa + y * ca))
    return res


# ----------------------------------------------------------------- parts ----


def post(c, x, y, col):
    if not hasattr(c, "post"):
        c.post = []
    c.post.append((x, y, col))


def _scallop(a, b, pull_to, depth=0.32, n=4):
    mid = ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2)
    ctrl = (mid[0] + (pull_to[0] - mid[0]) * depth, mid[1] + (pull_to[1] - mid[1]) * depth)
    return bezier(a, ctrl, b, n)[1:-1]


MEM_ROOT, MEM_SHOULDER, MEM_WRIST, MEM_TOP = (0, 0), (-1.5, -5.5), (-6.5, -10.5), (-11.5, -15)
MEM_FINGERS = [(-16.5, -10), (-17.5, -4.5), (-12, 0.5)]
MEM_TRAIL = (-5, 2)


def draw_wing(c, root, ang_deg, scale=1.0, far=False):
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


def draw_tail(c, root, ctrl, tip, glow=0.4, far=False):
    pts = bezier(root, ctrl, tip, 16)
    radii = [2.9 - 1.5 * (i / 16) for i in range(17)]
    m, nx, ny, nz = tube(c, pts, radii)
    c.paint(m, shade(nx, ny, nz, FUR), edge=EDGE_FUR)

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
    c.paint(tm, col, outline=OUTLINE, edge=hx("4f7fd6"))
    # darker cuff where fur turns into the tuft
    cm, cnx, cny, cnz = ellipsoid(c, base[0], base[1], 1.9, 1.9)
    c.paint(cm, shade(cnx, cny, cnz, FUR))

    # sparkle just past the tip
    sx, sy = end + d * 2.0 + n * 0.8
    post(c, sx, sy, STAR)
    for ox, oy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        post(c, sx + ox, sy + oy, STAR_DIM)




def draw_leg(c: Canvas, hip, foot, far=False, paw=True, r=3.0):
    ramp = FUR_FAR if far else FUR
    m, nx, ny, nz = tube(c, [hip, foot], [r + 0.3, r])
    c.paint(m, shade(nx, ny, nz, ramp), edge=None if far else EDGE_FUR)
    if paw:
        fx, fy = foot
        m2, nx2, ny2, nz2 = ellipsoid(c, fx + 0.8, fy - 0.3, 3.4, 2.3)
        c.paint(m2, shade(nx2, ny2, nz2, BELLY if not far else FUR), edge=None)


def draw_body(c: Canvas, cx, cy, rx, ry, ang=0.0, stars=0):
    m, nx, ny, nz = ellipsoid(c, cx, cy, rx, ry, ang)
    c.paint(m, shade(nx, ny, nz, FUR, BELLY, 0.5))
    # alien constellation spots on the back
    spots = [(-5, -0.65), (0, -0.82), (5, -0.6), (-1.5, -0.45)]
    for k, (ox, oyf) in enumerate(spots):
        x = cx + ox * math.cos(ang) - oyf * ry * math.sin(ang)
        y = cy + oyf * ry * math.cos(ang) + ox * math.sin(ang)
        col = STAR if (k + stars) % 3 == 0 else STAR_DIM
        c.px(x, y, col)
        if (k + stars) % 3 == 0:
            c.px(x + 1, y, STAR_DIM)


def draw_ears(c: Canvas, hx_, hy_, mode="up"):
    if mode == "down":
        near = [(hx_ - 11, hy_ - 2), (hx_ - 4.5, hy_ - 8.5), (hx_ - 16, hy_ - 8)]
        far = [(hx_ + 5, hy_ - 8.5), (hx_ + 11.5, hy_ - 3), (hx_ + 15, hy_ - 9)]
    elif mode == "back":
        near = [(hx_ - 11.5, hy_ - 2.5), (hx_ - 4.5, hy_ - 8.5), (hx_ - 14.5, hy_ - 13)]
        far = [(hx_ + 5, hy_ - 9), (hx_ + 11.5, hy_ - 3.5), (hx_ + 9, hy_ - 15)]
    else:
        near = [(hx_ - 12, hy_ - 2), (hx_ - 4.5, hy_ - 8.5), (hx_ - 11.5, hy_ - 16.5)]
        far = [(hx_ + 5, hy_ - 9), (hx_ + 12, hy_ - 3), (hx_ + 11, hy_ - 16)]
    for tri, ramp_i in ((near, 2), (far, 1)):
        m = poly_mask(c, tri)
        c.paint(m, FUR[ramp_i])
        cx = sum(p[0] for p in tri) / 3
        cy = sum(p[1] for p in tri) / 3
        inner = [(cx + (x - cx) * 0.55, cy + (y - cy) * 0.55 + 0.6) for x, y in tri]
        c.paint(poly_mask(c, inner), EAR_IN if ramp_i == 2 else EAR_IN_DARK)


def draw_horns(c: Canvas, hx_, hy_, glow=0.0, tilt=0.0):
    horns = [((hx_ - 3, hy_ - 9.5), (hx_ - 3.5, hy_ - 14.5), (hx_ - 6.5 + tilt, hy_ - 16.5)),
             ((hx_ + 4.5, hy_ - 10), (hx_ + 5, hy_ - 15), (hx_ + 2.5 + tilt, hy_ - 17.5))]
    allmask = np.zeros((c.h, c.w), bool)
    for p0, p1, p2 in horns:
        pts = bezier(p0, p1, p2, 8)
        radii = [1.9 - 1.3 * (i / 8) for i in range(9)]
        m, nx, ny, nz = tube(c, pts, radii)
        col = shade(nx, ny, nz, HORN)
        if glow > 0.5:
            tipm = m & (np.hypot(c.X - p2[0], c.Y - p2[1]) < 2.2)
            col[tipm] = hx("dffffb")
        c.paint(m, col, outline=HORN_OUT)
        allmask |= m
    return allmask


def draw_head(c: Canvas, hx_, hy_, eyes="open", mouth="w", ears="up", blush=True,
              look=(0, 0), glow=0.0, tongue=False, squash=0.0):
    draw_ears(c, hx_, hy_, ears)
    horn_mask = draw_horns(c, hx_, hy_, glow)
    m, nx, ny, nz = ellipsoid(c, hx_, hy_, 12.5, 11 - squash)
    # cheek fluff tufts
    tufts = poly_mask(c, [(hx_ - 12, hy_ + 1.5), (hx_ - 14.5, hy_ + 6), (hx_ - 9.5, hy_ + 7)]) | \
        poly_mask(c, [(hx_ + 11.5, hy_ + 2.5), (hx_ + 14.5, hy_ + 6), (hx_ + 9.5, hy_ + 8)])
    col = shade(nx, ny, nz, FUR, BELLY, 0.55)
    col[tufts & ~m] = BELLY[1]
    c.paint(m | tufts, col, edge=EDGE_FUR)
    # muzzle: lighter patch
    mm, mnx, mny, mnz = ellipsoid(c, hx_ + 2, hy_ + 5.5, 5.2, 3.4)
    c.paint(mm & m, shade(mnx, mny, mnz, BELLY))
    draw_face(c, hx_, hy_, eyes, mouth, blush, look, tongue)
    from new_poses import draw_outfit
    draw_outfit(c, hx_, hy_, squash=squash)
    if glow > 0:
        c.glow(horn_mask, glow)


def draw_eye(c, ex, ey, rx, ry, state, look, outer):
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
        c.px(hx0 + dx, hy0 + dy, WHITE)
    c.px(ex + rx * 0.38, ey + ry * 0.32, WHITE)
    c.px(ex + rx * 0.05, ey + ry * 0.55, hx("ffffff", 150))
    if state == "half":
        lid = m & (c.Y < ey + 0.3)
        c.rgba[lid] = FUR[1]
        row = m & (np.abs(c.Y - (ey + 0.8)) < 0.5)
        c.rgba[row] = EYE_RIM


def draw_face(c, hx_, hy_, eyes, mouth, blush, look, tongue):
    big = eyes == "wide"
    draw_eye(c, hx_ - 3.6, hy_ + 0.6, 3.6 + big * 0.4, 4.4 + big * 0.4, eyes, look, outer=-1)
    draw_eye(c, hx_ + 6.6, hy_ + 0.1, 2.9 + big * 0.3, 4.0 + big * 0.3, eyes, look, outer=1)

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
        c.px(hx_ + 2, y, MOUTH)
        for dx, dy in ((-0.2, 0.9), (0.8, 1.6), (2, 0.9), (3.2, 1.6), (4.2, 0.9)):
            c.px(hx_ + dx, y + dy, MOUTH)
        if tongue:
            c.px(hx_ + 2, y + 1.9, TONGUE)
            c.px(hx_ + 2, y + 2.9, TONGUE)
            c.px(hx_ + 3, y + 2.4, TONGUE)
    elif mouth == "smile":
        for dx, dy in ((-0.6, 0.4), (0.4, 1.4), (1.5, 1.9), (2.5, 1.9), (3.6, 1.4), (4.6, 0.4)):
            c.px(hx_ + dx, y + dy, MOUTH)
        c.px(hx_ + 1.5, y + 2.6, TONGUE)
        c.px(hx_ + 2.5, y + 2.6, TONGUE)
    else:
        other_mouth(c, hx_, hy_, mouth, tongue)

    # whiskers (light, after outline so they stay 1px thin)
    for a, b in (((hx_ - 9.5, hy_ + 5.0), (hx_ - 15.5, hy_ + 3.8)),
                 ((hx_ - 9.5, hy_ + 6.4), (hx_ - 15, hy_ + 7.8)),
                 ((hx_ + 12.0, hy_ + 4.8), (hx_ + 16.5, hy_ + 3.9)),
                 ((hx_ + 12.0, hy_ + 6.1), (hx_ + 16, hy_ + 7.2))):
        steps = int(abs(b[0] - a[0])) + 1
        for s in range(steps + 1):
            t = s / steps
            post(c, a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, WHISKER)


def other_mouth(c, hx_, hy_, mouth, tongue):
    """Re-use the original mouths for o/open/flat by drawing only the mouth part."""
    y = hy_ + 5.8
    if mouth == "o":
        m, *_ = ellipsoid(c, hx_ + 2, y + 1.4, 1.5, 1.7)
        c.rgba[m] = MOUTH_IN
        c.px(hx_ + 2, y + 2, TONGUE)
    elif mouth.startswith("open"):
        size = int(mouth[-1])
        m, *_ = ellipsoid(c, hx_ + 2, y + 1 + size * 0.5, 1.6 + size * 0.6, 0.9 + size * 0.9)
        c.rgba[m] = MOUTH_IN
        t, *_ = ellipsoid(c, hx_ + 2, y + 1.6 + size, 1.2 + size * 0.4, 0.7 + size * 0.3)
        c.rgba[t & m] = TONGUE
        if size >= 2:   # two tiny fangs
            c.px(hx_ + 2 - (1 + size * 0.5), y + 0.6, WHITE)
            c.px(hx_ + 2 + (size * 0.5), y + 0.6, WHITE)
    elif mouth == "flat":
        for dx in range(0, 4):
            c.px(hx_ + dx + 0.5, y + 0.9, MOUTH)


def draw_sign(c: Canvas, x, y, w=15, h=11, pulse=0):
    m = (c.X >= x) & (c.X < x + w) & (c.Y >= y) & (c.Y < y + h)
    col = np.zeros((c.h, c.w, 4), np.float32)
    col[...] = PAPER
    col[m & (c.Y >= y + h - 2)] = PAPER_DARK
    c.paint(m, col, outline=PAPER_OUT)
    # rolled ends
    for yy in range(int(y), int(y + h)):
        c.px(x, yy, PAPER_DARK)
        c.px(x + w - 1, yy, PAPER_DARK)
    # exclamation mark
    cx = x + w // 2
    for yy in range(int(y + 2), int(y + h - 4)):
        c.px(cx, yy, ALERT)
    c.px(cx, y + h - 3, ALERT)
    if pulse:
        c.px(cx - 1, y + 3, ALERT)
        c.px(cx + 1, y + 3, ALERT)


def draw_question(c: Canvas, x, y):
    pts = [(0, 1), (1, 0), (2, 0), (3, 1), (3, 2), (2, 3), (1, 4), (1, 5), (1, 7)]
    for dx, dy in pts:
        c.px(x + dx, y + dy, STAR)


# ------------------------------------------------------------------ poses ---

GROUND = 59.5


def standing(f=0, n=8, walk=False, eyes="open", mouth="w", ears="up", tail_phase=0.0,
             wing=-32, body_dy=0.0, crouch=0.0, front_paw=None, look=(0, 0), stars=0,
             glow=0.0, squash=0.0, tongue=False, body_dx=0.0):
    c = Canvas()
    bx, by = 28 + body_dx, 47.5 + body_dy + crouch * 0.6
    rx, ry = 13.5 + squash, 9.5 - squash * 0.6 - crouch * 0.15
    hx_, hy_ = 43, 33 + body_dy + crouch * 1.2 + squash
    phase = f / n * 2 * math.pi
    hips = {"nb": (bx - 7.5, by + 3), "nf": (bx + 7, by + 3.5),
            "fb": (bx - 3.5, by + 2), "ff": (bx + 11, by + 2.5)}
    offs = {"nb": 0, "ff": 0, "nf": math.pi, "fb": math.pi}

    def foot(k):
        hxp, hyp = hips[k]
        if walk:
            p = phase + offs[k]
            return (hxp + 3.2 * math.sin(p), GROUND - 2.2 * max(0.0, math.cos(p)))
        return (hxp + (0.5 if k.endswith("f") else -0.5), GROUND)

    # far wing, tail, far legs
    draw_wing(c, (bx + 5, by - 7), wing + 8, 0.95, far=True)
    sw = math.sin(tail_phase) * 3
    draw_tail(c, (bx - rx + 2.5, by - 1.5), (bx - rx - 7, by - 3),
              (bx - rx - 5 + sw, by - 17 + abs(sw) * 0.3))
    for k in ("fb", "ff"):
        if front_paw and k == "ff":
            continue
        draw_leg(c, hips[k], foot(k), far=True)
    draw_body(c, bx, by, rx, ry, stars=stars)
    draw_wing(c, (bx + 3, by - 6.5), wing, 1.0)
    for k in ("nb", "nf"):
        if front_paw and k == "nf":
            draw_leg(c, hips[k], front_paw)
        else:
            draw_leg(c, hips[k], foot(k))
    draw_head(c, hx_, hy_, eyes, mouth, ears, look=look, glow=glow, tongue=tongue)
    return c


def sitting(eyes="open", mouth="w", ears="up", tail_phase=0.0, wing=-40, breathe=0,
            head=(0, 0), paw=None, both_paws=None, glow=0.0, look=(0, 0), stars=0,
            sign=False, pulse=0, dy=0.0, squash=0.0, tongue=False, question=False,
            wing_scale=1.0):
    c = Canvas()
    oy = dy
    bx, by = 31, 46.5 + oy + squash
    hx_, hy_ = 38 + head[0], 28 + head[1] + oy + squash * 1.5
    draw_wing(c, (bx + 2, by - 8), wing + 10, 0.95 * wing_scale, far=True)
    sw = math.sin(tail_phase) * 2
    draw_tail(c, (bx - 7, by + 9), (bx - 18, by + 14 - oy), (bx - 13 + sw, by + 2 - abs(sw)))
    # far front leg
    if not both_paws:
        draw_leg(c, (bx + 8, by + 1), (bx + 8.5, GROUND + oy), far=True)
    m, nx, ny, nz = ellipsoid(c, bx, by, 10, 12 - breathe * 0.5 - squash * 0.5, -0.18)
    c.paint(m, shade(nx, ny, nz, FUR, BELLY, 0.1))
    draw_body_stars(c, bx, by, stars)
    # haunch
    m2, nx2, ny2, nz2 = ellipsoid(c, bx - 3, by + 7.5, 8.5, 6.2 - squash * 0.3)
    c.paint(m2, shade(nx2, ny2, nz2, FUR), edge=EDGE_FUR)
    mp, nxp, nyp, nzp = ellipsoid(c, bx + 3.5, GROUND - 1 + oy, 3.4, 2.1)
    c.paint(mp, shade(nxp, nyp, nzp, BELLY))
    draw_wing(c, (bx - 1, by - 7.5), wing, 1.0 * wing_scale)
    if both_paws:
        for i, (px_, py_) in enumerate(both_paws):
            draw_leg(c, (bx + 4 + i * 3, by - 1), (px_, py_), far=(i == 1))
    elif paw:
        draw_leg(c, (bx + 5, by + 0.5), paw)
    else:
        draw_leg(c, (bx + 5, by + 3), (bx + 5.5, GROUND + oy))
    draw_head(c, hx_, hy_, eyes, mouth, ears, look=look, glow=glow, tongue=tongue)
    if sign:
        draw_sign(c, hx_ + 1, by + 1, pulse=pulse)
        for i, (px_, py_) in enumerate([(hx_ + 1, by + 6), (hx_ + 15, by + 6)]):
            mp2, a, b, d = ellipsoid(c, px_, py_, 2.4, 2.2)
            c.paint(mp2, shade(a, b, d, BELLY))
    if question:
        draw_question(c, hx_ + 10, hy_ - 21)
    return c


def draw_body_stars(c, bx, by, stars):
    for k, (ox, oy) in enumerate([(-5, -6), (-6.5, -1), (-4, 3)]):
        col = STAR if (k + stars) % 3 == 0 else STAR_DIM
        c.px(bx + ox, by + oy, col)


def lying(breathe=0, eyes="closed", tail_phase=0.0, ears="up"):
    c = Canvas()
    bx, by = 30, 52.5
    draw_wing(c, (bx + 2, by - 6), -62, 0.9, far=True)
    m, nx, ny, nz = ellipsoid(c, bx, by, 16, 7.5 + breathe * 0.5)
    c.paint(m, shade(nx, ny, nz, FUR, BELLY, 0.55))
    for k, (ox, oyf) in enumerate([(-6, -0.7), (0, -0.85), (6, -0.7)]):
        c.px(bx + ox, by + oyf * (7.5 + breathe * 0.5), STAR if k == 1 else STAR_DIM)
    draw_wing(c, (bx + 1, by - 5.5), -66, 0.95)
    sw = math.sin(tail_phase) * 1.2
    draw_tail(c, (bx - 15, by + 2), (bx - 12, by + 9.5), (bx + 8 + sw, by + 7))
    for px_ in (bx + 16, bx + 19):
        mp, a, b, d = ellipsoid(c, px_, GROUND - 1, 3.2, 2.1)
        c.paint(mp, shade(a, b, d, BELLY))
    draw_head(c, 44, 48 + breathe * 0.5, eyes, "w" if eyes != "closed" else "flat", ears,
              squash=0.8)
    return c


def hanging(f=0, n=4, wing_up=False, eyes="wide", mouth="o", flap=0):
    c = Canvas()
    sway = math.sin(f / n * 2 * math.pi) * 1.5
    bx, by = 32, 36
    hx_, hy_ = 33, 19
    wang = (35 if flap % 2 == 0 else -5) if wing_up else 10
    draw_wing(c, (bx + 2, by - 7), wang + 10, 1.05, far=True)
    draw_tail(c, (bx - 2, by + 10), (bx - 9, by + 16), (bx - 6 + sway, by + 24))
    draw_leg(c, (bx + 3, by + 9), (bx + 4 + sway, by + 21), far=True)
    draw_leg(c, (bx + 5, by - 3), (bx + 8 + sway * 0.6, by + 8), far=True)
    m, nx, ny, nz = ellipsoid(c, bx, by, 8.5, 12)
    c.paint(m, shade(nx, ny, nz, FUR, BELLY, 0.0))
    draw_body_stars(c, bx + 2, by - 2, f)
    draw_wing(c, (bx - 2, by - 7), wang, 1.05)
    draw_leg(c, (bx - 2, by + 9), (bx - 2 + sway, by + 22))
    draw_leg(c, (bx + 2, by - 3), (bx + 5 + sway * 0.6, by + 9))
    draw_head(c, hx_, hy_, eyes, mouth, "up")
    return c


def stretching(amount=1.0):
    c = Canvas()
    bx, by = 26, 46 + 2 * amount
    ang = 0.22 * amount
    hx_, hy_ = 45 + 2 * amount, 33 + 9 * amount
    draw_wing(c, (bx + 4, by - 8), -20 + 25 * amount, 1.0, far=True)
    draw_tail(c, (bx - 12, by - 4 * amount), (bx - 18, by - 12), (bx - 12, by - 22 * (0.6 + 0.4 * amount)))
    draw_leg(c, (bx - 6, by + 1 - 3 * amount), (bx - 6, GROUND), far=True)
    draw_leg(c, (bx + 10, by + 4), (bx + 18 + 6 * amount, GROUND), far=True)
    m, nx, ny, nz = ellipsoid(c, bx, by, 14, 8.5 - amount, ang)
    c.paint(m, shade(nx, ny, nz, FUR, BELLY, 0.42))
    draw_wing(c, (bx + 2, by - 7), -25 + 30 * amount, 1.05)
    draw_leg(c, (bx - 9, by - 2 * amount), (bx - 9, GROUND))
    draw_leg(c, (bx + 8, by + 4), (bx + 16 + 8 * amount, GROUND))
    draw_head(c, hx_, hy_, "closed" if amount > 0.4 else "open", "w", "back" if amount > 0.4 else "up")
    return c


def flying(f, n=6):
    angs = [42, 22, -2, -28, -2, 22]
    a = angs[f % n]
    bob = [-1, 0, 1, 1, 0, -1][f % n]
    c = Canvas()
    bx, by = 28, 40 + bob
    draw_wing(c, (bx + 5, by - 7), a + 8, 1.35, far=True)
    draw_tail(c, (bx - 12, by - 1), (bx - 20, by + 2), (bx - 23, by - 8 + bob))
    for hip, ft in (((bx - 5.5, by + 1), (bx - 10, by + 9)), ((bx + 10.5, by + 2), (bx + 12, by + 10))):
        draw_leg(c, hip, ft, far=True)
    m, nx, ny, nz = ellipsoid(c, bx, by, 14, 8.5)
    c.paint(m, shade(nx, ny, nz, FUR, BELLY, 0.42))
    draw_body_stars(c, bx + 6, by + 1, f)
    draw_wing(c, (bx + 3, by - 6.5), a, 1.4)
    draw_leg(c, (bx - 8, by + 2), (bx - 13, by + 9))
    draw_leg(c, (bx + 8, by + 2.5), (bx + 10, by + 10))
    draw_head(c, 44, 28 + bob, "open", "smile", "back", look=(1, 0))
    return c


# -------------------------------------------------------------- clips ------

def build_cat_frames():
    clips: dict[str, list[Image.Image]] = {}
    ms: dict[str, int] = {}
    loop: dict[str, bool] = {}

    def add(name, frames, frame_ms, looped=True):
        clips[name] = frames
        ms[name] = frame_ms
        loop[name] = looped

    def fin(c, mirror=False):
        c.outline()
        if getattr(c, "symmetric", False):   # front view: right half mirrors the left
            c.rgba[:, 32:] = c.rgba[:, :32][:, ::-1]
        img = c.image(mirror)
        if getattr(c, "vflip", False):       # upside-down poses
            img = img.transpose(Image.FLIP_TOP_BOTTOM)
        return img

    # idle: breathing, blink on frame 5, tail swish, twinkling stars
    idle = []
    for f in range(8):
        eyes = "half" if f == 5 else ("closed" if f == 6 else "open")
        idle.append(fin(standing(f, 8, eyes=eyes, tail_phase=f / 8 * 2 * math.pi,
                                 body_dy=0.5 if f in (2, 3, 4) else 0, stars=f // 2,
                                 wing=-28 if f == 3 else -32)))
    add("idle", idle, 140)

    add("walk", [fin(standing(f, 8, walk=True, tail_phase=f / 8 * 2 * math.pi,
                              body_dy=0.6 * abs(math.sin(f / 8 * 2 * math.pi)), stars=f // 2,
                              wing=-30 + 4 * math.sin(f / 8 * 4 * math.pi)))
                 for f in range(8)], 95)

    add("sit", [fin(sitting(eyes="closed" if f == 3 else "open", tail_phase=f / 4 * 2 * math.pi,
                            breathe=f % 2, stars=f)) for f in range(4)], 220)

    sad = []
    for f in range(4):
        sad.append(fin(sitting(eyes="half", mouth="flat", ears="down", wing=-70, breathe=f % 2,
                               look=(0, 1), tail_phase=0, head=(0, 1))))
    add("sad", sad, 260)

    groom = []
    paws = [(44, 38), (45, 36), (44, 37), (45, 35), (44, 37), (43, 41)]
    for f in range(6):
        groom.append(fin(sitting(eyes="closed", mouth="w", paw=paws[f], head=(0, 1),
                                 tongue=f in (1, 3), tail_phase=f)))
    add("groom", groom, 150)

    add("stretch", [fin(stretching(a)) for a in (0.0, 0.45, 0.85, 1.0, 0.6, 0.15)], 260, False)

    yawn = []
    for f, (eyes, mouth, ears) in enumerate([("half", "w", "up"), ("closed", "open1", "back"),
                                             ("closed", "open2", "back"), ("closed", "open3", "back"),
                                             ("closed", "open2", "back"), ("half", "w", "up")]):
        yawn.append(fin(sitting(eyes=eyes, mouth=mouth, ears=ears, head=(0, -1 if f in (2, 3) else 0))))
    add("yawn", yawn, 200, False)

    add("sleep", [fin(lying(breathe=f % 2, tail_phase=f)) for f in range(4)], 600)

    add("fly", [fin(flying(f)) for f in range(6)], 80)

    add("dragged", [fin(hanging(f, 4)) for f in range(4)], 160)
    add("fall", [fin(hanging(f, 2, wing_up=True, flap=f, eyes="wide", mouth="o")) for f in range(2)], 90)
    add("land", [fin(standing(0, squash=2.2, eyes="closed", wing=10)),
                 fin(standing(0, squash=1.0, wing=-10)),
                 fin(standing(0))], 110, False)

    add("purr", [fin(sitting(eyes="happy", mouth="w", tail_phase=f * 1.6, head=(f % 2 * 0.6, 0),
                             breathe=f % 2)) for f in range(4)], 220)

    remind = []
    for f, g in enumerate([0.35, 0.7, 1.0, 1.0, 0.7, 0.35]):
        remind.append(fin(sitting(eyes="open", mouth="smile", sign=True, glow=g, pulse=f % 2,
                                  wing=-25 + (f % 2) * 6, both_paws=[(41, 52), (52, 52)],
                                  look=(1, 0), stars=f)))
    add("remind", remind, 130)
    nag = []
    for f, (g, dy) in enumerate([(1.0, 0), (1.0, -3), (1.0, -5), (1.0, -3)]):
        nag.append(fin(sitting(eyes="wide", mouth="open1", sign=True, glow=g, pulse=1, dy=dy,
                               wing=10 if dy else -20, both_paws=[(41, 52 + dy), (52, 52 + dy)])))
    add("remind_nag", nag, 110)

    happy = []
    seq = [(0, False, 0.0, -30), (0, False, 1.5, -10), (-4, False, 0, 30), (-7, True, 0, 40),
           (-7, True, 0, 5), (-4, False, 0, 30), (0, False, 1.5, -10), (0, False, 0, -30)]
    for f, (dy, mir, sq, wing) in enumerate(seq):
        c = sitting(eyes="happy", mouth="smile", dy=dy, squash=sq, wing=wing, stars=f)
        happy.append(fin(c, mir))
    add("happy", happy, 100, False)

    yarn = []
    yarn_paws = [None, (46, 55), (50, 51), (52, 56), None, (47, 54), (51, 50), (53, 56)]
    for f in range(8):
        yarn.append(fin(standing(f, 8, crouch=2, eyes="wide", front_paw=yarn_paws[f],
                                 tail_phase=f * 0.9, look=(1, 1), wing=-28)))
    add("yarn_play", yarn, 120)

    crouch = []
    for f in range(4):
        crouch.append(fin(standing(f, 4, crouch=4, eyes="wide", ears="back",
                                   tail_phase=f * 1.7, body_dx=(-0.6 if f % 2 else 0.6),
                                   look=(1, 0))))
    add("hunt_crouch", crouch, 130)

    pounce = []
    for f, (dy, paw) in enumerate([(4, None), (-1, (54, 44)), (-8, (56, 38)), (-6, (55, 42)),
                                   (0, (52, 58)), (3, None)]):
        pounce.append(fin(standing(f, 6, crouch=max(dy, 0), body_dy=min(dy, 0), eyes="wide",
                                   front_paw=paw, wing=20 if dy < 0 else -30, look=(1, 0),
                                   mouth="o" if dy < -4 else "w")))
    add("hunt_pounce", pounce, 90, False)

    add("hunt_puzzled", [fin(sitting(eyes="open", mouth="flat", head=(-1, 1), look=(1, -1),
                                     question=f % 2 == 0, ears="back" if f >= 2 else "up"))
                         for f in range(4)], 300)

    # --- tricks -----------------------------------------------------------
    from new_poses import front, web_hang
    add("pounce_leap", [fin(front("leap", f)) for f in range(2)], 120)
    add("pounce_cling", [fin(front("cling", f)) for f in range(2)], 200)
    add("pounce_slide", [fin(front("slide", f)) for f in range(4)], 150)
    add("web_hang", [fin(web_hang(f, 4)) for f in range(4)], 200)
    add("carry", [fin(standing(f, 8, walk=True, mouth="o", tail_phase=f / 8 * 2 * math.pi,
                               body_dy=0.6 * abs(math.sin(f / 8 * 2 * math.pi)), stars=f // 2,
                               wing=-24 + 4 * math.sin(f / 8 * 4 * math.pi), look=(1, 0)))
                  for f in range(8)], 80)

    return clips, ms, loop


# ------------------------------------------------------------- toys & fx ----


def toy_frames():
    clips, ms = {}, {}
    # yarn ball 24x24, rolling
    yarn = []
    for f in range(8):
        c = Canvas(24, 24)
        m, nx, ny, nz = ellipsoid(c, 12, 13, 8.5, 8.5)
        col = shade(nx, ny, nz, [hx("b8417f"), hx("e0609f"), hx("f68fbf"), hx("ffc2dc")])
        ang = f / 8 * math.pi
        for k in range(-3, 4):
            # wound threads: parallel slanted lines rotating with the roll
            d = (c.X - 12) * math.cos(ang) + (c.Y - 13) * math.sin(ang) - k * 2.6
            col[m & (np.abs(d) < 0.45)] = hx("9c2f69")
        c.paint(m, col, outline=hx("5e1c43"))
        # loose thread
        th = bezier((19, 18), (22, 23 - f % 2), (24, 21), 6)
        for x, y in th:
            c.px(x, y, hx("e0609f"))
        c.outline()
        yarn.append(c.image())
    clips["yarn_roll"], ms["yarn_roll"] = yarn, 90

    butterfly = []
    for f in range(4):
        c = Canvas(24, 24)
        spread = [1.0, 0.6, 0.15, 0.6][f]
        for side in (-1, 1):
            up = [(12, 12), (12 + side * 7 * spread, 5), (12 + side * 9 * spread, 9), (12 + side * 2, 13)]
            lo = [(12, 13), (12 + side * 6 * spread, 18), (12 + side * 3 * spread, 19), (12, 15)]
            c.paint(poly_mask(c, up), hx("7ff3ff"), outline=hx("2b5f9e"))
            c.paint(poly_mask(c, lo), hx("ff9ad1"), outline=hx("8b3a75"))
            if spread > 0.3:
                c.px(12 + side * 5 * spread, 8.5, WHITE)
        body = tube(c, [(12, 9), (12, 17)], [1.2, 0.9])
        c.paint(body[0], hx("3a2f86"), outline=hx("1c1550"))
        c.px(11, 7, hx("3a2f86"))
        c.px(13, 7, hx("3a2f86"))
        c.outline()
        butterfly.append(c.image())
    clips["butterfly_fly"], ms["butterfly_fly"] = butterfly, 90

    mouse = []
    for f in range(4):
        c = Canvas(24, 24)
        by = 16 + (f % 2) * 0.5
        # string tail
        for x, y in bezier((5, by + 1), (1, by - 3 + f % 2 * 2), (0, by + 2), 8):
            c.px(x, y, hx("e48aa8"))
        m, nx, ny, nz = ellipsoid(c, 12, by, 7, 4.5, -0.15)
        c.paint(m, shade(nx, ny, nz, [hx("7e8aa6"), hx("a7b2c9"), hx("cfd7e6"), hx("eef2f8")]),
                outline=hx("3c4460"))
        for ex in (15, 13):
            em, a, b, d = ellipsoid(c, ex, by - 4, 2.2, 2.2)
            c.paint(em, hx("f2a9c5"), outline=hx("3c4460"))
        c.px(17, by - 1, EYE)
        c.px(19.5, by + 0.5, NOSE)
        for k, lx in enumerate((9, 14)):
            off = 1 if (f + k) % 2 else -1
            c.px(lx + off * 0.5, by + 4.5, hx("3c4460"))
        c.outline()
        mouse.append(c.image())
    clips["toy_mouse_run"], ms["toy_mouse_run"] = mouse, 80
    return clips, ms


def fx_frames():
    clips, ms = {}, {}

    def heart(c, x, y, a=255):
        pts = [(1, 0), (2, 0), (4, 0), (5, 0), (0, 1), (1, 1), (2, 1), (3, 1), (4, 1), (5, 1), (6, 1),
               (0, 2), (1, 2), (2, 2), (3, 2), (4, 2), (5, 2), (6, 2), (1, 3), (2, 3), (3, 3), (4, 3), (5, 3),
               (2, 4), (3, 4), (4, 4), (3, 5)]
        for dx, dy in pts:
            c.px(x + dx, y + dy, hx("ff6fa8", a))
        c.px(x + 1, y + 1, hx("ffd0e3", a))

    hearts = []
    for f in range(4):
        c = Canvas(32, 32)
        heart(c, 6, 22 - f * 5, 255 - f * 40)
        heart(c, 18, 26 - f * 5 - 3, 255 - f * 50)
        c.outline()
        hearts.append(c.image())
    clips["hearts"], ms["hearts"] = hearts, 180

    def z(c, x, y, s):
        for i in range(s):
            c.px(x + i, y, hx("dff3ff"))
            c.px(x + i, y + s - 1, hx("dff3ff"))
            c.px(x + s - 1 - i, y + i, hx("dff3ff"))

    zzz = []
    for f in range(4):
        c = Canvas(32, 32)
        z(c, 4 + f, 24 - f * 3, 4)
        if f >= 1:
            z(c, 12 + f, 16 - f * 2, 5)
        if f >= 2:
            z(c, 20, 6 + (3 - f) * 2, 6)
        c.outline()
        zzz.append(c.image())
    clips["zzz"], ms["zzz"] = zzz, 450

    sparkles = []
    spots = [(6, 8), (24, 6), (16, 20), (4, 24), (26, 22)]
    for f in range(4):
        c = Canvas(32, 32)
        for k, (x, y) in enumerate(spots):
            size = (f + k) % 3
            c.px(x, y, STAR)
            for i in range(1, size + 1):
                col = STAR if i == 1 else STAR_DIM
                for dx, dy in ((i, 0), (-i, 0), (0, i), (0, -i)):
                    c.px(x + dx, y + dy, col)
        sparkles.append(c.image())
    clips["sparkles"], ms["sparkles"] = sparkles, 110

    from new_poses import puff_frames
    clips["puff"], ms["puff"] = puff_frames(), 110
    return clips, ms


# ------------------------------------------------------------------ output --


def pack(clips: dict[str, list[Image.Image]], cell: int, columns: int):
    total = sum(len(v) for v in clips.values())
    rows = math.ceil(total / columns)
    sheet = Image.new("RGBA", (columns * cell, rows * cell), (0, 0, 0, 0))
    index: dict[str, list[int]] = {}
    i = 0
    for name, frames in clips.items():
        index[name] = []
        for fr in frames:
            x, y = (i % columns) * cell, (i // columns) * cell
            ox = (cell - fr.width) // 2
            oy = (cell - fr.height) // 2
            sheet.paste(fr, (x + ox, y + oy))
            index[name].append(i)
            i += 1
    return sheet, index


def main():
    ASSETS.mkdir(parents=True, exist_ok=True)
    PREVIEW.mkdir(parents=True, exist_ok=True)
    global OUTFIT
    outfit_sheets = {}
    for outfit in ("astronaut", "pilot", "winter"):
        OUTFIT = outfit
        outfit_sheets[outfit], _, _ = build_cat_frames()
    OUTFIT = "none"
    cat, cat_ms, cat_loop = build_cat_frames()
    toys, toys_ms = toy_frames()
    fx, fx_ms = fx_frames()

    cat_sheet, cat_idx = pack(cat, 64, 16)
    toy_sheet, toy_idx = pack(toys, 24, 8)
    fx_sheet, fx_idx = pack(fx, 32, 8)
    cat_sheet.save(ASSETS / "cat.png")
    for outfit, frames in outfit_sheets.items():
        sheet, idx = pack(frames, 64, 16)
        assert idx == cat_idx, "outfit sheets must share the layout of cat.png"
        sheet.save(ASSETS / f"cat_{outfit}.png")
    toy_sheet.save(ASSETS / "toys.png")
    fx_sheet.save(ASSETS / "fx.png")

    clips_json = {}
    for name in cat:
        clips_json[name] = {"sheet": "cat.png", "frameWidth": 64, "frameHeight": 64,
                            "frames": cat_idx[name], "frameMs": cat_ms[name], "loop": cat_loop[name]}
    for name in toys:
        clips_json[name] = {"sheet": "toys.png", "frameWidth": 24, "frameHeight": 24,
                            "frames": toy_idx[name], "frameMs": toys_ms[name], "loop": True}
    for name in fx:
        clips_json[name] = {"sheet": "fx.png", "frameWidth": 32, "frameHeight": 32,
                            "frames": fx_idx[name], "frameMs": fx_ms[name], "loop": True}
    def info(idx, cols, cell):
        count = sum(len(v) for v in idx.values())
        return {"columns": cols, "cell": cell, "frameCount": count}

    sheets = {"cat.png": info(cat_idx, 16, 64), "toys.png": info(toy_idx, 8, 24), "fx.png": info(fx_idx, 8, 32)}
    for outfit in outfit_sheets:
        sheets[f"cat_{outfit}.png"] = info(cat_idx, 16, 64)
    (ASSETS / "animations.json").write_text(
        json.dumps({"sheets": sheets, "clips": clips_json}, ensure_ascii=False, indent=2), "utf-8")

    # icon: head close-up from the first sit frame
    head = cat["purr"][0].crop((20, 6, 56, 42))
    head.resize((256, 256), Image.NEAREST).save(
        ASSETS / "icon.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (256, 256)])

    write_previews(cat, cat_ms, toys, fx)
    write_trick_previews(cat, cat_ms, outfit_sheets, fx)
    print(f"cat clips: {len(cat)}, frames: {sum(len(v) for v in cat.values())}")


def write_previews(cat, cat_ms, toys, fx):
    scale = 4
    bg = (236, 240, 250, 255)
    try:
        font = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", 14)
    except OSError:
        font = ImageFont.load_default()
    rows = list(cat.items()) + list(toys.items()) + list(fx.items())
    label_w = 130
    max_frames = max(len(v) for _, v in rows)
    cell = 64 * scale
    sheet = Image.new("RGBA", (label_w + max_frames * cell, len(rows) * cell), bg)
    d = ImageDraw.Draw(sheet)
    for r, (name, frames) in enumerate(rows):
        d.text((8, r * cell + cell // 2 - 8), name, fill=(40, 50, 90, 255), font=font)
        for i, fr in enumerate(frames):
            big = fr.resize((fr.width * scale, fr.height * scale), Image.NEAREST)
            x = label_w + i * cell + (cell - big.width) // 2
            y = r * cell + (cell - big.height) // 2
            sheet.alpha_composite(big, (x, y))
    sheet.convert("RGB").save(PREVIEW / "preview.png")

    # animated gif: every cat clip played twice at x4, label on top
    gif_frames, durations = [], []
    for name, frames in cat.items():
        reps = 2 if len(frames) > 2 else 4
        for _ in range(reps):
            for fr in frames:
                img = Image.new("RGBA", (64 * 5, 64 * 5 + 24), bg)
                big = fr.resize((256, 256), Image.NEAREST)
                img.alpha_composite(big, (32, 24 + 32))
                ImageDraw.Draw(img).text((10, 4), name, fill=(40, 50, 90, 255), font=font)
                gif_frames.append(img.convert("RGB"))
                durations.append(cat_ms[name])
    gif_frames[0].save(PREVIEW / "preview.gif", save_all=True, append_images=gif_frames[1:],
                       duration=durations, loop=0, disposal=2)



def write_trick_previews(cat, cat_ms, outfit_sheets, fx):
    """Contact sheet of the new trick clips and of every outfit, plus a GIF of the tricks."""
    scale, bg = 5, (236, 240, 250, 255)
    try:
        font = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", 18)
    except OSError:
        font = ImageFont.load_default()
    cell = 64 * scale
    picks = [("idle", 0), ("walk", 2), ("sit", 0), ("fly", 1), ("sleep", 0), ("pounce_cling", 0), ("web_hang", 0)]
    rows = [("Без наряда", cat)] + [({"astronaut": "Космонавт", "pilot": "Пилот", "winter": "Зима"}[k], v)
                                     for k, v in outfit_sheets.items()]
    img = Image.new("RGBA", (190 + cell * len(picks), cell * len(rows)), bg)
    d = ImageDraw.Draw(img)
    for r, (title, clips) in enumerate(rows):
        d.text((12, r * cell + cell // 2 - 10), title, fill=(40, 50, 90, 255), font=font)
        for i, (name, f) in enumerate(picks):
            img.alpha_composite(clips[name][f].resize((cell, cell), Image.NEAREST), (190 + i * cell, r * cell))
    img.convert("RGB").save(PREVIEW / "outfits.png")

    tricks = ["pounce_leap", "pounce_cling", "pounce_slide", "web_hang", "carry"]
    tw = max(len(cat[n]) for n in tricks)
    sheet = Image.new("RGBA", (190 + cell * tw, cell * len(tricks)), bg)
    d = ImageDraw.Draw(sheet)
    for r, name in enumerate(tricks):
        d.text((12, r * cell + cell // 2 - 10), name, fill=(40, 50, 90, 255), font=font)
        for i, fr in enumerate(cat[name]):
            sheet.alpha_composite(fr.resize((cell, cell), Image.NEAREST), (190 + i * cell, r * cell))
    sheet.convert("RGB").save(PREVIEW / "tricks.png")


if __name__ == "__main__":
    main()
