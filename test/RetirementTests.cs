using PixelArt;
using Atomcraft;
using Atomcraft.TestHarness;
using Godot;

namespace PixelInspector.Test;

/// <summary>
/// The parts of this mod that exist only because of how the game draws, and the question of
/// whether it still draws that way.
///
/// <para><b>A failure in this file is good news.</b> Every test here asserts that the game still
/// hides something this mod exists to show, so a red result means the game started showing it
/// and a piece of the mod can be deleted. That is a completely different question from "is the
/// mod correct", and mixing the two makes a red suite unreadable.</para>
///
/// <para>So these are excluded from the default run and asked for deliberately:</para>
/// <code>
/// ./run-tests.sh --retirement
/// </code>
///
/// <para><b>Each test's doc comment says what to delete when it fails.</b> That is the whole
/// payload: a year later, the person reading the red result is not the person who wrote the
/// annotation.</para>
/// </summary>
public static class RetirementTests
{
    /// <summary>
    /// A programmed filter is still <i>drawn</i> exactly like an unprogrammed one.
    ///
    /// <para><b>When this fails, retire:</b> the per-cell branch in
    /// <c>MaterialAnnotations.At</c> that decodes a quadrant-tagged id, the three prefixes in
    /// <c>AnnotationRules</c>, the composed <c>≠</c> glyph in <c>PixelFont</c>, and
    /// <c>CellTests</c>. That is the largest single piece of the mod.</para>
    ///
    /// <para>The game stores a programmed filter as the <i>target's</i> id with a quadrant bit
    /// set, and then <c>Utils.ToMaterial</c> maps every such id back to the one plain filter
    /// material before anything draws it. So a filter set to Water and one set to Iron are
    /// different ids and the same pixel in the same colour. The hover box does name the target
    /// -- <c>HoveredMaterialHint</c> appends it for any non-normal id -- so what this asserts is
    /// that the <i>world</i> still shows nothing, which is what makes reading a bank of them at
    /// a glance worth a mod.</para>
    /// </summary>
    [GameTest]
    public static void AProgrammedFilterStillLooksLikeAnUnprogrammedOne(Region r)
    {
        var water = Materials.GetBaseMaterialId("Water");
        var iron = Materials.GetBaseMaterialId("Iron");
        var plain = Materials.GetBaseMaterial(Materials.MATCH_FILTER);

        var toWater = BaseMaterial.ToMatchFilterOffset(water).ToMaterial();
        var toIron = BaseMaterial.ToMatchFilterOffset(iron).ToMaterial();

        if (toWater == null || toIron == null || plain == null)
            throw new AssertionException(
                "a programmed match filter no longer resolves to a material at all, which is a " +
                "bigger change than this test was written for; read Utils.ToMaterial");

        if (!ReferenceEquals(toWater, toIron) || !ReferenceEquals(toWater, plain))
            throw new AssertionException(
                $"programmed filters now resolve to distinct materials ('{toWater.Name}' and " +
                $"'{toIron.Name}'), so the game may be showing the target itself. See the doc " +
                "comment: a large part of this mod can probably be retired.");

        if (toWater.Color != plain.Color)
            throw new AssertionException(
                "a programmed match filter is no longer drawn in the plain filter colour, so " +
                "the target may now be visible without this mod");
    }

    /// <summary>
    /// The four logic gates are still drawn in one colour, so which gate a pixel is cannot be
    /// seen.
    ///
    /// <para><b>When this fails, retire:</b> the gate symbols in
    /// <c>AnnotationRules.GateSymbol</c> and
    /// <c>VocabularyTests.EachGateFamilyGetsItsOwnSymbol</c>. Note the direction nub is a
    /// separate question and would survive: knowing a pixel is an XOR does not say which way its
    /// output goes.</para>
    /// </summary>
    [GameTest]
    public static void TheGateFamiliesStillShareOneColour(Region r)
    {
        var colors = new[] { "And", "Xor", "Not" }
            .Select(f => (Family: f, Color: ColorOf($"{f} Gate (Up) (Off)")))
            .ToList();

        if (colors.Select(c => c.Color).Distinct().Count() != 1)
            throw new AssertionException(
                "the gate families now have distinct colours (" +
                string.Join(", ", colors.Select(c => $"{c.Family}={c.Color}")) +
                "), so the game distinguishes them itself. See the doc comment.");
    }

