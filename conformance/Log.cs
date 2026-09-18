using Atomcraft.TestHarness;

namespace PixelInspectorConformance;

/// <summary>
/// This suite's log. <c>using Atomcraft.TestHarness;</c> brings the harness's own static
/// <c>Log</c> into scope, which writes under the harness's name; with several mods in a run
/// every line would then be unattributable.
/// </summary>
internal static class Log
{
    private static readonly ModLog Inner = Atomcraft.TestHarness.Log.For(ModEntry.ModId);

    internal static void Info(string message) => Inner.Info(message);
    internal static void Warn(string message) => Inner.Warn(message);
}
