using PixelArt;
using Atomcraft;
using Atomcraft.TestHarness;

namespace PixelInspector.Test;

/// <summary>
/// The whole annotation vocabulary, asserted against the game's live material registry.
///
/// <para>These are the cheapest and most valuable tests in the suite. <c>AnnotationRules</c> is
/// a pure function from a name to an <see cref="Annotation"/>, so every one of these runs in
/// microseconds with no world, no renderer and no frames -- and between them they are what
/// turns "a game update renamed a machine" from a mod that silently draws nothing into a named
/// failure.</para>
///
/// <para>Two shapes are used deliberately. <b>Spot checks</b> pin the exact reading of one name
/// per family, which is what documents the vocabulary. <b>Sweeps over the live registry</b> are
/// what catch a rule that starts matching something it should not, which a fixed list never
/// can: the game adds materials and other mods add materials, and the sweeps see both.</para>
/// </summary>
public static class VocabularyTests
{
    // ------------------------------------------------------------------------ spot checks

    /// <summary>
    /// Each logic family gets its own symbol, pointing the way its name says.
    ///
    /// <para>This is the annotation the mod most exists for. In the shipped data
    /// <c>And Gate (Up) (Off)</c>, <c>Xor Gate (Up) (Off)</c> and <c>Not Gate (Up) (Off)</c>
    /// are all the same colour, so a circuit of them is a wall of identical pixels until you
    /// hover each one -- see <c>RetirementTests</c>, which is where that claim about the game
    /// is checked.</para>
    /// </summary>
    [GameTest]
    public static void EachGateFamilyGetsItsOwnSymbol()
    {
        Expect("And Gate (Up) (On)",    Kind.Logic, Aim.Up,    "&");
        Expect("Or Gate (Down) (On)",   Kind.Logic, Aim.Down,  "|");
        Expect("Xor Gate (Left) (On)",  Kind.Logic, Aim.Left,  "^");
        Expect("Not Gate (Right) (On)", Kind.Logic, Aim.Right, "!");

        var symbols = new[] { "And", "Or", "Xor", "Not" }
            .Select(f => AnnotationRules.For($"{f} Gate (Up) (On)").Text)
            .ToList();
        if (symbols.Distinct().Count() != symbols.Count)
            throw new AssertionException(
                $"two gate families share a symbol ({string.Join(" ", symbols)}), so the mod " +
                "cannot tell them apart either -- which is the whole reason it draws them");
    }

    /// <summary>
    /// A latch says whether it is armed or has already fired, which decides whether the next
    /// pulse on its input does anything.
    ///
    /// <para>The game draws both halves identically. A latch at rest is waiting for its input to
    /// rise; one that has fired is waiting for it to fall again, and until it does, more input
    /// changes nothing -- which is exactly the state someone stares at a stuck circuit
    /// wondering about.</para>
    /// </summary>
    [GameTest]
    public static void ALatchSaysWhetherItHasAlreadyFired()
    {
        Expect("Latch (Down) (On) (Rest)",   Kind.Logic, Aim.Down, "L");
        Expect("Latch (Down) (On)",          Kind.Logic, Aim.Down, "L" + AnnotationRules.Primed);

        // The output it is holding is the on/off dimming, not part of the text, so both halves
        // read the same whichever way the output is latched.
        Expect("Latch (Down) (Off) (Rest)",  Kind.Logic, Aim.Down, "L");
        AssertOff("Latch (Down) (Off) (Rest)", true);
        AssertOff("Latch (Down) (On) (Rest)", false);
    }

