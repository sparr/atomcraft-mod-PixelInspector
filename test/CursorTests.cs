using Godot;
using Atomcraft;
using Atomcraft.TestHarness;
using PixelArt;

namespace PixelInspector.Test;

/// <summary>
/// Taking the cursor away over the panel, and giving it back.
///
/// <para>A screenshot cannot check any of this: the cursor is composited by the operating system
/// after the frame, and the harness photographs the render target, so it is never in the picture.
/// These assert on the decision instead.</para>
///
/// <para><b>Giving it back is the half that matters.</b> <c>Input.MouseMode</c> is process-wide.
/// A mod that hides the cursor and then faults, or is switched off, leaves the player with no
/// pointer anywhere in the game and no reason to suspect which mod did it.</para>
/// </summary>
public static class CursorTests
{
    /// <summary>
    /// The cursor is taken only where the panel would be drawn over it, and only while the panel
    /// is up at all.
    /// </summary>
    [GameTest]
    public static void TheCursorIsTakenOnlyWhereThePanelCoversIt(Region r)
    {
        try
        {
            Settings.RequireAlt = false;        // panel up without a real Alt key
            Settings.QuietCursor = true;

            var panel = Magnifier.PanelRect();
            if (panel.Size.X <= 0f)
                throw new AssertionException("the panel has no area, so this test asks nothing");

            if (!CursorPatch.WouldHide(panel.GetCenter()))
                throw new AssertionException(
                    "the cursor is not hidden in the middle of the panel, which is the one place " +
                    "it would certainly be drawn in front of it");

            // A hair outside the top left corner. HasPoint excludes the far edges, so the corner
            // itself is not a safe probe.
            if (CursorPatch.WouldHide(panel.Position - new Vector2(2f, 2f)))
                throw new AssertionException(
                    "the cursor is hidden while the pointer is outside the panel, so a player " +
                    "aiming at the edge of the screen would have no cursor at all");

            Settings.QuietCursor = false;
            if (CursorPatch.WouldHide(panel.GetCenter()))
                throw new AssertionException("quietCursor is off and the cursor is still taken");

            Settings.QuietCursor = true;
            PixelInspectorApi.Enabled = false;
            if (CursorPatch.WouldHide(panel.GetCenter()))
                throw new AssertionException(
                    "the mod is switched off and still taking the cursor");
        }
        finally
        {
            PixelInspectorApi.ResetState();
        }
    }

    /// <summary>
    /// The mod's own reset gives the cursor back.
    ///
    /// <para>Asserts the premise first: that the state really was taken. A test that only checked
    /// the end state would pass just as well against a mod that never hid anything.</para>
    /// </summary>
    [GameTest]
    public static void ResetGivesTheCursorBack(Region r)
    {
        try
        {
            Settings.RequireAlt = false;
            Settings.QuietCursor = true;
            CursorPatch.HideForTest();

            if (!CursorPatch.Hidden)
                throw new AssertionException(
                    "the cursor did not go hidden, so what follows proves nothing");

            PixelInspectorApi.ResetState();

            if (CursorPatch.Hidden)
                throw new AssertionException(
                    "ResetState left the cursor hidden. Input.MouseMode is process-wide, so this " +
                    "is a player with no pointer in the menus. See CursorPatch.Restore.");
        }
        finally
        {
            CursorPatch.Restore();
            PixelInspectorApi.ResetState();
        }
    }

    /// <summary>
    /// The swap is off unless the magnifier is up, so a player who never holds Alt keeps the tool
    /// cursor the game gave them.
    /// </summary>
    [GameTest]
    public static void TheSwapOnlyClaimsTheCursorWhileThePanelIsUp(Region r)
    {
        try
        {
            Settings.RequireAlt = true;
            if (ZoomKeys.Claiming && !Canvas.AltHeld)
                throw new AssertionException(
                    "the mod claims the cursor and the zoom keys without Alt being held");

            Settings.RequireAlt = false;
            if (!ZoomKeys.Claiming)
                throw new AssertionException(
                    "with requireAlt off the mod does not claim the cursor, so the panel would be " +
                    "up with the ordinary cursor over it");

            PixelInspectorApi.Enabled = false;
            if (ZoomKeys.Claiming)
                throw new AssertionException(
                    "the mod still claims the cursor and the zoom keys with the mod switched off");
        }
        finally
        {
            PixelInspectorApi.ResetState();
        }
    }
}
