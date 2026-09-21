using Atomcraft;
using Godot;
using PixelArt;

namespace PixelInspector;

/// <summary>
/// The magnifier: a fixed panel in the middle of the screen showing the world around the cursor,
/// blown up to a size a label can actually be read at.
///
/// <para><b>Why a panel rather than marks on the world.</b> A world pixel is 8 world units, which
/// is 12 screen pixels at the game's own closest zoom. Two characters of the smallest bitmap font
/// are 11 wide before margins, so a name never fits, and the mod's text half only ever worked for
/// players who also ran a zoom mod. Drawing the same pixels again at a size this mod chooses
/// decouples legibility from the camera entirely.</para>
///
/// <para><b>It covers more than it reveals, on purpose.</b> At 32 screen pixels a cell the panel
/// hides a 40x40 block of world to show 15x15 of it. That is what a magnifier is, and it is the
/// same bargain every accessibility zoom tool makes.</para>
///
/// <para><b>Clicks pass straight through.</b> Nothing here touches
/// <c>Gameplay.MouseIsOverHUD</c>, so the game still mines, paints and places on the world pixel
/// under the cursor: the panel shows a magnified view of what the cursor is pointing at, and
/// acting on that is the point rather than a hazard. It is also what keeps the vanilla hover box
/// visible, since the box hides itself when that property is true.</para>
///
/// <para><b>Pure material colour, no lighting.</b> The pixels are drawn from
/// <c>BaseMaterial.Color</c>, or from its <c>ColorSampler</c> where it has one so that gravel,
/// sand and granite keep their noise. No light, shadow, heat haze or foreground layer. That does
/// not leak anything: fog of war is fog <i>material</i> in the field rather than a mask over it,
/// so a fogged pixel reads as fog here exactly as it does in the world.</para>
/// </summary>
internal static class Magnifier
{
    private const string PassName = "magnifier";

    /// <summary>
    /// The zoom ladder, in screen pixels per world pixel.
    ///
    /// <para><b>Eights to 48, then sixteens.</b> A ladder wants even <i>ratios</i> rather than
    /// even differences, and these run 1.5, 1.33, 1.25, 1.2, 1.33, 1.25, 1.2 -- smooth all the way
    /// up, where the differences alone read as arbitrary.</para>
    ///
    /// <para><b>48 and 64 are measured rather than chosen.</b> Swept in steps of four, the cell
    /// sizes where a label actually gains a font size are 28 to 36 for a short name, 48 for a name
    /// of ordinary length, and <b>64 for every name tried</b>. The rungs at 32, 48 and 64 land on
    /// those; 80 and 96 sit above the last of them and buy bigger pixels and more room for a long
    /// name to wrap, which is real but a different kind of value. 16 is arrows and shapes only,
    /// and 24 fits a gate symbol.</para>
    ///
    /// <para>The rung at 64 used to be 60, from a note written against Pixel Art's old 9x13 face.
    /// 0.3.0 replaced that with 7x11 and every threshold moved with it, leaving 60 buying nothing
    /// and the step at 64 skipped. Re-derive this after a font change rather than trusting it.</para>
    ///
    /// <para>They no longer need to divide the panel. The panel is sized from the gap in the HUD
    /// and its edge pixels are clipped, so any cell size fills it; what these are chosen for is
    /// what each one buys a reader.</para>
    /// </summary>
    internal static readonly int[] Ladder = { 16, 24, 32, 40, 48, 64, 80, 96 };

    /// <summary>
    /// Where the ladder starts, and what a visit to the main menu puts it back to. 32 screen
    /// pixels a cell is a 15x15 window at the default panel size, which is about one machine and
    /// its plumbing.
    /// </summary>
    internal const int DefaultCell = 32;

    private static int _cell = DefaultCell;

