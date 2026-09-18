using Atomcraft;
using Godot;
using HarmonyLib;

namespace PixelInspector;

/// <summary>
/// Which pixels the player's tool is currently highlighting, read back out of the game rather
/// than worked out again.
///
/// <para><b>Why reading rather than recomputing.</b> The game highlights the area a tool is about
/// to affect, and what that area is depends on the tool: the hand and blender flood a contiguous
/// region out to a brush size the player sets, the drill and teledrill stamp a circular or square
/// mask at a projected target <i>and</i> a second one at the avatar, the chisel marks one pixel,
/// and the hammer family draws a shape that can be a line, a rectangle, an oval, hollow or
/// filled, sometimes dotted. Reimplementing that would be several hundred lines tracking a moving
/// target, and it would be wrong in a different way for every tool the game adds.</para>
///
/// <para><b>What it reads.</b> <c>Gameplay</c> builds the terrain image into a private
/// <c>byte[] PixelsData</c>, and the highlight is not a separate layer: it is written into those
/// same bytes. So a pixel is highlighted exactly when what the game wrote differs from the colour
/// the material would have had on its own, which this mod already computes for the magnifier
/// through <c>MaterialColorIndex</c>. Comparing the two finds the highlight whatever drew it, and
/// hands back the colour it was drawn in, so a crowbar's red stays red and a hammer's yellow stays
/// yellow.</para>
///
/// <para><b>The two tools write it in different ways, and the first attempt only caught one.</b>
/// The drill, chisel and hammer family go through <c>HighlightMiningPixel</c>, which <i>lerps</i>
/// the existing bytes two thirds toward yellow or red, so the result is a shifted version of the
/// material's colour and an RGB comparison finds it. The hand and the blender gun go through
/// <c>HandToolHighlight</c>, which does not lerp: it <i>overwrites</i> RGB with flat yellow or
/// flat cyan and puts the interesting part in the <b>alpha</b> -- full strength for a pixel the
/// tool would actually take, and half for one inside the brush but not reachable, which is the
/// translucent disc the hand shows to mean "this is as far as I could reach". Two things follow.
/// The alpha has to be read, or active and inactive are indistinguishable. And the disc falls
/// mostly on <b>air</b>, where there is no material colour to compare against at all, so air has
/// to be asked about rather than skipped -- which is what the first version did, and why the disc
/// was missing from the panel while the pixels inside it were not.</para>
///
/// <para><b>What that costs in honesty.</b> The comparison cannot tell a tool highlight from the
/// other things that tint the same buffer: the eyedropper's matching-material highlight, and the
/// heat and hardness vision modes. Those are all "the game is drawing attention to this pixel",
/// which is the thing being mirrored, so including them is right more often than not. It is
/// worth knowing rather than being surprised by.</para>
///
/// <para><b>One private field, checked at startup.</b> <c>PixelsData</c> is private and reached
/// by name, which is exactly what <see cref="GameBindings"/> exists for. Losing it costs the
/// highlight mirror and nothing else: <see cref="Available"/> goes false and the panel draws
/// everything else as before.</para>
/// </summary>
internal static class ToolHighlight
{
    private static readonly AccessTools.FieldRef<byte[]>? PixelsData =
        AccessTools.Field(typeof(Gameplay), "PixelsData") is { } field
            ? AccessTools.StaticFieldRefAccess<byte[]>(field)
            : null;

    /// <summary>Whether the game still has the buffer this reads. Checked at startup and by a test.</summary>
    internal static bool Available => PixelsData != null;

    /// <summary>
    /// How far apart two colours must be before this counts as a highlight.
    ///
    /// <para>The lerping tools move two thirds of the way to the highlight colour, so a real
    /// highlight moves a channel by a lot. The threshold is well under that and well over the one
    /// step of rounding that converting a float colour to a byte and back can introduce.</para>
    /// </summary>
    private const int Threshold = 24;

    /// <summary>
    /// How opaque a flat colour written over empty air has to be to count as drawn there.
    ///
    /// <para>Air is the case with nothing to compare against, so the only question that can be
    /// asked of it is whether anything was painted there at all. The hand writes 128 for the
    /// reachable part of its disc and 64 for the rest, so anything above a token amount is a real
    /// mark rather than the residue of whatever the buffer last held.</para>
    /// </summary>
    private const int AirAlpha = 32;

