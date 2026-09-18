using Godot;

namespace PixelInspector;

/// <summary>
/// Which neighbours of a cell carry the same highlight. Eight flags, because the diagonals decide
/// the inside corners.
/// </summary>
internal readonly record struct Neighbours(
    bool Up, bool Down, bool Left, bool Right,
    bool UpLeft, bool UpRight, bool DownLeft, bool DownRight);

/// <summary>
/// The rectangles that trace the border of a highlighted region, one cell at a time.
///
/// <para><b>Why this is not four strips.</b> The obvious version draws each exposed side as a strip
/// running the full width or height of the cell, and it is wrong twice over. At an <b>outside</b>
/// corner the horizontal and the vertical strip overlap in the corner square, so it is painted
/// twice -- invisible at full opacity and obvious at the half the game uses for a tool's reach,
/// where the corners came out twice as solid as the sides. At an <b>inside</b> corner nothing is
/// painted at all: the cell draws no edge on either of those sides, because both its orthogonal
/// neighbours share the highlight, and the two neighbours' own edges stop at their own boundaries,
/// so the border turns through a square nobody owns.</para>
///
/// <para>So the horizontals run full width, the verticals stop short of them, and a corner square
/// is added where the border turns. Every boundary pixel is covered exactly once, which is the
/// property <c>HighlightOutlineTests</c> asserts directly.</para>
///
/// <para>Separated from the draw for that test. The shape is pure geometry over eight booleans and
/// needs no canvas, no panel and no game; checking it by eye on a screenshot is how both bugs got
/// in.</para>
/// </summary>
internal static class HighlightOutline
{
    /// <summary>
    /// Appends this cell's share of the border to <paramref name="into"/>, which the caller reuses
    /// across cells rather than allocating per cell per frame.
    /// </summary>
    internal static void Edges(Rect2 cell, float edge, in Neighbours n, List<Rect2> into)
    {
        var above = n.Up ? 0f : edge;
        var below = n.Down ? 0f : edge;

        if (!n.Up)
            into.Add(new Rect2(cell.Position.X, cell.Position.Y, cell.Size.X, edge));
        if (!n.Down)
            into.Add(new Rect2(cell.Position.X, cell.End.Y - edge, cell.Size.X, edge));
        if (!n.Left)
            into.Add(new Rect2(cell.Position.X, cell.Position.Y + above,
                               edge, cell.Size.Y - above - below));
        if (!n.Right)
            into.Add(new Rect2(cell.End.X - edge, cell.Position.Y + above,
                               edge, cell.Size.Y - above - below));

        // The turns: both orthogonal neighbours are inside the region and the diagonal one is not.
        if (n.Up && n.Left && !n.UpLeft)
            into.Add(new Rect2(cell.Position.X, cell.Position.Y, edge, edge));
        if (n.Up && n.Right && !n.UpRight)
            into.Add(new Rect2(cell.End.X - edge, cell.Position.Y, edge, edge));
        if (n.Down && n.Left && !n.DownLeft)
            into.Add(new Rect2(cell.Position.X, cell.End.Y - edge, edge, edge));
        if (n.Down && n.Right && !n.DownRight)
            into.Add(new Rect2(cell.End.X - edge, cell.End.Y - edge, edge, edge));
    }
}
