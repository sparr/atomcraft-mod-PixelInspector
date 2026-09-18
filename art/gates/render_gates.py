#!/usr/bin/env python3
"""
Rasterize the logic-gate glyphs to 1bpp at any size, preserving vertical
symmetry, stroke weight, 45-degree step regularity and curve smoothness.

Why not scale the SVG and threshold the coverage?

    That is what produced the artifacts in the first 24_and.png.  At N=24 the
    6-unit body stroke scales to 1.5 px and the 12-unit heavy stroke to 3 px, so
    neither band has integer edges; the top horizontal landed on a coverage tie
    and picked up a second row while the bottom did not, and a 3 px output line
    cannot sit centred on the symmetry axis at y=12 because an odd width
    straddles a grid line.  Area coverage near 50% also flips unpredictably
    along a curve, which is what put the kinks in the 16 px arcs -- the outer
    contour jumped two columns in one row instead of stepping by one.

Two stages:

  1. SNAP (`design`).  Rebuild each glyph with stroke widths rounded to whole
     pixels, axis-parallel bands given integer edges, and the parity of any
     stroke centred on the symmetry axis forced to match where that axis falls.

  2. PEN RASTERIZE (`stroke`).  Walk each centreline and stamp a pen, rather
     than thresholding area.  Where the tangent is steep the pen is horizontal
     and the curve is driven one scanline at a time; where it is shallow the pen
     is vertical and the curve is driven one column at a time.  The pen is
     `floor(w / sin)` px long for the steep drive and `floor(w / cos)` for the
     shallow one -- the exact chord the stroke cuts from that scanline or column.

     The sine and cosine are floored at 1/sqrt(2), so neither pen ever exceeds
     w*sqrt(2).  Past 45 degrees the other drive owns the thickness, and a chord
     longer than that is no longer describing this drive's stroke -- it is what
     put the 3 px lumps on a 2 px curve at 24 px.

     That single expression is what makes one rule work at every weight.  For an
     axis-parallel stroke the chord is exactly w.  For a thin curve it stays at
     1 px and the line steps once per pixel, which is what a Bresenham pen would
     give.  For a thick curve it opens out to the true chord, so the boundary is
     the shape the design asked for rather than an axis-aligned approximation of
     it that pinches wherever the tangent nears 45 degrees.  At exactly 45
     degrees both drives fire and their union is w*sqrt(2)+1 px across, the
     correct horizontal run for a diagonal of that weight.

     The drives are not exclusive.  The steep one runs wherever |sin| >= 1/2
     and the shallow one wherever |cos| >= 1/2, so both run through the whole
     30-to-60-degree band rather than handing over at 45.  Without that overlap
     each stops exactly where its pen can no longer reach the other's first
     stamp, and a stroke w px wide leaves a w-px notch in the contour right at
     the seam.  The same threshold caps either chord at 2w, which is the point
     past which a straight chord stops being a fair reading of a curved stroke.

Corners are mitered separately (`joins`): a pen stamped square to an axis
cannot make a sharp outer corner on its own, and the union of two edges' pens
bevels it instead -- which is what rounded off the base angles of NOT's
triangle and notched the top-left corner of AND's body.

Only the top half is drawn; the bottom is its mirror, so symmetry is exact.

Designs are and.svg / or.svg / xor.svg / not.svg, all on a 96-unit canvas with
the symmetry axis at y = 48.
"""
import math, sys, zlib, struct

DESIGN = 96.0
BODY   = 6.0       # body stroke width in design units
HEAVY  = 12.0      # input/output stroke width in design units
FLAT   = 0.05      # curve flattening step, pixels
HALF_RT2 = math.sqrt(0.5)   # the 45-degree handover; caps either pen at w*sqrt(2)
MITER  = 4.0       # miter limit, as SVG's default

GATES = ('and', 'or', 'xor', 'not')


# ------------------------------------------------------------ path building

