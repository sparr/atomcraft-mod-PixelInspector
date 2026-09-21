using Atomcraft;
using Godot;
using PixelArt;

namespace PixelInspector;

/// <summary>
/// The per-frame work: decide which pixels to annotate, ask
/// <see cref="MaterialAnnotations"/> about each, and draw.
///
/// <para><b>A pass rather than a painter.</b> Pixel Art offers both: a painter is handed every
/// pixel on screen and a pass walks whatever it likes. This mod already knows which pixels it
/// cares about -- the block around the cursor -- so being handed all 57,000 to reject 56,800 of
/// them would be the expensive way round. <c>Canvas.ForEachPixel</c> is the walk, so the pass
/// says which pixels it wants and the library does the projecting.</para>
///
/// <para><b>Why a block around the cursor and not the whole screen.</b> Annotating everything
/// would be both unreadable and expensive; annotating what the cursor is over is neither, and it
/// matches what the gesture means -- Alt is already the game's "tell me more about what I am
/// looking at" modifier, and what you are looking at is where the mouse is. A radius of 6 is 169
/// pixels, which is one machine and its plumbing.</para>
///
/// <para>There is no try/catch here and no fault latch. A pass that throws is removed by the
/// library and this mod's canvas faulted, with one line in <c>godot.log</c> naming it; writing
/// that guard in every consumer is exactly what Pixel Art exists to stop.</para>
/// </summary>
internal static class Painter
{
    /// <summary>What the canvas is registered under. Two mods sharing a name would clear each other's marks.</summary>
    private const string Owner = ModEntry.ModId;

    private const string PassName = "annotations";

    // The colour families. Chosen to be distinguishable from each other and from the machine
    // colours underneath, all of which are fully saturated primaries: these are pale, so a label
    // reads as an overlay rather than as part of the pixel it sits on.
    private static readonly Color FlowColor   = new(0.55f, 0.90f, 1.00f);
    private static readonly Color LogicColor  = new(1.00f, 0.82f, 0.35f);
    private static readonly Color FilterColor = new(0.62f, 1.00f, 0.58f);
    private static readonly Color HeatColor   = new(1.00f, 0.62f, 0.45f);
    private static readonly Color CoolColor   = new(0.62f, 0.80f, 1.00f);
    private static readonly Color TimingColor = new(0.85f, 0.70f, 1.00f);
    private static readonly Color SplitColor  = new(0.70f, 0.95f, 0.90f);

    /// <summary>
    /// The dark edge drawn around every mark, so one reads over a fully saturated machine colour.
    /// Every machine colour in the shipped data is a saturated primary and every mark colour here
    /// is pale, which collides badly on the light ones: a cyan arrow on a light-green conveyor is
    /// nearly invisible without it.
    ///
    /// <para>Fully opaque, and deliberately <b>not</b> dimmed along with an off machine's mark.
    /// Translucent, the four offset copies of an outlined glyph blend unevenly where they
    /// overlap; and dimming the edge as well as the mark made an off machine's label nearly
    /// unreadable, which is backwards -- that a machine is off is information, and the reader
    /// should still be able to see what it is.</para>
    /// </summary>
    private static readonly Color EdgeColor = new(0f, 0f, 0f, 1f);

    /// <summary>
    /// The shape drawn behind a label: neutral, and faint.
    ///
    /// <para><b>Deliberately not the family colour.</b> Tinting it to match made the shape and the
    /// mark sitting on it the same hue, so a green <c>≠</c> over a green drop read as one smudge.
    /// Keeping the shape neutral splits the two channels cleanly: the <i>shape</i> says which
    /// phase, the <i>colour</i> says which family, and neither has to be read through the
    /// other.</para>
    /// </summary>
    /// <summary>
    /// The shape's tint, chosen against the machine it is drawn on.
    ///
    /// <para><b>Why not one colour.</b> The shapes were a flat white watermark, which reads on the
    /// dark two thirds of the palette and vanishes on the rest: a pale drop on a yellow Allow
    /// Liquids or a light grey Water Filter is white on near-white. The fix a watermark usually
    /// gets is more alpha, and that trades the problem for a worse one, since the shape sits
    /// behind a material name and a stronger shape is a less readable name.</para>
    ///
    /// <para>So the contrast comes from the direction rather than the amount: a light shape on a
    /// dark machine and a dark one on a light machine, decided by the cell's own luminance. The
    /// alpha is the same either way, so the name on top is no worse off than it was.</para>
    ///
    /// <para>The fallback when nothing is known about what is underneath is the old white, which
    /// is what the world pass gets -- there the shape is drawn on a single game pixel whose colour
    /// the pass does not carry.</para>
    /// </summary>
    private static Color ShapeColor(Color? under)
    {
        if (under is not { } beneath)
            return new Color(1f, 1f, 1f, ShapeAlpha);

        return IsLight(beneath)
            ? new Color(0f, 0f, 0f, ShapeAlpha)
            : new Color(1f, 1f, 1f, ShapeAlpha);
    }