    /// <summary>
    /// How many screen pixels one world pixel takes in the panel.
    ///
    /// <para><b>Deliberately not persisted</b>, in the settings file or anywhere else. It is a
    /// thing a player changes while looking at something, not a preference, and
    /// <see cref="Reset"/> puts it back when they leave the world.</para>
    /// </summary>
    internal static int Cell => _cell;

    /// <summary>
    /// How many world pixels fit across the panel, rounded.
    ///
    /// <para>Derived rather than chosen, and no longer a whole number underneath: the panel is
    /// sized from the gap in the HUD and the contents slide within it, so the edge pixels are
    /// usually partial. This is for saying how much you can see, not for laying anything
    /// out.</para>
    /// </summary>
    internal static int Cells => Math.Max(1, Mathf.RoundToInt(PanelRect().Size.X / Math.Max(1, _cell)));

    /// <summary>Roughly how far the panel reaches from the cursor, for a diagnostic to outline.</summary>
    internal static int Radius => Cells / 2;

    /// <summary>How many pixels were drawn in the panel on the last frame it ran.</summary>
    internal static int PixelsLastFrame { get; private set; }

    /// <summary>
    /// How many of those the panel put an annotation on. Separate from the world pass's counter,
    /// which counts a different set of pixels entirely.
    ///
    /// <para>Here so a test can compare it against <see cref="PixelsLastFrame"/>: with the whole
    /// view full of machines the two must agree, and they did not while a clipped cell drew its
    /// colour and nothing else.</para>
    /// </summary>
    internal static int AnnotatedLastFrame { get; private set; }

    /// <summary>
    /// How many of the cells drawn were wholly inside the panel.
    ///
    /// <para>Only a test wants this, and it wants it for one comparison:
    /// <see cref="AnnotatedLastFrame"/> above this number means annotations are reaching cells the
    /// panel is only showing part of, which is the whole question.</para>
    /// </summary>
    internal static int WholeCellsLastFrame { get; private set; }

    /// <summary>Reused across cells so tracing the highlight allocates nothing per frame.</summary>
    private static readonly List<Rect2> _outline = new();

    /// <summary>
    /// Whether the last frame left the hovered pixel's white ring off because the player's tool
    /// had already drawn a box around exactly that cell.
    ///
    /// <para>Here so a test can ask. The decision is made deep inside the draw and is otherwise
    /// visible only as one ring that is not there, which is not something an assertion can reach
    /// and not something a person reviewing a screenshot reliably notices either.</para>
    /// </summary>
    internal static bool MarkerSuppressedLastFrame { get; private set; }

    /// <summary>How many frames the panel has drawn on. The one thing a headless test can watch.</summary>
    internal static long Frames { get; private set; }

    private static Canvas? _canvas;

    /// <summary>The panel's canvas, or null before <see cref="Register"/> has run.</summary>
    internal static Canvas? Surface => _canvas;

    // Colours. The backing is fully opaque, and was briefly not: at 0.94 the world showed faintly
    // through it, which sounded like "a lens rather than a window pasted over the world" and
    // looked like a second, smaller, offset copy of the very machines the panel was magnifying.
    // A ghost of the thing you are looking at is worse than no world at all.
    /// <summary>The marker's colour when a click would not reach the world. The game's own red.</summary>
    private static readonly Color Blocked = new(1f, 0f, 0f, 1f);

    private static readonly Color Backing = new(0.04f, 0.04f, 0.06f, 1f);
    private static readonly Color Border = new(0.75f, 0.80f, 0.85f, 0.55f);
    private static readonly Color Grid = new(0f, 0f, 0f, 0.18f);
    private static readonly Color Centre = new(1f, 1f, 1f, 0.9f);
    private static readonly Color Readout = new(0.75f, 0.80f, 0.85f, 0.75f);

    internal static void Register()
    {
        _canvas = Canvas.For(ModEntry.ModId + ".magnifier", Canvas.DefaultLayer);
        _canvas.SetPass(PassName, Draw, () => Painter.Showing);
    }