    /// <summary>
    /// The four ways the game writes a direction all come out the same.
    ///
    /// <para>A parenthesised tag (<c>Gun (Down)</c>), a bare word in the middle
    /// (<c>Plasma Gun Down (Active)</c>), a bare word before an on/off word
    /// (<c>Laser Ruby Up Off</c>), and a trailing word (<c>Pump Left</c>). Four spellings for
    /// one fact is exactly why the rules read names with a parser rather than a lookup.</para>
    /// </summary>
    [GameTest]
    public static void EverySpellingOfADirectionReadsTheSame()
    {
        Expect("Gun (Down) (On)",          Kind.Flow, Aim.Down);
        Expect("Plasma Gun Left (Active)", Kind.Flow, Aim.Left);
        Expect("Laser Ruby Up Off",        Kind.Flow, Aim.Up);
        Expect("Pump Right",               Kind.Flow, Aim.Right);
        Expect("Conveyor Left",            Kind.Flow, Aim.Left);
        Expect("Rubber Ball Down",         Kind.Flow, Aim.Down);
    }

    /// <summary>
    /// A reversing belt is annotated with the way it is still running, not the way it is about
    /// to.
    ///
    /// <para><c>Reversible Conveyor Left Turning Right</c> names two directions. Reading the
    /// first is what makes the arrow agree with what the pixel is doing on the frame it is
    /// drawn; reading the last would show the player an arrow for a state that has not arrived.
    /// </para>
    /// </summary>
    [GameTest]
    public static void AReversingBeltShowsTheDirectionItIsStillRunning()
    {
        Expect("Reversible Conveyor Left Turning Right", Kind.Flow, Aim.Left);
    }

    /// <summary>A balance shows both of its sides: a filled nub out, a hollow one in.</summary>
    [GameTest]
    public static void ABalanceShowsBothOfItsSides()
    {
        var a = AnnotationRules.For("Balance Input Down to Left");
        if (a.Kind != Kind.Flow || a.Aim != Aim.Left || a.Source != Aim.Down)
            throw new AssertionException(
                $"'Balance Input Down to Left' read as {a}, expected Flow aim=Left from=Down");

        // The output family is spelled identically and must read identically; they differ in
        // what they do, not in where they point.
        var b = AnnotationRules.For("Balance Output Up to Right");
        if (b.Aim != Aim.Right || b.Source != Aim.Up)
            throw new AssertionException($"'Balance Output Up to Right' read as {b}");
    }

    /// <summary>
    /// Thermostats are labelled with their setting, which is the only thing that separates
    /// them: every heating element is one red and every cooling element one blue.
    /// </summary>
    [GameTest]
    public static void ThermostatsAreLabelledWithTheirSetting()
    {
        Expect("Heating Element (500)",      Kind.Heat, Aim.None, "500");
        Expect("Heating Element (2000)",     Kind.Heat, Aim.None, "2000");
        Expect("Cooling Element (150)",      Kind.Cool, Aim.None, "150");
        Expect("Cooling Element (273)",      Kind.Cool, Aim.None, "273");
        Expect("Temperature Sensor (1000)",  Kind.Heat, Aim.None, "1000");

        // The bare cooling element is not a missing number. CoolingElementMaterial.Step sets
        // every neighbour's heat to 0 unconditionally, where the numbered forms clamp to their
        // own value, so it really is the zero-kelvin one -- the strongest of the family, and
        // the one whose name says least.
        Expect("Cooling Element", Kind.Cool, Aim.None, "0");
    }

    /// <summary>Counters are labelled with their period.</summary>
    [GameTest]
    public static void CountersAreLabelledWithTheirPeriod()
    {
        Expect("2 Step Oscillator (On)",    Kind.Timing, Aim.None, "2");
        Expect("5 Step Oscillator (On)",    Kind.Timing, Aim.None, "5");
        Expect("Every 8th Tick Dispenser",  Kind.Timing, Aim.None, "8");
    }

