using System.Collections;
using Atomcraft.TestHarness;
using PixelArt;

using Session = Atomcraft.TestHarness.Session;

namespace PixelInspector.Test;

/// <summary>
/// The registered reset actually puts back everything the mod carries between tests.
///
/// <para><b>Why this is worth its own file, and why it matters more here than for most mods.</b>
/// The failure it guards against is silent and mis-attributing: a mod left switched off by a
/// previous test does not fail every test after it, it fails the ones asserting the <i>mod's</i>
/// behaviour and passes the ones asserting the <i>game's</i>, so a run goes partly red for
/// reasons that look unrelated and partly green for no reason at all.</para>
///
/// <para>The fault is Pixel Art's to raise and clear now, and it clears it on
/// <c>Simulation.Init</c> and <c>Simulation.Reset</c> -- neither of which a region test calls.
/// So the harness's state registry is still the only thing that puts it back between tests
/// here.</para>
///
/// <para>Each test opens with a <b>premise check</b>: an assertion that the narrower reset is
/// still insufficient. If someone later makes <c>Settings.Reset</c> cover the runtime state,
/// these would pass without exercising anything, and the premise check says so instead of going
/// quietly green.</para>
/// </summary>
public static class StateResetTests
{
    /// <summary>
    /// <c>ResetState</c> restores the runtime switch, which <c>Settings.Reset</c> structurally
    /// cannot: <see cref="PixelInspectorApi.Enabled"/> is the conjunction of a private field and
    /// the setting, so restoring the setting alone leaves the mod off.
    /// </summary>
    [GameTest]
    public static void ResetStateRestoresTheRuntimeSwitch(Region r)
    {
        PixelInspectorApi.Enabled = false;

        Settings.Reset();
        if (PixelInspectorApi.Enabled)
            throw new AssertionException(
                "Settings.Reset restored the mod by itself, so this test no longer covers the " +
                "gap it was written for. If the private switch is now reachable from " +
                "Settings.Reset, delete this test; if not, something else re-enabled the mod.");

        PixelInspectorApi.ResetState();
        if (!PixelInspectorApi.Enabled)
            throw new AssertionException(
                "ResetState left the mod switched off. Every test after this one would assert " +
                "against a disabled mod, and the ones checking vanilla behaviour would pass.");
    }

    /// <summary>
    /// <c>ResetState</c> clears the fault, and a fault is provoked for real rather than faked.
    ///
    /// <para><b>This test got better when the drawing moved to Pixel Art.</b> It used to reach
    /// into a private backing field by reflection, because the mod's own per-frame body could not
    /// be made to throw from outside and shipping a fault injector to let a test reach it would
    /// have been production code existing only for a test. Now the latch belongs to the canvas
    /// and anything registered on it can throw, so the real path is driven: a pass that throws,
    /// the library removing it and faulting the canvas, and <c>ResetState</c> clearing that.</para>
    ///
    /// <para>It runs headless, which it could not when the drawing first moved to the library:
    /// a pass only ran on a frame that was actually drawn. <c>RunPassesWithoutDisplay</c>, set in
    /// this mod's <c>Initialize</c>, runs them anyway and discards the draw calls -- and a pass
    /// that throws throws just the same with nothing to draw on, which is the whole of what this
    /// needs.</para>
    /// </summary>
    [GameTest]
    public static IEnumerator ResetStateClearsAFaultThatReallyHappened()
    {
        yield return Session.Enter("flat");

        var canvas = Canvas.For(PixelInspector.ModEntry.ModId);
        if (PixelInspectorApi.Faulted)
            throw new AssertionException(
                "the mod was already faulted on entry, so this test proves nothing. A previous " +
                "test left it that way, which is the exact failure this file exists to catch.");

        try
        {
            canvas.SetPass("stateresettests.boom", _ => throw new InvalidOperationException(
                "deliberate, from PixelInspector.Test.StateResetTests"));

            // Two frames: one for the pass to run and throw, one for the library to have removed
            // it and settled. The library reports the fault to godot.log, so a run of this test
            // leaves one alarming-looking line behind on purpose.
            yield return Wait.Frames(2);

            if (!PixelInspectorApi.Faulted)
                throw new AssertionException(
                    "a pass on this mod's canvas threw and the canvas did not fault, so either " +
                    "the library stopped catching it or PixelInspectorApi.Faulted is not looking " +
                    "at the canvas this mod actually draws on");

            PixelInspectorApi.ResetState();

            if (PixelInspectorApi.Faulted)
                throw new AssertionException(
                    "ResetState left the canvas faulted. The mod would draw nothing for every " +
                    "test after this one, and the tests asserting vanilla behaviour would still " +
                    "pass.");
        }
        finally
        {
            canvas.Remove("stateresettests.boom");
            PixelInspectorApi.ResetState();
        }

        yield return Session.Leave();
    }

    /// <summary>
    /// The description handed to the harness names the fault.
    ///
    /// A failure report that does not say "the mod is switched off" makes the commonest
    /// explanation for a confusing failure the one thing you have to go to <c>godot.log</c>
    /// to discover.
    /// </summary>
    [GameTest]
    public static void TheDescriptionNamesTheFault(Region r)
    {
        var described = PixelInspectorApi.DescribeState();
        if (!described.Contains("faulted"))
            throw new AssertionException(
                $"DescribeState does not mention the fault: '{described}'. It is what a " +
                "confusing failure is most often explained by, so it belongs in the message.");
    }
}
