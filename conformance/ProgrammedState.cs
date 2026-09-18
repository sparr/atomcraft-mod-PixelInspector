using Atomcraft;
using Atomcraft.TestHarness;

namespace PixelInspectorConformance;

/// <summary>
/// The game still records a pixel's programmed state where a mod can read it.
///
/// <para>Atomcraft lets a player <i>configure</i> a handful of placed pixels -- a match filter, a
/// nonmatch filter and a sensor are each set to a material, and a programmable filter takes its
/// setting from its neighbours -- and draws none of that configuration into the world, offering
/// it only in the hover box, one pixel at a time. Any mod that shows it more widely than that,
/// however it chooses to show it, has to get the setting back out of the world itself, and there
/// are exactly two places it lives: the top two bits of the cell's own material id, and the
/// cells beside it.</para>
///
/// <para><b>A failure here is bad news.</b> These are not defects being worked around; they are
/// the facts such a mod is built on, and if one stops holding then the mod is reading something
/// else and quietly labelling pixels with the wrong material.</para>
///
/// <para>Deliberately expressed as round trips over the <i>live</i> registry rather than as
/// comparisons against the constants. That the constants are unchanged is not the claim that
/// matters; the claim is that tagging an id and untagging it gets the same material back, for
/// every material the game actually has, and that a tagged id is never mistakable for a plain
/// one.</para>
/// </summary>
public static class ProgrammedState
{
    /// <summary>
    /// Tagging a material id as a filter target and reading it back returns the same material,
    /// for every material in the registry.
    ///
    /// <para>This is the whole mechanism. <c>ToMatchFilterOffset</c> sets a quadrant bit and
    /// <c>BaseId</c> masks it off; the low 14 bits carry the target, so the property holds only
    /// while the registry stays inside 16,384 materials. The shipped game uses about 1,900, so
    /// there is room -- but a large enough mod pack would silently break every mod that reads a
    /// filter's target, and the failure would present as filters labelled with the wrong
    /// material rather than as an error.</para>
    /// </summary>
    [GameTest]
    public static void EveryMaterialIdSurvivesBeingTaggedAsAFilterTarget(Region r)
    {
        var count = Materials.Count;
        if (count <= 0)
            throw new AssertionException("no materials are registered; the game did not load");

        if (count > BaseMaterial.BASE_ID_MASK)
            throw new AssertionException(
                $"{count} materials are registered, more than the {BaseMaterial.BASE_ID_MASK} a " +
                "filter target id can hold. Any mod reading a filter's target will now read the " +
                "wrong material for the ones past the limit, with no error.");

        for (short id = 0; id < count; id++)
        {
            if (Materials.GetBaseMaterial(id) == null)
                continue;

            foreach (var (name, tagged) in new (string, short)[]
                     {
                         ("match", BaseMaterial.ToMatchFilterOffset(id)),
                         ("nonmatch", BaseMaterial.ToNonMatchFilterOffset(id)),
                         ("sensor", BaseMaterial.ToSensorOffset(id)),
                     })
            {
                if (BaseMaterial.BaseId(tagged) != id)
                    throw new AssertionException(
                        $"tagging material {id} ('{Materials.GetBaseMaterial(id)?.Name}') as a " +
                        $"{name} target gave {tagged}, which reads back as material " +
                        $"{BaseMaterial.BaseId(tagged)}");

                if (BaseMaterial.IsNormal(tagged))
                    throw new AssertionException(
                        $"a {name} target id ({tagged}) reads as an ordinary material id, so a " +
                        "programmed pixel is indistinguishable from whatever material shares " +
                        "that number");
            }
        }
    }

    /// <summary>
    /// The three tags stay distinct from each other, so a filter is never mistaken for a sensor.
    ///
    /// <para>They differ only in two bits of a <c>short</c>, and the sensor tag is the negative
    /// quadrant, which is also where the game's own <c>-1</c> (air) and <c>-2</c> (out of bounds)
    /// sentinels live. <c>IsSensorOffset</c> excludes both by hand; a mod that tested the bits
    /// itself would annotate empty space.</para>
    /// </summary>
    [GameTest]
    public static void TheThreeTagsStayDistinctAndClearOfTheSentinels(Region r)
    {
        for (short id = 0; id < Materials.Count; id++)
        {
            if (Materials.GetBaseMaterial(id) == null)
                continue;

            var match = BaseMaterial.ToMatchFilterOffset(id);
            var nonmatch = BaseMaterial.ToNonMatchFilterOffset(id);
            var sensor = BaseMaterial.ToSensorOffset(id);

            if (match == nonmatch || match == sensor || nonmatch == sensor)
                throw new AssertionException(
                    $"material {id} tags to the same id for more than one of the three kinds " +
                    $"(match={match} nonmatch={nonmatch} sensor={sensor})");

            if (match == -1 || nonmatch == -1 || sensor == -1 ||
                match == -2 || nonmatch == -2 || sensor == -2)
                throw new AssertionException(
                    $"material {id} tags onto the air (-1) or out-of-bounds (-2) sentinel, so a " +
                    "programmed pixel and empty space would read as the same thing");
        }

        // The sentinels themselves must not read as programmed pixels, which is the same claim
        // from the other side and the one that decides whether a pass over empty sky draws
        // labels on it.
        if (BaseMaterial.IsSensorOffset(-1) || BaseMaterial.IsSensorOffset(-2))
            throw new AssertionException(
                "air or out-of-bounds now reads as a programmed sensor, so any mod annotating " +
                "programmed pixels will annotate empty space");
    }

