using Godot;
using FileAccess = Godot.FileAccess;

namespace PixelInspector;

/// <summary>
/// What the player can change, and the one file they change it in.
///
/// <para>The file is <c>user://PixelInspector.json</c>, beside the game's own
/// <c>DeviceSettings.json</c>, and it is written with the defaults the first time the mod runs
/// so that the options are discoverable without reading the README. It is read once, at
/// <c>Initialize</c>; nothing here is reread while the game runs.</para>
///
/// <para>Parsed with Godot's own <see cref="Json"/> rather than Newtonsoft. Newtonsoft is
/// loaded and would do, but a mod that reaches for the game's copy of a library takes on that
/// version; a handful of scalars do not need it.</para>
/// </summary>
public static class Settings
{
    /// <summary>Where the settings file lives, in Godot's user data directory.</summary>
    public const string Path = "user://" + ModEntry.ModId + ".json";

    /// <summary>
    /// Master switch. Off, the render hook falls straight through, so the mod can be turned
    /// off without uninstalling it. Also what a test toggles; see
    /// <see cref="PixelInspectorApi.Enabled"/>.
    /// </summary>
    public static bool Enabled = true;

    /// <summary>
    /// How many cells out from the cursor are annotated.
    ///
    /// <para>Six is a thirteen-by-thirteen block, which is about the size of one machine and
    /// its immediate plumbing: large enough to read a filter bank at a glance, small enough
    /// that labels on neighbouring cells rarely overlap. The whole point of a radius rather
    /// than the whole screen is that a screen of labels is not readable, so raising this a
    /// long way trades the mod's legibility for its reach.</para>
    /// </summary>
    public static int Radius = 6;

    /// <summary>
    /// Whether Alt has to be held. <c>false</c> annotates continuously, which is worth having
    /// while building something and tiring the rest of the time.
    /// </summary>
    public static bool RequireAlt = true;

    /// <summary>
    /// How long a press of Alt may last and still count as a tap, in seconds. A tap leaves the
    /// panel up; see <see cref="StickyPanel"/>.
    ///
    /// <para>It gates only the way <i>up</i>. Putting a raised panel back down takes any release,
    /// however long the press, because a press made while the panel is already on screen cannot
    /// have been a peek.</para>
    ///
    /// <para><b>0 means no press is ever a tap</b>, which is how a player asks for the plain
    /// hold-to-look behaviour and nothing else. A large value makes every release raise the
    /// panel. The default sits well above a deliberate tap, which is around a tenth of a second,
    /// and well below a glance at the panel, which takes as long as it takes to read.</para>
    /// </summary>
    public static double AltTapSeconds = 0.25;

    /// <summary>
    /// A ceiling on how much of a target's name is ever shown, however much room the cell has.
    /// Zero, the default, means no ceiling.
    ///
    /// <para><b>Not the cut length.</b> <see cref="LabelLayout"/> decides that against the cell
    /// on the frame it draws: zoomed in it shows the whole name, wrapped across lines, and
    /// zoomed out it cuts to whatever fits and marks the cut with an ellipsis.</para>
    ///
    /// <para><b>It used to default to 16, and that was wrong.</b> The cap is applied to the body
    /// <i>before</i> the layout sees it, so it is not a limit that only bites on a small cell --
    /// it bites on every cell. A 41-character name came out as two lines and an ellipsis in a cell
    /// with room for five full lines, which reads as the wrapping being broken rather than as a
    /// setting doing its job. The number also predates the magnifier: it was chosen when labels
    /// went on world pixels twelve screen pixels across, where a long name was hopeless anyway.
    /// The layout already declines to draw what will not fit, so nothing needs protecting.</para>
    ///
    /// <para>The knob stays for a player who wants terser labels than their cells could hold. An
    /// existing settings file carries the old 16 and has to be edited or deleted to pick up the
    /// new default.</para>
    ///
    /// <para>Neither the prefix nor the cut mark counts against it.</para>
    /// </summary>
    public static int MaxTextLength;

    /// <summary>
    /// Draws arrows and shapes on the machines themselves, across the whole world, as well as in
    /// the magnifier. Off by default.
    ///
    /// <para>Off because it changes how the game looks rather than what it tells you: marks
    /// scattered over the world read as part of the art, and the game's art is somebody else's.
    /// The magnifier is the mod's answer, and it keeps everything it draws inside one panel a
    /// player chose to look at. This is here for people who would rather have the arrows in
    /// place, which was what the mod did before the magnifier existed.</para>
    ///
    /// <para>Text is never drawn in the world even with this on: a world pixel is 12 screen
    /// pixels at the game's own closest zoom, which is not enough for two characters.</para>
    /// </summary>
    public static bool WorldMarks;

    /// <summary>
    /// Swaps the cursor for a faded crosshair while the magnifier is up, so it marks the pixel it
    /// is pointing at rather than covering it.
    ///
    /// <para>The crosshair is the game's own, the one it already uses for the Drill and the
    /// Multitool. The cost is that the tool-specific cursor art is not shown while the panel is
    /// up; turn this off to keep it.</para>
    /// </summary>
    public static bool QuietCursor = true;

