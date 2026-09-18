using Atomcraft.TestHarness;
using Godot;

namespace PixelInspector.Test;

/// <summary>
/// The tool-highlight border, as geometry: every boundary pixel covered exactly once.
///
/// <para>Both properties were broken and both were reported by eye rather than caught here, which
/// is why this exists. Painting a pixel twice is invisible at full opacity and doubles the apparent
/// strength at the half the game uses for a tool's reach; leaving one unpainted bites a notch out
/// of every concave corner.</para>
/// </summary>
public static class HighlightOutlineTests
{
    private const int Cell = 12;
    private const float Edge = 2f;

    /// <summary>
    /// Traces a shape given as rows of '#', at one cell per character, and returns how many times
    /// each screen pixel was painted.
    /// </summary>
    private static Dictionary<(int X, int Y), int> Paint(string[] shape)
    {
        bool Inside(int x, int y) =>
            y >= 0 && y < shape.Length && x >= 0 && x < shape[y].Length && shape[y][x] == '#';

        var painted = new Dictionary<(int, int), int>();
        var pieces = new List<Rect2>();

        for (var y = 0; y < shape.Length; y++)
        for (var x = 0; x < shape[y].Length; x++)
        {
            if (!Inside(x, y))
                continue;

            pieces.Clear();
            HighlightOutline.Edges(new Rect2(x * Cell, y * Cell, Cell, Cell), Edge,
                new Neighbours(Inside(x, y - 1), Inside(x, y + 1),
                               Inside(x - 1, y), Inside(x + 1, y),
                               Inside(x - 1, y - 1), Inside(x + 1, y - 1),
                               Inside(x - 1, y + 1), Inside(x + 1, y + 1)),
                pieces);

            foreach (var piece in pieces)
            for (var py = Mathf.RoundToInt(piece.Position.Y); py < Mathf.RoundToInt(piece.End.Y); py++)
            for (var px = Mathf.RoundToInt(piece.Position.X); px < Mathf.RoundToInt(piece.End.X); px++)
                painted[(px, py)] = painted.GetValueOrDefault((px, py)) + 1;
        }

        return painted;
    }

    /// <summary>
    /// No pixel is painted twice, on a shape with four outside corners.
    ///
    /// <para>A square is the minimal case: its corners are where a full-width horizontal strip and
    /// a full-height vertical strip used to overlap.</para>
    /// </summary>
    [GameTest]
    public static void OutsideCornersArePaintedOnce(Region r)
    {
        foreach (var (name, shape) in Shapes)
        {
            var doubled = Paint(shape).Count(p => p.Value > 1);
            if (doubled > 0)
                throw new AssertionException(
                    $"the '{name}' outline paints {doubled} screen pixels more than once. At the " +
                    "half opacity the game uses for a tool's reach that reads as double strength, " +
                    "so the corners of the disc look solider than its sides. See HighlightOutline.");
        }
    }

    /// <summary>
    /// The border has no holes: every cell of the shape that touches the outside has a painted
    /// pixel on that side, corners included.
    ///
    /// <para>The L is the case that matters. Its concave corner is a square owned by no cell: the
    /// cell it sits in draws no edge on either of those sides, and the two neighbours' edges stop
    /// at their own boundaries.</para>
    /// </summary>
    [GameTest]
    public static void InsideCornersAreNotLeftOpen(Region r)
    {
        // The L, whose inner corner is at the meeting of cells (0,1), (1,1) and (1,0).
        var shape = new[] { "#.", "##" };
        var painted = Paint(shape);

        // The turn belongs to cell (0,1): its neighbour above (0,0) and its neighbour right
        // (1,1) are both inside the shape while the diagonal (1,0) is not, so the border turns
        // through that cell's TOP-RIGHT square. Nothing else can reach it -- (0,1) draws neither a
        // top nor a right edge precisely because those neighbours are inside, (0,0)'s bottom edge
        // stops at y = Cell, and (1,1)'s top edge starts at x = Cell.
        //
        // Probing the top-LEFT of cell (1,1) instead, which is the square on the other side of the
        // same point, proves nothing: (1,1) draws a top edge across all of it either way. That was
        // the first version of this test and it passed against code with no corner handling at
        // all.
        var cornerX = Cell - (int)Edge;
        var cornerY = Cell;
        var missing = 0;
        for (var py = cornerY; py < cornerY + (int)Edge; py++)
        for (var px = cornerX; px < cornerX + (int)Edge; px++)
            if (!painted.ContainsKey((px, py)))
                missing++;

        if (missing > 0)
            throw new AssertionException(
                $"{missing} pixels of the inside corner are unpainted, so the border has a notch " +
                "where it turns. See HighlightOutline's corner cases.");
    }

    private static readonly (string Name, string[] Shape)[] Shapes =
    {
        ("single",  new[] { "#" }),
        ("square",  new[] { "##", "##" }),
        ("L",       new[] { "#.", "##" }),
        ("plus",    new[] { ".#.", "###", ".#." }),
        ("ring",    new[] { "###", "#.#", "###" }),
    };
}
