using System.Reflection;
using HarmonyLib;
using PixelArt;

namespace PixelInspector;

/// <summary>
/// Entry point, as named by <c>mod.json</c>.
///
/// <para><b>Initialize runs before the game has initialized anything.</b> The mod loader loads
/// every mod during <c>SceneTree._initialize()</c>, and the game's own <c>Game._Ready</c> does
/// not run until the following frame. So <c>Materials</c>, <c>Craftables</c>, <c>Reactions</c>
/// and <c>Simulation</c> do not exist yet, and the only correct thing to do here is install the
/// patch, take a canvas, and return. The annotation table is built from a postfix on
/// <c>Materials.Init</c>, and the drawing is a pass Pixel Art calls once a frame.</para>
///
/// <para>Reading the settings file is the one exception worth taking, because it depends on
/// nothing but Godot's user directory.</para>
/// </summary>
public static class ModEntry
{
    /// <summary>
    /// The mod id. One constant, used by <see cref="Log"/>, by <see cref="Settings.Path"/>, as
    /// the Harmony instance id, and matching <c>mod.json</c>'s <c>"id"</c> and the
    /// <c>ModId</c> property in the csproj.
    /// </summary>
    public const string ModId = "PixelInspector";

    /// <summary>
    /// The Pixel Art release this mod is written against. Its 0.x API changes between minor
    /// versions and the loader's dependencies carry no version constraint, so without this check
    /// a mismatched library surfaces later as a <c>MissingMethodException</c> from somewhere
    /// unrelated. Keep it in step with the zip named in <c>pixelart.conf</c>.
    /// </summary>
    public const string PixelArtVersion = "0.4";

    private static Harmony? _harmony;

    public static void Initialize()
    {
        Settings.Load();
        PixelArtApi.RequireVersion(PixelArtVersion);

        // Before PatchAll rather than after: this is the check that turns a game update into a
        // sentence, and PatchAll is what would otherwise throw first and less helpfully.
        if (!GameBindings.Complete)
        {
            Log.Error($"the game no longer has {GameBindings.Missing}, so nothing can be " +
                      "annotated. This usually means the game updated; the mod needs one too.");
            return;
        }

        // A warning rather than a refusal. If the encoding moved, everything except the
        // programmed filters and sensors still works, and those are the part of the mod that
        // would then quietly draw the wrong material -- which is worth a line in the log
        // whether or not anyone is watching for it.
        if (!GameBindings.HighlightBufferExists)
            Log.Warn("the game no longer has the pixel buffer the magnifier reads tool highlights " +
                     "out of, so the panel will not show what your tool is about to affect. " +
                     "Everything else is unaffected.");

        if (!GameBindings.FilterEncodingIntact())
            Log.Warn("the game's filter id encoding is not what this mod reads targets out of, " +
                     "so a programmed filter or sensor may be labelled with the wrong material. " +
                     "Everything else is unaffected.");

        _harmony = new Harmony(ModId);
        _harmony.PatchAll(Assembly.GetExecutingAssembly());

        // The drawing. Safe here, before the game has initialized anything: Canvas.For touches
        // nothing of the game's, and the library draws from a snapshot of its registry, so it does
        // not matter which order the loader ran this mod and Pixel Art in.
        Painter.Register();
        Magnifier.Register();

        // Patching the executing assembly explicitly, rather than the argument-less PatchAll that
        // walks the calling assembly. Same result here, and unambiguous when a mod grows a second
        // assembly.
        Log.Info($"initialized, {_harmony.GetPatchedMethods().Count()} method(s) patched, " +
                 $"drawing through Pixel Art {PixelArtApi.Version}");
    }
}
