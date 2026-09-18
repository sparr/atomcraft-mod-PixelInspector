using Godot;

namespace PixelInspector;

/// <summary>
/// Everything this mod says, funnelled through one prefix.
///
/// <c>user://logs/godot.log</c> is the only channel a mod has once the game is running, and it
/// carries every subsystem's output, so a consistent tag is what makes this mod's lines
/// findable in it. <c>play.sh --verify</c> greps for exactly this tag.
///
/// <para>Shipped code owns its own <c>Log</c> like this rather than borrowing the harness's: a
/// mod that referenced <c>Atomcraft.TestHarness.dll</c> from its shipped assembly would fail to
/// load for every player who had not installed the harness. The test mod has the opposite
/// problem and solves it differently; see <c>test/Log.cs</c>.</para>
/// </summary>
public static class Log
{
    public const string Tag = ModEntry.ModId;

    public static void Info(string message) => GD.Print($"[{Tag}] {message}");
    public static void Warn(string message) => GD.PrintErr($"[{Tag}] WARNING: {message}");
    public static void Error(string message) => GD.PrintErr($"[{Tag}] ERROR: {message}");
}