    /// <summary>
    /// An unprogrammed filter or sensor says so, by carrying its prefix and nothing else.
    ///
    /// <para>Worth its own test because it is the distinction a player most wants and the game
    /// shows least: a filter nobody has set yet is the same pixel, in the same colour, as one
    /// that is set.</para>
    /// </summary>
    [GameTest]
    public static void AnUnprogrammedFilterSaysSo()
    {
        // A tick and a cross from the game's own art, and a question mark for the sensor, which
        // has no symbol in the game to borrow. An unprogrammed one carries the symbol and no
        // name, which is exactly the distinction a player wants: a filter nobody has set yet
        // looks identical to one that is set.
        foreach (var (name, icon) in new[]
                 {
                     ("Match Filter", MarkIcon.Check),
                     ("Nonmatch Filter", MarkIcon.Cross),
                     ("Programmable Filter", MarkIcon.Check),
                 })
        {
            var a = AnnotationRules.For(name);
            if (a.Kind != Kind.Filter || a.Icon != icon)
                throw new AssertionException($"'{name}' read as {a}, expected Filter {icon}");
            if (a.Body.Length > 0)
                throw new AssertionException(
                    $"'{name}' names '{a.Body}'; an unprogrammed filter has no target to name");
        }

        Expect("Sensor",          Kind.Filter, Aim.None, AnnotationRules.Sense);


    }

    /// <summary>
    /// The phase filters read as a pass-or-stop mark over the game's own shape for the phase.
    ///
    /// <para>Worth the shapes rather than letters because two of these eight are the <i>same</i>
    /// colour in the shipped data -- <c>Allow Solids</c> and <c>Block Gases</c> are both pure red
    /// -- so the colour cannot be relied on to separate even the family, let alone the phase.
    /// </para>
    /// </summary>
    [GameTest]
    public static void PhaseFiltersReadAsAMarkOverTheGamesOwnShape()
    {
        // The allowing half carries no mark at all: the shape says which phase, and passing it
        // is what a filter does by default, so the mark would be a character saying nothing.
        // A bare shape passes; a shape with a bar through it does not.
        ExpectShape("Allow Solids",  "", Shape.Solid);
        ExpectShape("Allow Liquids", "", Shape.Liquid);
        ExpectShape("Allow Gases",   "", Shape.Gas);
        ExpectCross("Block Solids",  Shape.Solid);
        ExpectCross("Block Liquids", Shape.Liquid);
        ExpectCross("Block Gases",   Shape.Gas);

        // ...which means the two halves are still told apart, and by the mark rather than by
        // anything the shape does. Worth pinning: with the mark gone from both they would be
        // identical pixels, which is the bug this whole family exists to fix.
        foreach (var what in new[] { "Solids", "Liquids", "Gases" })
        {
            var allow = AnnotationRules.For("Allow " + what);
            var block = AnnotationRules.For("Block " + what);
            if (allow.Icon == block.Icon && allow.Text == block.Text)
                throw new AssertionException(
                    $"'Allow {what}' and 'Block {what}' read identically, so the one fact that " +
                    "separates them is not shown");
        }

        // The player gets a shape too, cut out of the game's own player-and-spaceship icon --
        // there is no avatar sprite to borrow, because the avatar is composited at run time from
        // layered sheets and depends on the player's cosmetics.
        ExpectShape("Allow Players", "", Shape.Player);

        // The rule covers "Block Players" symmetrically though the game does not ship one --
        // seven of the eight combinations exist. Asserted because the rules are a pure function
        // of a name and cost nothing to keep complete, and because a game that later adds the
        // missing machine should get the right mark without anyone remembering this.
        var blocked = AnnotationRules.For("Block Players");
        if (blocked.Icon != MarkIcon.Cross || blocked.Shape != Shape.Player)
            throw new AssertionException($"'Block Players' would read as {blocked}");

        // And all four shapes are distinct, or two of the eight machines would read alike.
        var shapes = new[] { "Solids", "Liquids", "Gases", "Players" }
            .Select(w => AnnotationRules.For("Allow " + w).Shape).ToList();
        if (shapes.Distinct().Count() != shapes.Count)
            throw new AssertionException(
                $"two phase filters share a shape ({string.Join(" ", shapes)})");

        static void ExpectCross(string name, Shape shape)
        {
            var a = AnnotationRules.For(name);
            if (a.Kind != Kind.Filter || a.Icon != MarkIcon.Cross || a.Shape != shape)
                throw new AssertionException(
                    $"'{name}' read as {a}, expected Filter Cross over {shape}");
        }

        static void ExpectShape(string name, string mark, Shape shape)
        {
            var a = AnnotationRules.For(name);
            if (a.Kind != Kind.Filter || a.Mark != mark || a.Shape != shape)
                throw new AssertionException(
                    $"'{name}' read as {a}, expected Filter '{mark}' over {shape}");
            if (a.Body.Length > 0)
                throw new AssertionException(
                    $"'{name}' also carries the body '{a.Body}'; the shape is meant to replace it");
        }
    }