    /// <summary>
    /// Whether a colour reads as light, by Rec. 709 luma -- which is what "looks light" means to
    /// an eye rather than to an average of the channels.
    /// </summary>
    private static bool IsLight(Color color) =>
        0.2126f * color.R + 0.7152f * color.G + 0.0722f * color.B > 0.5f;

    /// <summary>
    /// The colour a label is drawn in: black on a light machine, white on a dark one.
    ///
    /// <para><b>This replaced an outline.</b> Every label used to be drawn in its family's colour
    /// with a black edge around each glyph, which is the usual way to make text survive an unknown
    /// background and costs a great deal at this size: an outline on a three-pixel-wide glyph is
    /// more outline than glyph, and it thickens every stroke by a pixel on each side in a font
    /// whose strokes are one pixel. Choosing the colour against the background instead gets the
    /// contrast from the text itself and leaves the glyphs the shape they were drawn as.</para>
    ///
    /// <para>The family colour moves entirely onto the arrows and the marks, which have the mass
    /// to carry it. A label is now legibility only.</para>
    /// </summary>
    private static Color LabelColor(Color? under) =>
        under is { } beneath && IsLight(beneath) ? Colors.Black : Colors.White;

    /// <summary>
    /// How strong a shape is. Raised from the old 0.38 now that it is no longer always white:
    /// half the time it is the dark one, which a label's own light glyphs sit on perfectly well.
    /// </summary>
    /// <summary>
    /// How strong a shape is. Well clear of the watermark it used to be, which vanished on the
    /// lighter half of the palette, and well short of solid: a shape that reads as a block of flat
    /// colour stops looking like a mark on a machine and starts looking like a different machine.
    /// Nothing is drawn on top of one -- the six pixels that carry a shape all say what they say
    /// with the shape alone -- so the only thing the opacity trades against is that.
    /// </summary>
    private const float ShapeAlpha = 0.64f;

    /// <summary>
    /// How much of its colour a switched-off machine's annotation keeps. Enough to read as clearly
    /// dimmer than a running one, and not so little that reading it is a struggle: at the first
    /// value tried, 0.45, an off gate's symbol came out as a smudge over its own outline.
    /// </summary>
    private const float OffAlpha = 0.62f;

    /// <summary>
    /// How much of a mark's colour survives when it is sharing the cell with a label. Raised along
    /// with dropping the outline: the mark has only its colour left to be seen by, and the label on
    /// top is plain black or white against the machine, which reads over a strong mark perfectly
    /// well.
    /// </summary>
    private const float MarkedAlpha = 0.85f;

    /// <summary>How much of its colour the hollow "takes from here" arrow keeps.</summary>
    private const float SourceAlpha = 0.40f;

    private static Canvas? _canvas;

    /// <summary>This mod's drawing surface, or null before <see cref="Register"/> has run.</summary>
    internal static Canvas? Surface => _canvas;

    /// <summary>How many pixels were annotated on the last frame the pass ran.</summary>
    internal static int AnnotatedLastFrame { get; private set; }

    /// <summary>How many pixels the pass looked at on the last frame it ran.</summary>
    internal static int ScannedLastFrame { get; private set; }

    /// <summary>
    /// How many frames the pass has run on.
    ///
    /// <para>The one thing a headless test can watch. Everything this mod produces is pixels, and
    /// there are none without a display, so without this a suite could only ever assert that the
    /// drawing is <i>registered</i> -- never that it <i>runs</i>.</para>
    /// </summary>
    internal static long Frames { get; private set; }

