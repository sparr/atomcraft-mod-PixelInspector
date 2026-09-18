using PixelArt;
using System.Globalization;

namespace PixelInspector;

/// <summary>
/// What each machine gets annotated with, decided from its material name alone.
///
/// <para><b>Why names and not fields.</b> The obvious source for "which way does this point"
/// is <c>MaterialType.Direction</c>, and it is not usable: of the machines that face a
/// direction, only the gates, guns, lasers and latches declare it. <c>Pump Down</c>,
/// <c>Conveyor Left</c>, <c>Toggleable Pump Up (Active)</c> and <c>Plasma Gun Down
/// (Active)</c> all leave it unset, and <c>Plasma Gun Up (Active)</c> sets it while its three
/// siblings do not. <c>IsOn</c> is patchy in the same way: <c>Conveyor Left</c> declares it
/// and <c>And Gate (Up) (On)</c> does not, though both are the running half of an on/off
/// pair. The <i>names</i> are regular where the fields are not, so the names are what this
/// reads. Where a field does agree it is used as a cross-check by the tests, not here.</para>
///
/// <para><b>This file touches no game type on purpose.</b> Every rule is a pure function from
/// a string to an <see cref="Annotation"/>, so the whole vocabulary is assertable headlessly,
/// in milliseconds, without a world or a renderer -- and a game update that renames a machine
/// shows up as a rule that stops matching rather than as a mod that quietly draws nothing.
/// <see cref="MaterialAnnotations"/> is the thin layer that walks the game's registry and
/// calls in here.</para>
///
/// <para>Three things are deliberately <b>not</b> annotated. Wires, because their on/off
/// state is already their colour and a wire run is most of the cells in a circuit, so
/// labelling them would bury everything else. Machines with a single variant (solar panel,
/// turbine, blender, door, lightbulb) because there is nothing to disambiguate. And the
/// <c>Bits of</c> materials, which are loose dropped items rather than placed machines.</para>
/// </summary>
public static class AnnotationRules
{
    // The prefixes that say what a filter-like pixel does with what it is looking at. Public
    // so tests and MaterialAnnotations use the same characters rather than restating them.

    /// <summary>A match filter: material equal to its target passes.</summary>
    public const string Match = "=";

    /// <summary>A nonmatch filter: material unequal to its target passes.</summary>
    public const string Nonmatch = "≠";

    /// <summary>A sensor: switches its neighbours on when it sees its target.</summary>
    public const string Sense = "?";

    /// <summary>The whole nonmatch prefix as the font draws it, for a test to assert against.</summary>
    public const char NotEqual = '≠';