    /// <summary>
    /// The two water pixels are not filters, and are not labelled as though they were.
    ///
    /// <para><b>This was a real bug.</b> <c>Block Water</c> was matched by the phase-filter rule
    /// and drawn as <c>≠Water</c> -- which is exactly what a <i>nonmatch filter programmed to
    /// Water</i> is drawn as, and a completely different machine. <c>Water Filter</c>, despite
    /// the name, was not annotated at all.</para>
    ///
    /// <para>Neither tests what arrives and passes it or not. Both <b>split</b>: a hydrate
    /// (anything whose Composition holds "+H2O" and exactly one other element) is separated, and
    /// they differ in which half goes on. So they get a family of their own.</para>
    ///
    /// <para><b>They no longer differ from each other, on purpose.</b> Each used to carry a word
    /// for what comes out the far side, "wet" against "dry", and this test asserted the two were
    /// not the same. That assertion is gone rather than weakened: the two pixels are different
    /// colours in the game's own art, so which is which was the one thing a player could already
    /// see, and the label was spending the cell to repeat it. What remains asserted is everything
    /// that is still a bug if it breaks -- the family, the drop, no body at all, and above all
    /// that neither reads like a filter programmed to Water.</para>
    /// </summary>
    [GameTest]
    public static void TheWaterPixelsAreNotLabelledAsFilters()
    {
        var filter = AnnotationRules.For("Water Filter");
        var block = AnnotationRules.For("Block Water");

        foreach (var (name, a) in new[] { ("Water Filter", filter), ("Block Water", block) })
        {
            if (a.Kind != Kind.Split)
                throw new AssertionException(
                    $"'{name}' is in the {a.Kind} family. It splits hydrates rather than testing " +
                    "what arrives, and sharing a family with the filters is what made it read as " +
                    "a machine it is not.");
            if (a.Shape != Shape.Liquid)
                throw new AssertionException($"'{name}' has no drop to say it is about water: {a}");
        }

        foreach (var (name, a) in new[] { ("Water Filter", filter), ("Block Water", block) })
            if (a.Body.Length > 0)
                throw new AssertionException(
                    $"'{name}' carries the body '{a.Body}'. The drop and the split colour are " +
                    "meant to be the whole annotation; a word here is the cell being spent on " +
                    "something the pixel's own colour already says.");

        // And specifically: neither may read the way a programmed filter reads, or the bug is back.
        foreach (var a in new[] { filter, block })
            if (a.Text == AnnotationRules.Nonmatch + "Water" || a.Text == AnnotationRules.Match + "Water")
                throw new AssertionException(
                    $"a water pixel reads as '{a.Text}', which is exactly how a filter programmed " +
                    "to Water reads. These are different machines and must not look alike.");
    }

    /// <summary>
    /// All three spellings of "switched off" dim the annotation, and <c>(Rest)</c> is not one
    /// of them: a latch at rest is still powered.
    /// </summary>
    [GameTest]
    public static void TheOffStateIsRecognisedInEverySpelling()
    {
        AssertOff("Conveyor Left (Off)", true);
        AssertOff("Conveyor Left (Turning Off)", true);
        AssertOff("Laser Ruby Up Off", true);
        AssertOff("Conveyor Left", false);
        AssertOff("Latch (Down) (On) (Rest)", false);

        // The two families that spell "off" by saying nothing at all. Only the game's
        // TurnsOnInto separates them from a machine with a single state, which is why
        // AnnotationRules.For takes it as an argument rather than guessing.
        if (AnnotationRules.For("Toggleable Pump Up", canBeSwitchedOn: false).Off)
            throw new AssertionException(
                "'Toggleable Pump Up' read as off from its name alone, so this test no longer " +
                "covers the gap it was written for: the name does not say off, and the whole " +
                "point of the canBeSwitchedOn argument is that only the game's field does.");
        if (!AnnotationRules.For("Toggleable Pump Up", canBeSwitchedOn: true).Off)
            throw new AssertionException(
                "'Toggleable Pump Up' did not read as off with canBeSwitchedOn set, so the " +
                "argument is being ignored and two whole families draw as if they were running");
    }

