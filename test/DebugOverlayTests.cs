using Atomcraft.TestHarness;
using PixelArt;

namespace PixelInspector.Test;

/// <summary>
/// The diagnostic overlay, which is shipped code now rather than test-mod code.
///
/// <para>It moved because it stopped needing the harness to draw. That made it reachable by a
/// player, which is what it was always for -- and it also made it something this suite has to
/// cover, because a shipped switch that silently does nothing is worse than no switch.</para>
/// </summary>
public static class DebugOverlayTests
{
    /// <summary>
    /// The setting reaches the drawing in both directions.
    ///
    /// <para>Both directions matter and the second is the one that bites: a pass registered and
    /// never removed keeps drawing over every test after the one that switched it on, and over
    /// the rest of a play session after a player switched it off.</para>
    /// </summary>
    [GameTest]
    public static void TheSettingRegistersAndRemovesThePass(Region r)
    {
        try
        {
            Settings.DebugOverlay = false;
            DebugOverlay.Sync();
            var off = DebugOverlay.Surface?.PassCount ?? 0;
            if (off != 0)
                throw new AssertionException(
                    $"the overlay's canvas carries {off} pass(es) with the setting off");

            Settings.DebugOverlay = true;
            DebugOverlay.Sync();
            if ((DebugOverlay.Surface?.PassCount ?? 0) != 1)
                throw new AssertionException(
                    "the overlay's canvas carries no pass with the setting on, so the switch " +
                    "does nothing");

            Settings.DebugOverlay = false;
            DebugOverlay.Sync();
            if ((DebugOverlay.Surface?.PassCount ?? 0) != 0)
                throw new AssertionException(
                    "the pass survived the setting being switched off, so it would keep drawing " +
                    "over every test after this one");
        }
        finally
        {
            PixelInspectorApi.ResetState();
        }
    }

    /// <summary>
    /// It draws on a canvas of its own, below the magnifier.
    ///
    /// <para>Two reasons, and the second is the one worth a test. The outline marks the part of
    /// the <i>world</i> the panel is showing, so it belongs under the panel that covers it;
    /// drawn on top it cut across the magnified view it was describing. And a pass that throws
    /// faults only the canvas it belongs to, so a diagnostic sharing the magnifier's canvas
    /// could take the mod's real drawing down with it.</para>
    /// </summary>
    [GameTest]
    public static void ItDrawsOnItsOwnCanvasBelowTheMagnifier(Region r)
    {
        try
        {
            Settings.DebugOverlay = true;
            DebugOverlay.Sync();

            var diagnostic = DebugOverlay.Surface;
            var annotations = Painter.Surface;
            if (diagnostic == null || annotations == null)
                throw new AssertionException("one of the two canvases was never registered");

            if (ReferenceEquals(diagnostic, annotations))
                throw new AssertionException(
                    "the diagnostic shares the annotations' canvas, so a throw in it would fault " +
                    "the mod's real drawing too");

            if (diagnostic.Layer >= annotations.Layer)
                throw new AssertionException(
                    $"the diagnostic is on layer {diagnostic.Layer} and the annotations on " +
                    $"{annotations.Layer}, so the outline draws over the panel it is describing");
        }
        finally
        {
            PixelInspectorApi.ResetState();
        }
    }

    /// <summary>
    /// <c>ResetState</c> puts the overlay away.
    ///
    /// <para>The premise check first: <c>Settings.Reset</c> alone puts the <i>setting</i> back and
    /// leaves the pass registered, because nothing re-reads the setting on its own. That gap is
    /// exactly what <c>ResetState</c> calling <c>Sync</c> exists to close, and the day someone
    /// makes the setting self-applying this test should say it is stale rather than go quietly
    /// green.</para>
    /// </summary>
    [GameTest]
    public static void ResetStatePutsTheOverlayAway(Region r)
    {
        try
        {
            Settings.DebugOverlay = true;
            DebugOverlay.Sync();
            if ((DebugOverlay.Surface?.PassCount ?? 0) != 1)
                throw new AssertionException("the overlay did not register, so this proves nothing");

            Settings.Reset();
            if ((DebugOverlay.Surface?.PassCount ?? 0) != 1)
                throw new AssertionException(
                    "Settings.Reset removed the pass by itself, so this test no longer covers the " +
                    "gap it was written for. If the setting is now self-applying, delete it.");

            PixelInspectorApi.ResetState();
            if ((DebugOverlay.Surface?.PassCount ?? 0) != 0)
                throw new AssertionException(
                    "ResetState left the overlay registered. It would draw over every test after " +
                    "this one, and over the screenshots they leave behind.");
        }
        finally
        {
            PixelInspectorApi.ResetState();
        }
    }
}