    /// <summary>Drops the frame counters, so a test can measure from a known point.</summary>
    internal static void ResetCounters()
    {
        Frames = 0;
        AnnotatedLastFrame = 0;
        ScannedLastFrame = 0;
    }

    /// <summary>
    /// Takes a canvas and registers the drawing.
    ///
    /// <para>Safe to call from <c>Initialize</c>, before the game has initialized anything:
    /// <c>Canvas.For</c> touches nothing of the game's, and the library draws from a snapshot of
    /// its registry, so a canvas registered at any moment starts drawing on the next frame. It
    /// does not matter which order the loader happens to run this mod and Pixel Art in.</para>
    /// </summary>
    internal static void Register()
    {
        _canvas = Canvas.For(Owner);

        // The gate is a predicate rather than DrawWhen.AltHeld, because the condition is this
        // mod's rather than the library's: Alt matters only while `requireAlt` says so, and a
        // player who turns that off wants the annotations without holding anything. AltHeld
        // reads the real keyboard and nothing else, so it could express neither that nor the
        // automated case -- a test cannot hold a key down.
        //
        // Asked once per frame, before the pass is called, so the pass costs nothing at all on
        // the frames nobody asked for.
        // Gated on the setting as well as the modifier: the world marks are off by default, so
        // the pass is not called at all for most players.
        _canvas.SetPass(PassName, Draw, () => Settings.WorldMarks && Showing);

        // The diagnostic, if the settings file asked for it. Its own canvas and its own pass; see
        // DebugOverlay for why it is here rather than in the test mod.
        DebugOverlay.Sync();
    }

    /// <summary>
    /// Whether the annotations are being drawn right now.
    ///
    /// <para>Handed to the library as the pass's predicate, so it is asked once a frame and the
    /// pass is not called at all when it answers false. See <see cref="Register"/> for why it is
    /// this rather than <see cref="DrawWhen.AltHeld"/>.</para>
    ///
    /// <para>The third term is what makes Alt a toggle as well as a modifier; see
    /// <see cref="StickyPanel"/>. It is one predicate for the whole mod, so the magnifier, the
    /// world marks, the zoom keys, the click guard and the hidden cursor all follow it together
    /// and cannot disagree about whether the panel is up.</para>
    /// </summary>
    internal static bool Showing => !Settings.RequireAlt || Canvas.AltHeld || StickyPanel.Stuck;

    /// <summary>
    /// One frame's worth. Called by the library's shared per-frame pass, after the game has
    /// positioned the world from the camera, so the projection is the one the frame was drawn
    /// with.
    /// </summary>
    private static void Draw(Canvas canvas)
    {
        AnnotatedLastFrame = 0;
        ScannedLastFrame = 0;

        if (!PixelInspectorApi.Enabled || !MaterialAnnotations.Ready)
            return;

        Frames++;

        if (ViewGeometry.MouseTile() is not { } mouse)
            return;

        var field = Simulation.CurrentState?.Field;
        if (field == null)
            return;

        // The library walks the block: it clips to what is on screen and inside the world,
        // projects the corner once and steps the rows from it, and hands over each pixel with
        // its material and its rectangle already in hand. This used to be twenty lines here, and
        // the stepping was a copy of the library's own.
        ScannedLastFrame = canvas.ForEachPixel(mouse, Settings.Radius, p =>
        {
            var annotation = MaterialAnnotations.At(field, p.Tile.X, p.Tile.Y);
            if (!annotation.Any)
                return;
            // Marks only, never text: a world pixel is 12 screen pixels at the game's own closest
            // zoom, and two characters of the smallest font are 11 wide before margins. Text in
            // the world was always either skipped or a smear; it lives in the magnifier now.
            DrawAnnotation(canvas, p.Screen, annotation, marksOnly: true);
            AnnotatedLastFrame++;
        });
    }