class P:
    """A polyline in pixel space, flattened as it is built."""

    def __init__(self, closed=False):
        self.pts = []
        self.closed = closed

    def to(self, x, y):
        self.pts.append((x, y))
        return self

    def cubic(self, c0, c1, p1):
        """Cubic Bezier from the current point, flattened to ~FLAT px steps."""
        x0, y0 = self.pts[-1]
        rough = (math.hypot(c0[0] - x0, c0[1] - y0) + math.hypot(c1[0] - c0[0], c1[1] - c0[1])
                 + math.hypot(p1[0] - c1[0], p1[1] - c1[1]))
        n = max(16, int(rough / FLAT) + 1)
        for i in range(1, n + 1):
            t = i / n
            u = 1 - t
            self.pts.append((
                u**3 * x0 + 3 * u * u * t * c0[0] + 3 * u * t * t * c1[0] + t**3 * p1[0],
                u**3 * y0 + 3 * u * u * t * c0[1] + 3 * u * t * t * c1[1] + t**3 * p1[1]))
        return self

    def arc(self, cx, cy, r, a0, a1):
        """Circular arc, angles in radians, measured from +x and turning toward +y."""
        n = max(16, int(abs(a1 - a0) * r / FLAT) + 1)
        for i in range(n + 1):
            a = a0 + (a1 - a0) * i / n
            self.pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
        return self


# ------------------------------------------------------------------ snapping

def nearest_with_parity(ideal, parity, tiebreak):
    """Nearest integer to `ideal` whose parity matches, at least 1.

    A band of width w centred on the symmetry axis only lands on whole pixels
    for one parity of w: even when N is even (the axis is a grid line) and odd
    when N is odd (the axis is the centre of the middle row).  `ideal` is often
    an exact tie between the two candidates, which `tiebreak` -- the unrounded
    design width at this scale -- settles.
    """
    lo = int(math.floor((ideal - parity) / 2.0)) * 2 + parity
    if lo > ideal:
        lo -= 2
    cands = [c for c in (lo, lo + 2) if c >= (1 if parity else 2)]
    # On a full tie prefer the heavier: the alternative can collapse a stroke
    # onto the weight of a lighter one it is meant to contrast with.
    return min(cands, key=lambda c: (abs(c - ideal), abs(c - tiebreak), -c))


def band(c, w):
    """Snap an axis-parallel stroke centreline so its band has integer edges."""
    return math.floor(c - w / 2.0 + 0.5) + w / 2.0


def widths(N):
    s = N / DESIGN
    bw = max(1, int(math.floor(BODY * s + 0.5)))
    lw = nearest_with_parity(bw * HEAVY / BODY, N % 2, HEAVY * s)
    if HEAVY > BODY:                          # the design's contrast must survive
        lw = max(lw, bw + 1 + ((bw + 1 + N) % 2))
    return bw, lw


# ------------------------------------------------------------------- designs

def design(gate, N):
    """Snapped geometry for one gate at size N, as a list of (path, width)."""
    s = N / DESIGN
    bw, lw = widths(N)
    cx = cy = N / 2.0
    vx = band(21 * s, bw)                     # vertical body edge, x = 21
    out = []

    if gate == 'and':
        hy = band(21 * s, bw)                 # top horizontal, y = 21
        r = cy - hy                           # so the arc meets the horizontals exactly
        body = P(closed=True).to(vx, hy).to(vx, N - hy).to(cx, N - hy)
        body.arc(cx, cy, r, math.pi / 2, -math.pi / 2)
        body.to(vx, hy)
        out.append((body, bw))
        tip = cx + r - bw / 2.0               # output line starts inside the arc

    elif gate in ('or', 'xor'):
        # The two right-hand curves are not quite mirror images of each other --
        # a hand-drag artifact shared by or.svg and xor.svg.  The control point
        # below is the average of the pair, so neither drawn half is thrown away.
        k = (40.62427, 20.887466)

        def right(p):                     # the two curves that meet at the tip
            p.cubic((k[0] * s, (DESIGN - k[1]) * s), (75 * s, 63 * s), (75 * s, cy))
            p.cubic((75 * s, 33 * s), (k[0] * s, k[1] * s), (18 * s, 21 * s))
            return p

        def bow(x, p):                    # a left-hand arc rooted at x
            q = P().to(x * s, 21 * s)
            q.cubic(((x + p) * s, 39 * s), ((x + p) * s, 57 * s), (x * s, 75 * s))
            return q

        if gate == 'or':
            body = P(closed=True).to(18 * s, 21 * s)
            body.cubic((30 * s, 39 * s), (30 * s, 57 * s), (18 * s, 75 * s))
            out.append((right(body), bw))
        else:
            # xor.svg splits the body open: the right-hand curves stand alone
            # and the left edge is its own arc, moved slightly right of their
            # ends, with the outer bow further out again.
            out.append((right(P().to(18 * s, 75 * s)), bw))
            out.append((bow(18.944325, 12), bw))
            out.append((bow(6, 12), bw))
        tip = 75 * s

    elif gate == 'not':
        tri = P(closed=True).to(vx, 21 * s).to(vx, 75 * s).to(81 * s, cy).to(vx, 21 * s)
        out.append((tri, bw))
        out.append((P().to(0, cy).to(vx, cy), lw))          # single input, on the axis
        tip = 75 * s

    else:
        raise ValueError(gate)

    out.append((P().to(tip, cy).to(N, cy), lw))             # output line
    if gate != 'not':                                        # two 45-degree inputs
        e = vx + bw / 2.0                                    # stop at the body's inner edge
        out.append((P().to(-lw, -lw).to(e, e), lw))
        out.append((P().to(-lw, N + lw).to(e, N - e), lw))
    return out


