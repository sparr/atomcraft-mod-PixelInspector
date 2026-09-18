using PixelArt;
using System.Collections;
using Atomcraft;
using Atomcraft.TestHarness;
using Godot;

using Session = Atomcraft.TestHarness.Session;

namespace PixelInspector.Test;

/// <summary>
/// What needs a display, and nothing that does not.
///
/// <para>Every test here is marked <c>RequiresDisplay</c> and abstains without one, so the
/// ordinary headless suite stays green and <c>./run-tests.sh --headful</c> is where they count.
/// The abstention is the harness's doing: a test marked this way is reported as
/// <c>inapplicable</c> rather than passed, so a headless run cannot be mistaken for coverage it
/// did not have.</para>
///
/// <para><b>Why the end of this mod's chain genuinely needs pixels.</b> The pass stops at
/// <c>AnnotationCanvas.Begin</c> when there is no display, before it walks a single cell --
/// which is correct, since there is nothing to draw on, and means the cell walk and everything
/// after it is only ever exercised here.</para>
/// </summary>
public static class ScreenTests
{
    /// <summary>
    /// A pixel only half inside the panel is annotated too.
    ///
    /// <para><b>The bug.</b> The panel skipped everything but the colour on a cell it could not
    /// show whole, so the ring of partly-visible pixels around its rim were flat blocks with no
    /// arrow, no shape and no name -- 72 cells of 361 at the default zoom. The reason was real: a
    /// label laid out on a half-visible cell spills outside the panel, and until Pixel Art 0.4.0
    /// a consumer could not set a clip rectangle. Two workarounds came and went before the library
    /// grew one -- laying the cell out against its visible part, which made annotations resize as
    /// the view scrolled, and measuring each element to drop the ones that crossed the edge, which
    /// was stable but still lost 55 of the 72.</para>
    ///
    /// <para><b>Why counting is a better test than a picture.</b> The bench photograph cannot show
    /// this: the bench is shorter than the panel, so none of its machines reach the rim, and
    /// arranging one to straddle the edge would depend on the window size. Filling the whole view
    /// with one machine and comparing the two counters asks the question directly -- every pixel
    /// the panel drew must also have been annotated -- and it is exactly the comparison that
    /// failed before.</para>
    /// </summary>
    [GameTest(RequiresDisplay = true)]
    public static IEnumerator APixelHalfInsideThePanelIsStillAnnotated()
    {
        yield return Session.Enter("flat");
        Session.CloseAllWindows();

        var field = Simulation.CurrentState?.Field;
        if (field == null)
            throw new AssertionException("no simulation field inside a session");

        var centre = new Vector2I(field.Width / 2, field.Height / 2);
        Session.PauseSimulation();

        // A block wider and taller than the panel can show at any zoom, so every cell in view is
        // a machine and the rim is guaranteed to be clipped ones.
        var conveyor = Materials.GetBaseMaterialId("Conveyor Left");
        const int reach = 40;
        for (var dy = -reach; dy <= reach; dy++)
        for (var dx = -reach; dx <= reach; dx++)
        {
            field.Set(centre.X + dx, centre.Y + dy, conveyor);
            Session.MarkDirty(centre.X + dx, centre.Y + dy);
        }

        yield return View.LookAt(centre);
        yield return Cursor.Hover(centre);

        try
        {
            Settings.RequireAlt = false;
            PixelInspectorApi.ResetCounters();
            yield return Wait.Frames(3);

            var drawn = PixelInspectorApi.PanelPixelsLastFrame;
            var whole = PixelInspectorApi.PanelWholeCellsLastFrame;
            var annotated = PixelInspectorApi.PanelAnnotatedLastFrame;

            if (drawn <= 0)
                throw new AssertionException(
                    "the panel drew no pixels, so this test asks nothing -- " +
                    PixelInspectorApi.DescribeState());

            if (drawn <= whole)
                throw new AssertionException(
                    $"all {drawn} cells the panel drew were wholly inside it, so there were no " +
                    "clipped ones to ask about. The panel's size or the zoom has changed such " +
                    "that its edges land on cell boundaries.");

            // Every cell in view is the same machine and the canvas is clipped to the panel, so
            // every one of them gets annotated -- the clipped ones cut at the glass edge rather
            // than skipped. Anything less means a cell was passed over.
            if (annotated != drawn)
                throw new AssertionException(
                    $"the panel drew {drawn} cells, {whole} of them whole, and annotated " +
                    $"{annotated}. The {drawn - annotated} missing are partly-visible cells being " +
                    "skipped instead of clipped. See Magnifier's cell loop and canvas.Clip.");
        }
        finally
        {
            Settings.Reset();
        }

        Session.ResumeSimulation();
        yield return Session.Leave();
    }