    /// <summary>
    /// A thermostat's setting still cannot be seen in the world: every heating element is one
    /// red and every cooling element one blue, whatever they are set to. (The hover box gives
    /// it for the hovered pixel, via <c>Materials.IfTemperatureRelatedPixelGetTemp</c>.)
    ///
    /// <para><b>When this fails, retire:</b> the <c>Heating Element</c>, <c>Cooling Element</c>
    /// and <c>Temperature Sensor</c> rules in <c>AnnotationRules</c>, the <c>Heat</c> and
    /// <c>Cool</c> kinds if nothing else uses them, and
    /// <c>VocabularyTests.ThermostatsAreLabelledWithTheirSetting</c>.</para>
    /// </summary>
    [GameTest]
    public static void AThermostatSettingIsStillInvisible(Region r)
    {
        if (ColorOf("Heating Element (500)") != ColorOf("Heating Element (2000)"))
            throw new AssertionException(
                "heating elements now differ in colour by setting, so the number may be visible " +
                "without this mod");
        if (ColorOf("Cooling Element (150)") != ColorOf("Cooling Element (273)"))
            throw new AssertionException(
                "cooling elements now differ in colour by setting, so the number may be visible " +
                "without this mod");
        if (ColorOf("Temperature Sensor (500)") != ColorOf("Temperature Sensor (2000)"))
            throw new AssertionException(
                "temperature sensors now differ in colour by trip point, so the number may be " +
                "visible without this mod");
    }

    /// <summary>
    /// Most machines that face a direction still do not declare one.
    ///
    /// <para><b>When this fails, retire:</b> nothing outright, but
    /// <c>AnnotationRules.EmbeddedAim</c> and the whole name-parsing route to a direction could
    /// be replaced by reading <c>MaterialType.Direction</c>, which would be shorter and would
    /// not care what the game renames things to. Check every family before switching, and keep
    /// <c>VocabularyTests.TheRulesAgreeWithTheGameWhereverBothSpeak</c>, which becomes a
    /// tautology and should be deleted with the parser.</para>
    ///
    /// <para>The families listed are ones that visibly point somewhere and leave the field
    /// unset, which is why the mod reads names at all.</para>
    /// </summary>
    [GameTest]
    public static void MostFacingMachinesStillDoNotDeclareADirection(Region r)
    {
        var undeclared = new[] { "Conveyor Left", "Pump Down", "Toggleable Pump Up", "Prism Left" }
            .Where(n => Materials.GetBaseMaterial(Materials.GetBaseMaterialId(n))
                            ?.MaterialType.Direction == EightWayDirection.None)
            .ToList();

        if (undeclared.Count < 4)
            throw new AssertionException(
                "the game now declares MaterialType.Direction on machines that used to leave it " +
                $"unset (still unset: {string.Join(", ", undeclared)}). See the doc comment: the " +
                "name parser may be replaceable by the field.");
    }

    /// <summary>
    /// The game's own constant for the programmable filter is still never assigned.
    ///
    /// <para><b>When this fails, retire:</b> <c>MaterialAnnotations._programmableFilter</c> and
    /// the name lookup that fills it, and use <c>Materials.PROGRAMMABLE_FILTER</c> directly.</para>
    ///
    /// <para>Every other named material id in <c>Materials</c> is assigned from
    /// <c>GetBaseMaterialId</c> during init. <c>PROGRAMMABLE_FILTER</c> is declared beside
    /// <c>MATCH_FILTER</c> and <c>NON_MATCH_FILTER</c> and is simply missed, so it holds the
    /// default 0 -- which is a real id belonging to an unrelated material. Any mod that trusts
    /// it labels the wrong pixel and never finds the right one, with no error at all; this mod
    /// did exactly that until its own tests caught it.</para>
    ///
    /// <para>Asserted as "the constant does not name the programmable filter" rather than as
    /// "the constant is 0", so that the day it is assigned correctly this goes red whatever
    /// number it ends up holding.</para>
    /// </summary>
    [GameTest]
    public static void TheGamesProgrammableFilterConstantIsStillDead(Region r)
    {
        var real = Materials.GetBaseMaterialId("Programmable Filter");
        if (real < 0)
            throw new AssertionException(
                "the game no longer has a material named 'Programmable Filter', which is a " +
                "bigger change than this test was written for");

        if (Materials.PROGRAMMABLE_FILTER == real)
            throw new AssertionException(
                $"Materials.PROGRAMMABLE_FILTER is now {Materials.PROGRAMMABLE_FILTER}, which is " +
                "the programmable filter's real id. The game assigns it now; see the doc comment.");
    }

    private static Color ColorOf(string name) =>
        Materials.GetBaseMaterial(Materials.GetBaseMaterialId(name))?.Color
        ?? throw new AssertionException($"the game no longer has a material named '{name}'");
}
