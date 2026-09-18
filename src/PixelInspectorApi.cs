using Atomcraft;
using PixelArt;

namespace PixelInspector;

/// <summary>
/// What this mod is doing right now, and the one switch that stops it. Everything here is safe
/// to read with no game loaded; the answers are zero or false until there is one.
///
/// <para><b>This is the surface a test or another mod uses.</b> The patches themselves are
/// internal on purpose: what they hook is this mod's business and changes with the game, while
/// this does not. Keeping the tests on this side of the line has a second payoff -- if they can
/// observe everything they need through it, so can another mod, and the fact that they pass is
/// a standing check that the public surface is sufficient.</para>
/// </summary>
public static class PixelInspectorApi
{
    private static bool _enabled = true;

    /// <summary>
    /// Whether the mod is doing anything. Setting it false makes the render hook fall through
    /// and clears the canvas, which returns the game to exactly how it shipped without
    /// unloading anything.
    /// </summary>
    public static bool Enabled
    {
        get => _enabled && Settings.Enabled;
        set
        {
            // Against the effective value, not the backing field: with the setting file saying
            // off, the field alone would report no change and quietly refuse to switch the mod
            // on.
            if (Enabled == value)
                return;
            _enabled = value;
            Settings.Enabled = value;
            Log.Info(value ? "enabled" : "disabled");
        }
    }

    /// <summary>
    /// Whether annotations are being drawn right now: the mod is on, and either Alt is held or
    /// the mod is configured not to need it.
    /// </summary>
    public static bool Showing => Enabled && Painter.Showing;

    /// <summary>
    /// Whether the game still has the art behind one of the shapes an annotation can name.
    /// Exposed so a test can assert it rather than grep the log: a missing shape costs only the
    /// shape, so nothing else would notice.
    ///
    /// <para>The art itself is Pixel Art's; this only resolves the name.</para>
    /// </summary>
    public static bool HasShape(Shape shape) => Painter.ArtFor(shape) != null;

    /// <summary>Whether the per-material table has been built. False until <c>Materials.Init</c> has run.</summary>
    public static bool Ready => MaterialAnnotations.Ready;

    /// <summary>How many of the game's materials the rules have something to say about.</summary>
    public static int AnnotatedMaterials => MaterialAnnotations.Annotated;

    /// <summary>How many cells were labelled on the last frame the pass ran.</summary>
    public static int AnnotatedLastFrame => Painter.AnnotatedLastFrame;

    /// <summary>How many cells the pass looked at on the last frame it ran.</summary>
    public static int ScannedLastFrame => Painter.ScannedLastFrame;

    /// <summary>
    /// How many frames the pass has run on. The only evidence of the render hook firing that
    /// survives a headless run, where there are no pixels to look at; see
    /// <see cref="Magnifier.Frames"/>.
    ///
    /// <para>The magnifier's count, because the magnifier is what the mod draws by default: the
    /// world marks are opt-in and their pass is not called at all with <c>worldMarks</c> off.</para>
    /// </summary>
    public static long Frames => Magnifier.Frames;

    /// <summary>How many world pixels the magnifier drew on its last frame.</summary>
    public static int PanelPixelsLastFrame => Magnifier.PixelsLastFrame;

    /// <summary>How many of those it put an annotation on.</summary>
    public static int PanelAnnotatedLastFrame => Magnifier.AnnotatedLastFrame;

    /// <summary>How many of those were wholly inside the panel rather than clipped by its edge.</summary>
    public static int PanelWholeCellsLastFrame => Magnifier.WholeCellsLastFrame;

    /// <summary>
    /// Whether the panel left the hovered pixel's white ring off on the last frame, because the
    /// player's tool had already outlined exactly that one cell and a second ring on the same
    /// four edges only makes the first harder to read.
    /// </summary>
    public static bool MarkerSuppressedLastFrame => Magnifier.MarkerSuppressedLastFrame;

    /// <summary>How many screen pixels one world pixel takes in the magnifier right now.</summary>
    public static int PanelCell => Magnifier.Cell;

