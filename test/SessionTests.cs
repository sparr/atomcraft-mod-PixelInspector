using PixelArt;
using System.Collections;
using Atomcraft;
using Atomcraft.TestHarness;
using Godot;

// The game has a Session type of its own; the harness's is the one meant here.
using Session = Atomcraft.TestHarness.Session;

namespace PixelInspector.Test;

/// <summary>
/// The mod in a real world rather than in a private rectangle.
///
/// <para>A session costs seconds where a region test costs milliseconds, so there are few of
/// these and each one asks something a region cannot. Everything about <i>what</i> is annotated
/// is asserted far more cheaply in <see cref="VocabularyTests"/> and <see cref="CellTests"/>.
/// </para>
///
/// <para><b>The wiring test below runs headless, and only because the test mod asks for it.</b>
/// Pixel Art returns before any pass on a frame with nothing to draw on, which would make "the
/// drawing really runs" a display-only question. <c>PixelArtApi.RunPassesWithoutDisplay</c>, set
/// once in this mod's <c>Initialize</c>, runs the passes anyway and discards the draw calls --
/// so the per-pixel logic is exercised in the everyday loop and only the <c>RenderingServer</c>
/// calls wait for a window.</para>
/// </summary>
public static class SessionTests
{
    /// <summary>
    /// A filter programmed in a real world reads back the way it does in a region.
    ///
    /// <para>The region tests write the tagged id straight into the field with
    /// <c>Region.SetRaw</c>, which is the right tool there and deliberately bypasses everything
    /// the game would do on a real write. This places the same thing through the session's own
    /// pixel path, in a world with planet data, fog, chunk streaming and a live simulation, and
    /// asks for the same answer.</para>
    /// </summary>
    [GameTest]
    public static IEnumerator AFilterProgrammedInARealWorldNamesItsTarget()
    {
        yield return Session.Enter("flat");

        var field = Simulation.CurrentState?.Field;
        if (field == null)
            throw new AssertionException("no simulation field inside a session");

        // Above the surface of the flat fixture and well inside the rendered window, so nothing
        // streams over it.
        var tile = new Vector2I(field.Width / 2, field.Height / 2);
        Session.PauseSimulation();
        field.Set(tile.X, tile.Y,
                  BaseMaterial.ToMatchFilterOffset(Materials.GetBaseMaterialId("Iron")));
        Session.MarkDirty(tile.X, tile.Y);

        yield return Wait.Frames(1);

        var annotation = PixelInspectorApi.At(field, tile.X, tile.Y);
        var expected = "Iron";
        if (annotation.Text != expected || annotation.Icon != MarkIcon.Check)
            throw new AssertionException(
                $"the filter at {tile} reads '{annotation.Text}', expected '{expected}'. The " +
                "world's own read path disagrees with the region tests', which they should not.");

        Session.ResumeSimulation();
        yield return Session.Leave();
    }

    /// <summary>
    /// <b>The test that would fail with the mod not installed.</b>
    ///
    /// <para>Everything else in the suite drives this mod's own classes directly and would pass
    /// against a mod that never loaded. This one watches the counter the pass increments, so it
    /// is evidence that the canvas was registered, that Pixel Art called the pass from its
    /// per-frame hook, and that it got past this mod's own gate -- on real frames of a real
    /// world.</para>
    ///
    /// <para>It asserts on a counter rather than on pixels, which is what lets it run headless
    /// given <c>RunPassesWithoutDisplay</c>. The pixels are <see cref="ScreenTests"/>'s job.</para>
    /// </summary>
    [GameTest]
    public static IEnumerator TheDrawingRunsOnEveryFrameOfARealWorld()
    {
        yield return Session.Enter("flat");

        try
        {
            // A test cannot hold Alt -- Canvas.AltHeld reads the real keyboard -- so the
            // requirement is switched off for the duration. That this is possible at all is the
            // point of gating the pass on a predicate rather than on DrawWhen.AltHeld.
            Settings.RequireAlt = false;
            PixelInspectorApi.ResetCounters();

            yield return Wait.Frames(3);

            if (PixelInspectorApi.Frames < 2)
                throw new AssertionException(
                    $"the pass ran on {PixelInspectorApi.Frames} of 3 frames. Either the canvas " +
                    "was never registered, or Pixel Art's own per-frame hook is not firing, or " +
                    "this mod's predicate rejected the frame -- " +
                    PixelInspectorApi.DescribeState() + "; " + PixelArtApi.DescribeState());
        }
        finally
        {
            Settings.Reset();
        }

        yield return Session.Leave();
    }
}