    /// <summary>
    /// Puts the zoom back to <see cref="DefaultCell"/>.
    ///
    /// <para>Called from <c>Simulation.Init</c> and <c>Simulation.Reset</c>, so the zoom survives
    /// for as long as a world does and resets on the way out to the menu. That is deliberately the
    /// opposite of how a setting behaves and the same as how this mod's other per world state
    /// behaves.</para>
    /// </summary>
    internal static void Reset() => _cell = DefaultCell;

    /// <summary>Drops the frame counters, so a test can measure from a known point.</summary>
    internal static void ResetCounters()
    {
        Frames = 0;
        PixelsLastFrame = 0;
        AnnotatedLastFrame = 0;
        WholeCellsLastFrame = 0;
        MarkerSuppressedLastFrame = false;
    }

    /// <summary>
    /// Steps the zoom one rung. Positive zooms in, which shows fewer, larger pixels.
    ///
    /// <para>Clamped at both ends rather than wrapping: a player holding the key expects to arrive
    /// somewhere and stay, not to fall off the top back to the bottom.</para>
    /// </summary>
    /// <summary>
    /// The cell size one rung below the current one, or the current one at the bottom of the
    /// ladder. What a label's size must not fall below when the player zooms in; see
    /// <see cref="LabelCache.ChooseMonotone"/>.
    /// </summary>
    internal static int PreviousCell
    {
        get
        {
            var i = Array.IndexOf(Ladder, _cell);
            return i <= 0 ? _cell : Ladder[i - 1];
        }
    }

    internal static void Step(int direction)
    {
        var i = Array.IndexOf(Ladder, _cell);
        if (i < 0)
            i = Array.IndexOf(Ladder, DefaultCell);
        _cell = Ladder[Math.Clamp(i + Math.Sign(direction), 0, Ladder.Length - 1)];
    }

    /// <summary>
    /// How much of the space between the health bar and the toolbar the panel fills.
    ///
    /// <para>Not all of it, so the panel reads as sitting in that gap rather than wedged into
    /// it, and so the game's own HUD keeps a margin it can grow into: the health bar gains
    /// hearts as your maximum health rises and the toolbar gains a second row with the Tool
    /// Belt upgrade.</para>
    /// </summary>
    private const float BandFraction = 0.9f;

    private static Rect2 _panel;
    private static Vector2 _panelFor = new(-1f, -1f);
    private static float _panelTop = -1f, _panelBottom = -1f;

    /// <summary>
    /// The panel's rectangle: square, as tall as nine tenths of the gap between the health bar
    /// and the toolbar, centred in that gap.
    ///
    /// <para><b>Measured from the live HUD rather than from constants.</b> The game lays its UI
    /// out in a fixed 1600x900 space and scales the whole frame to the window, and
    /// ActualResolution changes both the render target and that scale, so a hardcoded band would
    /// be right on exactly one setup. Asking <c>HealthHUD</c> and <c>ToolbarHUD</c> where they
    /// actually are costs two rectangles and is right everywhere.</para>
    ///
    /// <para><b>Recomputed only when the layout moves.</b> The size must not change while a
    /// player is looking at it, and the health bar in particular is a Control whose width tracks
    /// your heart count. Keyed on the viewport and the two edges, so a resize or a HUD scale
    /// change is picked up on the frame it happens and nothing else disturbs it.</para>
    ///
    /// <para>Falls back to two thirds of the viewport height when the HUD cannot be reached,
    /// which happens for a few frames around a world load.</para>
    /// </summary>
    internal static Rect2 PanelRect()
    {
        var viewport = ViewGeometry.ViewportSize;
        var top = Gameplay.HealthHUD?.GetGlobalRect().End.Y ?? -1f;
        var bottom = ToolbarHUD.Instance?.GetGlobalRect().Position.Y ?? -1f;

        if (viewport == _panelFor && Mathf.IsEqualApprox(top, _panelTop) &&
            Mathf.IsEqualApprox(bottom, _panelBottom))
            return _panel;

        var band = bottom > top && top >= 0f
            ? bottom - top
            : viewport.Y * 0.67f;
        var bandTop = bottom > top && top >= 0f ? top : viewport.Y * 0.165f;

        var side = Mathf.Round(band * BandFraction);
        _panel = new Rect2(
            Mathf.Round((viewport.X - side) / 2f),
            Mathf.Round(bandTop + (band - side) / 2f),
            side, side);
        _panelFor = viewport;
        _panelTop = top;
        _panelBottom = bottom;
        return _panel;
    }

