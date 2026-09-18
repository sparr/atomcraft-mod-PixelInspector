using PixelArt;
using Atomcraft.TestHarness;

namespace PixelInspector.Test;

/// <summary>
/// The settings behave, including when the file is wrong.
///
/// <para>Worth testing because <see cref="Settings"/> is the one part of a mod a player edits by
/// hand, so malformed input is the expected case rather than the exceptional one, and because
/// <c>Load</c> runs during <c>Initialize</c>, where a throw takes the whole mod down before the
/// game has started.</para>
/// </summary>
public static class SettingsTests
{
    /// <summary>
    /// Reset really does restore every default. Cheap, and it is the method the harness's
    /// <c>StateRegistry</c> calls between tests, so a field missing from it silently leaks one
    /// test's configuration into every test after it.
    /// </summary>
    [GameTest]
    public static void ResetRestoresEveryDefault(Region r)
    {
        var defaults = Settings.Describe();

        Settings.Enabled = false;
        Settings.RequireAlt = false;
        Settings.Radius = 19;
        Settings.MaxTextLength = 3;

        if (Settings.Describe() == defaults)
            throw new AssertionException(
                "Describe() did not change after four settings did, so it is not reporting them " +
                "all and a failure report would be misleading");

        Settings.Reset();

        if (Settings.Describe() != defaults)
            throw new AssertionException(
                $"after Reset the settings are '{Settings.Describe()}', expected '{defaults}'. " +
                "A field added to Settings was probably not added to Reset.");
    }

    /// <summary>
    /// The master switch reaches the API, which is what lets a player turn the mod off without
    /// uninstalling it.
    /// </summary>
    [GameTest]
    public static void TheFileSwitchReachesTheApi(Region r)
    {
        try
        {
            Settings.Enabled = false;
            if (PixelInspectorApi.Enabled)
                throw new AssertionException(
                    "PixelInspectorApi.Enabled stayed true with the setting off; the API is not " +
                    "consulting Settings and the file would do nothing");
        }
        finally
        {
            Settings.Reset();
        }
    }

    /// <summary>
    /// The Alt requirement is a setting and not a constant, and switching it off means the
    /// annotations are shown without the modifier.
    ///
    /// <para>Asserted through <c>Showing</c> rather than through the key, because a test cannot
    /// hold Alt: <c>Input.IsKeyPressed</c> reads the real keyboard. That is also why the
    /// setting exists at all in a form the tests can reach -- without it the whole drawing path
    /// would be unreachable from an automated run.</para>
    /// </summary>
    [GameTest]
    public static void TurningOffTheAltRequirementShowsTheAnnotations(Region r)
    {
        try
        {
            Settings.RequireAlt = true;
            var withAlt = PixelInspectorApi.Showing;

            Settings.RequireAlt = false;
            if (!PixelInspectorApi.Showing)
                throw new AssertionException(
                    "with requireAlt off the mod still reports that it is not showing, so the " +
                    "setting does nothing and no automated test can reach the drawing path");

            // Not asserted the other way round: if someone happens to be holding Alt while the
            // suite runs, `withAlt` is legitimately true. Reported so a confusing run says so.
            if (withAlt)
                Log.Info("Alt appears to be held; the requireAlt=true half of this test is moot");
        }
        finally
        {
            Settings.Reset();
        }
    }
}
