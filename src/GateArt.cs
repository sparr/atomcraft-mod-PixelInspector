using Godot;
using PixelArt;

namespace PixelInspector;

/// <summary>Which gate a pixel is, for <see cref="GateArt"/>. <c>None</c> draws nothing.</summary>
public enum GateKind
{
    None,
    And,
    Or,
    Xor,
    Not,
    /// <summary>A latch waiting for its input to rise.</summary>
    LatchRest,
    /// <summary>A latch that has fired and is holding until its input falls.</summary>
    LatchFired,
}

/// <summary>
/// The logic gates, drawn as the shapes an electronics diagram uses, pointed the way the pixel is
/// pointed.
///
/// <para><b>Why shapes rather than letters.</b> A circuit is read by following it, and the four
/// gates used to be <c>&amp;</c>, <c>|</c>, <c>^</c> and <c>!</c> -- four characters that have to
/// be read one at a time and that carry no orientation at all. The standard outlines are
/// recognised rather than read, and they have a front and a back, so which way a gate feeds is
/// part of the mark instead of a separate triangle beside it.</para>
///
/// <para><b>The input legs are at 45 degrees because the inputs are.</b> That is not stylisation:
/// <c>AndGateMaterial.GetInputPositions</c> reads the two <i>diagonal</i> neighbours on the side
/// away from the output -- a gate pointing up is fed from below-left and below-right. So a leg
/// drawn at 45 degrees points at the pixel that actually feeds it, and a player can see at a
/// glance which two cells of a crowded circuit are the inputs. A latch is the exception and is
/// drawn with a straight leg, because <c>LatchMaterial.GetInputPositions</c> reads the single cell
/// directly behind it.</para>
///
/// <para><b>Authored once, rotated three times.</b> Every glyph faces right, and the draw turns it
/// in quarter turns to match. A quarter turn of a square grid is exact -- it is a transpose and a
/// flip, with no resampling -- which is the whole reason the grids are square and odd-sided.</para>
/// </summary>
internal static class GateArt
{
    // The four gates are no longer drawn from a grid authored here. They come from
    // art/gates/*.svg, rasterized once per zoom rung by render_gates.py and packed by
    // pack_atlas.py into GateSprites -- a bitmap per gate per rung rather than one drawing
    // scaled, because a 16-pixel glyph is not a shrunken 96-pixel one. See art/gates/.
    //
    // The latch below is still authored here: it has no drawing yet.

    // Two cross-coupled gates, which is what a latch is. The lit one is filled; see Draw.
    private static readonly string[] LatchFrame =
    {
        ".............",
        ".............",
        "...####......",
        "...#..#......",
        "...#..#..##..",
        "...####...#..",
        "#####....####",
        "...####...#..",
        "...#..#..##..",
        "...#..#......",
        "...####......",
        ".............",
        ".............",
    };

    // Which cells of the frame belong to each half, so one can be filled without the other.
    private static readonly string[] LatchTop =
    {
        ".............",
        ".............",
        "....##.......",
        "....##.......",
        "....##.......",
        ".............",
        ".............",
        ".............",
        ".............",
        ".............",
        ".............",
        ".............",
        ".............",
    };

    private static readonly string[] LatchBottom =
    {
        ".............",
        ".............",
        ".............",
        ".............",
        ".............",
        ".............",
        ".............",
        ".............",
        "....##.......",
        "....##.......",
        "....##.......",
        ".............",
        ".............",
    };

    /// <summary>How much of the cell the glyph is allowed to take.</summary>
    private const float Fill = 0.92f;

    /// <summary>The side of every glyph grid.</summary>
    private const int Side = 13;

    /// <summary>Which grid, for the bake cache's key. A grid is never compared by reference.</summary>
    private enum Face { And, Or, Xor, Not, LatchFrame, LatchTop, LatchBottom }

    /// <summary>
    /// The drawn gates, decoded from <see cref="GateSprites"/> on first use: for each gate and
    /// each zoom rung, the runs of its canonical right-facing bitmap.
    /// </summary>
    private static Dictionary<(int Gate, int Cell), List<(int Y, int X, int Len)>>? _sprites;

    private static Dictionary<(int, int), List<(int, int, int)>> Sprites()
    {
        if (_sprites != null)
            return _sprites;

        _sprites = new Dictionary<(int, int), List<(int, int, int)>>();
        var blob = Convert.FromBase64String(GateSprites.Packed);
        var i = 0;
        while (i + 4 <= blob.Length)
        {
            var gate = blob[i];
            var cell = blob[i + 1];
            var count = blob[i + 2] | (blob[i + 3] << 8);
            i += 4;

            var runs = new List<(int, int, int)>(count);
            for (var r = 0; r < count && i + 3 <= blob.Length; r++, i += 3)
                runs.Add((blob[i], blob[i + 1], blob[i + 2]));
            _sprites[(gate, cell)] = runs;
        }
        return _sprites;
    }