    /// <summary>
    /// Draws one annotation into a rectangle: the shape behind, the arrows on the edges, the
    /// label in the middle.
    ///
    /// <para>Shared by the world pass and the magnifier, which is the whole reason it takes a
    /// rectangle rather than a pixel: the magnifier's cells are its own, several times the size
    /// of the world's, and nothing else about the drawing changes.</para>
    /// </summary>
    /// <param name="marksOnly">
    /// Skip the label. What the world pass asks for, because a world pixel has no room for text.
    /// </param>
    /// <param name="smallerCell">
    /// The magnifier's next zoom rung down, so a label can be held to at least the size it had
    /// there. Null for the world pass, which has one pixel size and no ladder.
    /// </param>
    internal static void DrawAnnotation(Canvas canvas, Rect2 screen, in Annotation annotation,
                                        bool marksOnly = false, Color? under = null,
                                        int? smallerCell = null)
    {
        var color = ColorOf(annotation.Kind);
        if (annotation.Off)
            color = new Color(color, color.A * OffAlpha);

        // Behind everything, so an arrow or a label always sits on top of it.
        if (annotation.Shape != Shape.None)
        {
            var shape = ShapeColor(under);
            canvas.DrawIcon(screen, ArtFor(annotation.Shape),
                            annotation.Off ? new Color(shape, shape.A * OffAlpha) : shape);
        }

        // A gate draws as a shape that has a front and a back, so it carries its own facing and
        // the triangle beside it would be saying the same thing twice -- and colliding while it
        // did. Asked before the arrows rather than after, because the arrow has to be skipped
        // before it is drawn; on a cell too small for the shape it is drawn as usual and the
        // letter is the fallback.
        var gate = GateArt.Fits(screen, annotation.Gate);

        // The source arrow first, so that where a balance's two sides meet at a corner the side
        // it gives to is the one drawn on top.
        if (!gate)
        {
            canvas.DrawArrow(screen, annotation.Source,
                             new Color(color, color.A * SourceAlpha), EdgeColor);
            canvas.DrawArrow(screen, annotation.Aim, color, EdgeColor);
        }

        if (marksOnly)
            return;

        // The symbol, where there is one: across the whole cell, behind the label, the same
        // way the phase shape above it is. It used to be stacked above the body in half the
        // height, drawn from the game's own 12x12 badge art; see MarkArt for why that could not
        // be made bigger without also making it heavier, and why it is drawn rather than loaded.
        var body = annotation.Kelvin is { } kelvin ? Temperatures.Format(kelvin) : annotation.Body;

        // Held back to a watermark where it shares the cell with a name, at full strength where
        // it is the only thing in it. The phase shapes have always been drawn this way, at 0.38,
        // and that is the whole reason "wet" over a drop reads: a mark at full strength across
        // the cell wins every contest with the text on top of it, which was the first thing the
        // larger marks did. A little stronger than the shapes, because a one-unit stroke has far
        // less mass to be seen by than a filled drop. Drawn flat, with no outline: see MarkArt.
        var faded = body.Length > 0 || annotation.Mark.Length > 0 ? MarkedAlpha : 1f;
        if (annotation.Off)
            faded *= OffAlpha;
        var mark = MarkColor(annotation.Icon);
        if (Drawn(annotation.Icon))
            MarkArt.Draw(canvas, screen, annotation.Icon, new Color(mark, mark.A * faded));

        // The shape, in place of the letter.
        if (gate)
        {
            GateArt.Draw(canvas, screen, annotation.Gate, annotation.Aim,
                         new Color(color, color.A * (annotation.Off ? OffAlpha : 1f)),
                         LabelColor(under));
            return;
        }

        // The label keeps out of the arrows' way. Each arrow reaches a third of the way in from
        // its own edge, so the box loses that third on any edge that has one and keeps the whole
        // cell on the edges that do not -- which is every edge of a filter, the annotations that
        // actually carry long names.
        //
        // The overlap this fixes is not new; it was invisible. A gate's glyph was drawn in the
        // family colour and so was its arrow, so an ampersand sitting on top of an amber triangle
        // was amber on amber and read as one slightly busy mark. Drawing labels in plain white or
        // black against the machine made it a white ampersand with a triangle through it.
        var text = KeepClearOfArrows(screen, annotation);

        if (body.Length == 0 && annotation.Mark.Length == 0)
            return;

        // One layout call. This used to be two -- the library's, then a hand-rolled
        // hard-wrapped candidate, then a comparison between them by character count and glyph
        // height -- because LabelLayout wrapped at spaces only and a one-word material name
        // therefore had exactly one arrangement. Breaking.Anywhere is that, done properly and
        // priced: a word is split when it buys a real size step and not when it buys a little.
        //
        // maxBody is off by default, and that matters more than it sounds: the cap is applied to
        // the body before the layout sees it, so a 41-character name came out as two lines and an
        // ellipsis in a cell with room for five full lines. The line budget is generous rather
        // than measured, because Choose rejects what does not fit anyway and a tall cell can hold
        // five rows of the smallest font.
        var ceiling = Settings.MaxTextLength > 0 ? Settings.MaxTextLength : int.MaxValue;

        // The same cell one rung down, run through the same arrow insets, so the floor is measured
        // against the box the label would really have had there rather than a scaled guess.
        var fitted = smallerCell is { } smaller
            ? LabelCache.ChooseMonotone(
                  annotation.Mark, body, text.Size,
                  KeepClearOfArrows(new Rect2(Vector2.Zero, smaller, smaller), annotation).Size,
                  ceiling, MaxLabelLines, SplitCost)
            : LabelCache.Choose(annotation.Mark, body, text.Size, ceiling, MaxLabelLines, SplitCost);

        var label = LabelColor(under);
        if (annotation.Off)
            label = new Color(label, label.A * OffAlpha);

        // Overflowing labels are skipped rather than drawn. One is readable; this mod draws up to
        // 169 at once on adjacent pixels, and a block of them spilling into each other is a grey
        // smear that hides the machines it is describing. The arrow and the shape still draw, so
        // a pixel too small for text is not a pixel with nothing on it.
        if (fitted.Overflows)
            return;

        // Lifted so the ink looks centred. Measure counts the descender on every label, to keep a
        // live label's baseline still, so text with nothing below the baseline sits low in its own
        // box by half a descender -- two rows on the largest font. The library knows which
        // characters descend, from the glyph table; this mod used to guess with a list.
        var placed = new Rect2(
            text.Position + new Vector2(0f, LabelLayout.OpticalCenterOffset(fitted)),
            text.Size);
        canvas.DrawLabel(placed, fitted, label);
    }

