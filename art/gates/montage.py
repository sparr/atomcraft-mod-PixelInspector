#!/usr/bin/env python3
"""
Build the comparison sheet: 4 columns (and, or, xor, not), one row per size.

    16 24 32 40 48 64 80 96, rasterized by render_gates.py

Every cell is scaled to 96x96 by exact area averaging: an output pixel takes
the coverage-weighted mean of the source pixels its footprint lands on.  Where
the source size divides 96 that footprint always sits inside a single source
pixel, so those rows come out with no anti-aliasing at all -- 16, 24, 32, 48
and 96 are exact, and only 40, 64 and 80 pick up blending.

(Plain bilinear will not do this.  Upscaling by an even integer factor puts
output pixel centres half a source pixel off the source centres, so 16 -> 96
comes back with 67 distinct greys rather than 2.)
"""
import sys
from PIL import Image

GATES = ('and', 'or', 'xor', 'not')
SIZES = (16, 24, 32, 40, 48, 64, 80, 96)
CELL = 96


def area_scale(im, out):
    """Resample to out x out by exact area averaging."""
    src = im.convert('L')
    w, h = src.size
    px = src.load()
    dst = Image.new('L', (out, out))
    dp = dst.load()
    # 1-D overlap weights, reused for both axes since the images are square.
    spans = []
    for j in range(out):
        a, b = j * w / out, (j + 1) * w / out
        cells = []
        i = int(a)
        while i < b:
            lo, hi = max(a, i), min(b, i + 1)
            if hi > lo:
                cells.append((min(i, w - 1), hi - lo))
            i += 1
        tot = sum(c[1] for c in cells) or 1.0
        spans.append([(i, wt / tot) for i, wt in cells])
    for y in range(out):
        for x in range(out):
            v = sum(px[i, j] * wx * wy
                    for j, wy in spans[y] for i, wx in spans[x])
            dp[x, y] = int(round(v))
    return dst


def main():
    rows = [(n, f'png/%d_%%s.png' % n) for n in SIZES]
    sheet = Image.new('L', (CELL * len(GATES), CELL * len(rows)), 255)
    for r, (_, pat) in enumerate(rows):
        for c, g in enumerate(GATES):
            sheet.paste(area_scale(Image.open(pat % g), CELL), (c * CELL, r * CELL))
    out = sys.argv[1] if len(sys.argv) > 1 else 'montage.png'
    sheet.save(out)
    print(f'{out}  {sheet.size[0]}x{sheet.size[1]}  '
          f'({len(GATES)} cols x {len(rows)} rows of {CELL}px)')


if __name__ == '__main__':
    main()
