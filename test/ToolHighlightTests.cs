using System.Collections;
using Atomcraft;
using Atomcraft.TestHarness;
using Godot;

using Session = Atomcraft.TestHarness.Session;

namespace PixelInspector.Test;

/// <summary>
/// The tool-highlight mirror against a real tool highlighting real pixels.
///
/// <para><b>Why this had to be a screen test, and why it took a real one to find the bug.</b> The
/// mirror reads a buffer the game only fills while it is drawing a frame, so nothing short of a
/// session with a window exercises it. Before this existed the suite asserted only that the field
/// the mirror reads still had its name, which is a rename check and not a behavior check, and the
/// mirror shipped with half of the hand tool's highlight missing: the pixels it would pick up were
/// mirrored and the translucent disc showing how far it could reach was not, because the disc
/// falls on air and air was never asked about. <see cref="ToolHighlight"/> has the detail.</para>
///
/// <para>The hand is the tool used here because it is the one with two strengths to tell apart,
/// and because it needs no setup: <c>ToolbarHUD.CurrentToolType</c> falls back to it when nothing
/// has been selected, which is where a fresh session starts.</para>
/// </summary>
public static class ToolHighlightTests
{
    /// <summary>How far from the cursor the hand is told to reach, so the probes below are inside it.</summary>
    private const int BrushSize = 5;

    /// <summary>
    /// Loose pixels under the hand tool come back highlighted, and so does the air around them.
    ///
    /// <para>Three claims, in the order they would break. The material the tool would take is
    /// mirrored at all, which is the mirror working. It is yellow, which is the game's own
    /// highlight colour arriving rather than some unrelated difference between the buffer and the
    /// material's colour being read as a highlight. And the air inside the brush is mirrored too,
    /// at about half the strength, which is the half the first version missed.</para>
    /// </summary>
    [GameTest(RequiresDisplay = true)]
    public static IEnumerator TheHandToolsHighlightReachesThePanel()
    {
        yield return Session.Enter("flat");

        // The session lands with the welcome window up, and the game refuses to draw a tool
        // highlight at all while any window is open -- correctly, since the cursor is then aimed
        // at the interface rather than at the world. Without this the test fails on the first
        // assertion with the mirror working perfectly.
        // Both halves, and they are not the same thing. The harness's helper hides every window
        // node; the gate in front of the highlight reads Gameplay.CurrentWindowId, a separate
        // static that a hidden window does not clear. Hiding alone left it on WelcomeWindow and
        // the game went on refusing to highlight anything.
        Session.CloseAllWindows();
        Gameplay.CloseCurrentWindow();

        var field = Simulation.CurrentState?.Field;
        if (field == null)
            throw new AssertionException("no simulation field inside a session");

        // Well above the flat fixture's surface, so the blob sits in air with nothing static
        // near enough to stop the hand's flood fill.
        var centre = new Vector2I(field.Width / 2, field.Height / 2);

        // Paused, or the sand falls out from under the cursor between the warp and the frame that
        // reads the buffer.
        Session.PauseSimulation();

        var sand = Materials.GetBaseMaterialId("Sand");
        if (!Materials.IsPickupableMaterial(sand))
            throw new AssertionException(
                "Sand is no longer pickupable, so the hand would not highlight it and this test " +
                "is asserting nothing. Pick another loose material.");

        // A three by three blob: big enough that the probe below is unambiguously on sand, small
        // enough to leave air inside the brush for the second half of the test.
        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            field.Set(centre.X + dx, centre.Y + dy, sand);
            Session.MarkDirty(centre.X + dx, centre.Y + dy);
        }

        // Set rather than read: the highlight's radius is a player setting, and a default that
        // changes would silently move the air probe outside the disc.
        if (Game.LocalPlayerSaveData != null)
            Game.LocalPlayerSaveData.HandBrushSize = BrushSize;

        // Read rather than set: the game recomputes it from a held key every frame, so a test
        // cannot hold it off, and nothing here presses it. Checked because it collapses the brush
        // to a single pixel, which would leave the air probe below outside the disc and turn a
        // green test red for a reason that is not the mirror's fault.
        if (InputManager.Precision)
            throw new AssertionException(
                "the precision modifier reads as held, which shrinks the hand's brush to one " +
                "pixel. A stuck key, or KB_Precision now bound to something this run holds down.");

        yield return View.LookAt(centre);
        yield return Cursor.Hover(centre);
        yield return Wait.Frames(3);        // Gameplay.Process has to draw a frame with it there

        var onSand = ToolHighlight.At(centre.X, centre.Y, ColorOf(field, centre.X, centre.Y));
        if (onSand is not { } lit)
            throw new AssertionException(
                "the hand tool is over a pixel it would pick up and the panel sees no highlight " +
                "on it. " + Gate() + " See ToolHighlight.");