    /// <summary>
    /// Loose dropped material is not a machine.
    ///
    /// <para>Every machine has a <c>Bits of</c> form, and without the guard a pile of mined
    /// conveyor on the floor would carry the conveyor's arrow -- pointing a direction it has no
    /// meaning in, in a place the player did not build anything.</para>
    /// </summary>
    [GameTest]
    public static void DroppedBitsAreNotAnnotated()
    {
        foreach (var name in new[] { "Bits of Conveyor Left", "Bits of Match Filter",
                                     "Bits of Heating Element (1000)" })
            if (AnnotationRules.For(name).Any)
                throw new AssertionException(
                    $"'{name}' is annotated as {AnnotationRules.For(name)}, but it is loose " +
                    "dropped material rather than a placed machine");
    }

    // --------------------------------------------------------------- sweeps over the registry

    /// <summary>
    /// Wherever the game declares a direction and the mod reads one, they agree.
    ///
    /// <para><b>This is the cross-check the rules are built on.</b> <c>MaterialType.Direction</c>
    /// is not usable as the mod's source -- most of the machines that face a direction leave it
    /// unset -- but where it <i>is</i> set it is the game's own answer, and the name parser
    /// disagreeing with it would mean the parser is wrong. 97 materials declare one.</para>
    ///
    /// <para>Only the cardinal directions are compared. The game's enum has eight and its
    /// diagonals are used for falling and support rules; no machine in the shipped data faces
    /// one, and a diagonal appearing here would be a new fact about the game rather than a
    /// failure of this mod, so it is counted and reported rather than asserted against.</para>
    /// </summary>
    [GameTest]
    public static void TheRulesAgreeWithTheGameWhereverBothSpeak(Region r)
    {
        var compared = 0;
        var diagonal = 0;
        var wrong = new List<string>();

        foreach (var material in EveryMaterial())
        {
            var declared = material.MaterialType.Direction;
            if (declared == EightWayDirection.None)
                continue;

            var expected = declared switch
            {
                EightWayDirection.Up    => Aim.Up,
                EightWayDirection.Down  => Aim.Down,
                EightWayDirection.Left  => Aim.Left,
                EightWayDirection.Right => Aim.Right,
                _                       => Aim.None,
            };
            if (expected == Aim.None)
            {
                diagonal++;
                continue;
            }

            var read = AnnotationRules.For(material.Name).Aim;
            if (read == Aim.None)
                continue;   // a family the mod does not annotate at all; that is not a conflict

            compared++;
            if (read != expected)
                wrong.Add($"'{material.Name}' declares {declared} and reads as {read}");
        }

        if (wrong.Count > 0)
            throw new AssertionException(
                $"{wrong.Count} material(s) point a different way than the game says:\n  " +
                string.Join("\n  ", wrong.Take(10)) +
                "\nThe name parser in AnnotationRules disagrees with MaterialType.Direction.");

        if (compared < 50)
            throw new AssertionException(
                $"only {compared} materials had a direction to compare (and {diagonal} diagonals), " +
                "which is far fewer than the ~97 the shipped game declares. Either the field " +
                "stopped being set or the rules stopped matching; either way this test is no " +
                "longer checking what it says it checks.");
    }

