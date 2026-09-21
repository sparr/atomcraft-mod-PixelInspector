using Godot;
using PixelArt;

namespace PixelInspector;

/// <summary>
/// The line from a pixel's centre toward each neighbour it is wired to.
///
/// <para><b>Matched to the gate art, not invented.</b> A line stops the same distance from the
/// edge that a gate glyph is trimmed by -- a sixteenth of the cell, floored, never under a pixel
/// -- so a wire and a gate leave the same margin and neither touches the next cell. It is drawn
/// at the width the gate art gives its own wires, an eighth of the cell rounded to even, so a run
/// of wire meeting a gate's leg is one continuous thickness rather than two.</para>
///
/// <para><b>Even width, for the reason the gate art uses one.</b> A line along the centre of a
/// cell sits on a half coordinate when the cell is an even number of pixels across, and only an
/// even width straddles that exactly. An odd one lands half a pixel off and the error flips sign
/// between a line drawn up and the same line drawn down.</para>
///
/// <para><b>Coloured by lightness, not by hue.</b> The line takes the machine's own hue and
/// saturation and moves only its value, away from wherever the pixel already is. So it reads as
/// part of that pixel rather than as something laid over it, and it stays legible on a palette
/// this mod does not choose -- a dark wire gets a light line, a bright one gets a dark line.</para>
/// </summary>
internal static class CircuitLines
{
    /// <summary>
    /// How far a line stops short of the edge: a sixteenth, floored, at least one pixel. The same
    /// rule <c>art/gates/pack_atlas.py</c> trims a glyph by.
    /// </summary>
    internal static float Border(float cell) => Mathf.Max(1f, Mathf.Floor(cell / 16f));

    /// <summary>
    /// The wire width: an eighth of the cell, rounded to even and never under two. That is what
    /// the gate art's own heavy stroke works out to -- 2 at a 16 pixel cell, 12 at 96.
    /// </summary>
    internal static float Width(float cell)
    {
        var w = Mathf.RoundToInt(cell / 8f);
        if (w % 2 != 0)
            w++;
        return Mathf.Max(2, w);
    }

    /// <summary>
    /// The line's colour: the pixel's own hue and saturation, with the value pushed away from it.
    ///
    /// <para>Pushed rather than inverted. Inverting the value sends a mid-grey pixel to another
    /// mid-grey, which is the case that needs the most help; moving it a fixed distance away from
    /// wherever it already is always separates the two.</para>
    /// </summary>
    internal static Color Tint(Color under)
    {
        var v = under.V;
        // Far enough to read at a glance, and the direction chosen by which side has more room.
        var lifted = v < 0.5f ? Mathf.Min(1f, v + 0.45f) : Mathf.Max(0f, v - 0.45f);
        return Color.FromHsv(under.H, under.S, lifted, under.A);
    }

    /// <summary>
    /// Draws one link, from the centre of <paramref name="cell"/> toward the neighbour at
    /// <paramref name="step"/>.
    ///
    /// <para>Built as a rectangle rather than a stroked line so it lands on whole pixels: the four
    /// straight directions give an axis-aligned bar, and a diagonal is stepped one pixel at a time
    /// so its stairs are even, the same way the gate art's 45 degree legs are.</para>
    /// </summary>
    internal static void Draw(Canvas canvas, Rect2 cell, Vector2I step, Color colour)
    {
        var side = Mathf.Min(cell.Size.X, cell.Size.Y);
        var w = Width(side);
        var reach = side / 2f - Border(side);
        if (reach <= 0f)
            return;

        var mid = cell.Position + cell.Size / 2f;

        if (step.X == 0 || step.Y == 0)
        {
            // Straight: one bar from the centre outward. Half the width either side of the axis,
            // which for an even width straddles the cell's centre line exactly.
            var half = w / 2f;
            var bar = step.X != 0
                ? new Rect2(step.X > 0 ? mid.X : mid.X - reach, mid.Y - half, reach, w)
                : new Rect2(mid.X - half, step.Y > 0 ? mid.Y : mid.Y - reach, w, reach);
            canvas.DrawFill(bar, colour);
            return;
        }

        // Diagonal: stepped, one pixel across and one down per step, so the stairs are regular.
        // The reach is measured along the axis rather than along the diagonal, which keeps a
        // diagonal link the same distance from the corner as a straight one is from the edge.
        var n = Mathf.FloorToInt(reach);
        for (var i = 0; i < n; i++)
        {
            var x = mid.X + step.X * i - w / 2f;
            var y = mid.Y + step.Y * i - w / 2f;
            canvas.DrawFill(new Rect2(Mathf.Round(x), Mathf.Round(y), w, w), colour);
        }
    }
}