    /// <summary>
    /// What to draw on a pixel of this material, or <see cref="Annotation.None"/> for the
    /// vast majority that have nothing worth saying.
    /// </summary>
    /// <param name="name">The material's name, as the game registered it.</param>
    /// <param name="canBeSwitchedOn">
    /// Whether the game gives this material a <c>TurnsOnInto</c>, which is exactly the
    /// statement that it is the off half of an on/off pair.
    ///
    /// <para>Needed because two families spell their off state by saying nothing at all:
    /// <c>Toggleable Pump Down</c> is off and <c>Toggleable Pump Down (Active)</c> is on, and
    /// the same for the ship ports. No reading of the name alone can tell that from a machine
    /// with only one state. It is passed in rather than read here so that this file stays free
    /// of game types; <see cref="MaterialAnnotations.Build"/> supplies it, and the tests assert
    /// that it agrees with the names wherever both have an opinion.</para>
    /// </param>
    public static Annotation For(string? name, bool canBeSwitchedOn = false)
    {
        if (string.IsNullOrEmpty(name))
            return Annotation.None;

        // Loose dropped material, not a placed machine. Every machine has one of these and
        // they would otherwise inherit their machine's whole annotation while sitting in a
        // pile on the floor.
        if (name.StartsWith("Bits of ", StringComparison.Ordinal))
            return Annotation.None;

        Split(name, out var stem, out var tags);
        var off = canBeSwitchedOn || IsOff(stem, tags);

        // --- things whose reading is not in their own name --------------------------------
        // A programmed filter or sensor is stored as its target's id with a quadrant bit set,
        // so the target never reaches this method; MaterialAnnotations resolves it from the
        // cell and appends it to the prefix these return. An unprogrammed one lands here and
        // gets the bare prefix, which is exactly the useful distinction: a filter nobody has
        // set yet looks identical to one that is set.
        switch (stem)
        {
            case "Match Filter":         return new Annotation(Kind.Filter, icon: MarkIcon.Check);
            case "Nonmatch Filter":      return new Annotation(Kind.Filter, icon: MarkIcon.Cross);
            case "Sensor":               return new Annotation(Kind.Filter, mark: Sense);
            case "Heat Resistant Sensor":return new Annotation(Kind.Filter, mark: Sense);

            // Takes its target from whichever material sits immediately left or right of it,
            // and passes only vertical movement. The neighbours are live world state, so
            // MaterialAnnotations fills the target in per cell the same way it does for the
            // three above.
            case "Programmable Filter":  return new Annotation(Kind.Filter, icon: MarkIcon.Check);
        }

        // --- the dehydrators ---------------------------------------------------------------
        // Not filters, though one of them is called one, and treating them as filters was a real
        // bug: it put Block Water in the same family and the same colour as a nonmatch filter
        // programmed to Water, which is a different machine entirely.
        //
        // Both SPLIT rather than test. WaterFilterMaterial passes Water and Steam straight
        // through, and for a hydrate -- anything whose Composition holds "+H2O" and exactly one
        // other element -- it leaves the dry part behind and sends the water on.
        // BlockWaterMaterial is its mirror: it refuses Water and Steam outright, passes anything
        // anhydrous unchanged, and splits a hydrate the other way, keeping the water and sending
        // the dry part on.
        //
        // Marked as being about water, and no further. Each used to carry a word for what comes
        // out the far side -- "wet" for one, "dry" for the other -- and that word is now gone: the
        // two are different colours in the game's own art, so a player looking at the pixel can
        // already tell which is which, and a label repeating it spent the cell on the one thing
        // the pixel was not hiding. What the pixel does hide is that these are about water at all,
        // and that is what the drop and the split colour say.
        //
        // The cost is deliberate and worth knowing: the annotation no longer distinguishes them.
        // Both come back as a drop in the split colour, and telling them apart is back to reading
        // the pixel underneath or hovering it.
        if (stem == "Water Filter" || stem == "Block Water")
            return new Annotation(Kind.Split, shape: Shape.Liquid, off: off);

        // --- state filters ----------------------------------------------------------------
        // Four "Allow" and four "Block" pixels that pass or stop a whole phase of matter.
        // Same shape as a filter, so they read with the same prefixes.
        if (StateFilter(stem) is { } stateFilter)
            return stateFilter;

        // --- balances ---------------------------------------------------------------------
        // "Balance Input Down to Left": takes from one side and gives to another, and the two
        // sides are the entire content of the name. Drawn as a filled nub on the side it
        // gives to and a hollow one on the side it takes from.
        if (stem.StartsWith("Balance ", StringComparison.Ordinal))
        {
            var to = stem.LastIndexOf(" to ", StringComparison.Ordinal);
            if (to > 0)
            {
                var source = LastWordAim(stem.AsSpan(0, to).ToString());
                var aim = ParseAim(stem[(to + 4)..]);
                if (aim != Aim.None && source != Aim.None)
                    return new Annotation(Kind.Flow, aim, source: source, off: off);
            }
            return Annotation.None;
        }

        // --- logic gates ------------------------------------------------------------------
        // And, Or, Xor and Not share one colour in the shipped data, so which gate a pixel is
        // are drawn identically. The hover box names the gate for the one pixel under the
        // cursor; this is what puts a whole circuit's worth on screen at once.
        // The letter is kept as well as the shape: a cell too small for a 13-unit glyph falls
        // back to it rather than showing nothing. See GateArt.Draw.
        if (GateSymbol(stem) is { } gate)
            return new Annotation(Kind.Logic, TagAim(tags), gate, off: off,
                                  gate: GateShape(stem));

        // A latch is a toggle: at rest it waits for its input to rise, and having fired it
        // waits for the input to fall again before it will fire a second time
        // (LatchRestOnMaterial and LatchActivationOnMaterial are the two halves). The game
        // draws both identically, and which one a latch is in decides whether the next pulse
        // does anything -- so "rest" is the plain form and "has fired, holding" gets the
        // primed mark. The output it is holding shows as the on/off dimming, from the (On) and
        // (Off) tags.
        if (stem == "Latch")
            return new Annotation(Kind.Logic, TagAim(tags),
                                  Armed(tags) ? "L" : "L" + Primed, off: off,
                                  gate: Armed(tags) ? GateKind.LatchRest : GateKind.LatchFired);

        // --- thermostats ------------------------------------------------------------------
        // Every heating element is the same red and every cooling element the same blue,
        // whatever they are set to, so the number is the whole annotation.
        if (stem == "Heating Element")
            return NumberTag(tags) is { } heat
                ? new Annotation(Kind.Heat, body: heat, off: off, kelvin: int.Parse(heat, CultureInfo.InvariantCulture))
                : Annotation.None;

        if (stem == "Cooling Element")
            // The bare form is not a missing number: CoolingElementMaterial.Step sets every
            // neighbour's heat to 0 unconditionally, where the (150) and (273) forms clamp to
            // their own value. So its setting really is zero, and saying so is the point --
            // it is the one that freezes everything it touches.
        {
            var cool = NumberTag(tags) ?? "0";
            return new Annotation(Kind.Cool, body: cool, off: off,
                                  kelvin: int.Parse(cool, CultureInfo.InvariantCulture));
        }

        if (stem == "Temperature Sensor")
            return NumberTag(tags) is { } trip
                ? new Annotation(Kind.Heat, body: trip, off: off, kelvin: int.Parse(trip, CultureInfo.InvariantCulture))
                : Annotation.None;

        // --- timing -----------------------------------------------------------------------
        // "2 Step Oscillator" .. "5 Step Oscillator", all one yellow.
        if (stem.EndsWith(" Step Oscillator", StringComparison.Ordinal))
            return LeadingNumber(stem) is { } steps
                ? new Annotation(Kind.Timing, body: steps, off: off)
                : Annotation.None;

        // "Every 2nd Tick Dispenser", "Every 4th", "Every 8th".
        if (stem.StartsWith("Every ", StringComparison.Ordinal) &&
            stem.EndsWith(" Tick Dispenser", StringComparison.Ordinal))
            return Digits(stem) is { } period
                ? new Annotation(Kind.Timing, body: period, off: off)
                : Annotation.None;

        // --- everything that just points somewhere ----------------------------------------
        // Conveyors, pumps, guns, lasers, prisms and projectiles. Their direction is in the
        // name either as a "(Down)" tag or as a bare word, and which of the two depends on
        // the family, so both are tried.
        if (Pointing(stem) is { } kind)
        {
            var aim = TagAim(tags);
            if (aim == Aim.None)
                aim = EmbeddedAim(stem);
            if (aim != Aim.None)
                return new Annotation(kind, aim, LoadedMark(stem), off: off);
        }

        return Annotation.None;
    }