    /// <summary>How many world pixels the magnifier is across right now.</summary>
    public static int PanelCells => Magnifier.Cells;

    /// <summary>Drops the frame counters, so a test can measure from a known point.</summary>
    public static void ResetCounters()
    {
        Painter.ResetCounters();
        Magnifier.ResetCounters();
    }

    /// <summary>
    /// Whether this mod's drawing has been switched off for the session by an unhandled failure.
    ///
    /// <para>The latch is Pixel Art's, and per <i>canvas</i>: a pass that throws is removed, this
    /// mod's canvas is faulted, one line names it in <c>godot.log</c>, and every other mod
    /// drawing through the library keeps working. <c>Simulation.Init</c> and
    /// <c>Simulation.Reset</c> clear it there, so a fault costs a session rather than a process.
    /// This mod used to own an identical latch and no longer needs to.</para>
    /// </summary>
    public static bool Faulted =>
        (Painter.Surface?.Faulted ?? false) || (Magnifier.Surface?.Faulted ?? false);

    /// <summary>
    /// What this mod would draw on a given material, decided from its name alone.
    ///
    /// <para>Public because it is the whole vocabulary, and because another mod adding a
    /// machine can ask what the rules make of its name without installing it first.</para>
    /// </summary>
    public static Annotation Describe(string materialName) => AnnotationRules.For(materialName);

    /// <summary>
    /// What this mod would draw on a given cell, neighbours and quadrant-encoded filter
    /// targets included. Null field or no table gives <see cref="Annotation.None"/>.
    /// </summary>
    public static Annotation At(SimField? field, int x, int y) =>
        field == null ? Annotation.None : MaterialAnnotations.At(field, x, y);

    /// <summary>
    /// Everything this mod carries between tests, put back. Registered with the harness's
    /// <c>StateRegistry</c> by the test mod; see its <c>ModEntry</c>.
    ///
    /// <para><b>This resets runtime state, not only configuration</b>, which is the whole point
    /// of the method and is silent when got wrong. The fault latch matters most here: this mod
    /// has no per-tick hook, so its <c>Simulation.Init</c>/<c>Reset</c> clearing never fires in
    /// a region test, and a latch left set makes the mod draw nothing for every test after it
    /// -- failing the ones that assert the mod's behaviour and passing the ones that assert the
    /// game's. The runtime switch is the other: <see cref="Enabled"/> is the conjunction of a
    /// private field and the setting, so restoring the setting alone cannot reach it.</para>
    ///
    /// <para>There is no effect to re-apply, and nothing to clear. This mod writes nothing into
    /// the game and retains no marks -- everything it draws is drawn for one frame by a pass, so
    /// switching it off is complete on the next one.</para>
    /// </summary>
    public static void ResetState()
    {
        Settings.Reset();
        _enabled = true;
        Painter.Surface?.ClearFault();
        Magnifier.Surface?.ClearFault();
        Magnifier.Reset();
        LabelCache.Clear();
        GateArt.Clear();

        // The cursor is process-wide state this mod takes away, so it has to be given back here
        // as well as on the frame the panel comes down. A test that throws before its finally, or
        // a fault while the panel is up, would otherwise leave the player with no cursor in the
        // menus and nothing to suggest which mod did it.
        CursorPatch.Restore();
        ClickGuard.Clear();
        ResetCounters();

        // Settings.Reset put debugOverlay back to false, and Sync is what makes that mean
        // something: without it a test that switched the overlay on leaves its pass registered
        // and drawing over every test after it.
        DebugOverlay.Sync();
        DebugOverlay.Surface?.ClearFault();
    }

    /// <summary>
    /// Settings and runtime state in one line, for a failure message.
    ///
    /// Names the fault latch explicitly, because "the mod is switched off" is the explanation a
    /// confusing failure most often has, and it should be in the failure message rather than
    /// something you go looking for in <c>godot.log</c>.
    /// </summary>
    public static string DescribeState() =>
        $"{Settings.Describe()} faulted={Faulted} ready={Ready} " +
        $"annotatedMaterials={AnnotatedMaterials} panel={PanelCells}x{PanelCells}@{PanelCell}";
}