# ----------------------------------------------------------------- rasterize

def stroke(grid, path, w, N, top):
    """Stamp one centreline into the top half of the grid with a chord pen."""
    pts = list(path.pts)
    if path.closed and pts[0] != pts[-1]:
        pts.append(pts[0])

    # Butt caps.  A pen is stamped square to an axis, not to the centreline, so
    # near the end of an open path the non-dominant drive can reach past the
    # end -- a chord w/cos px tall centred on the last point hangs half its
    # length beyond it.  These two half-planes trim it back to the real end.
    caps = []
    if not path.closed:
        for seq in (pts, pts[::-1]):
            a = seq[0]
            b = next((r for r in seq[1:] if math.hypot(r[0] - a[0], r[1] - a[1]) > 1e-9), None)
            if b is None:
                continue
            L = math.hypot(b[0] - a[0], b[1] - a[1])
            caps.append((a, ((b[0] - a[0]) / L, (b[1] - a[1]) / L)))

    def inside(x, y):
        return all((x - p[0]) * t[0] + (y - p[1]) * t[1] >= -1e-9 for p, t in caps)

    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        L = math.hypot(x1 - x0, y1 - y0)
        if L < 1e-12:
            continue
        cs, sn = abs(x1 - x0) / L, abs(y1 - y0) / L

        if sn >= 0.5:                      # steep: drive by scanline
            k = max(1, int(math.floor(w / max(sn, HALF_RT2) + 1e-9)))
            lo, hi = (y0, y1) if y0 <= y1 else (y1, y0)
            for row in range(int(math.floor(lo)), int(math.ceil(hi)) + 1):
                yc = row + 0.5
                if not (lo <= yc < hi) or not (0 <= row < top):
                    continue
                xc = x0 + (x1 - x0) * (yc - y0) / (y1 - y0)
                a = int(math.floor(xc - k / 2.0 + 0.5))
                for col in range(max(0, a), min(N, a + k)):
                    if inside(col + 0.5, yc):
                        grid[row][col] = 1

        if cs >= 0.5:                      # shallow: drive by column
            k = max(1, int(math.floor(w / max(cs, HALF_RT2) + 1e-9)))
            lo, hi = (x0, x1) if x0 <= x1 else (x1, x0)
            for col in range(int(math.floor(lo)), int(math.ceil(hi)) + 1):
                xc = col + 0.5
                if not (lo <= xc < hi) or not (0 <= col < N):
                    continue
                yc = y0 + (y1 - y0) * (xc - x0) / (x1 - x0)
                a = int(math.floor(yc - k / 2.0 + 0.5))
                for row in range(max(0, a), min(top, a + k)):
                    if inside(xc, row + 0.5):
                        grid[row][col] = 1


def fill(grid, poly, N, top):
    """Even-odd scanline fill of a small convex polygon, on pixel centres."""
    ys = [p[1] for p in poly]
    for row in range(max(0, int(math.floor(min(ys)))), min(top, int(math.ceil(max(ys))) + 1)):
        yc = row + 0.5
        xs = []
        for (ax, ay), (bx, by) in zip(poly, poly[1:] + poly[:1]):
            if (ay <= yc) != (by <= yc):
                xs.append(ax + (bx - ax) * (yc - ay) / (by - ay))
        xs.sort()
        for a, b in zip(xs[0::2], xs[1::2]):
            for col in range(max(0, int(math.floor(a))), min(N, int(math.ceil(b)) + 1)):
                if a <= col + 0.5 <= b:
                    grid[row][col] = 1