    // ------------------------------------------------------------------ family recognition

    /// <summary>
    /// The families whose annotation is "it points that way", and nothing else. Matched on the
    /// stem so that every on/off and turning-off variant of each comes along without being
    /// listed.
    /// </summary>
    private static Kind? Pointing(string stem)
    {
        foreach (var family in PointingFamilies)
        {
            // Whole-word, not a bare prefix. There is no "Gunpowder" in the shipped data
            // today, and a rule that would annotate it as a gun if there were is a rule that
            // breaks silently on a content update -- or on another mod's material, which this
            // has no say over at all.
            if (stem.Equals(family, StringComparison.Ordinal) ||
                (stem.Length > family.Length &&
                 stem[family.Length] == ' ' &&
                 stem.StartsWith(family, StringComparison.Ordinal)))
                return Kind.Flow;
        }
        return null;
    }

    /// <summary>
    /// The stems that mean "this points somewhere". Longest first where one is a prefix of
    /// another, so <c>Plasma Gun</c> is not read as the start of a <c>Gun</c> and
    /// <c>Toggleable Pump</c> is not read as a <c>Pump</c>; the answer is the same either way
    /// today, and the ordering is what keeps it so if the families ever diverge.
    /// </summary>
    private static readonly string[] PointingFamilies =
    {
        "Laser",
        "Reversible Conveyor",
        "Conveyor",
        "Toggleable Pump",
        "Pump",
        "Plasma Gun",
        "Gun Loaded",
        "Gun",
        "Plasma Bullet",
        "Bullet",
        "Rubber Ball",
        "Prism",
    };

