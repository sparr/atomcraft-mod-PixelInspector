using PixelArt;
using Atomcraft;
using Atomcraft.TestHarness;

namespace PixelInspector.Test;

/// <summary>
/// The annotations that cannot be worked out from a material alone, because they are stored in
/// the cell or in its neighbours.
///
/// <para><b>This is what the mod is for.</b> When a player programs a match filter, the game
/// does not place a "match filter set to water" material -- it writes the <i>water id</i> into
/// the cell with a quadrant bit set (<c>BaseMaterial.ToMatchFilterOffset</c>), and then draws
/// the cell in the one flat colour every match filter shares. Two filters set to different
/// materials are different ids and identical pixels. The game will name the target in its hover
/// box, one pixel at a time; reading it back out here is what lets a whole bank of filters be
/// read at once.</para>
///
/// <para>These are region tests, so they cost milliseconds: a filter's programmed state is a
/// value in the field, and <c>Region.SetRaw</c> can write one directly without a world, a
/// window, or the game's material-select UI.</para>
/// </summary>
public static class CellTests
{
    /// <summary>
    /// Each of the three programmable pixels names what it was set to, with its own prefix.
    /// </summary>
    [GameTest]
    public static void EachProgrammablePixelNamesItsTarget(Region r)
    {
        var water = Materials.GetBaseMaterialId("Water");

        r.SetRaw(2, 2, BaseMaterial.ToMatchFilterOffset(water));
        r.SetRaw(4, 2, BaseMaterial.ToNonMatchFilterOffset(water));
        r.SetRaw(6, 2, BaseMaterial.ToSensorOffset(water));

        // A tick, a cross and a question mark: the first two are the game's own art and the
        // third is still a character, because the game has no "watching for" symbol to borrow.
        AssertMark(r, 2, 2, MarkIcon.Check, "Water");
        AssertMark(r, 4, 2, MarkIcon.Cross, "Water");
        AssertMark(r, 6, 2, MarkIcon.None,  "Water");
        AssertText(r, 6, 2, AnnotationRules.Sense + "Water");
    }

    /// <summary>
    /// Two filters set to different materials read differently.
    ///
    /// <para>The claim in one line. The game draws both in the same colour -- see
    /// <c>RetirementTests.AProgrammedFilterStillLooksLikeAnUnprogrammedOne</c>, which is where
    /// that half is checked -- so if this ever passed for the wrong reason the mod would be
    /// drawing a label that says nothing.</para>
    /// </summary>
    [GameTest]
    public static void TwoFiltersSetDifferentlyReadDifferently(Region r)
    {
        r.SetRaw(2, 2, BaseMaterial.ToMatchFilterOffset(Materials.GetBaseMaterialId("Water")));
        r.SetRaw(4, 2, BaseMaterial.ToMatchFilterOffset(Materials.GetBaseMaterialId("Iron")));

        var a = TextAt(r, 2, 2);
        var b = TextAt(r, 4, 2);
        if (a == b)
            throw new AssertionException(
                $"both filters read as '{a}'. The target is not reaching the annotation, so the " +
                "mod is drawing the one thing the game already shows.");
        if (a != "Water" || b != "Iron")
            throw new AssertionException($"read '{a}' and '{b}', expected 'Water' and 'Iron'");
    }

    /// <summary>
    /// A target is named, whether or not it has a chemical formula.
    ///
    /// <para>The formula was what this drew first, and it was replaced: only 1041 of 1915
    /// materials have one, so more than a third of targets fell through to the name anyway and
    /// what a label meant depended on which side of that line the target fell. Sand has no
    /// formula and Water has one, so the two of them together are the check that the label no
    /// longer varies with it.</para>
    /// </summary>
    [GameTest]
    public static void ATargetIsNamedWhetherOrNotItHasAFormula(Region r)
    {
        var sand = Materials.GetBaseMaterial(Materials.GetBaseMaterialId("Sand"));
        var water = Materials.GetBaseMaterial(Materials.GetBaseMaterialId("Water"));
        if (!string.IsNullOrEmpty(sand?.Formula) || string.IsNullOrEmpty(water?.Formula))
            throw new AssertionException(
                "this test needs one material with a formula and one without, and the shipped " +
                $"data no longer provides that pair (Sand='{sand?.Formula}', Water='{water?.Formula}')");

        r.SetRaw(2, 2, BaseMaterial.ToMatchFilterOffset(Materials.GetBaseMaterialId("Sand")));
        r.SetRaw(4, 2, BaseMaterial.ToMatchFilterOffset(Materials.GetBaseMaterialId("Water")));

        AssertMark(r, 2, 2, MarkIcon.Check, "Sand");
        AssertMark(r, 4, 2, MarkIcon.Check, "Water");
    }