        if (lit.R < 0.8f || lit.G < 0.8f || lit.B > 0.2f)
            throw new AssertionException(
                $"the highlight on a picked-up pixel came back {lit} rather than yellow. The " +
                "mirror is reporting a difference that is not the tool's highlight, which means " +
                "the comparison against the material's own colour has gone wrong.");

        // Two cells out, which is inside a brush of five and outside the three by three blob, so
        // it is air. This is the half that was missing: the game writes flat yellow at half alpha
        // over air to show how far the hand reaches, and nothing about the material colour
        // comparison could ever have found it.
        var airProbe = new Vector2I(centre.X + 2, centre.Y + 2);
        if (field.Get(airProbe.X, airProbe.Y) != -1)
            throw new AssertionException(
                $"the air probe at {airProbe} is not air, so the second half of this test is not " +
                "asking what it means to. The fixture's surface may have moved.");

        var onAir = ToolHighlight.At(airProbe.X, airProbe.Y, null);
        if (onAir is not { } reach)
            throw new AssertionException(
                "the hand tool's disc of reach covers this air cell and the panel sees nothing " +
                "there. This is the bug the air path exists to fix: ToolHighlight.At must be " +
                "asked about empty cells, and must judge them on alpha, since there is no " +
                "material colour to differ from.");

        if (reach.A >= 0.9f)
            throw new AssertionException(
                $"the highlight over air came back at alpha {reach.A:0.00}, which is the full " +
                "strength the game uses for a pixel it would actually take. Over air it writes " +
                "half. Either the alpha is not being carried through, or the probe is not on air.");

        // And a picture of what all that looks like in the panel, which is the only way the
        // merging is checked at all: whether forty highlighted cells come out as one outline
        // around the brush or as forty boxes is not a question any assertion above can ask.
        try
        {
            Settings.RequireAlt = false;        // the panel only draws while Alt is held
            yield return Wait.Frames(3);

            // With a brush this size the highlight runs well past the cell under the cursor, so
            // the panel's own white ring says something the tool's outline does not and both are
            // drawn. This is the premise for the second half: assert it before inverting it, or
            // the day the suppression stops working the test below would still be green.
            if (PixelInspectorApi.MarkerSuppressedLastFrame)
                throw new AssertionException(
                    "the panel dropped the hovered pixel's ring while the tool's highlight " +
                    "covers a disc several cells across. The ring is only redundant when the " +
                    "tool has outlined that one cell alone. See Magnifier.");

            var image = Game.CanvasLayer.GetViewport().GetTexture()?.GetImage();
            if (image == null)
                throw new AssertionException("the viewport handed back no image to photograph");
            Artifacts.WriteBytes("pixelinspector-toolhighlight.png", image.SavePngToBuffer());

            // And now the case the suppression exists for. A brush of zero is the hand doing what
            // the chisel always does: outlining exactly the cell under the cursor. Two rings on
            // the same four edges is one ring too many, so the panel's own comes off.
            if (Game.LocalPlayerSaveData != null)
                Game.LocalPlayerSaveData.HandBrushSize = 0;
            yield return Wait.Frames(3);

            if (!PixelInspectorApi.MarkerSuppressedLastFrame)
                throw new AssertionException(
                    "the tool is outlining the hovered cell and nothing else, and the panel drew " +
                    "its white ring on top of it anyway. See Magnifier.");
        }
        finally
        {
            if (Game.LocalPlayerSaveData != null)
                Game.LocalPlayerSaveData.HandBrushSize = BrushSize;
            Settings.Reset();
        }

