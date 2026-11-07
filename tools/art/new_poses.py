"""Poses and outfits added for the tricks & wardrobe update.

Imported by generate_sprites.py *after* all of its own definitions, so the
helpers (Canvas, ellipsoid, tube, shade, draw_* …) are available through `gs`.
"""
from __future__ import annotations

import math

import numpy as np

import generate_sprites as gs
from generate_sprites import (BELLY, EDGE_FUR, FUR, OUTLINE, STAR, STAR_DIM, Canvas, bezier, ellipsoid, hx,
                              poly_mask, post, shade, tube)

# ----------------------------------------------------------------- palette --

METAL = [hx("6f7aa6"), hx("aab3d6"), hx("dfe4f6"), hx("ffffff")]
METAL_OUT = hx("3a4270")
GLASS = hx("bfe8ff", 48)
GLASS_OUT = hx("6f95d2")
ANTENNA_LIGHT = hx("ff5d9e")

LEATHER = [hx("5a3420"), hx("85532f"), hx("ad7547"), hx("d9a36f")]
LEATHER_OUT = hx("3a2014")
LEATHER_DARK = hx("4a2a18")
GOLD = [hx("8a6416"), hx("c99a32"), hx("efcd72"), hx("fff0bb")]
LENS = hx("39b3dd")
SILK = [hx("b9bdd8"), hx("dcdff0"), hx("f4f5fb"), hx("ffffff")]
SILK_OUT = hx("6c7096")

KNIT = [hx("9e2448"), hx("cc3e68"), hx("ee6f93"), hx("ffa9c2")]
KNIT_OUT = hx("5c1630")
WOOL = [hx("b8bfdd"), hx("dde2f5"), hx("f3f5fd"), hx("ffffff")]
STRIPE = hx("f3f5fd")

PAD = hx("ff9dc4")
PAD_DARK = hx("e5779f")
CLAW = hx("f6f8ff")


def _shade_mask(c, m, ramp, cx, cy, rx, ry):
    """Shade an arbitrary mask as if it were part of the ellipsoid (cx, cy, rx, ry)."""
    _, nx, ny, nz = ellipsoid(c, cx, cy, rx, ry)
    return shade(np.clip(nx, -1, 1), np.clip(ny, -1, 1), nz, ramp)


# ----------------------------------------------------------------- outfits --
# Head anchor: (hx_, hy_) is the centre of the head ellipse (rx 12.5, ry 11).
# In the 3/4 view the face sits ~1.5px right of centre; `front` is symmetric.


def draw_outfit(c, hx_, hy_, front=False, squash=0.0):
    outfit = gs.OUTFIT
    if outfit == "astronaut":
        _astronaut(c, hx_, hy_, front)
    elif outfit == "pilot":
        _pilot(c, hx_, hy_, front)
    elif outfit == "winter":
        _winter(c, hx_, hy_, front)


def _astronaut(c, hx_, hy_, front):
    cx = hx_ if front else hx_ + 1
    cy, rx, ry = hy_ - 3, 15.5, 16.5
    # collar ring the dome sits in
    m, nx, ny, nz = ellipsoid(c, cx, hy_ + 10.2, 9.5, 2.4)
    c.paint(m, shade(nx, ny, nz, METAL), outline=METAL_OUT)
    # glass dome (stops at the collar)
    dm, *_ = ellipsoid(c, cx, cy, rx, ry)
    dm &= c.Y <= hy_ + 9.5
    c.paint(dm, GLASS, outline=GLASS_OUT)
    # reflections: a bright arc upper-left and a small spot
    for deg in range(200, 262, 6):
        a = math.radians(deg)
        c.px(cx + rx * 0.8 * math.cos(a), cy + ry * 0.8 * math.sin(a), hx("ffffff", 200))
    c.px(cx - rx * 0.5, cy - ry * 0.5, hx("ffffff", 230))
    if not front:
        c.px(cx + rx * 0.55, cy - ry * 0.45, hx("ffffff", 140))
    # antenna with a blinking light
    ax = cx if front else cx + 3
    top = cy - ry
    for k in range(1, 4):
        c.px(ax - (0.5 if front else 0), top - k, METAL[1])
    bm, bnx, bny, bnz = ellipsoid(c, ax, top - 4.6, 1.6, 1.6)
    c.paint(bm, ANTENNA_LIGHT, outline=hx("7a1f48"))
    c.px(ax - 0.6, top - 5.2, hx("ffd0e5"))


