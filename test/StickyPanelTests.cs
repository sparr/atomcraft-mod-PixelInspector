using Atomcraft.TestHarness;

namespace PixelInspector.Test;

/// <summary>
/// Alt as a toggle as well as a modifier: a tap leaves the panel up, and any release puts it back
/// down.
///
/// <para><b>The off cases carry the weight.</b> A panel that will not come up costs a keystroke;
/// a panel that will not go down covers the middle of the screen, takes the zoom keys, hides the
/// cursor and swallows clicks over itself, all with nothing to say why. So every route back down
/// is asserted, including the reset, which is checked against a premise so it cannot pass by
/// never having stuck anything.</para>
///
/// <para>These drive <c>StickyPanel.Sync(bool, ulong)</c> with a clock of their own rather than a
/// keyboard and the real one: the modifier is read from the hardware, which a test cannot hold
/// down, and waiting out a threshold would mean spending the time.</para>
/// </summary>
public static class StickyPanelTests
{
    /// <summary>Comfortably inside the default threshold, and nothing like a hold.</summary>
    private const ulong TapMs = 90;

    /// <summary>Comfortably outside it. A real peek lasts as long as it takes to read the panel.</summary>
    private const ulong HoldMs = 3000;

    private static ulong _clock;

    /// <summary>A press of a given length, starting wherever the last one left off.</summary>
    private static void Press(ulong forMs)
    {
        StickyPanel.Sync(true, _clock);
        _clock += forMs;
        StickyPanel.Sync(false, _clock);
        _clock += 500;              // a gap, so consecutive gestures are not one long press
    }

    private static void Begin()
    {
        StickyPanel.Clear();
        Settings.AltTapSeconds = 0.25;
        _clock = 10_000;            // not zero: a cleared _pressedAt must not read as "just now"
    }

    /// <summary>
    /// The feature in one test. A tap raises the panel; a hold does not; and once it is up, either
    /// gesture puts it down.
    /// </summary>
    [GameTest]
    public static void OnlyATapRaisesItAndAnyReleaseLowersIt(Region r)
    {
        try
        {
            Begin();

            Press(HoldMs);
            if (PixelInspectorApi.PanelStuck)
                throw new AssertionException(
                    "holding Alt and letting go left the panel up, so a peek now sticks");

            Press(TapMs);
            if (!PixelInspectorApi.PanelStuck)
                throw new AssertionException("a tap of Alt did not leave the panel up");

            Press(TapMs);
            if (PixelInspectorApi.PanelStuck)
                throw new AssertionException("a second tap did not put the panel back down");

            // And the same, ended with a hold rather than a tap: the length of the press is read
            // only on the way up.
            Press(TapMs);
            Press(HoldMs);
            if (PixelInspectorApi.PanelStuck)
                throw new AssertionException(
                    "holding Alt and letting go did not put a raised panel down, so the duration " +
                    "test is being applied in both directions when it belongs only on the way up");
        }
        finally
        {
            PixelInspectorApi.ResetState();
        }
    }

    /// <summary>
    /// The threshold is the setting, and it is read at the moment of the release rather than
    /// baked in. Without this the default could be anything and every other test here would still
    /// pass.
    /// </summary>
    [GameTest]
    public static void TheThresholdIsTheSetting(Region r)
    {
        try
        {
            Begin();

            // A press that is a hold by default becomes a tap when the threshold is raised past it.
            Settings.AltTapSeconds = 5.0;
            Press(HoldMs);
            if (!PixelInspectorApi.PanelStuck)
                throw new AssertionException(
                    $"a {HoldMs}ms press did not count as a tap with the threshold at 5s");

            Begin();

            // And zero means no press is ever short enough, which is the hold-only behaviour.
            Settings.AltTapSeconds = 0.0;
            Press(TapMs);
            if (PixelInspectorApi.PanelStuck)
                throw new AssertionException(
                    "a tap raised the panel with altTapSeconds at 0, which is how a player asks " +
                    "for hold-to-look and nothing else");

            // Zero is a threshold, not a disabled feature: a press of no measurable length must
            // not slip under it.
            StickyPanel.Sync(true, _clock);
            StickyPanel.Sync(false, _clock);
            if (PixelInspectorApi.PanelStuck)
                throw new AssertionException(
                    "a press and release inside one millisecond raised the panel at a threshold " +
                    "of 0, so the comparison lets an instantaneous press through");
        }
        finally
        {
            PixelInspectorApi.ResetState();
        }
    }