    /// <summary>
    /// Whether a click is swallowed when the panel is up and the pointer is over interface that
    /// would take it. On by default.
    ///
    /// <para>The panel already warns about this by turning the marker around the centre pixel
    /// thick and red, but a warning does not stop the click: it still lands on whatever toolbar
    /// slot or inventory button is underneath. Reading a machine and changing your tool by
    /// accident is a poor trade, and the click almost never means what it would do.</para>
    ///
    /// <para>Off leaves the warning and lets the click through, which is what someone who uses the
    /// interface with the panel held up would want. See <see cref="ClickGuard"/>.</para>
    /// </summary>
    public static bool BlockClicksOverUI = true;

    /// <summary>
    /// Draws the annotation radius as an outline, with the pass's counters on the pixel under the
    /// cursor. Off by default.
    ///
    /// <para>It answers the question someone tuning <see cref="Radius"/> actually has -- is that
    /// block the size I meant? -- and it is the quickest way to see the mod is alive when it
    /// appears not to be, because the counters move even over a block where nothing is annotated.
    /// See <see cref="DebugOverlay"/>.</para>
    /// </summary>
    public static bool DebugOverlay;

    /// <summary>
    /// The key <c>textScale</c> used to live here, and now lives in Pixel Art.
    ///
    /// <para>It magnifies every label <i>any</i> mod draws through that library, which is the
    /// kind of thing a player wants to say once rather than once per mod -- and a duplicate here
    /// would be one of two knobs doing the same job, with no way to tell which was in force.
    /// <c>LabelLayout</c> reads Pixel Art's value by default and this mod does not override
    /// it.</para>
    ///
    /// <para>Kept as a name rather than deleted outright so <see cref="Load"/> can recognise it
    /// in an older settings file and say where it went.</para>
    /// </summary>
    private const string MovedToPixelArt = "textScale";

    /// <summary>Whether <see cref="Load"/> has run, so it does not run twice.</summary>
    private static bool _loaded;