    /// <summary>
    /// Where the cursor is, in tiles, to whatever precision the mouse has.
    ///
    /// <para>Fractional on purpose. <c>ViewGeometry.MouseTile</c> floors to a whole tile, which
    /// would make the panel jump a whole cell at a time: at 32 screen pixels a cell and a world
    /// zoom of 12, one cell is nearly three screen pixels of mouse travel, so the contents would
    /// lurch rather than track. Taking the world position straight lets a one screen pixel move
    /// of the mouse shift the contents by its proper fraction of a cell.</para>
    /// </summary>
    /// <summary>Whether a tile is inside the block the panel gathered marks for.</summary>
    private static bool InRange(int minX, int minY, int maxX, int maxY, int x, int y) =>
        x >= minX && x <= maxX && y >= minY && y <= maxY;

    /// <summary>
    /// Whether a neighbouring cell carries the same highlight, so the edge between them is inside
    /// one shape rather than on its border.
    ///
    /// <para>Outside the gathered block counts as "not the same", which draws an edge at the
    /// panel's rim. That is the honest answer: the panel cannot see whether the highlight
    /// continues past what it is showing, and closing the shape at the edge says less than
    /// leaving it open would imply.</para>
    /// </summary>
    private static bool Same(Color?[] marks, int span, int minX, int minY, int maxX, int maxY,
                             int x, int y, Color mark) =>
        InRange(minX, minY, maxX, maxY, x, y) &&
        marks[(y - minY) * span + (x - minX)] is { } other &&
        other.IsEqualApprox(mark);

    private static Vector2? MouseTileExact()
    {
        if (Game.World == null || Client.FollowCam == null)
            return null;
        return Game.World.GetGlobalMousePosition() / ViewGeometry.TileSize;
    }

