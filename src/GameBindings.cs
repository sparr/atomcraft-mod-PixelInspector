using Atomcraft;
using Godot;
using HarmonyLib;

namespace PixelInspector;

/// <summary>
/// Everything this mod needs the game to still have, checked once at startup.
///
/// <para><b>Why this exists.</b> A mod's whole failure mode after a game update is silence:
/// Harmony finds nothing to patch, nothing throws, the game runs, and the mod simply does not
/// do its job. The player reports "it stopped working" and there is nothing in the log. One
/// check at startup turns that into a sentence naming what moved.</para>
///
/// <para>Everything this mod patches is public and named with <c>nameof</c>, so a rename is
/// already a build error. Resolving it again here covers the other case the compiler cannot: a
/// target that was present at build time and is absent at run time, which is what a player on a
/// different game build has.</para>

/// <para>There is only one left. The render hook and the two simulation lifecycle hooks moved
/// to Pixel Art along with the drawing, and that mod checks its own; a player whose game has
/// lost <c>Gameplay.Process</c> is told so once, by the library, rather than once per mod that
/// draws through it.</para>
///
/// <para>The second group is different, and is the reason this file is longer than the mod's
/// patch list. The whole annotation vocabulary rests on the game's material <i>names</i> and on
/// the quadrant-bit encoding that stores a filter's target in its cell id. A rename on either
/// side produces no error at all -- the mod loads, patches, runs, and draws nothing -- so the
/// few load-bearing pieces are checked by looking them up.</para>
/// </summary>
internal static class GameBindings
{
    /// <summary>The methods this mod patches.</summary>
    private static readonly (string Name, bool Found)[] Targets =
    {
        ("Materials.Init", AccessTools.Method(typeof(Materials), nameof(Materials.Init)) != null),
        ("Simulation.Init", AccessTools.Method(typeof(Simulation), nameof(Simulation.Init)) != null),
        ("Simulation.Reset", AccessTools.Method(typeof(Simulation), nameof(Simulation.Reset)) != null),
        ("FollowCam.IncreaseZoom", AccessTools.Method(typeof(FollowCam), nameof(FollowCam.IncreaseZoom)) != null),
        ("FollowCam.DecreaseZoom", AccessTools.Method(typeof(FollowCam), nameof(FollowCam.DecreaseZoom)) != null),
        ("Cursors.Process", AccessTools.Method(typeof(Cursors), nameof(Cursors.Process)) != null),
    };

    /// <summary>
    /// Whether the buffer the tool-highlight mirror reads is still there. Not fatal: losing it
    /// costs the highlight outline in the panel and nothing else, so it is reported rather than
    /// refused. See <see cref="ToolHighlight"/>.
    /// </summary>
    internal static bool HighlightBufferExists => ToolHighlight.Available;

    /// <summary>
    /// The game's own zoom actions, which the magnifier borrows. Not fatal if they are gone: the
    /// panel still draws, it just cannot be zoomed, so this is reported rather than refused.
    /// </summary>
    internal static bool ZoomActionsExist =>
        InputMap.HasAction("KB_ZoomIncrease") && InputMap.HasAction("KB_ZoomDecrease");

    /// <summary>Whether everything this mod patches is still there.</summary>
    internal static bool Complete => Targets.All(t => t.Found);

    /// <summary>Names what is missing, for one log line at startup.</summary>
    internal static string Missing =>
        string.Join(", ", Targets.Where(t => !t.Found).Select(t => t.Name));

    /// <summary>
    /// Whether the filter id encoding is still what the mod reads targets out of.
    ///
    /// <para>Checked by round-tripping a known id rather than by comparing the constants,
    /// because the constants being unchanged is not the claim that matters: the claim is that
    /// tagging an id and untagging it gets the same material back, which is exactly what
    /// <see cref="MaterialAnnotations.At"/> relies on. Safe to call before any world exists --
    /// it is pure arithmetic on a literal.</para>
    /// </summary>
    internal static bool FilterEncodingIntact()
    {
        const short probe = 1234;
        return BaseMaterial.IsMatchFilterOffset(BaseMaterial.ToMatchFilterOffset(probe))
            && BaseMaterial.IsNonMatchFilterOffset(BaseMaterial.ToNonMatchFilterOffset(probe))
            && BaseMaterial.IsSensorOffset(BaseMaterial.ToSensorOffset(probe))
            && BaseMaterial.BaseId(BaseMaterial.ToMatchFilterOffset(probe)) == probe
            && BaseMaterial.BaseId(BaseMaterial.ToNonMatchFilterOffset(probe)) == probe
            && BaseMaterial.BaseId(BaseMaterial.ToSensorOffset(probe)) == probe
            && BaseMaterial.IsNormal(probe);
    }
}