    /// <summary>
    /// The zoom rung whose sprite fits a cell this size, or -1 when none does.
    ///
    /// <para>The largest rung whose SPRITE -- the glyph after its border is trimmed -- is no wider
    /// than the cell. On the panel the cell is always a rung, so this picks that rung exactly; the
    /// world pass, whose pixels are twelve screen pixels across, gets nothing and falls back to
    /// the letter.</para>
    /// </summary>
    private static int RungFor(float cell)
    {
        var best = -1;
        for (var i = 0; i < GateSprites.Sizes.Length; i++)
            if (GateSprites.Sides[i] <= cell)
                best = GateSprites.Sizes[i];
        return best;
    }

    /// <summary>The drawn side of the sprite for a rung.</summary>
    private static int SideOf(int rung)
    {
        for (var i = 0; i < GateSprites.Sizes.Length; i++)
            if (GateSprites.Sizes[i] == rung)
                return GateSprites.Sides[i];
        return 0;
    }

    /// <summary>One baked rendering: a glyph, at a quarter turn, at a unit size.</summary>
    private readonly record struct Bake(Face Face, int Turns, int Unit);

    /// <summary>
    /// Rectangles ready to draw, relative to the glyph's top-left corner.
    ///
    /// <para><b>Why this is baked rather than walked.</b> Drawing used to scan all 169 cells of the
    /// grid per gate per frame, rotating each coordinate as it went. The result depends on nothing
    /// but the glyph, the facing and the unit size -- and the unit size changes only when the
    /// player zooms -- so a panel of forty gates was recomputing forty identical answers sixty
    /// times a second. Baking is keyed on exactly those three things.</para>
    ///
    /// <para>Baking also merges runs of adjacent lit cells along a row into one rectangle, which
    /// it can do because it happens once: a thirteen-wide outline is mostly short horizontal runs,
    /// and merging them cuts the draw calls by about half again.</para>
    ///
    /// <para>The set is small and bounded -- seven grids, four facings, eight rungs of the zoom
    /// ladder -- so there is nothing to evict. It is cleared with the rest of the mod's state.</para>
    /// </summary>
    private static readonly Dictionary<Bake, Rect2[]> Baked = new();

    /// <summary>
    /// Bakes every gate, at every zoom rung, in all four facings. Returns how many were baked.
    ///
    /// <para>Exists to be measured, and to be called at load if that measurement ever justifies
    /// it. Nothing calls it in play: the lazy path bakes only the rung the player is actually
    /// looking at, which is a thirty-second of this.</para>
    /// </summary>
    internal static int WarmAll()
    {
        var n = 0;
        foreach (var face in new[] { Face.And, Face.Or, Face.Xor, Face.Not })
        foreach (var rung in GateSprites.Sizes)
        for (var turns = 0; turns < 4; turns++)
        {
            Drawn(face, rung, turns);
            n++;
        }
        return n;
    }

    /// <summary>Decodes the blob without baking anything, so the two costs can be told apart.</summary>
    internal static int DecodeOnly() => Sprites().Count;

    /// <summary>Forgets the baked glyphs. Called from the mod's state reset.</summary>
    internal static void Clear()
    {
        Baked.Clear();
        _sprites = null;
    }

    /// <summary>
    /// Whether a gate shape would draw in a cell this size. Asked before the arrows, because a
    /// gate that draws replaces them.
    /// </summary>
    internal static bool Fits(Rect2 screen, GateKind kind)
    {
        if (kind == GateKind.None)
            return false;
        var cell = Mathf.Min(screen.Size.X, screen.Size.Y);
        return kind is GateKind.LatchRest or GateKind.LatchFired
            ? Mathf.Floor(cell * Fill / Side) >= 1f      // the latch is still a scaled grid
            : RungFor(cell) > 0;                         // the gates have a sprite per rung
    }