    /// <summary>
    /// The game still collapses every programmed pixel back to its plain material before
    /// anything looks at it.
    ///
    /// <para>This is the half that keeps the setting out of the <i>world</i>, and therefore the
    /// half that makes such a mod worth having. <c>Utils.ToMaterial</c> maps a tagged id to the
    /// plain Match Filter, Nonmatch Filter or Sensor, so the renderer and the save file see one
    /// material whatever it is set to. (The hover box is the exception: it reads the raw id and
    /// appends the target itself, for the hovered pixel only.)</para>
    ///
    /// <para>It is asserted here rather than left to a mod's own retirement suite because it
    /// cuts both ways: while it holds, a mod is needed; and a mod that reads targets is relying
    /// on <c>ToMaterial</c> behaving exactly this way when it asks what a tagged cell is.</para>
    /// </summary>
    [GameTest]
    public static void AProgrammedPixelStillResolvesToItsPlainMaterial(Region r)
    {
        var water = Materials.GetBaseMaterialId("Water");

        Check(BaseMaterial.ToMatchFilterOffset(water), Materials.MATCH_FILTER, "match filter");
        Check(BaseMaterial.ToNonMatchFilterOffset(water), Materials.NON_MATCH_FILTER, "nonmatch filter");
        Check(BaseMaterial.ToSensorOffset(water), Materials.SENSOR, "sensor");

        static void Check(short tagged, short plain, string what)
        {
            var resolved = tagged.ToMaterial();
            var expected = Materials.GetBaseMaterial(plain);
            if (resolved == null || expected == null)
                throw new AssertionException(
                    $"a programmed {what} no longer resolves to a material at all");
            if (!ReferenceEquals(resolved, expected))
                throw new AssertionException(
                    $"a programmed {what} resolves to '{resolved.Name}' rather than to " +
                    $"'{expected.Name}'. A mod asking the game what a programmed cell is will " +
                    "now get a different answer than it was written against.");
        }
    }

    /// <summary>
    /// A programmable filter still takes its setting from the cells immediately left and right
    /// of it.
    ///
    /// <para>The one configurable pixel whose setting is not in its own id. Asserted through
    /// behaviour rather than by reading the class: material falling onto it passes through only
    /// when it matches a horizontal neighbour, which is both what the pixel is for and the only
    /// evidence a mod could have that its neighbours are its setting.</para>
    ///
    /// <para>The layout is boxed in deliberately. Sand that the filter blocks would otherwise
    /// slide diagonally around it and read as having passed, and sand that <i>did</i> pass would
    /// fall out of the bottom of the region and read as having vanished -- so both outcomes are
    /// made into positions rather than absences.</para>
    ///
    /// <code>
    ///   # S #     the sand, walled so the only way down is through the filter
    ///   f P f     the filter, flanked by whatever is selecting
    ///   # . #     where it lands if it passed
    ///   # # #     a floor
    /// </code>
    ///
    /// <para>Both cases are built side by side and ticked together, rather than one after the
    /// other: <c>Region.Clear</c> clears the mod field channels and the per-tick flags but not
    /// the material map, so a second layout in the same place would be assembled on top of the
    /// first one's result.</para>
    /// </summary>
    [GameTest]
    public static void AProgrammableFilterStillTakesItsSettingFromItsNeighbours(Region r)
    {
        const string art = """
                           #S#
                           fPf
                           #.#
                           ###
                           """;
        const int Passes = 4;    // the left layout's x origin: flanked by what is falling
        const int Blocks = 20;   // the right one's: flanked by something else

        Paint(r, art, flank: "Sand", atX: Passes);
        Paint(r, art, flank: "Granite", atX: Blocks);

        // Eight ticks is far more than the two steps either outcome needs, so a slow frame
        // cannot be mistaken for a blocked pixel.
        r.Ticks(8);

        if (r.At(Passes + 1, 6) != "Sand")
            throw new AssertionException(
                "sand did not pass a programmable filter flanked by sand, so the neighbours are " +
                "no longer what sets it -- or the pixel no longer passes matching material at " +
                $"all. The column reads {Column(r, Passes + 1)}.");

        if (r.At(Blocks + 1, 4) != "Sand")
            throw new AssertionException(
                "sand did not stay above a programmable filter flanked by granite, so the " +
                "filter is not selecting on its neighbours and a mod reading them is reading " +
                $"the wrong thing. The column reads {Column(r, Blocks + 1)}.");

        static void Paint(Region r, string art, string flank, int atX) =>
            r.Paint(art, new Dictionary<char, string>
            {
                ['#'] = "Granite",
                ['S'] = "Sand",
                ['P'] = "Programmable Filter",
                ['f'] = flank,
            }, atX, atY: 4);

        // For the failure message: where the sand actually ended up says which of the two
        // failures this is, where "the assertion failed" says neither.
        static string Column(Region r, int x) =>
            "[" + string.Join(" ", Enumerable.Range(4, 4).Select(y => r.At(x, y) ?? "air")) + "]";
    }
}