    private static void Draw(Canvas canvas)
    {
        PixelsLastFrame = 0;
        AnnotatedLastFrame = 0;
        WholeCellsLastFrame = 0;
        MarkerSuppressedLastFrame = false;

        if (!PixelInspectorApi.Enabled || !MaterialAnnotations.Ready || !ViewGeometry.Ready)
            return;

        Frames++;

        // The zoom keys, polled here rather than from a patch of their own: this is already a
        // once-a-frame callback that only runs while the panel is up, which is exactly when the
        // keys are ours.
        ZoomKeys.Poll();

        var field = Simulation.CurrentState?.Field;
        var mouse = MouseTileExact();
        if (field == null || mouse == null)
            return;

        var panel = PanelRect();

        // One clip around the whole panel, for everything the panel is made of: the backing, the
        // cells, the grid, the annotations, the tool highlight, the cursor marker and the border.
        //
        // All of it inside, and that is not tidiness. Clipped drawing goes to a child canvas item
        // and a child draws after its parent, so anything left outside the scope would be painted
        // UNDER everything inside it however the calls were ordered -- the border in particular
        // would end up beneath the cells and vanish. The zoom readout below the panel is the one
        // thing deliberately outside, and it is outside the panel's rectangle too, so the layering
        // between them never arises.
        using var clipped = canvas.Clip(panel);

        canvas.DrawFill(panel, Backing);

        // Where tile (0,0)'s corner would be on screen, if it were drawn. Rounded once, here,
        // rather than per cell: rounding each cell independently would leave one pixel seams and
        // overlaps between neighbours, and rounding nothing would hand the renderer fractional
        // rectangles it can only resolve by blurring. One rounded origin keeps every cell the
        // same size and on whole screen pixels, and still tracks the mouse to within a pixel.
        var centre = panel.GetCenter();
        var originX = Mathf.Round(centre.X - mouse.Value.X * _cell);
        var originY = Mathf.Round(centre.Y - mouse.Value.Y * _cell);

        // Every tile the panel touches, including the ones it only partly touches. Deliberately
        // not a whole number of cells: clipping at the edges is what lets the contents slide.
        var minX = Mathf.FloorToInt((panel.Position.X - originX) / _cell);
        var maxX = Mathf.CeilToInt((panel.End.X - originX) / _cell);
        var minY = Mathf.FloorToInt((panel.Position.Y - originY) / _cell);
        var maxY = Mathf.CeilToInt((panel.End.Y - originY) / _cell);

        // The player's tool highlight, gathered before anything is drawn. It has to be a map
        // rather than a per-cell test because the outlines merge: whether this cell draws its
        // left edge is a question about the cell to its left, which the loop has already passed.
        var span = maxX - minX + 1;
        var marks = new Color?[span * (maxY - minY + 1)];

        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++)
        {
            var full = new Rect2(originX + x * _cell, originY + y * _cell, _cell, _cell);
            var shown = full.Intersection(panel);
            if (shown.Size.X <= 0f || shown.Size.Y <= 0f)
                continue;

            var id = field.Get(x, y);
            if (id == -2)
                continue;               // outside the world: leave the backing showing

            PixelsLastFrame++;

            // The colour goes in clipped, so a pixel half off the edge is drawn as half a pixel.
            var plain = id == -1 ? null : ColorOf(id, x, y);
            if (plain is { } color)
                canvas.DrawFill(shown, color);

            // Whatever the player's tool is marking. Asked of air as well as of matter, because
            // the hand's disc of reach falls mostly on air; asked of clipped cells too, since a
            // cell half off the edge still decides whether its whole neighbour draws an edge.
            marks[(y - minY) * span + (x - minX)] = ToolHighlight.At(x, y, plain);

            // Everything else is laid out against the part of the cell that is actually on
            // screen, not against the whole cell.
            //
            // Everything is laid out against the WHOLE cell. The canvas is clipped to the panel
            // for the length of this pass, so a cell at the rim draws whatever part of it is
            // inside and the rest is cut at the glass edge.
            //
            // Laying out against the visible part instead was tried and is wrong: a cell's visible
            // fraction changes continuously as the view scrolls, so every annotation at the rim
            // resized under the mouse, a label dropping a font size and coming back as its cell
            // crossed the edge. An annotation should change size when the player zooms and at no
            // other time.
            if (Mathf.IsEqualApprox(shown.Size.X, _cell) && Mathf.IsEqualApprox(shown.Size.Y, _cell))
                WholeCellsLastFrame++;

            if (_cell >= 24)
                canvas.DrawOutline(full, Grid);

            // The circuit, behind everything. A pixel's links are drawn before its annotation so
            // a number or a name sits on top of its own wiring rather than under it.
            if (plain is { } beneath && Circuit.Ready)
            {
                Span<Vector2I> links = stackalloc Vector2I[8];
                var n = Circuit.LinksAt(field, x, y, links);
                if (n > 0)
                {
                    var wire = CircuitLines.Tint(beneath);
                    for (var k = 0; k < n; k++)
                        CircuitLines.Draw(canvas, full, links[k], wire);
                }
            }

            var annotation = MaterialAnnotations.At(field, x, y);
            if (annotation.Any)
            {
                Painter.DrawAnnotation(canvas, full, annotation, under: plain,
                                       smallerCell: PreviousCell);
                AnnotatedLastFrame++;
            }
        }