    /// <summary>
    /// The mark for "this is holding something and will act on the next thing that happens to
    /// it": a gun with a round in it, a latch that has fired and is waiting for its input to
    /// fall.
    ///
    /// <para>Deliberately not a dash, which was the first choice and read as part of the
    /// direction nub sitting against it on the same cell.</para>
    /// </summary>
    public const string Primed = "*";

    /// <summary>
    /// Whether a latch is at rest -- waiting for its input to rise, so the next pulse will fire
    /// it. The bare <c>Latch</c> with no direction is the inventory form and is counted as
    /// at rest, since it is not in a circuit at all.
    /// </summary>
    private static bool Armed(List<string> tags) =>
        Has(tags, "Rest") || TagAim(tags) == Aim.None;

    /// <summary>
    /// A loaded gun has a round in it and an empty one does not, and they are the same pixel
    /// otherwise. The only pointing family with something to say beyond its direction.
    /// </summary>
    private static string LoadedMark(string stem) =>
        stem.StartsWith("Gun Loaded", StringComparison.Ordinal) ? Primed : "";

    /// <summary>
    /// The four logic families, as the characters an electronics reader already knows:
    /// <c>&amp;</c>, <c>|</c>, <c>^</c>, <c>!</c>.
    /// </summary>
    /// <summary>Which shape a gate name draws as. Paired with <see cref="GateSymbol"/>, which is
    /// the fallback for a cell too small for one.</summary>
    private static GateKind GateShape(string stem) => stem switch
    {
        "And Gate" => GateKind.And,
        "Or Gate"  => GateKind.Or,
        "Xor Gate" => GateKind.Xor,
        "Not Gate" => GateKind.Not,
        _          => GateKind.None,
    };

    private static string? GateSymbol(string stem) => stem switch
    {
        "And Gate" => "&",
        "Or Gate"  => "|",
        "Xor Gate" => "^",
        "Not Gate" => "!",
        _          => null,
    };

    /// <summary>
    /// The phase filters. <c>Allow Solids</c> passes solids and stops everything else;
    /// <c>Block Solids</c> is its complement.
    ///
    /// <para>They are distinct materials with distinct colours, and two of those colours are the
    /// <i>same</i> colour: <c>Allow Solids</c> and <c>Block Gases</c> are both pure red in the
    /// shipped data. Eight near-identical machines with a collision in the middle is more than
    /// anyone keeps straight.</para>
    ///
    /// <para>What is filtered is drawn as the game's own square, drop, cloud or little person
    /// rather than as a letter, so it reads without being parsed and matches what the game
    /// already shows elsewhere. The art is <c>PixelArt.GameArt</c>'s; this only names a shape.
    /// </para>
    ///
    /// <para>Only the <c>Block</c> half carries a mark. See the comment on <c>mark</c> below.</para>
    /// </summary>
    private static Annotation? StateFilter(string stem)
    {
        var allow = stem.StartsWith("Allow ", StringComparison.Ordinal);
        var block = stem.StartsWith("Block ", StringComparison.Ordinal);
        if (!allow && !block)
            return null;

        // Only the blocking half is marked. The shape already says which phase, and "allows
        // this phase" is what a filter does by default, so an "=" on every one of them was a
        // character spent saying nothing. A bare shape passes, a shape with a bar through it
        // does not, which is how the symbols read anyway.
        var icon = allow ? MarkIcon.None : MarkIcon.Cross;

        var shape = stem[6..] switch
        {
            "Solids"  => Shape.Solid,
            "Liquids" => Shape.Liquid,
            "Gases"   => Shape.Gas,
            _         => Shape.None,
        };
        if (shape != Shape.None)
            return new Annotation(Kind.Filter, shape: shape, icon: icon);

        return stem[6..] == "Players"
            ? new Annotation(Kind.Filter, shape: Shape.Player, icon: icon)
            : null;
    }

    // ------------------------------------------------------------------- name decomposition

