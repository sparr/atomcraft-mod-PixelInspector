using Godot;
using PixelArt;

namespace PixelInspector;

/// <summary>
/// The tick and the cross, drawn as pixel art of this mod's own rather than borrowed from the
/// game.
///
/// <para><b>Why not the game's own <c>ui_status_checkmark</c> and <c>ui_status_x</c>.</b> They are
/// 12x12 with strokes about three pixels thick, which is a quarter of the glyph. That is right for
/// a HUD badge sitting on its own and wrong here, where the mark shares a cell with a material
/// name: at any size where the mark reads, it is a solid diagonal bar through the middle of the
/// text. And it could not be made bigger to fix that, because scaling the art scales the stroke
/// with it. Drawing the shape instead of loading it separates the two: the glyph grows with the
/// cell and the stroke stays one unit, the way a letter of the bitmap font does.</para>
///
/// <para>So the mark is now drawn across the whole cell, behind the label, the same way the phase
/// shapes already were, instead of stacked above it in half the height. That is about three times
/// the size it was, at a third of the stroke weight.</para>
///
/// <para><b>One unit per lit square, at a whole number of screen pixels.</b> Same rule the rest of
/// the drawing follows: a fractional unit would hand the renderer rectangles it can only resolve
/// by blurring, which is exactly what a one-unit stroke cannot survive.</para>
/// </summary>
internal static class MarkArt
{
    // Eleven wide, so a one-square stroke is a eleventh of the glyph rather than the borrowed
    // art's quarter. The tick is eight rows rather than eleven because that is its natural shape;
    // the draw centres whatever it is given, so the two need not agree.
    private static readonly string[] Tick =
    {
        ".........#.",
        "........#..",
        ".......#...",
        "......#....",
        ".....#.....",
        "#...#......",
        ".#.#.......",
        "..#........",
    };

    private static readonly string[] Cross =
    {
        "#.........#",
        ".#.......#.",
        "..#.....#..",
        "...#...#...",
        "....#.#....",
        ".....#.....",
        "....#.#....",
        "...#...#...",
        "..#.....#..",
        ".#.......#.",
        "#.........#",
    };

    /// <summary>How much of the cell the glyph's longer side is allowed to take.</summary>
    private const float Fill = 0.92f;

    /// <summary>
    /// Draws a mark across <paramref name="screen"/>, centred, or nothing for
    /// <see cref="MarkIcon.None"/>.
    ///
    /// <para><b>No outline.</b> Each lit square used to be drawn twice, once grown by a screen
    /// pixel in black and once in colour, on the theory that a one-unit stroke has no mass to be
    /// seen by. What it actually produced was a black cross with a red core: the halo is two
    /// screen pixels wider than the stroke it surrounds, so it was the larger part of the mark and
    /// the colour was a line down the middle of it. The colour is strong and the glyph is large,
    /// which between them are enough.</para>
    /// </summary>
    internal static void Draw(Canvas canvas, Rect2 screen, MarkIcon icon, Color color,
                              Rect2? clip = null)
    {
        var glyph = icon switch
        {
            MarkIcon.Check  => Tick,
            MarkIcon.Cross => Cross,
            _              => null,
        };
        if (glyph == null)
            return;

        var rows = glyph.Length;
        var cols = glyph[0].Length;
        var unit = Mathf.Floor(Mathf.Min(screen.Size.X, screen.Size.Y) * Fill / Mathf.Max(rows, cols));
        if (unit < 1f)
            return;                         // smaller than a screen pixel a square: nothing to draw

        var origin = new Vector2(
            Mathf.Round(screen.GetCenter().X - cols * unit / 2f),
            Mathf.Round(screen.GetCenter().Y - rows * unit / 2f));

        // Every square is a rectangle, which is the one thing that can be clipped without the
        // library's help: the mark survives on a cell the panel is only showing part of, where the
        // label and the shape cannot.
        for (var row = 0; row < rows; row++)
        for (var col = 0; col < cols; col++)
        {
            if (glyph[row][col] != '#')
                continue;
            var square = new Rect2(origin.X + col * unit, origin.Y + row * unit, unit, unit);
            if (clip is { } bounds)
            {
                square = square.Intersection(bounds);
                if (square.Size.X <= 0f || square.Size.Y <= 0f)
                    continue;
            }
            canvas.DrawFill(square, color);
        }
    }
}