        // The tool highlight, drawn as one outline around each connected run rather than as a
        // box around every cell in it. A brush covering forty pixels drew forty boxes before
        // this, which at any real brush size is a hatched blob that says nothing about its own
        // shape; an edge is drawn only where it borders something that is not the same
        // highlight, which is the shape the tool is actually going to affect.
        //
        // The colour is compared, not merely presence, so the boundary between the part a tool
        // would take and the part it can only reach survives the merge: the game draws those at
        // full and half alpha and they are two different statements.
        var edge = Math.Max(1f, _cell / 12f);
        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++)
        {
            if (marks[(y - minY) * span + (x - minX)] is not { } mark)
                continue;

            var full = new Rect2(originX + x * _cell, originY + y * _cell, _cell, _cell);

            bool Joined(int dx, int dy) =>
                Same(marks, span, minX, minY, maxX, maxY, x + dx, y + dy, mark);

            _outline.Clear();
            HighlightOutline.Edges(full, edge, new Neighbours(
                Joined(0, -1), Joined(0, +1), Joined(-1, 0), Joined(+1, 0),
                Joined(-1, -1), Joined(+1, -1), Joined(-1, +1), Joined(+1, +1)), _outline);

            foreach (var piece in _outline)
                canvas.DrawFill(piece, mark);
        }

        // The pixel the cursor is on, which is the one a click will act on, so it is marked. It
        // is wherever the mouse happens to be rather than the middle of the panel, because the
        // contents slide: at rest it is dead centre, and between cells it is not.
        var hovered = new Rect2(originX + Mathf.Floor(mouse.Value.X) * _cell,
                                originY + Mathf.Floor(mouse.Value.Y) * _cell,
                                _cell, _cell);
        // Unless the tool has already drawn a box around exactly that cell and nothing else, in
        // which case two rings sit on the same four edges and the white one is only making the
        // coloured one harder to read. The chisel does this always, and any brush does it on a
        // lone pixel. A highlight that extends past this cell is a different statement and both
        // are drawn.
        var hoverTile = new Vector2I(Mathf.FloorToInt(mouse.Value.X), Mathf.FloorToInt(mouse.Value.Y));
        var ringed = InRange(minX, minY, maxX, maxY, hoverTile.X, hoverTile.Y) &&
                     marks[(hoverTile.Y - minY) * span + (hoverTile.X - minX)] is { } own &&
                     !Same(marks, span, minX, minY, maxX, maxY, hoverTile.X, hoverTile.Y - 1, own) &&
                     !Same(marks, span, minX, minY, maxX, maxY, hoverTile.X, hoverTile.Y + 1, own) &&
                     !Same(marks, span, minX, minY, maxX, maxY, hoverTile.X - 1, hoverTile.Y, own) &&
                     !Same(marks, span, minX, minY, maxX, maxY, hoverTile.X + 1, hoverTile.Y, own);

        // A tool outlining this one cell is why the ordinary marker is dropped; it is not a
        // reason to drop the warning, and while input is blocked there is no tool highlight to
        // collide with anyway.
        ringed &= !WorldInput.Blocked;
        MarkerSuppressedLastFrame = ringed;

        var hoveredShown = hovered.Intersection(panel);
        if (!ringed && hoveredShown.Size.X > 0f && hoveredShown.Size.Y > 0f)
        {
            if (WorldInput.Blocked)
            {
                // The pointer is over the toolbar, a window, or some other control that eats the
                // click. The panel is still showing the world under there, perfectly truthfully
                // and perfectly misleadingly, so the marker stops being a hairline and becomes a
                // warning: thick, red, and impossible to read as the ordinary one.
                canvas.DrawOutline(hovered, Blocked, thickness: Math.Max(2f, _cell / 10f));
            }
            else
            {
                // Two rings, light over dark, so the marker reads on a bright machine as well as
                // on a dark one. A single ring in either colour disappears against half the
                // palette.
                //
                // One screen pixel each, at every zoom. Scaling the thickness with the cell made
                // the ring a frame rather than a hairline as you zoomed in, and a thick ring eats
                // the pixel it is pointing at: at 96 screen pixels a cell it was six pixels of
                // white around the one thing the panel exists to show you.
                canvas.DrawOutline(hovered.Grow(1f), new Color(0f, 0f, 0f, 0.8f), thickness: 1f);
                canvas.DrawOutline(hovered, Centre, thickness: 1f);
            }
        }

        // Where the cursor actually is inside that pixel, to one screen pixel. The ring says
        // which pixel a click lands on; this says where in it the pointer is sitting, which is
        // the thing the real cursor used to say and no longer does now that it is hidden behind
        // the panel. It is also the only part of the panel that moves continuously, so it is what
        // tells a player the panel is tracking them rather than snapping.
        var inside = new Vector2(mouse.Value.X - Mathf.Floor(mouse.Value.X),
                                 mouse.Value.Y - Mathf.Floor(mouse.Value.Y));
        var dot = new Rect2(Mathf.Floor(hovered.Position.X + inside.X * _cell),
                            Mathf.Floor(hovered.Position.Y + inside.Y * _cell), 1f, 1f);
        var dotShown = dot.Intersection(panel);
        if (dotShown.Size.X > 0f && dotShown.Size.Y > 0f)
        {
            // A single lit pixel is invisible on half the palette, so it gets a dark square under
            // it. The square is backing, not the indicator: what marks the spot is still exactly
            // one screen pixel, at the centre of it.
            var backing = dot.Grow(1f).Intersection(panel);
            if (backing.Size.X > 0f && backing.Size.Y > 0f)
                canvas.DrawFill(backing, new Color(0f, 0f, 0f, 0.7f));
            canvas.DrawFill(dotShown, Colors.White);
        }

        canvas.DrawOutline(panel, Border, thickness: 2f);

        // Out of the clip: this sits below the panel, outside its rectangle, and would be cut away
        // entirely by it.
        canvas.ClearClip();

        // The zoom, because it is not persisted and the keys are not in the game's own settings:
        // without this a player who pressed Alt and = has no way to tell what changed.
        canvas.DrawLabel(new Rect2(panel.Position.X, panel.End.Y + 4f, panel.Size.X, 16f),
                         $"{Cells}x{Cells} @ {_cell}", Readout, TextSize.Small,
                         LabelPlacement.Center, outline: Colors.Black);

        // Last, so the box is placed against the panel that was actually drawn, and after every
        // other mod has finished appending to it. See HudPlacement.
        HudPlacement.PlaceHoverBox(panel);

    }

    /// <summary>
    /// What colour a world pixel is, before anything the renderer does to it.
    ///
    /// <para><b>Through the game's own <c>MaterialColorIndex</c></b>, which is the table
    /// <c>Gameplay</c> builds its terrain texture from. Three things come with that rather than
    /// with reading <c>BaseMaterial.Color</c>: the animated materials are sampled exactly as the
    /// world samples them, a quadrant-tagged filter comes back the same yellow the world draws
    /// it, and an id the table does not have is refused rather than guessed at.</para>
    ///
    /// <para><b>And the tick is <c>Gameplay.AnimTick</c>, not the simulation's.</b> That is the
    /// whole of why the conveyors used to strobe: the simulation tick advances sixty times a
    /// second, so an animation keyed to it changed every frame, where the world steps a four
    /// frame cycle every sixteenth of a second. Passing the wrong clock to the right function is
    /// exactly the kind of thing that looks like a rendering bug and is not.</para>
    /// </summary>
    private static Color? ColorOf(short id, int x, int y) =>
        MaterialColorIndex.TryGetColor(id, x, y, Gameplay.AnimTick, out var color) ? color : null;

}