def _pilot(c, hx_, hy_, front):
    head, nx, ny, nz = ellipsoid(c, hx_, hy_, 12.9, 11.4)
    slope = 0 if front else 0.15
    cap = head & (c.Y < hy_ - 2.5 + slope * (c.X - hx_))
    col = shade(nx, ny, nz, LEATHER)
    c.paint(cap, col, outline=LEATHER_OUT)
    # centre seam
    sx = hx_ if front else hx_ + 1.5
    for y in range(int(hy_ - 11), int(hy_ - 3)):
        c.px(sx - (0.5 if front else 0), y + 0.5, LEATHER[0])
    # ear flaps and chin strap
    flaps = [(hx_ - 11.5, hy_ + 1.5, 2.6, 3.6, LEATHER)]
    if not front:
        flaps.append((hx_ + 11.8, hy_ + 0.5, 2.0, 3.2, [LEATHER[0], LEATHER[0], LEATHER[1], LEATHER[2]]))
    for fx, fy, frx, fry, ramp in flaps:
        fm, fnx, fny, fnz = ellipsoid(c, fx, fy, frx, fry)
        c.paint(fm, shade(fnx, fny, fnz, ramp), outline=LEATHER_OUT)
    chin = (hx_, hy_ + 10) if front else (hx_ + 2, hy_ + 9.6)
    for a, b in ([((hx_ - 10.5, hy_ + 4.8), chin)] + ([] if front else [((hx_ + 11, hy_ + 3.6), chin)])):
        for s in range(13):
            t = s / 12
            c.px(a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, LEATHER_DARK)
    # goggles pushed up on the forehead
    gy = hy_ - 7
    for x in range(int(hx_ - 12), int(hx_ + 13)):
        if head[int(gy), x]:
            c.rgba[int(gy), x] = LEATHER_DARK
    lenses = [(hx_ - 4.6, gy, 2.9)] if front else [(hx_ - 3.6, gy, 2.9), (hx_ + 6.6, gy - 0.4, 2.4)]
    for lx, ly, r in lenses:
        rm, rnx, rny, rnz = ellipsoid(c, lx, ly, r, r)
        c.paint(rm, shade(rnx, rny, rnz, GOLD), outline=LEATHER_OUT)
        lm, *_ = ellipsoid(c, lx, ly, r - 1.0, r - 1.0)
        c.rgba[lm] = LENS
        c.px(lx - 0.8, ly - 0.8, hx("ffffff"))
    # silk scarf with a tail streaming back
    sm, snx, sny, snz = ellipsoid(c, hx_ if front else hx_ + 0.5, hy_ + 10.5, 9, 2.7)
    c.paint(sm, shade(snx, sny, snz, SILK), outline=SILK_OUT)
    tails = [((hx_ - 6.5, hy_ + 10.5), (hx_ - 12, hy_ + 13.5), (hx_ - 17.5, hy_ + 9.5)),
             ((hx_ - 5.5, hy_ + 11.5), (hx_ - 9, hy_ + 16), (hx_ - 14, hy_ + 15.5))]
    for p0, p1, p2 in tails:
        pts = bezier(p0, p1, p2, 10)
        tm, tnx, tny, tnz = tube(c, pts, [2.2 - 0.9 * i / 10 for i in range(11)])
        c.paint(tm, shade(tnx, tny, tnz, SILK), outline=SILK_OUT, edge=SILK[0])


