using Godot;
using PixelArt;

namespace PixelInspector;

/// <summary>
/// The annotation radius drawn as an outline, and the pass's counters on the pixel under the
/// cursor. Off unless <c>debugOverlay</c> says otherwise.
///
/// <para>For a mod whose whole effect is marks placed on pixels, seeing <i>which</i> pixels is
/// most of what goes wrong. It is the quickest way to answer the question a player tuning
/// <c>radius</c> actually has -- is that block the size I meant? -- and the quickest way to see
/// that the mod is alive at all when it appears not to be, since the counters move even on a
/// block where nothing is annotated.</para>
///
/// <para><b>This used to live in the test mod, and the reason is worth recording because it
/// stopped being true.</b> A shipped assembly must never reference
/// <c>Atomcraft.TestHarness.dll</c> -- it would fail to load for every player without the
/// harness -- and this drew through the harness's <c>Overlay</c>, so it had to sit on the far
/// side of that line. Which meant the one diagnostic built for a person playing the game was
/// reachable only by <c>play.sh --debug</c>, a launcher flag that loaded the harness and gave up
/// the game's automatic saves to do it. Drawing through Pixel Art instead needs nothing of the
/// harness, so it can be what it was always meant to be: a switch in the settings file.</para>
///
/// <para><b>Its own canvas, above the annotations.</b> Layer <c>DefaultLayer + 1</c>, so the
/// outline draws over them rather than under, and so a fault here cannot take the mod's real
/// drawing down with it -- Pixel Art faults the canvas a throwing pass belongs to and leaves
/// every other one alone.</para>
///
/// <para>One thing was lost in the move and is not coming back. The harness version raised the
/// camera's zoom ceiling to four times the game's, so a pixel was big enough to see a label
/// inside. A shipped mod has no business doing that to somebody's camera; a player who wants it
/// installs <a href="https://github.com/sparr/atomcraft-mod-Zoooom">Zoooom</a>, which is the
/// player-facing form of the same thing.</para>
/// </summary>
internal static class DebugOverlay
{
    private const string PassName = "radius";

    private static readonly Color EdgeColor = new(1f, 1f, 1f, 0.18f);

    private static Canvas? _canvas;

    /// <summary>The canvas it draws on, or null while the setting is off.</summary>
    internal static Canvas? Surface => _canvas;

    /// <summary>
    /// Registers the overlay if <see cref="Settings.DebugOverlay"/> asks for it, and removes it
    /// if it does not.
    ///
    /// <para>Both directions, so this is also what a test calls after changing the setting. The
    /// canvas is left registered once taken -- <c>Canvas.Forget</c> would be the way to drop it
    /// entirely, and there is no reason to: an empty canvas is asked whether it has work before
    /// anything is built for it, so one carrying no pass costs a boolean a frame and leaves no
    /// layer in the scene tree.</para>
    /// </summary>
    internal static void Sync()
    {
        // Below the magnifier, not above it. The outline says which part of the world the panel
        // is showing, so it belongs on the world, under the panel that replaced it. Drawn on top
        // it cut across the magnified view it was describing.
        _canvas ??= Canvas.For(ModEntry.ModId + ".debug", Canvas.DefaultLayer - 1);

        if (!Settings.DebugOverlay)
        {
            _canvas.Remove(PassName);
            return;
        }

        // Gated on the same predicate as the annotations themselves, so the outline appears and
        // disappears with the thing it is describing rather than on its own schedule.
        _canvas.SetPass(PassName, Draw, () => Painter.Showing);
    }

    private static void Draw(Canvas canvas)
    {
        if (ViewGeometry.MouseTile() is not { } mouse)
            return;

        // The magnifier's source region, which is what the panel is showing. One outline around
        // the whole block rather than one per edge pixel: a single border is both cheaper and
        // what the eye wants.
        //
        // The outline is the whole of it now. There used to be a readout of the panel's size and
        // pixel count under the cursor as well, in the worst possible place: text pinned to the
        // pointer, over the world, in the middle of everything the player was trying to look at.
        // The panel prints its own zoom underneath itself, which is the same information
        // somewhere it can be ignored.
        canvas.DrawOutline(ViewGeometry.Around(mouse, Magnifier.Radius), EdgeColor);
    }
}