    /// <summary>
    /// Nothing is annotated that is not a machine or a projectile.
    ///
    /// <para>The guard against a rule over-matching. Terrain, ores, liquids, gases, creatures
    /// and food all flow through the same <c>For</c>, and a family prefix that is one character
    /// too loose would start labelling them -- silently, and on materials the player sees
    /// thousands of at a time. It sweeps the live registry, so a material added by a game update
    /// or by another mod is covered on the day it appears.</para>
    ///
    /// <para>Two deliberate exceptions. Bullets and plasma bullets are not <c>IsMechanical</c>
    /// because they are in flight rather than placed, and where they are headed is worth showing.
    /// And <c>Water Filter</c> is simply missing the flag in the game's own data -- <c>Bits of
    /// Water Filter</c> has it -- which is worth knowing when reading this list rather than
    /// worth working around silently.</para>
    /// </summary>
    [GameTest]
    public static void NothingButMachinesAndProjectilesIsAnnotated(Region r)
    {
        var strays = new List<string>();

        foreach (var material in EveryMaterial())
        {
            if (!AnnotationRules.For(material.Name).Any)
                continue;
            if (material.IsMechanical)
                continue;
            if (material.Name.StartsWith("Bullet ", StringComparison.Ordinal) ||
                material.Name.StartsWith("Plasma Bullet", StringComparison.Ordinal))
                continue;
            // The game does not flag Water Filter as mechanical, though Bits of Water Filter is
            // flagged and every comparable machine is. It is a placed machine by every other
            // measure -- a Static pixel with a StaticMaterial subclass and a craftable recipe --
            // so this is the game's data being inconsistent rather than the rules over-matching.
            if (material.Name == "Water Filter")
                continue;
            strays.Add($"'{material.Name}' -> {AnnotationRules.For(material.Name)}");
        }

        if (strays.Count > 0)
            throw new AssertionException(
                $"{strays.Count} material(s) that are neither mechanical nor projectiles are " +
                $"annotated:\n  {string.Join("\n  ", strays.Take(10))}\n" +
                "A family prefix in AnnotationRules is matching too loosely; see PointingFamilies, " +
                "which matches on whole words for exactly this reason.");
    }

    /// <summary>
    /// The rules never throw, whatever a name looks like.
    ///
    /// <para><c>For</c> is called once per registered material at startup and once per
    /// programmed-filter cell per frame, so a name that makes it throw is a mod that fails to
    /// start or a per-frame exception storm. The empty, punctuation-only and unbalanced-paren
    /// cases are the ones the parser could plausibly trip on, and another mod's material can be
    /// named anything at all.</para>
    /// </summary>
    [GameTest]
    public static void TheRulesSurviveAnyName()
    {
        foreach (var name in new[]
                 {
                     "", " ", "(", ")", "()", "(((", ")))", "( )", "Gun (", "Gun )",
                     "Latch ()", "Heating Element ()", "Every  Tick Dispenser",
                     " Step Oscillator", "Balance Input to", "Balance  to ",
                     "Allow ", "Block ", "Conveyor", "Bits of ", "\t\n",
                 })
            AnnotationRules.For(name);

        // And every real one, which is the sweep that would catch a name shape nobody thought of.
        foreach (var material in Materials.BaseMaterialsDict)
            AnnotationRules.For(material?.Name);
    }

    // --------------------------------------------------------------------------- helpers

    private static IEnumerable<BaseMaterial> EveryMaterial()
    {
        for (short id = 0; id < Materials.Count; id++)
        {
            var material = Materials.GetBaseMaterial(id);
            if (material != null)
                yield return material;
        }
    }

    private static void Expect(string name, Kind kind, Aim aim, string? text = null)
    {
        var a = AnnotationRules.For(name);
        if (a.Kind != kind || a.Aim != aim || (text != null && a.Text != text))
            throw new AssertionException(
                $"'{name}' read as {a}, expected {kind}" +
                (aim != Aim.None ? $" aim={aim}" : "") +
                (text != null ? $" '{text}'" : ""));
    }

    private static void AssertOff(string name, bool off)
    {
        var a = AnnotationRules.For(name);
        if (a.Off != off)
            throw new AssertionException(
                $"'{name}' read as {a}; expected it to be {(off ? "off" : "on")}");
    }
}
