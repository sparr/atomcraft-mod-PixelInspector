using PixelArt;
using System.Reflection;
using Atomcraft;
using Atomcraft.TestHarness;
using HarmonyLib;

namespace PixelInspector.Test;

/// <summary>
/// The mod is actually wired into the game.
///
/// <para><b>These fail first and loudest when a Harmony patch has gone missing</b>, which makes
/// every other failure in the suite easier to read: a mod whose hooks never installed fails its
/// behaviour tests for a reason that has nothing to do with its behaviour.</para>
///
/// <para>Worth knowing while reading a failure here: a postfix does not run when the original
/// method throws. <c>GetPatchedMethods</c> will list the method, the method demonstrably
/// executes, and the postfix silently never fires. If a hook seems not to be installed, check
/// <c>godot.log</c> for an exception inside the target before suspecting Harmony.</para>
/// </summary>
public static class RegistrationTests
{
    /// <summary>
    /// The one method the mod patches is patched, and it is ours.
    ///
    /// <para>There used to be four. Three of them -- <c>Gameplay.Process</c> to draw from, and
    /// <c>Simulation.Init</c> and <c>Simulation.Reset</c> to clear a fault latch on -- belonged
    /// to the drawing, and the drawing now goes through Pixel Art, which owns them. What is left
    /// is the one hook that is not a drawing question: where the per-material table is built
    /// from.</para>
    /// </summary>
    [GameTest]
    public static void TheMaterialHookIsInstalledAndOurs(Region r)
    {
        var target = AccessTools.Method(typeof(Materials), nameof(Materials.Init));
        if (target == null)
            throw new AssertionException(
                "the game no longer has Materials.Init; the mod needs an update");

        if (!Harmony.GetAllPatchedMethods().Contains(target))
            throw new AssertionException(
                "Materials.Init is not patched. ModEntry.Initialize may have returned early " +
                "because GameBindings reported something missing.");

        var owners = Harmony.GetPatchInfo(target)?.Owners ?? (IReadOnlyList<string>)Array.Empty<string>();
        if (!owners.Contains(PixelInspector.ModEntry.ModId))
            throw new AssertionException(
                $"no patch on Materials.Init owned by '{PixelInspector.ModEntry.ModId}'; " +
                $"owners are [{string.Join(", ", owners)}]");
    }

    /// <summary>
    /// The drawing is registered with Pixel Art: a canvas of this mod's own, carrying a pass.
    ///
    /// <para>This is what replaced "the render hook is installed". The mod no longer patches
    /// anything to draw from -- the library owns the per-frame hook and calls every registered
    /// pass from it -- so what there is to check is that this mod took a canvas and put its
    /// drawing on it.</para>
    ///
    /// <para>Checked through <c>Canvas.All</c> rather than through this mod's own field, so it is
    /// the library's view of the registration that is asserted. A canvas this mod believed it had
    /// registered but which the library had never heard of would pass the other way round.</para>
    /// </summary>
    [GameTest]
    public static void TheDrawingIsRegisteredWithPixelArt(Region r)
    {
        var ours = Canvas.All.FirstOrDefault(c => c.Owner == PixelInspector.ModEntry.ModId);
        if (ours == null)
            throw new AssertionException(
                "Pixel Art has no canvas owned by '" + PixelInspector.ModEntry.ModId + "'; the " +
                $"owners it knows about are [{string.Join(", ", Canvas.All.Select(c => c.Owner))}]. " +
                "Painter.Register did not run, or ModEntry.Initialize returned before it.");

        if (ours.PassCount < 1)
            throw new AssertionException(
                "this mod's canvas carries no pass, so nothing will ever be drawn on it");

        if (ours.Faulted)
            throw new AssertionException(
                $"this mod's canvas is faulted on entry, so its drawing is switched off for the " +
                $"session: {ours.Fault}");
    }

    /// <summary>
    /// The game still has the four shapes this borrows from its own art.
    ///
    /// <para>They are loaded by resource path, which nothing in the compiler can check, and the
    /// player is read out of a <i>region</i> of a larger icon, which nothing checks either --
    /// that art being resized would leave the mod drawing a slice of a spaceship. A rename or a
    /// resize costs the shapes and nothing else, so this is the only thing that would
    /// notice.</para>
    /// </summary>
    [GameTest]
    public static void TheGamesShapesAreStillThere(Region r)
    {
        foreach (var glyph in new[] { Shape.Solid, Shape.Liquid, Shape.Gas, Shape.Player })
            if (!PixelInspectorApi.HasShape(glyph))
                throw new AssertionException(
                    $"the game no longer has usable art for the {glyph} shape, so some labels " +
                    "will be drawn without one. See GlyphArt for the paths and regions.");
    }

    /// <summary>
    /// The buffer the tool-highlight mirror reads is still there.
    ///
    /// <para><c>Gameplay.PixelsData</c> is private and reached by name, which nothing in the
    /// compiler can check. Losing it costs only the highlight outline in the panel, so nothing
    /// else in the suite would notice. The mirror itself is exercised by
    /// <c>ToolHighlightTests</c>, which needs a session; this runs in the cheap tier so a rename
    /// is named as a rename rather than as a session test going quiet.</para>
    /// </summary>
    [GameTest]
    public static void TheHighlightBufferIsStillThere(Region r)
    {
        if (!ToolHighlight.Available)
            throw new AssertionException(
                "the game no longer has Gameplay.PixelsData, so the panel cannot show what the " +
                "player's tool is about to affect. See ToolHighlight.");
    }

    /// <summary>
    /// The startup binding check agrees with reality. If this fails, <c>GameBindings</c> is
    /// looking for something the mod does not actually depend on, or missing something it does.
    /// </summary>
    [GameTest]
    public static void TheBindingCheckIsHonest(Region r)
    {
        var bindings = typeof(PixelInspector.ModEntry).Assembly.GetType("PixelInspector.GameBindings");
        if (bindings == null)
            throw new AssertionException("PixelInspector.GameBindings no longer exists");

        var complete = bindings.GetProperty("Complete", BindingFlags.NonPublic | BindingFlags.Static)
                               ?.GetValue(null) as bool?;
        if (complete != true)
            throw new AssertionException(
                "GameBindings reports something missing, but the tests below need it: " +
                bindings.GetProperty("Missing", BindingFlags.NonPublic | BindingFlags.Static)
                        ?.GetValue(null));
    }
}
