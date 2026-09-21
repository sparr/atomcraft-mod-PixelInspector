using Atomcraft;
using HarmonyLib;

namespace PixelInspector;

/// <summary>
/// Builds the per-material table once the game has finished registering materials.
///
/// <para><c>Materials.Init</c> is the right moment and the only one: it is called from
/// <c>Game._Ready</c> after the shipped <c>AllMaterials.json</c>, after <c>user://Materials/</c>,
/// and after the mod loader's postfix on
/// <c>FileManager.LoadMaterialTypesFromUserDirectory</c> has injected every other mod's
/// materials. Building any earlier would miss some of them; building lazily on the first frame
/// would put the cost on a frame rather than on the loading screen.</para>
///
/// <para><b>This is the only thing this mod patches.</b> It used to patch three more --
/// <c>Gameplay.Process</c> to draw from, and <c>Simulation.Init</c> and <c>Simulation.Reset</c>
/// to clear a fault latch on. All three belong to the drawing, and the drawing now goes through
/// Pixel Art, which owns the per-frame hook, the latch and the lifecycle. A consumer of that
/// library does not need Harmony at all; this mod keeps it only because <i>what</i> to draw is
/// worked out from the game's material registry, which is not a drawing question.</para>
/// </summary>
[HarmonyPatch(typeof(Materials), nameof(Materials.Init))]
internal static class MaterialsInitPatch
{
    [HarmonyPostfix]
    internal static void AfterInit()
    {
        try
        {
            MaterialAnnotations.Build();
            Circuit.Build(Materials.Count);
            Log.Info($"{MaterialAnnotations.Annotated} of {Materials.Count} materials annotated");
        }
        catch (Exception e)
        {
            // Not fatal: with no table every lookup returns "nothing to draw", so the mod is
            // inert rather than broken, and the game boots.
            MaterialAnnotations.Forget();
            Circuit.Clear();
            Log.Error($"could not build the annotation table, so nothing will be drawn: {e}");
        }
    }
}


/// <summary>
/// Puts the magnifier's zoom back when a world starts or ends.
///
/// <para><c>Simulation.Reset</c> is called from <c>Game.StopSession</c>, which is the "exit to
/// menu" boundary, and <c>Simulation.Init</c> covers entering a new world without having left the
/// last one properly. So the zoom survives for exactly as long as a world does.</para>
///
/// <para>This is the one piece of state in the mod that is deliberately neither persisted nor
/// permanent. It is not in the settings file, because it is something a player changes while
/// looking at a thing rather than a preference; and it is not kept for the process, because
/// arriving in a new world at whatever zoom the last one ended on is a surprise.</para>
/// </summary>
[HarmonyPatch]
internal static class MagnifierLifecyclePatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Simulation), nameof(Simulation.Init))]
    internal static void AfterInit() => Magnifier.Reset();

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Simulation), nameof(Simulation.Reset))]
    internal static void AfterReset() => Magnifier.Reset();
}