    /// <summary>
    /// A cell carries the target's <b>whole</b> name, however long, and keeps the mark apart
    /// from it.
    ///
    /// <para>Both halves matter and both were bugs once. Cutting here would fix a character
    /// budget at startup and throw away, on every frame, the room a player zoomed in to create --
    /// how much fits is <see cref="LabelLayout"/>'s question, asked against the cell on the frame
    /// it draws. And folding the <c>=</c> into the text would take away the layout's ability to
    /// stack it above the name, which is usually worth a whole font size.</para>
    /// </summary>
    [GameTest]
    public static void ACellCarriesTheWholeNameAndKeepsTheMarkApart(Region r)
    {
        r.SetRaw(2, 2, BaseMaterial.ToNonMatchFilterOffset(
            Materials.GetBaseMaterialId("Carbon Dioxide")));

        var a = PixelInspectorApi.At(Field(), r.OriginX + 2, r.OriginY + 2);

        if (a.Body != "Carbon Dioxide")
            throw new AssertionException(
                $"the cell carries the body '{a.Body}', expected the whole name 'Carbon Dioxide'. " +
                "If it was cut here, the layout can never show more of it however far you zoom in.");
        if (a.Icon != MarkIcon.Cross)
            throw new AssertionException(
                $"the mark is {a.Icon}, expected Cross. A nonmatch filter and a match filter must " +
                "not read alike.");
        if (a.Mark.Length != 0)
            throw new AssertionException(
                $"the annotation carries a text mark '{a.Mark}' as well as an icon; the two are " +
                "never both drawn");
    }

    /// <summary>
    /// A programmable filter names whatever is beside it, which is where it takes its target
    /// from and is state no cell of its own records.
    /// </summary>
    [GameTest]
    public static void AProgrammableFilterNamesItsNeighbours(Region r)
    {
        r.Set(4, 4, "Programmable Filter");

        // Nothing either side: it passes nothing, and says so by carrying a bare tick.
        AssertMark(r, 4, 4, MarkIcon.Check, "");

        r.Set(3, 4, "Water");
        // Two different neighbours means two targets, and both are named.
        r.Set(5, 4, "Iron");
        AssertMark(r, 4, 4, MarkIcon.Check, "Water/Iron");

        // The same material on both sides is one target, not two.
        r.Set(5, 4, "Water");
        AssertMark(r, 4, 4, MarkIcon.Check, "Water");
    }

    /// <summary>
    /// Air, out of bounds, and ordinary terrain are all left alone.
    ///
    /// <para>The annotation pass runs over every cell in the radius, most of which are none of
    /// those things, so "has nothing to say" has to be the cheap and silent answer rather than
    /// a special case.</para>
    /// </summary>
    [GameTest]
    public static void OrdinaryCellsAreNotAnnotated(Region r)
    {
        r.Set(2, 2, "Granite");
        r.SetAir(2, 4);

        foreach (var (x, y, what) in new[] { (2, 2, "granite"), (2, 4, "air") })
            if (PixelInspectorApi.At(Field(), r.OriginX + x, r.OriginY + y).Any)
                throw new AssertionException($"{what} at ({x},{y}) was annotated");

        // Far outside the field. SimField.Get answers -2 rather than throwing, and the mod has
        // to read that as "nothing" rather than as a material id.
        if (PixelInspectorApi.At(Field(), -5000, -5000).Any)
            throw new AssertionException("a cell outside the world was annotated");
    }

    /// <summary>
    /// A placed machine reads the same through the cell path as through the name path.
    ///
    /// <para>Two routes reach an annotation -- the prebuilt material table and the per-cell
    /// resolution -- and everything that is not a filter goes through the table. A drift between
    /// them would show as a machine that is annotated in the vocabulary tests and blank on
    /// screen.</para>
    /// </summary>
    [GameTest]
    public static void APlacedMachineReadsTheSameThroughBothPaths(Region r)
    {
        foreach (var (name, x) in new[]
                 {
                     ("Conveyor Left", 2), ("Pump Up", 4), ("Heating Element (1000)", 6),
                     ("And Gate (Up) (On)", 8), ("Match Filter", 10), ("Allow Solids", 12),
                 })
        {
            r.Set(x, 6, name);
            var byCell = PixelInspectorApi.At(Field(), r.OriginX + x, r.OriginY + 6);
            var byName = PixelInspectorApi.Describe(name);

            if (byCell.Kind != byName.Kind || byCell.Aim != byName.Aim || byCell.Text != byName.Text)
                throw new AssertionException(
                    $"'{name}' reads as {byCell} from the cell and {byName} from the name");
            if (!byCell.Any)
                throw new AssertionException($"'{name}' is a machine the mod should annotate, but did not");
        }
    }

    // --------------------------------------------------------------------------- helpers

    private static SimField Field() =>
        Simulation.CurrentState?.Field
        ?? throw new AssertionException("no simulation field; is a region allocated?");

    private static string TextAt(Region r, int x, int y) =>
        PixelInspectorApi.At(Field(), r.OriginX + x, r.OriginY + y).Text;

    private static void AssertMark(Region r, int x, int y, MarkIcon icon, string body)
    {
        var a = PixelInspectorApi.At(Field(), r.OriginX + x, r.OriginY + y);
        if (a.Icon != icon)
            throw new AssertionException(
                $"the cell at ({x},{y}) carries {a.Icon}, expected {icon}");
        if (a.Body != body)
            throw new AssertionException(
                $"the cell at ({x},{y}) names '{a.Body}', expected '{body}'");
    }

    private static void AssertText(Region r, int x, int y, string expected)
    {
        var actual = TextAt(r, x, y);
        if (actual != expected)
            throw new AssertionException(
                $"the cell at ({x},{y}) reads '{actual}', expected '{expected}'");
    }
}