    /// <summary>
    /// Draws a gate in <paramref name="screen"/>, turned to face <paramref name="aim"/>.
    ///
    /// <para>Returns false when the cell is too small for one unit a square, so the caller can
    /// fall back to the letter it used to draw rather than showing nothing.</para>
    /// </summary>
    internal static bool Draw(Canvas canvas, Rect2 screen, GateKind kind, Aim aim, Color color,
                              Color lit)
    {
        if (kind == GateKind.None)
            return false;

        var turns = aim switch
        {
            Aim.Down => 1,
            Aim.Left => 2,
            Aim.Up   => 3,
            _        => 0,      // Right is the canonical facing; None gets it too
        };

        // The latch is still one authored grid, scaled. The four gates are a drawn sprite per
        // rung; see art/gates.
        if (kind is GateKind.LatchRest or GateKind.LatchFired)
        {
            var unit = Mathf.Floor(Mathf.Min(screen.Size.X, screen.Size.Y) * Fill / Side);
            if (unit < 1f)
                return false;
            var origin = new Vector2(
                Mathf.Round(screen.GetCenter().X - Side * unit / 2f),
                Mathf.Round(screen.GetCenter().Y - Side * unit / 2f));
            Stamp(canvas, Face.LatchFrame, turns, origin, unit, color);
            // At rest the first gate holds; once fired the second does. Filling the one that is
            // holding is the whole state, and it is the thing the old "L" against "L*" made you
            // remember rather than see.
            Stamp(canvas, kind == GateKind.LatchRest ? Face.LatchTop : Face.LatchBottom,
                  turns, origin, unit, lit);
            return true;
        }

        var rung = RungFor(Mathf.Min(screen.Size.X, screen.Size.Y));
        if (rung <= 0)
            return false;

        var side = SideOf(rung);
        var at = new Vector2(Mathf.Round(screen.GetCenter().X - side / 2f),
                             Mathf.Round(screen.GetCenter().Y - side / 2f));
        foreach (var piece in Drawn(FaceOf(kind), rung, turns))
            canvas.DrawFill(new Rect2(at.X + piece.Position.X, at.Y + piece.Position.Y,
                                      piece.Size.X, piece.Size.Y), color);
        return true;
    }

    /// <summary>
    /// A drawn gate's rectangles, turned and merged, baked on first use.
    ///
    /// <para>Same bake as the authored grids get, for the same reason: the answer depends only on
    /// the gate, the rung and the facing, and a panel of gates asks for it over and over. The turn
    /// is applied before the runs are merged, because a vertical run becomes a horizontal one
    /// after a quarter turn and merging first would miss it.</para>
    /// </summary>
    private static Rect2[] Drawn(Face face, int rung, int turns)
    {
        var key = new Bake(face, turns, rung);
        if (Baked.TryGetValue(key, out var ready))
            return ready;

        var side = SideOf(rung);
        var lit = new bool[side, side];
        if (Sprites().TryGetValue(((int)face, rung), out var runs))
            foreach (var (ry, rx, len) in runs)
                for (var d = 0; d < len; d++)
                {
                    int x = rx + d, y = ry;
                    for (var t = 0; t < turns; t++)
                        (x, y) = (side - 1 - y, x);
                    lit[y, x] = true;
                }

        var pieces = new List<Rect2>();
        for (var y = 0; y < side; y++)
        {
            var x = 0;
            while (x < side)
            {
                if (!lit[y, x]) { x++; continue; }
                var run = x;
                while (run < side && lit[y, run])
                    run++;
                pieces.Add(new Rect2(x, y, run - x, 1));
                x = run;
            }
        }

        var baked = pieces.ToArray();
        Baked[key] = baked;
        return baked;
    }

    private static Face FaceOf(GateKind kind) => kind switch
    {
        GateKind.And => Face.And,
        GateKind.Or  => Face.Or,
        GateKind.Xor => Face.Xor,
        _            => Face.Not,
    };

    private static string[] Grid(Face face) => face switch
    {
        Face.LatchFrame  => LatchFrame,
        Face.LatchTop    => LatchTop,
        _                => LatchBottom,
    };

    /// <summary>Draws a baked glyph at <paramref name="origin"/>.</summary>
    private static void Stamp(Canvas canvas, Face face, int turns, Vector2 origin, float unit,
                              Color color)
    {
        foreach (var piece in Pieces(face, turns, unit))
            canvas.DrawFill(new Rect2(origin.X + piece.Position.X, origin.Y + piece.Position.Y,
                                      piece.Size.X, piece.Size.Y), color);
    }

    /// <summary>
    /// The rectangles for one glyph at one facing and size, baked on first use.
    /// </summary>
    private static Rect2[] Pieces(Face face, int turns, float unit)
    {
        var key = new Bake(face, turns, Mathf.RoundToInt(unit));
        if (Baked.TryGetValue(key, out var ready))
            return ready;

        var glyph = Grid(face);

        // Turn first, into a plain bool grid, so the merge below reads rows of the FINAL image
        // rather than rows of the source -- a vertical run in the source is a horizontal one after
        // a quarter turn, and merging before the turn would miss it.
        var lit = new bool[Side, Side];
        for (var row = 0; row < Side; row++)
        for (var col = 0; col < Side; col++)
        {
            if (glyph[row][col] != '#')
                continue;
            int x = col, y = row;
            for (var t = 0; t < turns; t++)
                (x, y) = (Side - 1 - y, x);   // clockwise
            lit[y, x] = true;
        }

        var pieces = new List<Rect2>();
        for (var y = 0; y < Side; y++)
        {
            var x = 0;
            while (x < Side)
            {
                if (!lit[y, x]) { x++; continue; }
                var run = x;
                while (run < Side && lit[y, run])
                    run++;
                pieces.Add(new Rect2(x * unit, y * unit, (run - x) * unit, unit));
                x = run;
            }
        }

        var baked = pieces.ToArray();
        Baked[key] = baked;
        return baked;
    }
}