    /// <summary>
    /// The whole chain, end to end: a programmed filter placed in a real world, the camera and
    /// the cursor put on it, and the pass reporting that it walked that cell and had something
    /// to say about it.
    ///
    /// <para>This is the one test that exercises the radius walk, the projection, and the draw
    /// calls. It asserts on the pass's own counters rather than on the frame's pixels: reading
    /// back a glyph would be asserting the font renders, which <see cref="FontTests"/> already
    /// covers without a window, and it would be at the mercy of the fractional rescale between
    /// the 1600x900 render target and whatever size the window is.</para>
    /// </summary>
    [GameTest(RequiresDisplay = true)]
    public static IEnumerator AProgrammedFilterUnderTheCursorIsAnnotated()
    {
        yield return Session.Enter("flat");

        var field = Simulation.CurrentState?.Field;
        if (field == null)
            throw new AssertionException("no simulation field inside a session");

        var tile = new Vector2I(field.Width / 2, field.Height / 2);

        // The view is aimed first: aiming at a region the game has not activated makes it
        // stream that region's planet segment in over anything written there beforehand.
        yield return View.LookAt(tile);

        Session.PauseSimulation();
        field.Set(tile.X, tile.Y,
                  BaseMaterial.ToMatchFilterOffset(Materials.GetBaseMaterialId("Water")));
        Session.MarkDirty(tile.X, tile.Y);

        yield return Cursor.Hover(tile);

        try
        {
            Settings.RequireAlt = false;
            // This test is about the world marks specifically, which are opt-in: the magnifier is
            // what the mod draws by default and it is covered by the bench test below.
            Settings.WorldMarks = true;
            PixelInspectorApi.ResetCounters();

            yield return Wait.Frames(3);

            if (PixelInspectorApi.ScannedLastFrame == 0)
                throw new AssertionException(
                    "the pass walked no cells on the last frame, though the cursor is over the " +
                    "world and a display is present. The radius, the visible-tile intersection, " +
                    "or the mouse-tile read is wrong -- " + PixelInspectorApi.DescribeState());

            var expected = (2 * Settings.Radius + 1) * (2 * Settings.Radius + 1);
            if (PixelInspectorApi.ScannedLastFrame > expected)
                throw new AssertionException(
                    $"the pass walked {PixelInspectorApi.ScannedLastFrame} cells, more than the " +
                    $"{expected} a radius of {Settings.Radius} allows. It is not staying inside " +
                    "the block around the cursor, which is what keeps it cheap and readable.");

            if (PixelInspectorApi.AnnotatedLastFrame < 1)
                throw new AssertionException(
                    $"the pass walked {PixelInspectorApi.ScannedLastFrame} cells and annotated " +
                    "none of them, though a programmed match filter is under the cursor");
        }
        finally
        {
            Settings.Reset();
        }

        Session.ResumeSimulation();
        yield return Session.Leave();
    }

    /// <summary>
    /// Switching the mod off stops the drawing on the next frame, with nothing left on screen.
    ///
    /// <para>A mod that draws every frame has no effect to undo -- clearing the canvas is the
    /// whole of it -- and this is the check that the clear actually happens rather than the last
    /// frame's labels being left frozen where they were. The same path runs when the player
    /// releases Alt, which is the case a person would notice.</para>
    /// </summary>
    [GameTest(RequiresDisplay = true)]
    public static IEnumerator SwitchingOffStopsTheDrawingAtOnce()
    {
        yield return Session.Enter("flat");

        var field = Simulation.CurrentState?.Field;
        if (field == null)
            throw new AssertionException("no simulation field inside a session");

        var tile = new Vector2I(field.Width / 2, field.Height / 2);
        yield return View.LookAt(tile);

        Session.PauseSimulation();
        field.Set(tile.X, tile.Y, Materials.GetBaseMaterialId("Conveyor Left"));
        Session.MarkDirty(tile.X, tile.Y);
        yield return Cursor.Hover(tile);

        try
        {
            Settings.RequireAlt = false;
            Settings.WorldMarks = true;
            yield return Wait.Frames(2);
            if (PixelInspectorApi.AnnotatedLastFrame < 1 || PixelInspectorApi.PanelPixelsLastFrame < 1)
                throw new AssertionException(
                    "nothing was drawn with the mod on, so the off half below proves nothing -- " +
                    PixelInspectorApi.DescribeState());

            PixelInspectorApi.Enabled = false;
            yield return Wait.Frames(2);

            // Both halves, because they are separate canvases with separate passes and switching
            // the mod off has to reach both. An earlier version of this test watched only the
            // world marks, which are the half that is off by default.
            if (PixelInspectorApi.AnnotatedLastFrame != 0)
                throw new AssertionException(
                    $"{PixelInspectorApi.AnnotatedLastFrame} pixels still carried world marks " +
                    "with the mod switched off");
            if (PixelInspectorApi.PanelPixelsLastFrame != 0)
                throw new AssertionException(
                    $"the magnifier still drew {PixelInspectorApi.PanelPixelsLastFrame} pixels " +
                    "with the mod switched off");
        }
        finally
        {
            PixelInspectorApi.ResetState();
        }

        Session.ResumeSimulation();
        yield return Session.Leave();
    }

