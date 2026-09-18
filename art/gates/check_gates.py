#!/usr/bin/env python3
"""Verify the properties render_gates.py is supposed to hold, per gate and size.

  symmetry     row y and row N-1-y are identical
  weight       the input and output lines are lw px; AND's vertical body edge
               is bw px
  diagonal     every row clear of the body carries the same 45-degree run,
               stepping exactly one pixel per row
  solidity     the ink is one 8-connected component (two for XOR, whose second
               arc is a separate stroke) with no dangling pixels, so no join or
               octant seam has opened a gap
  arc          AND's quarter arc advances at most one pixel per scanline over
               its whole steep octant, and one per column over its whole
               shallow one -- the kink test, checked edge to edge rather than
               only between the 45-degree knees
"""
import math, sys, render_gates
from render_gates import widths, band, DESIGN, GATES


def runs(row):
    out, x = [], 0
    while x < len(row):
        if row[x]:
            s = x
            while x < len(row) and row[x]:
                x += 1
            out.append((s, x - s))
        else:
            x += 1
    return out


def components(g, N):
    seen = [[False] * N for _ in range(N)]
    n = 0
    for sy in range(N):
        for sx in range(N):
            if g[sy][sx] and not seen[sy][sx]:
                n += 1
                stack = [(sy, sx)]
                seen[sy][sx] = True
                while stack:
                    y, x = stack.pop()
                    for dy in (-1, 0, 1):
                        for dx in (-1, 0, 1):
                            b, a = y + dy, x + dx
                            if 0 <= b < N and 0 <= a < N and g[b][a] and not seen[b][a]:
                                seen[b][a] = True
                                stack.append((b, a))
    return n


def check(gate, N):
    g = render_gates.render(gate, N)
    bw, lw = widths(N)
    bad = []

    if any(g[y] != g[N - 1 - y] for y in range(N)):
        bad.append('not symmetric about y=N/2')

    orows = [y for y in range(N) if g[y][N - 1]]
    if orows != list(range((N - lw) // 2, (N + lw) // 2)):
        bad.append(f'output line rows {orows}, expected {lw} centred on {N/2}')

    if gate == 'not':
        irows = [y for y in range(N) if g[y][0]]
        if irows != list(range((N - lw) // 2, (N + lw) // 2)):
            bad.append(f'input line rows {irows}, expected {lw} centred on {N/2}')
    else:
        vx = band(21 * N / DESIGN, bw)
        want = list(range(int(vx - bw / 2), int(vx + bw / 2)))
        if gate == 'and':
            got = [x for x in range(int(N / 3)) if g[N // 2][x]]
            if got != want:
                bad.append(f'vertical body edge {got}, expected {want}')

        # 45-degree inputs: constant run, one pixel of step per row.  Only the
        # rows above the body count -- below that the diagonal has merged into
        # it and the run is no longer the diagonal's.
        s_ = N / DESIGN
        body_top = (band(21 * s_, bw) - bw / 2.0) if gate == 'and' else (21 * s_ - bw / 2.0)
        # The union of the two drives on a 45-degree line is the next odd
        # integer at or above floor(w*sqrt(2)), the chord each drive stamps.
        k = int(math.floor(lw * math.sqrt(2) + 1e-9))
        want_run = k if k % 2 else k + 1
        full = []
        for y in range(N):
            if y + 0.5 >= body_top:
                break
            r = runs(g[y])
            if r and r[0][0] > 0:          # skip the rows clipped by the corner
                full.append(r[0])
        if full:
            if {d[1] for d in full} != {want_run}:
                bad.append(f'diagonal runs {[d[1] for d in full]}, expected {want_run}')
            if any(b[0] - a[0] != 1 for a, b in zip(full, full[1:])):
                bad.append(f'diagonal steps vary: {[d[0] for d in full]}')

    # XOR's second arc is a stroke of its own, so two pieces is correct there;
    # at the smaller sizes the input diagonals fatten enough to touch it and it
    # becomes one.  Anything beyond that is a join or a seam that has opened.
    n = components(g, N)
    if n > (2 if gate == 'xor' else 1):
        bad.append(f'{n} disconnected pieces')
    # The tip of a 1 px open stroke legitimately has a single neighbour, so
    # exempt the ends of the open paths in the design (XOR's second arc).
    tips = [p_ for path, _ in render_gates.design(gate, N) if not path.closed
            for p_ in (path.pts[0], path.pts[-1])]
    dangle = [(y, x) for y in range(N) for x in range(N) if g[y][x] and
              sum(g[y + dy][x + dx] for dy in (-1, 0, 1) for dx in (-1, 0, 1)
                  if 0 <= y + dy < N and 0 <= x + dx < N) <= 2
              and 0 < x < N - 1 and 0 < y < N - 1
              and not any(abs(x + .5 - t[0]) <= bw and abs(y + .5 - t[1]) <= bw for t in tips)]
    if dangle:
        bad.append(f'dangling pixels {dangle[:4]}')

    if gate == 'and':
        # The arc runs from the horizontals' tangent point to the axis.  Its
        # steep octant must step by at most one column per row and its shallow
        # one by at most one row per column, edge to edge.
        hy = band(21 * N / DESIGN, bw)
        cx = cy = N / 2.0
        rc = cy - hy
        knee = rc / math.sqrt(2)
        lo_row, hi_row = int(cy - knee), (N - lw) // 2
        edge = [max(x for x in range(int(cx), N) if g[y][x]) for y in range(lo_row, hi_row)]
        if any(b - a > 1 for a, b in zip(edge, edge[1:])):
            bad.append(f'arc steep octant steps by >1: {edge}')
        lo_col, hi_col = int(cx), int(cx + knee) + 1
        topc = [min(y for y in range((N + 1) // 2) if g[y][x]) for x in range(lo_col, hi_col)]
        if any(b - a > 1 for a, b in zip(topc, topc[1:])):
            bad.append(f'arc shallow octant steps by >1: {topc}')

    label = f'{gate:4s} {N:3d}  bw={bw} lw={lw}'
    print(f'{label}  ' + ('OK' if not bad else 'FAIL\n      ' + '\n      '.join(bad)))
    return not bad


if __name__ == '__main__':
    sizes = [int(a) for a in sys.argv[1:]] or list(range(16, 97))
    ok = all([check(g, N) for N in sizes for g in GATES])
    sys.exit(0 if ok else 1)