def joins(grid, path, w, N, top):
    """Miter the outer side of every real corner.

    A pen is stamped square to an axis, so where two edges meet the union of
    their pens stops at the bevel and the outer point is simply missing.  The
    flattened curves turn by a fraction of a degree per segment, so the angle
    threshold picks out the corners the design actually has and skips those.
    """
    pts = list(path.pts)
    if path.closed:
        while len(pts) > 1 and pts[0] == pts[-1]:
            pts.pop()                       # the wrap is implicit below
        idx, n = range(len(pts)), len(pts)
    else:
        idx, n = range(1, len(pts) - 1), len(pts)
    if n < 3:
        return

    for i in idx:
        v = pts[i]
        a0, b0 = pts[i - 1], pts[(i + 1) % n]
        d0 = (v[0] - a0[0], v[1] - a0[1])
        d1 = (b0[0] - v[0], b0[1] - v[1])
        l0, l1 = math.hypot(*d0), math.hypot(*d1)
        if l0 < 1e-9 or l1 < 1e-9:
            continue
        d0 = (d0[0] / l0, d0[1] / l0)
        d1 = (d1[0] / l1, d1[1] / l1)
        cross = d0[0] * d1[1] - d0[1] * d1[0]
        dot = d0[0] * d1[0] + d0[1] * d1[1]
        if abs(math.atan2(cross, dot)) < math.radians(15):
            continue
        sg = 1.0 if cross < 0 else -1.0                 # outward normal side
        n0 = (-d0[1] * sg, d0[0] * sg)
        n1 = (-d1[1] * sg, d1[0] * sg)
        h = w / 2.0
        a = (v[0] + n0[0] * h, v[1] + n0[1] * h)
        b = (v[0] + n1[0] * h, v[1] + n1[1] * h)
        den = d0[0] * d1[1] - d0[1] * d1[0]
        poly = [v, a, b]
        if abs(den) > 1e-9:
            t = ((b[0] - a[0]) * d1[1] - (b[1] - a[1]) * d1[0]) / den
            m = (a[0] + d0[0] * t, a[1] + d0[1] * t)
            if math.hypot(m[0] - v[0], m[1] - v[1]) <= MITER * h:
                poly = [v, a, m, b]                     # else fall back to a bevel
        fill(grid, poly, N, top)


def render(gate, N):
    grid = [[0] * N for _ in range(N)]
    top = (N + 1) // 2          # at odd N the middle row is on the axis and is its own mirror
    for path, w in design(gate, N):
        stroke(grid, path, w, N, top)
        joins(grid, path, w, N, top)
    for y in range(N // 2):
        grid[N - 1 - y] = list(grid[y])
    return grid


# -------------------------------------------------------------------- output

def write_png1(path, grid):
    """1-bit greyscale PNG, 0 = ink."""
    N = len(grid)
    raw = b''.join(
        b'\x00' + bytes(
            sum((0 if row[x] else 1) << (7 - (x & 7))
                for x in range(b8, min(b8 + 8, N)))
            for b8 in range(0, N, 8))
        for row in grid)

    def chunk(tag, data):
        c = tag + data
        return struct.pack('>I', len(data)) + c + struct.pack('>I', zlib.crc32(c))

    with open(path, 'wb') as f:
        f.write(b'\x89PNG\r\n\x1a\n')
        f.write(chunk(b'IHDR', struct.pack('>IIBBBBB', N, N, 1, 0, 0, 0, 0)))
        f.write(chunk(b'IDAT', zlib.compress(raw, 9)))
        f.write(chunk(b'IEND', b''))


USAGE = '''usage: render_gates.py [--ascii] [--gate=NAME] [SIZE ...]

Writes <SIZE>_<gate>.png (1bpp) for each gate and size.
Default gates and,or,xor,not; default sizes 16 24 32 40 48 64 80 96.
'''

if __name__ == '__main__':
    args = sys.argv[1:]
    if '-h' in args or '--help' in args:
        sys.exit(USAGE)
    gates = [g for a in args if a.startswith('--gate=') for g in a.split('=')[1].split(',')] or list(GATES)
    sizes = [int(a) for a in args if not a.startswith('-')] or [16, 24, 32, 40, 48, 64, 80, 96]
    for g in gates:
        for N in sizes:
            grid = render(g, N)
            write_png1(f'{N}_{g}.png', grid)
            if '--ascii' in args:
                print(f'=== {g} {N} ===')
                for row in grid:
                    print(''.join('#' if v else '.' for v in row))