    /// <summary>
    /// The most lines a label may wrap to. Not a measurement -- the layout rejects what does not
    /// fit -- just a ceiling past which a name is being shredded rather than wrapped.
    /// </summary>
    private const int MaxLabelLines = 5;

    /// <summary>
    /// How much a break inside a word has to buy before it is worth taking, as a fraction of a
    /// size step per break.
    ///
    /// <para><b>Well above the library's default of 0.4, and measured rather than picked.</b> The
    /// library's number is right for prose; these labels are mostly four or five characters, and a
    /// short string gains a lot of glyph height from being folded in two, so at 0.4 it folded
    /// things that had no business being folded. <c>Iron</c> became <c>Ir</c> over <c>on</c> and a
    /// thermostat's <c>1727</c> became <c>17</c> over <c>27</c>, which is a number split across two
    /// lines.</para>
    ///
    /// <para><b>A single figure can serve both cases because the gain differs.</b> The price is a
    /// demanded size ratio of <c>1 + cost</c> per break, so it is read against the ratios the font
    /// ladder actually offers. Folding <c>Iron</c> on a 64-pixel cell buys exactly twice the glyph
    /// height; folding <c>Water</c> on a 32-pixel cell buys 2.2 times. So the price has to sit
    /// strictly between those, and 1.1 is the middle of a window running from just above 1.0 to
    /// just below 1.2 -- measured, and verified to give identical answers at 1.05, 1.1 and
    /// 1.15.</para>
    ///
    /// <para>It is deliberately not at either end. At exactly 1.0 or 1.2 the comparison lands on a
    /// tie with a real arrangement, and which way a tie falls is not something to build on.</para>
    ///
    /// <para>This went up from 0.8 when the zoom ladder gained a rung at 64, which is where every
    /// name measured gains a font size -- and where folding <c>Iron</c> first buys a full doubling
    /// and so beat the old price. The number and the ladder are coupled; re-derive it if either the
    /// ladder or the fonts change.</para>
    ///
    /// <para>The smallest cell still folds a four-character label, and should: at 16 screen pixels
    /// there is no font in which four characters fit on one line, so the choice is two lines or an
    /// ellipsis.</para>
    /// </summary>
    internal const float SplitCost = 1.1f;