    /// <summary>
    /// Reads the settings file, writing it with the defaults first if it is not there.
    ///
    /// Never throws: a settings file that cannot be read or does not parse leaves the defaults
    /// in place and says so in the log. Refusing to start over a stray comma would be a worse
    /// outcome than ignoring it.
    /// </summary>
    public static void Load()
    {
        if (_loaded)
            return;
        _loaded = true;

        try
        {
            if (!FileAccess.FileExists(Path))
            {
                Save();
                return;
            }

            using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Read);
            if (file == null)
            {
                Log.Warn($"could not open {Path} ({FileAccess.GetOpenError()}); using defaults");
                return;
            }

            var parsed = Json.ParseString(file.GetAsText());
            if (parsed.VariantType != Variant.Type.Dictionary)
            {
                Log.Warn($"{Path} is not a JSON object; using defaults");
                return;
            }

            var settings = parsed.AsGodotDictionary();
            Enabled = Bool(settings, "enabled", Enabled);
            RequireAlt = Bool(settings, "requireAlt", RequireAlt);
            AltTapSeconds = Clamp(Double(settings, "altTapSeconds", AltTapSeconds), 0.0, 5.0, "altTapSeconds");
            Radius = Clamp(Int(settings, "radius", Radius), 0, 64, "radius");
            WorldMarks = Bool(settings, "worldMarks", WorldMarks);
            QuietCursor = Bool(settings, "quietCursor", QuietCursor);
            BlockClicksOverUI = Bool(settings, "blockClicksOverUI", BlockClicksOverUI);
            DebugOverlay = Bool(settings, "debugOverlay", DebugOverlay);
            MaxTextLength = Clamp(Int(settings, "maxTextLength", MaxTextLength), 0, 64, "maxTextLength");

            // A key this mod used to own. Saying where it went costs one line and saves a player
            // wondering why the file they edited stopped doing anything.
            if (settings.ContainsKey(MovedToPixelArt))
                Log.Info($"'{MovedToPixelArt}' has moved to Pixel Art and is ignored here; set it " +
                         "in user://PixelArt.json, where it applies to every mod that draws " +
                         "through that library. Delete it from this file to silence this.");

            Log.Info($"settings: {Describe()}");
        }
        catch (Exception e)
        {
            Log.Warn($"could not read {Path}; using defaults: {e.Message}");
        }
    }

    /// <summary>The current values on one line, for the startup log and for a failure report.</summary>
    public static string Describe() =>
        $"enabled={Enabled} requireAlt={RequireAlt} altTap={AltTapSeconds}s radius={Radius} " +
        $"maxTextLength={MaxTextLength} worldMarks={WorldMarks} " +
        $"quietCursor={QuietCursor} blockClicksOverUI={BlockClicksOverUI} debugOverlay={DebugOverlay}";

    /// <summary>
    /// Restores every setting to its default, without touching the file. Used between tests.
    ///
    /// Written out longhand rather than by re-reading the file, because a test must not depend
    /// on what happens to be on the developer's disk.
    /// </summary>
    public static void Reset()
    {
        Enabled = true;
        RequireAlt = true;
        AltTapSeconds = 0.25;
        Radius = 6;
        MaxTextLength = 0;
        WorldMarks = false;
        QuietCursor = true;
        DebugOverlay = false;
    }

    /// <summary>Writes the current values, which on a first run are the defaults.</summary>
    private static void Save()
    {
        using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        if (file == null)
        {
            Log.Warn($"could not write {Path} ({FileAccess.GetOpenError()})");
            return;
        }

        // Hand-written rather than serialized, because the comments are the point: this file
        // is the only documentation a player who never finds the README will see.
        file.StoreString(
            "{\n" +
            "    \"_\": \"Settings for the PixelInspector mod. Delete this file to restore defaults.\",\n" +
            "    \"_enabled\": \"false turns the whole mod off without uninstalling it.\",\n" +
            $"    \"enabled\": {(Enabled ? "true" : "false")},\n" +
            "    \"_requireAlt\": \"false annotates all the time instead of only while Alt is held.\",\n" +
            $"    \"requireAlt\": {(RequireAlt ? "true" : "false")},\n" +
            "    \"_altTapSeconds\": \"How long a press of Alt can last and still count as a tap. A tap leaves the panel up; any release puts a raised one back down. 0 means never leave it up.\",\n" +
            $"    \"altTapSeconds\": {Invariant(AltTapSeconds)},\n" +
            "    \"_radius\": \"How many cells out from the cursor are annotated. 0 labels only the cell under it.\",\n" +
            $"    \"radius\": {Radius},\n" +
            "    \"_maxTextLength\": \"Ceiling on how much of a target name is shown; 0 for no ceiling. The zoom decides the rest, and a cut name ends in an ellipsis.\",\n" +
            $"    \"maxTextLength\": {MaxTextLength},\n" +
            "    \"_labelSize\": \"Label magnification lives in user://PixelArt.json as textScale, because it applies to every mod drawing through that library.\",\n" +
            "    \"_worldMarks\": \"true also draws arrows and shapes on the machines themselves, across the world, not just in the magnifier.\",\n" +
            $"    \"worldMarks\": {(WorldMarks ? "true" : "false")},\n" +
            "    \"_quietCursor\": \"false keeps the normal tool cursor over the magnifier instead of a faded crosshair.\",\n" +
            $"    \"quietCursor\": {(QuietCursor ? "true" : "false")},\n" +
            "    \"_blockClicksOverUI\": \"true swallows a click made while the panel is up and the pointer is over interface that would take it. The panel marks that case with a thick red outline.\",\n" +
            $"    \"blockClicksOverUI\": {(BlockClicksOverUI ? "true" : "false")},\n" +
            "    \"_debugOverlay\": \"true outlines the annotated block and shows how many pixels it looked at. Useful while choosing a radius.\",\n" +
            $"    \"debugOverlay\": {(DebugOverlay ? "true" : "false")}\n" +
            "}\n");
        Log.Info($"wrote default settings to {Path}");
    }

    private static bool Bool(Godot.Collections.Dictionary settings, string key, bool fallback) =>
        settings.TryGetValue(key, out var value) ? value.AsBool() : fallback;

    private static int Int(Godot.Collections.Dictionary settings, string key, int fallback) =>
        settings.TryGetValue(key, out var value) ? (int)value.AsDouble() : fallback;

    private static double Double(Godot.Collections.Dictionary settings, string key, double fallback) =>
        settings.TryGetValue(key, out var value) ? value.AsDouble() : fallback;

    /// <summary>
    /// A number the way JSON spells it, whatever the machine's locale spells it as. The only
    /// non-integer setting written here, and a culture that uses a comma for the decimal point
    /// would otherwise put one in the file and make it unparseable the next time the mod starts.
    /// </summary>
    private static string Invariant(double value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Keeps a number inside the range the mod can actually honor, and says so when it had to.
    /// A value silently clamped is a setting that did not do what the file says it does.
    /// </summary>
    private static int Clamp(int value, int min, int max, string key)
    {
        var clamped = Math.Clamp(value, min, max);
        if (clamped != value)
            Log.Warn($"{key}={value} is outside {min}..{max}; using {clamped}");
        return clamped;
    }

    /// <inheritdoc cref="Clamp(int,int,int,string)"/>
    private static double Clamp(double value, double min, double max, string key)
    {
        // NaN survives Math.Clamp, and a NaN threshold compares false against everything, so a
        // mistyped value would silently mean "no press is ever a tap" instead of being reported.
        var clamped = double.IsNaN(value) ? min : Math.Clamp(value, min, max);
        if (clamped != value)
            Log.Warn($"{key}={value} is outside {min}..{max}; using {clamped}");
        return clamped;
    }
}