    /// <summary>
    /// The colour the game is highlighting a pixel in, or null if it is not highlighting it.
    ///
    /// <para><paramref name="plain"/> is what the pixel would look like unhighlighted, or null for
    /// a cell holding nothing. The caller has already computed it and this does not recompute
    /// it.</para>
    ///
    /// <para>The returned colour carries the game's own alpha, so the caller can draw the
    /// tool's half-strength reach at half strength rather than flattening the distinction the
    /// game is drawing.</para>
    /// </summary>
    internal static Color? At(int tileX, int tileY, Color? plain)
    {
        if (PixelsData == null)
            return null;

        var data = PixelsData();
        if (data == null)
            return null;

        var window = Gameplay.GetWindowRectAndOriginForRenderingWorld().Item1;
        var renderX = tileX - window.min.X;
        var renderY = tileY - window.min.Y;
        if (renderX < 0 || renderY < 0 || renderX >= window.width || renderY >= window.height)
            return null;

        var i = (renderY * window.width + renderX) * 4;
        if (i < 0 || i + 3 >= data.Length)
            return null;

        int r = data[i], g = data[i + 1], b = data[i + 2], a = data[i + 3];
        var found = new Color(r / 255f, g / 255f, b / 255f, a / 255f);

        // Empty air. The game writes nothing here for the terrain itself, so anything opaque
        // enough to see is something a tool put there.
        if (plain is not { } unhighlighted)
            return a >= AirAlpha ? Normalize(found) : null;

        var far = Math.Abs(r - (int)(unhighlighted.R * 255f)) +
                  Math.Abs(g - (int)(unhighlighted.G * 255f)) +
                  Math.Abs(b - (int)(unhighlighted.B * 255f));

        return far >= Threshold ? Normalize(found) : null;
    }

    /// <summary>The three colours the game highlights in.</summary>
    private static readonly Color[] Hues = { Colors.Yellow, Colors.Red, Colors.Cyan };

    /// <summary>
    /// The colour a highlight <i>means</i>, with the material underneath it divided out.
    ///
    /// <para><b>Why the raw colour will not do.</b> The two families of tool mark a pixel in
    /// completely different ways. The hand, the blender and the hammer family <i>write</i> a flat
    /// colour, so two adjacent pixels of different materials come back identical. The drill, the
    /// chisel and the teledrill go through <c>HighlightMiningPixel</c>, which <i>lerps</i> the
    /// pixel two thirds of the way toward yellow -- so what lands in the buffer still carries a
    /// third of the material's own colour, and sand next to granite next to iron are three
    /// different yellows. Anything comparing the raw colours therefore sees a boundary at every
    /// material boundary, which is what made a drill's outline over mixed terrain come apart into
    /// one box per vein instead of one ring around the bore.</para>
    ///
    /// <para><b>Snapping rather than inverting the lerp.</b> Undoing the blend needs the exact
    /// weight and the exact pre-blend colour and is wrong for the tools that never blended. A
    /// two-thirds lerp toward yellow is already far nearer yellow than red or cyan, so the nearest
    /// of the three is the same answer without depending on the constant, and it is the same
    /// answer for a flat write, which is already one of the three exactly.</para>
    ///
    /// <para>Alpha is bucketed rather than kept, for the same reason. The hand puts its meaning
    /// there -- full strength for a pixel it would take, half for one it can only reach -- while
    /// the lerping tools carry the material's own alpha through untouched. Two buckets keep the
    /// hand's distinction and stop a material's transparency from being read as one.</para>
    ///
    /// <para>The one case this gets wrong is the editor's highlight, which
    /// <c>GetHighlightColor</c> can set to a selected material's colour rather than to one of the
    /// three. That colour would snap to whichever of the three it happens to sit nearest. It costs
    /// nothing today, because <c>ShouldShowToolHighlightOnPixels</c> refuses outright while
    /// <c>Game.IsInEditor</c>, so the editor never reaches here.</para>
    /// </summary>
    internal static Color Normalize(Color found)
    {
        var best = Hues[0];
        var bestDistance = float.MaxValue;
        foreach (var hue in Hues)
        {
            var distance = Math.Abs(hue.R - found.R) +
                           Math.Abs(hue.G - found.G) +
                           Math.Abs(hue.B - found.B);
            if (distance >= bestDistance)
                continue;
            bestDistance = distance;
            best = hue;
        }

        return new Color(best, found.A >= FullStrength ? 1f : 0.5f);
    }

    /// <summary>Above this the game is marking a pixel it would act on, below it one it can only reach.</summary>
    private const float FullStrength = 0.75f;
}