    /// <summary>
    /// Splits "Heating Element (Turning Off) (500)" into the stem "Heating Element" and the
    /// tags ["Turning Off", "500"].
    ///
    /// <para>Parenthesised tags are the game's own convention for every axis a machine varies
    /// along, and they arrive in no fixed order -- <c>Cooling Element (Off) (150)</c> puts the
    /// state first and <c>Latch (Down) (On) (Rest)</c> puts three in a row -- so they are
    /// collected and asked about by name rather than by position.</para>
    /// </summary>
    internal static void Split(string name, out string stem, out List<string> tags)
    {
        tags = new List<string>();
        var end = name.Length;

        while (true)
        {
            var close = name.LastIndexOf(')', end - 1);
            if (close != end - 1)
                break;
            var open = name.LastIndexOf('(', close);
            if (open <= 0)
                break;
            tags.Insert(0, name[(open + 1)..close]);
            end = open;
            while (end > 0 && name[end - 1] == ' ')
                end--;
            if (end == 0)
                break;
        }

        stem = name[..end];
    }

    /// <summary>
    /// Whether this variant is the switched-off one, which draws dimmed.
    ///
    /// <para>Three spellings, all in the shipped data: a <c>(Off)</c> tag, the transitional
    /// <c>(Turning Off)</c>, and a bare trailing word on the laser family
    /// (<c>Laser Ruby Up Off</c>). <c>(Rest)</c> is deliberately not one of them: a latch at
    /// rest is still powered, and saying so is the <c>L-</c> label's job.</para>
    /// </summary>
    private static bool IsOff(string stem, List<string> tags) =>
        Has(tags, "Off") || Has(tags, "Turning Off") ||
        stem.EndsWith(" Off", StringComparison.Ordinal) ||
        stem.EndsWith(" Turning Off", StringComparison.Ordinal);

    private static bool Has(List<string> tags, string tag) =>
        tags.Contains(tag, StringComparer.Ordinal);

    /// <summary>The direction named by a tag, as in <c>And Gate (Down) (On)</c>.</summary>
    private static Aim TagAim(List<string> tags)
    {
        foreach (var tag in tags)
        {
            var aim = ParseAim(tag);
            if (aim != Aim.None)
                return aim;
        }
        return Aim.None;
    }

    /// <summary>
    /// The direction named by a bare word inside the stem, as in <c>Laser Ruby Up Off</c>,
    /// <c>Pump Down</c> or <c>Reversible Conveyor Left Turning Right</c>.
    ///
    /// <para>Scanned left to right and the <b>first</b> match wins, which is what makes
    /// <c>Reversible Conveyor Left Turning Right</c> come out as Left: that name means a
    /// left-running belt in the middle of reversing, and the belt is still running left. The
    /// laser family needs the opposite reading of the same rule and gets it for free, because
    /// its trailing word is <c>On</c> or <c>Off</c> rather than a second direction.</para>
    /// </summary>
    private static Aim EmbeddedAim(string stem)
    {
        foreach (var word in stem.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var aim = ParseAim(word);
            if (aim != Aim.None)
                return aim;
        }
        return Aim.None;
    }

    /// <summary>The direction named by the last word, which is how a balance's source side reads.</summary>
    private static Aim LastWordAim(string text)
    {
        var space = text.LastIndexOf(' ');
        return space < 0 ? Aim.None : ParseAim(text[(space + 1)..]);
    }

    internal static Aim ParseAim(string word) => word switch
    {
        "Up"    => Aim.Up,
        "Down"  => Aim.Down,
        "Left"  => Aim.Left,
        "Right" => Aim.Right,
        _       => Aim.None,
    };

    /// <summary>A tag that is entirely digits, which is how every threshold is written.</summary>
    private static string? NumberTag(List<string> tags)
    {
        foreach (var tag in tags)
            if (tag.Length > 0 && tag.All(char.IsAsciiDigit))
                return tag;
        return null;
    }

    /// <summary>The number a name opens with, as in <c>4 Step Oscillator</c>.</summary>
    private static string? LeadingNumber(string stem)
    {
        var space = stem.IndexOf(' ');
        if (space <= 0)
            return null;
        var head = stem[..space];
        return head.All(char.IsAsciiDigit) ? head : null;
    }

    /// <summary>
    /// The digits embedded in a name, as in <c>Every 8th Tick Dispenser</c>, where the number
    /// carries an ordinal suffix and so is not a word of its own.
    /// </summary>
    private static string? Digits(string stem)
    {
        var digits = new string(stem.Where(char.IsAsciiDigit).ToArray());
        return digits.Length > 0
            ? int.Parse(digits, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)
            : null;
    }
}
