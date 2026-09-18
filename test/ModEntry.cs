using Atomcraft.TestHarness;
using PixelArt;

namespace PixelInspector.Test;

/// <summary>
/// Entry point for the test mod.
///
/// <para>A peer of <c>PixelInspector</c> rather than a module of it, because the mod loader
/// treats a missing dependency as an error: a test module shipped inside the mod's own zip
/// would show a red entry in the loader report for every player who did not also install the
/// harness.</para>
///
/// <para>The harness discovers <c>[GameTest]</c> methods in every loaded assembly by itself, so
/// there is nothing to register for the tests. What is registered here is the mod's own state,
/// so the harness can reset it between tests and report it on a failure.</para>
///
/// <para><b>There is no <c>TickSpec</c>.</b> A mod that hangs work off <c>Simulation.Step</c>
/// registers one so a region test can drive the mechanism without a real game. This mod does no
/// per-tick work at all -- it reads the field on a render frame and draws -- so there is nothing
/// for a tick to drive, and a spec here would be a hook that never fires pretending to be
/// coverage.</para>
/// </summary>
public static class ModEntry
{
    public const string ModId = "PixelInspector.Test";

    /// <summary>
    /// The harness this mod is written against. Its 0.x API changes between minor versions, and
    /// the loader's dependencies carry no version constraint, so without this check a mismatched
    /// harness surfaces later as a <c>MissingMethodException</c> from somewhere unrelated. Keep
    /// it in step with the zip named in <c>harness.conf</c>.
    /// </summary>
    public const string HarnessVersion = "0.4";

    public static void Initialize()
    {
        Harness.RequireVersion(HarnessVersion);

        // Run this mod's drawing on headless frames too, discarding the draw calls.
        //
        // Without it nothing the mod draws is observable without a display: Pixel Art's shared
        // frame returns before any pass, so the pass never runs and the counter it increments
        // never moves. That would leave the everyday headless loop with no test that fails when
        // the mod is not really wired up -- only the weaker claim that a pass is registered,
        // which nobody watched fire. See ScreenTests for the version of that test that did have
        // to be display-only before this existed.
        //
        // Set here rather than per test because it is a decision about the whole run, and
        // PixelArtApi.ResetState deliberately does not clear it: a reset between tests would
        // switch it off before the first test that needed it.
        PixelArtApi.RunPassesWithoutDisplay = true;

        // ONE StateSpec for the whole mod, resetting everything it carries between tests: the
        // settings, the runtime switch, and the fault latch.
        //
        // The latch matters most and is the least obvious here. This mod has no per-tick hook,
        // so the Simulation.Init/Reset clearing that a simulation mod relies on never fires in
        // a region test: without this registration one throwing frame would switch the mod off
        // for the rest of the run. And a disabled mod does not fail every test after it -- it
        // fails the ones asserting the mod's behaviour and PASSES the ones asserting the game's,
        // so the run goes partly red for unrelated-looking reasons and partly green for no
        // reason at all.
        StateRegistry.Register(new StateSpec
        {
            Name = "pixelinspector",
            OnReset = PixelInspectorApi.ResetState,

            // No OnChecksum. This mod writes nothing into the world -- it reads the field and
            // draws on its own canvas layer -- so it contributes nothing a determinism run
            // could compare, and a checksum over its settings would make a determinism failure
            // out of a test that deliberately changed one.

            // Names the fault latch, because "the mod is switched off" is the explanation a
            // confusing failure most often has, and it belongs in the failure message rather
            // than in godot.log.
            OnDescribe = PixelInspectorApi.DescribeState,
        });

        Log.Info("loaded");
    }
}