    /// <summary>
    /// It is the release that decides, not the press. A press has no effect of its own, which is
    /// what lets one gesture serve as both a peek and a tap.
    /// </summary>
    [GameTest]
    public static void ItIsTheReleaseThatDecidesAndNotThePress(Region r)
    {
        try
        {
            Begin();

            StickyPanel.Sync(true, _clock);
            if (PixelInspectorApi.PanelStuck)
                throw new AssertionException("Alt going down raised the panel; only a release should");

            _clock += 20;
            StickyPanel.Sync(true, _clock);
            if (PixelInspectorApi.PanelStuck)
                throw new AssertionException("a second frame of Alt held raised the panel");

            _clock += 20;
            StickyPanel.Sync(false, _clock);
            if (!PixelInspectorApi.PanelStuck)
                throw new AssertionException("the release did not raise the panel");
        }
        finally
        {
            PixelInspectorApi.ResetState();
        }
    }

    /// <summary>
    /// A raised panel shows with nothing held, and that is the only thing telling it apart from a
    /// held one. If this fails the whole feature is invisible, whatever the flag says.
    /// </summary>
    [GameTest]
    public static void ARaisedPanelShowsWithNothingHeld(Region r)
    {
        try
        {
            Begin();
            Settings.RequireAlt = true;

            if (Painter.Showing)
                throw new AssertionException(
                    "the panel is showing with nothing held and nothing raised, so this test " +
                    "could not tell the two apart");

            Press(TapMs);
            if (!Painter.Showing)
                throw new AssertionException("a tap left the flag set but the panel not showing");

            Press(TapMs);
            if (Painter.Showing)
                throw new AssertionException("the panel is still showing after being put down");
        }
        finally
        {
            PixelInspectorApi.ResetState();
        }
    }

    /// <summary>
    /// The reset puts it down, with a premise check first so it cannot pass by never having raised
    /// anything. A raised panel outliving a test makes every test after it draw.
    /// </summary>
    [GameTest]
    public static void TheResetPutsARaisedPanelDown(Region r)
    {
        Begin();
        Press(TapMs);
        if (!PixelInspectorApi.PanelStuck)
            throw new AssertionException(
                "premise failed: a tap no longer raises the panel, so this test is stale and " +
                "proves nothing about the reset");

        PixelInspectorApi.ResetState();

        if (PixelInspectorApi.PanelStuck)
            throw new AssertionException("ResetState left the panel up");
    }

    /// <summary>
    /// The reset forgets that Alt was down, not only that the panel was up. Without this a reset
    /// taken mid-press leaves the next release looking like the end of a gesture that a test never
    /// made, and the panel comes up on its own.
    /// </summary>
    [GameTest]
    public static void TheResetForgetsTheKeyAsWellAsThePanel(Region r)
    {
        try
        {
            Begin();
            StickyPanel.Sync(true, _clock);     // a press the reset lands in the middle of

            PixelInspectorApi.ResetState();

            _clock += 20;
            StickyPanel.Sync(false, _clock);    // the release of a gesture that no longer exists
            if (PixelInspectorApi.PanelStuck)
                throw new AssertionException(
                    "a release after the reset raised the panel, so the reset kept the key down " +
                    "and the next frame of no-Alt reads as the end of a press");
        }
        finally
        {
            PixelInspectorApi.ResetState();
        }
    }

    /// <summary>
    /// With the modifier not required at all, the panel is up regardless, and a tap cannot take it
    /// down. The toggle adds a way to switch the panel on, never a way to switch it off.
    /// </summary>
    [GameTest]
    public static void TheToggleCannotHideAPanelThatNeedsNoModifier(Region r)
    {
        try
        {
            Begin();
            Settings.RequireAlt = false;

            if (!Painter.Showing)
                throw new AssertionException("requireAlt off did not show the panel");

            Press(TapMs);
            Press(TapMs);
            if (!Painter.Showing)
                throw new AssertionException(
                    "tapping Alt hid a panel that requires no modifier, so the toggle is being " +
                    "read as an off switch rather than an extra way on");
        }
        finally
        {
            PixelInspectorApi.ResetState();
        }
    }
}
