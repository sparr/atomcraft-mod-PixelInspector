using PixelArt;

namespace PixelInspector;

/// <summary>
/// What family of machine an annotation belongs to. Decides only its colour, so that a glance
/// separates "this moves things" from "this decides things" without reading the text.
/// </summary>
public enum Kind
{
    /// <summary>Nothing to say about this pixel.</summary>
    None,

    /// <summary>Moves or fires matter: conveyors, pumps, guns, lasers, balances, projectiles.</summary>
    Flow,

    /// <summary>Decides something: gates, latches.</summary>
    Logic,

    /// <summary>Tests what is next to it: match and nonmatch filters, sensors.</summary>
    Filter,

    /// <summary>Adds heat, or reports a temperature reached.</summary>
    Heat,

    /// <summary>Removes heat.</summary>
    Cool,

    /// <summary>Counts ticks: oscillators, dispensers.</summary>
    Timing,

    /// <summary>
    /// Separates water from what it is dissolved in: the Water Filter and Block Water.
    ///
    /// <para>A family of its own rather than part of <see cref="Filter"/>, because they do a
    /// different job and saying otherwise was a real bug. A filter <i>tests</i> what arrives and
    /// passes it or does not; these two <b>split</b> a hydrate into its water and its dry part
    /// and send one of each way. Labelling Block Water as "a filter that stops water" put it in
    /// the same family, and the same colour, as a nonmatch filter programmed to Water -- which
    /// is a different machine with a different effect.</para>
    /// </summary>
    Split,
}

/// <summary>
/// A small symbol drawn in front of a label rather than spelled in the font.
///
/// <para>The two that matter are a tick and a cross, and neither is in printable ASCII, so
/// neither is in the bitmap font. The game has both as art -- <c>ui_status_checkmark</c> and
/// <c>ui_status_x</c> -- already coloured green and red, which is why they are drawn untinted
/// and why the cross is the red the request asked for without anything here choosing a
/// red.</para>
///
/// <para>Named here rather than carried as a texture for the same reason
/// <see cref="Shape"/> is: <see cref="AnnotationRules"/> runs over every material at startup and
/// must stay free of game types.</para>
/// </summary>
public enum MarkIcon
{
    /// <summary>No symbol. The mark, if any, is text.</summary>
    None,

    /// <summary>A green tick: this passes.</summary>
    Check,

    /// <summary>A red cross: this is stopped.</summary>
    Cross,
}

/// <summary>
/// A shape drawn across the whole pixel, behind anything else.
///
/// <para>Each of these names one of the four shapes <c>PixelArt.GameArt</c> borrows from the
/// game's own art: the material guide's square, drop and cloud, and the little person from the
/// spaceship inventory icon. <see cref="Painter"/> is where the name is turned into the
/// texture.</para>
///
/// <para><b>Why a name here rather than the art itself.</b> An <c>IconArt</c> is a loaded
/// texture, and a texture cannot be asked for before the engine has one -- which is to say, not
/// from <see cref="AnnotationRules"/>, which runs over every material at startup and is a pure
/// function of a name with no game type anywhere in it. Naming the shape keeps that property,
/// and costs one <c>switch</c> on the frame it is drawn.</para>
/// </summary>
public enum Shape
{
    /// <summary>No shape behind the label.</summary>
    None,

    /// <summary>A filled square: a solid.</summary>
    Solid,

    /// <summary>A falling drop: a liquid.</summary>
    Liquid,

    /// <summary>A cloud: a gas.</summary>
    Gas,

    /// <summary>A little person.</summary>
    Player,
}