    /// <summary>
    /// A bench of one machine from each family, zoomed in, labelled, and photographed.
    ///
    /// <para>The assertion is that every one of them got a label. The <b>screenshot</b> is the
    /// point: this mod's whole output is marks placed on cells, and nothing but a picture
    /// answers whether they land on the right cells, fit inside them, and can be read over the
    /// machine colours underneath. It lands in the run's artifacts as
    /// <c>pixelinspector-bench.png</c>.</para>
    ///
    /// <para>Read back from <c>GetViewport().GetTexture()</c>, which samples the 1600x900 render
    /// target <i>before</i> the engine rescales the finished frame to the window. That is the
    /// right surface to judge: the rescale is fractional at most window sizes and would show
    /// every bitmap glyph with rows doubled or dropped, which is a display concern and not
    /// something this mod can do anything about from inside the frame.</para>
    /// </summary>
    [GameTest(RequiresDisplay = true)]
    public static IEnumerator AMachineBenchIsLabelledAndPhotographed()
    {
        yield return Session.Enter("flat");

        var field = Simulation.CurrentState?.Field;
        if (field == null)
            throw new AssertionException("no simulation field inside a session");

        var centre = new Vector2I(field.Width / 2, field.Height / 2);

        // Aimed first: aiming at a region the game has not activated makes it stream that
        // region's planet segment in over anything written there beforehand.
        yield return View.LookAt(centre);
        Session.PauseSimulation();

        // One of each family, so the picture shows every shape the mod can draw: a direction
        // nub on its own, a nub with a label, labels of one to four characters, a balance's two
        // nubs, and a dimmed off machine.
        var bench = new[]
        {
            Named("Conveyor Left"),
            Named("Pump Up"),
            Named("And Gate (Up) (On)"),
            Named("Xor Gate (Down) (Off)"),
            Named("Latch (Right) (On) (Rest)"),
            // Written as raw ids, because a programmed filter is not a material of its own: it
            // is the target's id with a quadrant bit set. Note the nonmatch and sensor tags are
            // in the negative quadrants, so a tagged id is routinely a negative short and must
            // not be mistaken for a lookup failure.
            BaseMaterial.ToMatchFilterOffset(Named("Water")),
            BaseMaterial.ToNonMatchFilterOffset(Named("Iron")),
            Named("Heating Element (1000)"),
            Named("3 Step Oscillator (On)"),
            Named("Allow Solids"),
            Named("Block Liquids"),
            Named("Allow Gases"),
            Named("Allow Players"),
            Named("Water Filter"),
            Named("Block Water"),
            Named("Balance Input Up to Right"),
        };

        var left = centre.X - bench.Length / 2;
        for (var i = 0; i < bench.Length; i++)
        {
            field.Set(left + i, centre.Y, bench[i]);
            Session.MarkDirty(left + i, centre.Y);
        }

        static short Named(string material)
        {
            var id = Materials.GetBaseMaterialId(material);
            return id >= 0
                ? id
                : throw new AssertionException($"the game no longer has a material named '{material}'");
        }

        // The game's own closest zoom, 12 screen pixels a world pixel. Deliberately not the 8x
        // ceiling the harness can raise: the point of the magnifier is that it is legible at the
        // zoom a player without a zoom mod actually has, and a picture taken at 96 pixels a cell
        // would be a picture of a situation almost nobody is in.
        yield return View.SetZoom(1.5f);
        yield return Cursor.Hover(centre);

        try
        {
            Settings.RequireAlt = false;
            // The magnifier is what the mod draws by default, and it is what this photographs.
            // The world marks are switched on as well so the picture shows both halves at once.
            Settings.WorldMarks = true;
            Settings.Radius = bench.Length / 2 + 1;
            PixelInspectorApi.ResetCounters();
            yield return Wait.Frames(3);

            if (PixelInspectorApi.AnnotatedLastFrame < bench.Length)
                throw new AssertionException(
                    $"{PixelInspectorApi.AnnotatedLastFrame} of the {bench.Length} machines on " +
                    "the bench were annotated. Every one of them is a family the mod claims to " +
                    "label -- " + PixelInspectorApi.DescribeState());

            if (PixelInspectorApi.PanelPixelsLastFrame <= 0)
                throw new AssertionException(
                    "the magnifier drew no pixels, so the picture below is of the world marks " +
                    "only -- " + PixelInspectorApi.DescribeState());

            var image = Game.CanvasLayer.GetViewport().GetTexture()?.GetImage();
            if (image == null)
                throw new AssertionException("the viewport handed back no image to photograph");
            Artifacts.WriteBytes("pixelinspector-bench.png", image.SavePngToBuffer());
        }
        finally
        {
            Settings.Reset();
        }

        Session.ResumeSimulation();
        yield return Session.Leave();
    }
}
