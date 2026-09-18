using System.Collections;
using Atomcraft;
using Atomcraft.TestHarness;
using Godot;

using Session = Atomcraft.TestHarness.Session;

namespace PixelInspector.Test;

/// <summary>
/// Swallowing a click that would land on the interface while the panel is up.
///
/// <para><b>Why the off cases carry the weight here.</b> The guard is a full-screen control that
/// consumes mouse input. Failing to raise it costs a misclick; failing to lower it costs the
/// player their entire interface, with no error and nothing to suggest the cause. So every route
/// out is asserted, and the reset is asserted against a premise so it cannot pass by never having
/// raised anything.</para>
/// </summary>
public static class ClickGuardTests
{
    /// <summary>
    /// Every condition that must hold, tested by removing one at a time.
    /// </summary>
    [GameTest]
    public static void TheGuardWantsToBeOnOnlyWhenAllOfItHolds(Region r)
    {
        try
        {
            // WorldInput.Blocked reads the live HUD, and in a region test it depends on whatever
            // ran before in this process -- a session left open a window, or did not. So it is
            // taken as a baseline rather than assumed: with every setting on, the guard must want
            // exactly what the world says; with any setting off, it must want nothing whatever the
            // world says.
            Settings.RequireAlt = false;          // panel up without a real key
            Settings.BlockClicksOverUI = true;
            var blocked = WorldInput.Blocked;

            if (ClickGuard.Wanted != blocked)
                throw new AssertionException(
                    $"with every setting on, the guard wants {ClickGuard.Wanted} while input " +
                    $"blocked reads {blocked}. Those have to agree: the settings decide whether " +
                    "the guard is available, and the world decides whether it applies.");

            Settings.BlockClicksOverUI = false;
            if (ClickGuard.Wanted)
                throw new AssertionException("the setting is off and the guard still wants to be on");

            Settings.BlockClicksOverUI = true;
            Settings.RequireAlt = true;           // panel down: Alt is not held in a test
            if (ClickGuard.Wanted)
                throw new AssertionException(
                    "the panel is down and the guard still wants to be on. It would swallow " +
                    "clicks for a player who never holds Alt at all.");

            Settings.RequireAlt = false;
            PixelInspectorApi.Enabled = false;
            if (ClickGuard.Wanted)
                throw new AssertionException("the mod is off and the guard still wants to be on");
        }
        finally
        {
            PixelInspectorApi.ResetState();
        }
    }

    /// <summary>
    /// The reset lowers a raised guard.
    ///
    /// <para>Asserts the premise first. A test that only checked the end state would pass just as
    /// well against a mod that never raised the guard at all, and the failure being guarded
    /// against is precisely a guard left up.</para>
    /// </summary>
    [GameTest(RequiresDisplay = true)]
    public static IEnumerator ResetLowersTheGuard()
    {
        yield return Session.Enter("flat");

        try
        {
            Settings.RequireAlt = false;
            Settings.BlockClicksOverUI = true;

            // A session lands with a window open, which is exactly the blocked case, so the guard
            // should raise on the next frame the per-frame hook runs.
            if (!WorldInput.Blocked)
                throw new AssertionException(
                    "a window is open and input does not read as blocked, so nothing here raises " +
                    "the guard and the assertion below would prove nothing");

            yield return Wait.Frames(3);

            if (!ClickGuard.Active)
                throw new AssertionException(
                    "the panel is up over an open window and the guard did not raise. Either the " +
                    "per-frame hook is not firing or the scene tree refused the node. See " +
                    "ClickGuard.Sync.");

            PixelInspectorApi.ResetState();

            if (ClickGuard.Active)
                throw new AssertionException(
                    "ResetState left the guard swallowing clicks. That is a game whose interface " +
                    "does not respond, with nothing to say why. See ClickGuard.Clear.");
        }
        finally
        {
            ClickGuard.Clear();
            PixelInspectorApi.ResetState();
        }

        yield return Session.Leave();
    }

    /// <summary>
    /// Letting the panel go lowers the guard, even though the panel's own drawing stops running.
    ///
    /// <para>This is the bug the wiring was moved to avoid. <c>Sync</c> first lived in the
    /// magnifier's draw pass, which does not run when the panel is down -- so a guard raised on the
    /// last frame Alt was held would never be told to come off.</para>
    /// </summary>
    [GameTest(RequiresDisplay = true)]
    public static IEnumerator DroppingThePanelLowersTheGuard()
    {
        yield return Session.Enter("flat");

        try
        {
            Settings.RequireAlt = false;
            Settings.BlockClicksOverUI = true;
            yield return Wait.Frames(3);

            if (!ClickGuard.Active)
                throw new AssertionException(
                    "the guard did not raise, so lowering it proves nothing -- " +
                    PixelInspectorApi.DescribeState());

            // The panel comes down. Its draw pass stops running entirely.
            Settings.RequireAlt = true;
            yield return Wait.Frames(3);

            if (ClickGuard.Active)
                throw new AssertionException(
                    "the panel is down and the guard is still swallowing clicks. Sync is being " +
                    "driven from something that stops running with the panel; it belongs on a " +
                    "hook that ticks every frame. See HudPlacement.CursorPatch.AfterProcess.");
        }
        finally
        {
            ClickGuard.Clear();
            PixelInspectorApi.ResetState();
        }

        yield return Session.Leave();
    }
}