    /// <summary>
    /// <paramref name="screen"/> with a third taken off every edge that has an arrow on it.
    ///
    /// <para>A third is <c>Canvas.DrawArrow</c>'s own reach. Returns the rectangle unchanged if
    /// taking the arrows off would leave nothing: a cell with four arrows and a label is not a
    /// case worth degrading everything else to handle, and an overlapping label there is better
    /// than none.</para>
    /// </summary>
    private static Rect2 KeepClearOfArrows(Rect2 screen, in Annotation annotation)
    {
        if (annotation.Aim == Aim.None && annotation.Source == Aim.None)
            return screen;

        var reach = Mathf.Max(2f, Mathf.Round(Mathf.Min(screen.Size.X, screen.Size.Y) / 3f));
        var aim = annotation.Aim;
        var source = annotation.Source;
        bool Has(Aim side) => aim == side || source == side;

        var left   = Has(Aim.Left)  ? reach : 0f;
        var right  = Has(Aim.Right) ? reach : 0f;
        var top    = Has(Aim.Up)    ? reach : 0f;
        var bottom = Has(Aim.Down)  ? reach : 0f;

        var size = screen.Size - new Vector2(left + right, top + bottom);
        return size.X < 4f || size.Y < 4f
            ? screen
            : new Rect2(screen.Position + new Vector2(left, top), size);
    }

    /// <summary>
    /// Whether a mark is actually drawn.
    ///
    /// <para><b>The tick is deliberately not.</b> It only ever appeared on a filter that passes
    /// what it names, which is what a filter does unless it says otherwise: beside a material name
    /// it was a symbol for "yes" attached to the only kind of answer that was ever going to be
    /// there, spending a cell's worth of attention to say nothing the name did not. The cross is
    /// the one that carries information, because it inverts what the name means, and it still
    /// draws. So a passing filter now shows its target and nothing else, and a stopping one shows
    /// its target with a cross over it, which is the distinction the pair was for.</para>
    ///
    /// <para><b>Everything behind it is kept.</b> The tick's glyph, its colour, and
    /// <see cref="MarkIcon.Check"/> on the annotations that carry it are all still here and still
    /// tested: the rules say what a filter is, and the drawing decides what is worth showing, and
    /// those are two different questions. Turning it back on is this method.</para>
    /// </summary>
    private static bool Drawn(MarkIcon icon) => icon == MarkIcon.Cross;

    /// <summary>
    /// The colour a mark is drawn in: green for a tick, red for a cross.
    ///
    /// <para>Picked here rather than inherited from the game's art. The badges this replaced
    /// (<c>ui_status_checkmark</c>, <c>ui_status_x</c>) came already coloured, which was
    /// convenient and is not a loss: these are sampled off those same two files, so a player who
    /// has seen the game say yes and no sees the same two colours here.</para>
    ///
    /// <para><b>The red is pure.</b> It was softened toward pink while the cross still carried a
    /// black outline, which gave it an edge to be read against and let the fill be any red at all.
    /// Without the outline the colour is the whole of the contrast, and a red held back from full
    /// and then drawn at part opacity over a yellow machine arrives as a washed-out orange.</para>
    ///
    /// <para>Deliberately not <see cref="ColorOf"/>'s family colour. Every filter is green under
    /// that scheme, and "which family is this machine in" and "does this one pass or stop" are
    /// two different questions that should not answer in the same channel.</para>
    /// </summary>
    internal static Color MarkColor(MarkIcon icon) => icon switch
    {
        MarkIcon.Check => new Color(0.16f, 0.72f, 0.24f),
        MarkIcon.Cross => new Color(1f, 0f, 0f),
        _              => Colors.White,
    };

    /// <summary>
    /// The library's art for one of the shapes an annotation can name.
    ///
    /// <para>A <c>switch</c> rather than a dictionary because it runs per annotated pixel per
    /// frame, and because <c>GameArt</c> caches the load behind each of these anyway.</para>
    /// </summary>
    internal static IconArt? ArtFor(Shape shape) => shape switch
    {
        Shape.Solid  => GameArt.Solid,
        Shape.Liquid => GameArt.Liquid,
        Shape.Gas    => GameArt.Gas,
        Shape.Player => GameArt.Player,
        _            => null,
    };

    internal static Color ColorOf(Kind kind) => kind switch
    {
        Kind.Flow   => FlowColor,
        Kind.Logic  => LogicColor,
        Kind.Filter => FilterColor,
        Kind.Heat   => HeatColor,
        Kind.Cool   => CoolColor,
        Kind.Timing => TimingColor,
        Kind.Split  => SplitColor,
        _           => Colors.White,
    };
}