/// <summary>
/// What to draw on one pixel: a direction nub on the cell's edge, optionally a second one for
/// the side a two-ended machine takes from, and a short string in the middle.
///
/// <para><b>Why the direction is a nub on the edge and not a character.</b> A cell is 12
/// screen pixels across at the game's own maximum zoom. An arrow glyph and a label cannot
/// share that, and the direction is the half that survives being drawn tiny -- a triangle
/// pointing out of an edge reads at three pixels, where a character does not. Putting it on
/// the edge also means the two never collide however long the text gets.</para>
///
/// <para>This is a plain value with no game type in it, which is the point: every rule that
/// produces one is a pure function of a material's name, so the whole vocabulary can be
/// asserted headlessly without a world, a renderer, or the mod being installed.</para>
/// </summary>
public readonly struct Annotation
{
    /// <summary>Nothing to draw. What every pixel the mod has no opinion about gets.</summary>
    public static readonly Annotation None = default;

    public Annotation(Kind kind, Aim aim = Aim.None, string body = "",
                      Aim source = Aim.None, bool off = false,
                      string mark = "", Shape shape = Shape.None,
                      MarkIcon icon = MarkIcon.None, int? kelvin = null,
                      GateKind gate = GateKind.None)
    {
        Kind = kind;
        Aim = aim;
        Body = body;
        Source = source;
        Off = off;
        Mark = mark;
        Shape = shape;
        Icon = icon;
        Gate = gate;
        Kelvin = kelvin;
    }

    /// <summary>The colour family. <see cref="Kind.None"/> means there is nothing to draw.</summary>
    public Kind Kind { get; }

    /// <summary>
    /// Where the machine sends things, or which way it faces. Drawn as a filled nub on that
    /// edge of the cell.
    /// </summary>
    public Aim Aim { get; }

    /// <summary>
    /// The side a two-ended machine takes from, drawn as a hollow nub. Only the balance
    /// pixels have one; everything else leaves it <see cref="Aim.None"/>.
    /// </summary>
    public Aim Source { get; }

    /// <summary>
    /// The leading symbol, kept apart from <see cref="Body"/> so the two can be drawn on
    /// separate lines when the cell is tall enough for that to read better.
    ///
    /// <para><c>=</c>, <c>≠</c> and <c>?</c> are the whole vocabulary here, and each is the part
    /// of a filter's label that must never be lost: <c>=</c> against <c>≠</c> is the difference
    /// between a machine that passes something and one that stops it. Splitting it out is what
    /// lets the layout stack it above the target's name and give the name the cell's full width,
    /// rather than spending characters of that width on the symbol.</para>
    /// </summary>
    public string Mark { get; }

    /// <summary>
    /// The rest of the label: a target's name, a gate's symbol, a thermostat's setting. Empty
    /// when the nub or the mark says everything.
    /// </summary>
    public string Body { get; }

    /// <summary>
    /// A shape drawn across the whole pixel behind the label, or <see cref="Shape.None"/>.
    /// </summary>
    public Shape Shape { get; }

    /// <summary>
    /// A symbol drawn in front of the label, or <see cref="MarkIcon.None"/>. Takes the place of
    /// <see cref="Mark"/> where it is set; the two are never both used.
    /// </summary>
    public MarkIcon Icon { get; }

    /// <summary>
    /// Which logic gate this is, drawn as a shape rather than spelled as a letter. The
    /// <see cref="Mark"/> is kept alongside as the fallback for a cell too small for the shape.
    /// </summary>
    public GateKind Gate { get; }

    /// <summary>
    /// The temperature this annotation is about, in kelvin, where it is about one.
    ///
    /// <para>Carried alongside <see cref="Body"/> rather than baked into it, because how a
    /// temperature should be written is the player's choice: the game has a Celsius, Kelvin and
    /// Fahrenheit setting and honouring it means converting at the moment of drawing. The rules
    /// cannot do that and stay free of game types, so they record the number and leave the units
    /// to whoever draws it. <see cref="Body"/> holds the kelvin figure as written in the
    /// material's own name, which is what a test asserts on.</para>
    /// </summary>
    public int? Kelvin { get; }

    /// <summary>
    /// The whole label on one line, which is what it comes to when the cell has no room to
    /// stack it. What the tests assert on, and what <see cref="ToString"/> reports.
    /// </summary>
    public string Text => Mark + Body;

    /// <summary>
    /// Whether the machine is in its off state, which draws everything dimmed.
    ///
    /// <para>Read from the material's <i>name</i> rather than from <c>MaterialType.IsOn</c>,
    /// which is not set consistently: <c>Conveyor Left</c> declares it and
    /// <c>And Gate (Up) (On)</c> does not, though both are the running half of an on/off
    /// pair. The names are regular where the flag is not.</para>
    /// </summary>
    public bool Off { get; }

    /// <summary>Whether there is anything at all to draw.</summary>
    public bool Any => Kind != Kind.None &&
                       (Aim != Aim.None || Text.Length > 0 || Shape != Shape.None ||
                        Icon != MarkIcon.None ||
                        Gate != GateKind.None);

    /// <summary>A one-line form, for tests and for the log. Not shown to a player.</summary>
    public override string ToString() =>
        !Any ? "none"
             : $"{Kind}" +
               (Aim != Aim.None ? $" aim={Aim}" : "") +
               (Source != Aim.None ? $" from={Source}" : "") +
               (Text.Length > 0 ? $" '{Text}'" : "") +
               (Shape != Shape.None ? $" {Shape.ToString().ToLowerInvariant()}" : "") +
               (Icon != MarkIcon.None ? $" {Icon.ToString().ToLowerInvariant()}" : "") +
               (Off ? " off" : "");
}