        Session.ResumeSimulation();
        yield return Session.Leave();
    }

    /// <summary>
    /// Why the game might be refusing to draw a tool highlight at all, reported in the failure
    /// rather than left for whoever reads the red line to go and find. Mirrors the private
    /// <c>Gameplay.ShouldShowToolHighlightOnPixels</c>, which is the gate in front of everything
    /// this test is about.
    /// </summary>
    private static string Gate() =>
        $"tool={ToolbarHUD.CurrentToolType?.Name} window={Gameplay.CurrentWindowId} " +
        $"editor={Game.IsInEditor} overHUD={Gameplay.MouseIsOverHUD} " +
        $"hoveredControl={HoverSystem.CurrentHoveredControl?.Name} " +
        $"avatar={(Avatars.LocalAvatar == null ? "none" : Avatars.LocalAvatar.IsDead ? "dead" : "alive")} " +
        $"interactable={InputManager.IsHoveringInteractable()} " +
        $"brush={Game.LocalPlayerSaveData?.HandBrushSize}";

    /// <summary>
    /// The panel knows when a click would not reach the world.
    ///
    /// <para>Asserts both directions against the game's own state, which is what makes it more
    /// than a restatement of the implementation: with a window open the world is unreachable and
    /// the marker must warn, and with everything closed and the cursor in the world it must not.
    /// A warning that is always on is no warning.</para>
    /// </summary>
    [GameTest(RequiresDisplay = true)]
    public static IEnumerator TheMarkerWarnsWhenAClickWouldNotReachTheWorld()
    {
        yield return Session.Enter("flat");

        // A session lands with the welcome window up, which is exactly the blocked case.
        if (!WorldInput.Blocked)
            throw new AssertionException(
                "a window is open and the panel thinks a click would reach the world. The marker " +
                "would stay a hairline over a pixel nothing can be done to. See WorldInput.");

        Session.CloseAllWindows();
        Gameplay.CloseCurrentWindow();

        var field = Simulation.CurrentState?.Field;
        if (field == null)
            throw new AssertionException("no simulation field inside a session");

        var centre = new Vector2I(field.Width / 2, field.Height / 2);
        yield return View.LookAt(centre);
        yield return Cursor.Hover(centre);

        if (WorldInput.Blocked)
            throw new AssertionException(
                "with every window closed and the cursor over open world, the panel still thinks " +
                "input is blocked, so the marker would warn on every pixel and mean nothing.");

        Session.ResumeSimulation();
        yield return Session.Leave();
    }

    /// <summary>
    /// A mining highlight on two different materials comes back as one colour, so the outlines
    /// around them merge.
    ///
    /// <para><b>The bug this exists for.</b> The drill, chisel and teledrill do not write a
    /// highlight colour, they blend one: <c>HighlightMiningPixel</c> lerps the pixel two thirds of
    /// the way toward yellow, so a third of the material's own colour survives in what lands in
    /// the buffer. Sand, granite and iron under one bore therefore came back as three different
    /// yellows, the panel compared them, found them different, and drew a box around each vein
    /// instead of one ring around the hole. The hand and hammer families write flat colour and
    /// were never affected, which is why the first look at this said merging worked.</para>
    ///
    /// <para>Region tier on purpose. Getting a real drill to highlight real terrain needs a
    /// session, a tool selection and a projected target, and would test the lerp the game already
    /// tests; the claim that broke is about what the mirror does with two colours, and two colours
    /// are free.</para>
    /// </summary>
    [GameTest]
    public static void OneHighlightOnTwoMaterialsIsOneColour(Region r)
    {
        // Two materials far apart in colour, blended the way the game blends them.
        var pale = new Color(0.86f, 0.78f, 0.55f);      // sand, roughly
        var dark = new Color(0.18f, 0.16f, 0.20f);      // basalt, roughly
        const float weight = 0.666f;                    // HighlightMiningPixel's lerp

        var onPale = ToolHighlight.Normalize(pale.Lerp(Colors.Yellow, weight));
        var onDark = ToolHighlight.Normalize(dark.Lerp(Colors.Yellow, weight));

        if (!onPale.IsEqualApprox(onDark))
            throw new AssertionException(
                $"the same mining highlight came back as {onPale} on one material and {onDark} " +
                "on another, so the panel would draw a boundary between them. The outlines merge " +
                "by colour, and the colour has to have the material divided out of it first. See " +
                "ToolHighlight.Normalize.");

        if (!onPale.IsEqualApprox(new Color(Colors.Yellow, 1f)))
            throw new AssertionException(
                $"a highlight lerped toward yellow normalized to {onPale} rather than to yellow. " +
                "The snap is picking the wrong one of the three colours the game highlights in.");

        // The hand's two strengths have to survive it, or the ring around the pixels a tool would
        // take merges into the disc showing how far it can reach and stops being a separate claim.
        var takes = ToolHighlight.Normalize(new Color(Colors.Yellow, 1f));
        var reaches = ToolHighlight.Normalize(new Color(Colors.Yellow, 0.5f));
        if (takes.IsEqualApprox(reaches))
            throw new AssertionException(
                "full strength and half strength normalized to the same colour, so the ring " +
                "around what the hand would pick up merges into the disc of what it can reach.");

        // And a crowbar's red stays red rather than snapping to the yellow everything else uses.
        var crowbar = ToolHighlight.Normalize(dark.Lerp(Colors.Red, weight));
        if (!crowbar.IsEqualApprox(new Color(Colors.Red, 1f)))
            throw new AssertionException(
                $"a highlight lerped toward red normalized to {crowbar}. Snapping every highlight " +
                "to yellow would merge two tools' marks into one shape.");
    }

    /// <summary>The colour the panel would draw a cell in, which is what the mirror compares against.</summary>
    private static Color? ColorOf(SimField field, int x, int y)
    {
        var id = field.Get(x, y);
        if (id < 0)
            return null;
        return MaterialColorIndex.TryGetColor(id, x, y, Gameplay.AnimTick, out var color) ? color : null;
    }
}