def _winter(c, hx_, hy_, front):
    cx = hx_ if front else hx_ + 1
    head, *_ = ellipsoid(c, hx_, hy_, 12.9, 11.4)
    dome, *_ = ellipsoid(c, cx, hy_ - 7, 12, 7.6)
    hat = (dome | head) & (c.Y < hy_ - 4.3)
    col = _shade_mask(c, hat, KNIT, cx, hy_ - 7, 12, 7.6)
    # knitted look: a light zig-zag row and dotted texture
    zig = np.abs(c.Y - (hy_ - 9 + 0.8 * np.sin(c.X * 1.6))) < 0.5
    col[hat & zig] = KNIT[3]
    dots = ((c.X.astype(int) + c.Y.astype(int)) % 3 == 0) & (np.abs(c.Y - (hy_ - 11.8)) < 0.6)
    col[hat & dots] = KNIT[2]
    c.paint(hat, col, outline=KNIT_OUT)
    # folded cuff with ribs
    cuff = (np.abs(c.Y - (hy_ - 5.2)) < 1.3) & (np.abs(c.X - cx) < 13)
    ccol = _shade_mask(c, cuff, WOOL, cx, hy_ - 5.2, 13, 2)
    ccol[cuff & (c.X.astype(int) % 2 == 0)] = WOOL[1]
    c.paint(cuff, ccol, outline=KNIT_OUT)
    # pompom
    px_, py_ = (cx, hy_ - 15)
    pm, pnx, pny, pnz = ellipsoid(c, px_, py_, 3.5, 3.3)
    for dx, dy in ((-3.5, -1), (3.4, 0.5), (0, -3.6), (-2.4, 2.6), (2.6, -2.6)):
        pm[int(np.clip(py_ + dy, 0, 63)), int(np.clip(px_ + dx, 0, 63))] = True
    c.paint(pm, shade(np.clip(pnx, -1, 1), np.clip(pny, -1, 1), pnz, WOOL), outline=KNIT_OUT)
    # striped scarf and a hanging end
    sm, snx, sny, snz = ellipsoid(c, hx_ if front else hx_ + 0.5, hy_ + 10.3, 9.5, 2.8)
    scol = shade(snx, sny, snz, KNIT)
    scol[sm & ((c.X.astype(int) // 2) % 2 == 0)] = STRIPE
    c.paint(sm, scol, outline=KNIT_OUT)
    end = [(hx_ - 4.5, hy_ + 11), (hx_ - 5.5, hy_ + 15), (hx_ - 6, hy_ + 18.5)]
    em, enx, eny, enz = tube(c, end, [2.3, 2.2, 2.1])
    ecol = shade(enx, eny, enz, KNIT)
    ecol[em & ((c.Y.astype(int) // 2) % 2 == 0)] = STRIPE
    c.paint(em, ecol, outline=KNIT_OUT)
    for dx in (-1.5, 0, 1.5):
        c.px(hx_ - 6 + dx, hy_ + 21, KNIT[2])


# --------------------------------------------------------------- front pose --
# Seen from the front, pressed against the glass. Everything is drawn on the
# left half and mirrored (Canvas.symmetric), so the cat is perfectly symmetric.


def _paw_front(c, x, y, claws):
    m, nx, ny, nz = ellipsoid(c, x, y, 3.7, 3.5)
    c.paint(m, shade(nx, ny, nz, BELLY), edge=EDGE_FUR)
    pm, *_ = ellipsoid(c, x + 0.2, y + 0.9, 1.6, 1.2)
    c.rgba[pm & m] = PAD
    for bx, by in ((-2.0, -1.6), (-0.4, -2.4), (1.4, -1.9)):
        c.px(x + bx, y + by, PAD_DARK)
    if claws:
        for bx in (-2.0, 1.0):
            post(c, x + bx, y - 3.9, CLAW)
            post(c, x + bx, y - 4.9, CLAW)


def front(pose="cling", f=0):
    c = Canvas()
    c.symmetric = True
    leap, slide = pose == "leap", pose == "slide"
    by = 45 if not slide else 46 + f * 0.4
    hy_ = 26 if not slide else 27 + f * 0.3
    if leap:
        arm_paw, leg_paw = (17, 31), (21, 58)
    elif slide:
        arm_paw, leg_paw = (14, 24.5 - f * 0.2), (19, 57)
    else:
        arm_paw, leg_paw = (13.5, 30), (18, 57)
    wing_angle = -14 if not slide else -4 + f * 2
    gs.draw_wing(c, (25.5, by - 9), wing_angle, 1.05)
    # hind leg + paw (seen from the front, toe beans out)
    leg, lnx, lny, lnz = tube(c, [(26, by + 5), (leg_paw[0] + 1, leg_paw[1] - 2)], [3.6, 3.0])
    c.paint(leg, shade(lnx, lny, lnz, FUR), edge=EDGE_FUR)
    _paw_front(c, leg_paw[0], leg_paw[1], claws=False)
    # body with a pale tummy
    sq = 1.2 if pose == "cling" else 0
    m, nx, ny, nz = ellipsoid(c, 32, by, 10.5 + sq, 11 - sq)
    c.paint(m, shade(nx, ny, nz, FUR))
    tm, tnx, tny, tnz = ellipsoid(c, 32, by + 2, 6.2, 8)
    c.paint(tm & m, shade(tnx, tny, tnz, BELLY))
    c.px(31, by - 4, STAR)
    c.px(26, by + 1 + f % 2, STAR_DIM)
    # arm reaching to the glass
    arm, anx, any_, anz = tube(c, [(25, by - 6), (arm_paw[0] + 1.5, arm_paw[1] + 1.5)], [3.5, 3.1])
    c.paint(arm, shade(anx, any_, anz, FUR), edge=EDGE_FUR)
    _paw_front(c, arm_paw[0], arm_paw[1], claws=not leap)
    _head_front(c, hy_, pose)
    return c


def _head_front(c, hy_, pose):
    hx_ = 32
    # ear and horn (left; mirrored)
    ear = [(hx_ - 13, hy_ - 1), (hx_ - 5, hy_ - 9), (hx_ - 12, hy_ - 16.5)]
    c.paint(poly_mask(c, ear), FUR[2])
    cx = sum(p[0] for p in ear) / 3
    cy = sum(p[1] for p in ear) / 3
    c.paint(poly_mask(c, [(cx + (x - cx) * 0.55, cy + (y - cy) * 0.55 + 0.6) for x, y in ear]), gs.EAR_IN)
    hpts = bezier((hx_ - 4, hy_ - 9.5), (hx_ - 4.5, hy_ - 14.5), (hx_ - 7.5, hy_ - 16.5), 8)
    hm, hnx, hny, hnz = tube(c, hpts, [1.9 - 1.3 * i / 8 for i in range(9)])
    c.paint(hm, shade(hnx, hny, hnz, gs.HORN), outline=gs.HORN_OUT)
    # head
    m, nx, ny, nz = ellipsoid(c, hx_, hy_, 13, 11.3)
    tuft = poly_mask(c, [(hx_ - 12.5, hy_ + 1.5), (hx_ - 15, hy_ + 6), (hx_ - 9.5, hy_ + 7)])
    col = shade(nx, ny, nz, FUR, BELLY, 0.55)
    col[tuft & ~m] = BELLY[1]
    c.paint(m | tuft, col, edge=EDGE_FUR)
    mm, mnx, mny, mnz = ellipsoid(c, hx_, hy_ + 5, 5.6, 3.4)
    c.paint(mm & m, shade(mnx, mny, mnz, BELLY))
    # face
    eyes = {"leap": "wide", "cling": "closed", "slide": "half"}[pose]
    gs.draw_eye(c, hx_ - 5.2, hy_ + 0.4, 3.6, 4.3, eyes, (0, 0), outer=-1)
    bm, *_ = ellipsoid(c, hx_ - 9.5, hy_ + 5.2, 2.2, 1.1)
    c.paint(bm & m, gs.BLUSH)
    c.px(hx_ - 0.5, hy_ - 7.2, gs.GEM[2])
    c.px(hx_ - 1.5, hy_ - 7.2, gs.GEM[0])
    c.px(hx_ - 0.5, hy_ - 8.2, gs.GEM[0])
    c.px(hx_ - 0.5, hy_ - 6.2, gs.GEM[0])
    c.px(hx_ - 1.5, hy_ + 3.2, gs.NOSE)
    c.px(hx_ - 0.5, hy_ + 3.2, gs.NOSE_LIGHT)
    c.px(hx_ - 0.5, hy_ + 4.2, gs.NOSE_DARK)
    y = hy_ + 5.4
    if pose == "leap":
        om, *_ = ellipsoid(c, hx_, y + 1.6, 1.9, 2.0)
        c.rgba[om] = gs.MOUTH_IN
        c.px(hx_ - 0.5, y + 2.6, gs.TONGUE)
    elif pose == "cling":
        for dx, dy in ((-0.5, 0), (-1.5, 1), (-2.5, 0.4)):
            c.px(hx_ + dx, y + dy, gs.MOUTH)
    else:
        om, *_ = ellipsoid(c, hx_, y + 1.4, 1.4, 1.4)
        c.rgba[om] = gs.MOUTH_IN
    for a, b in (((hx_ - 9.5, hy_ + 4.8), (hx_ - 16, hy_ + 3.4)), ((hx_ - 9.5, hy_ + 6.2), (hx_ - 15.5, hy_ + 7.8))):
        for s in range(8):
            t = s / 7
            post(c, a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, gs.WHISKER)
    draw_outfit(c, hx_, hy_, front=True)


# ------------------------------------------------------------ upside down ---
# Drawn the right way up with the light coming from below, then flipped, so
# after the flip the light still comes from the top-left.


def web_hang(f=0, n=4):
    """Hangs from the thread by its tail (tassel holds the thread), paws dangling."""
    saved = gs.LIGHT
    gs.LIGHT = np.array([saved[0], -saved[1], saved[2]])
    try:
        c = Canvas()
        c.vflip = True
        sway = math.sin(f / n * 2 * math.pi)
        bx, by = 32, 35
        hx_, hy_ = 32, 18
        # wings spread a little (after the flip they open downwards)
        gs.draw_wing(c, (bx + 3, by - 5), 58 + sway * 5, 0.95, far=True)
        # tail straight to the bottom-centre: after the flip the tassel grips the thread at the top
        gs.draw_tail(c, (bx, by + 8), (bx + 0.5, by + 16), (bx, 59.5))
        # paws dangle (pre-flip they point up beside the head)
        gs.draw_leg(c, (bx + 4, by + 5), (bx + 12 + sway, by + 1), far=True, r=2.5)
        gs.draw_leg(c, (bx + 3, by - 5), (bx + 9 + sway, by - 14), far=True, r=2.5)
        m, nx, ny, nz = ellipsoid(c, bx, by, 8, 10.5)
        c.paint(m, shade(nx, ny, nz, FUR, BELLY, 0.0))
        gs.draw_body_stars(c, bx + 2, by - 1, f)
        gs.draw_wing(c, (bx - 2, by - 5), 40 + sway * 5, 1.0)
        gs.draw_leg(c, (bx - 4, by + 5), (bx - 12 + sway, by + 1), r=2.7)
        gs.draw_leg(c, (bx - 3, by - 5), (bx - 9 + sway, by - 14), r=2.7)
        gs.draw_head(c, hx_ + 0.5 * sway, hy_, "open", "smile", "up")
        return c
    finally:
        gs.LIGHT = saved


# ------------------------------------------------------------------- puff ---


def puff_frames():
    frames = []
    cloud = [(-7, 2), (6, 3), (0, -6), (-4, -3), (5, -4), (0, 6), (-8, -4), (8, -1)]
    for f in range(6):
        c = Canvas(32, 32)
        grow = 0.45 + f * 0.17
        fade = max(0, 255 - f * 40)
        for k, (dx, dy) in enumerate(cloud):
            r = (3.2 + (k % 3) * 0.8) * min(1.2, grow)
            m, nx, ny, nz = ellipsoid(c, 16 + dx * grow, 17 + dy * grow, r, r)
            col = shade(nx, ny, nz, [hx("b6b2ef", fade), hx("d8d6ff", fade), hx("f1f0ff", fade), hx("ffffff", fade)])
            c.paint(m, col, outline=hx("6f69c6", fade))
        for k in range(4):
            a = f * 0.7 + k * 1.6
            sx, sy = 16 + math.cos(a) * (6 + f * 2), 16 + math.sin(a) * (6 + f * 2)
            post(c, sx, sy, STAR)
            for ox, oy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                post(c, sx + ox, sy + oy, STAR_DIM)
        c.outline()
        frames.append(c.image())
    return frames
