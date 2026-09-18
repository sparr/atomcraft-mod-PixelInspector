using System.Collections;
using Atomcraft;
using Atomcraft.TestHarness;
using Godot;
using PixelArt;

using Session = Atomcraft.TestHarness.Session;

namespace PixelInspector.Test;

/// <summary>
/// The layout cache: that it keys on the right things, and that it actually gets used.
/// </summary>
public static class LabelCacheTests
{
    /// <summary>Any price will do here; the tests are about the key, not the layout.</summary>
    private const float Cost = 0.8f;

    /// <summary>
    /// A repeat is a hit, a different box or a different ceiling is not, and clearing forgets.
    ///
    /// <para>The negative cases are the point. A cache that hits on everything is a cache that
    /// returns the wrong layout for a cell of a different size, which would show as labels at the
    /// wrong zoom rather than as anything obviously broken.</para>
    /// </summary>
    [GameTest]
    public static void TheCacheKeysOnTheBoxAndTheCeiling(Region r)
    {
        LabelCache.Clear();
        var box = new Vector2(32f, 32f);

        LabelCache.Choose("", "Sulfuric Acid", box, int.MaxValue, 5, Cost);
        if (LabelCache.Misses != 1 || LabelCache.Hits != 0)
            throw new AssertionException(
                $"a first layout counted {LabelCache.Hits} hits and {LabelCache.Misses} misses");

        LabelCache.Choose("", "Sulfuric Acid", box, int.MaxValue, 5, Cost);
        if (LabelCache.Hits != 1)
            throw new AssertionException(
                "the same label in the same box laid out again instead of being remembered");

        LabelCache.Choose("", "Sulfuric Acid", new Vector2(48f, 48f), int.MaxValue, 5, Cost);
        if (LabelCache.Misses != 2)
            throw new AssertionException(
                "a different cell size hit the cache, so every zoom would share one layout");

        LabelCache.Choose("", "Sulfuric Acid", box, 8, 5, Cost);
        if (LabelCache.Misses != 3)
            throw new AssertionException(
                "a different maxTextLength hit the cache, so changing the setting would not " +
                "take effect until something else evicted the entry");

        LabelCache.Clear();
        if (LabelCache.Count != 0 || LabelCache.Hits != 0)
            throw new AssertionException("Clear left something behind");
    }

    /// <summary>
    /// A short label stays on one line wherever one line is possible.
    ///
    /// <para>The property the word-break price is tuned for. Folding a short string buys a lot of
    /// glyph height, so at the library's default price the layout folded things that should not
    /// be: <c>Iron</c> became <c>Ir</c> over <c>on</c>, and a thermostat's <c>1727</c> became a
    /// number split across two lines.</para>
    ///
    /// <para><b>The premises are asserted too</b>, and they are what stop this from being a test
    /// that passes by never folding anything. Sixteen screen pixels is too narrow for four
    /// characters in any font, so a fold there is the only way to show them at all and must still
    /// happen; and <c>Water</c> must still fold at 32, because that is a real size step and the
    /// case the mid-word breaking was asked for. A price high enough to keep <c>Iron</c> whole and
    /// low enough to fold <c>Water</c> is the whole trick, and it exists because the two gain
    /// different amounts from folding.</para>
    /// </summary>
    [GameTest]
    public static void AShortLabelStaysOnOneLine(Region r)
    {
        static string Fit(string text, int cell) =>
            LabelCache.Choose("", text, new Vector2(cell, cell), int.MaxValue, 5, Painter.SplitCost)
                      .Text;

        LabelCache.Clear();

        foreach (var text in new[] { "Iron", "1727" })
        foreach (var cell in Magnifier.Ladder)
        {
            var laid = Fit(text, cell);
            var folded = laid.Contains('\n');

            if (cell <= 16)
            {
                if (!folded)
                    throw new AssertionException(
                        $"'{text}' fits on one line in a {cell} pixel cell, which no font allows. " +
                        "This test is measuring something other than it thinks.");
                continue;
            }

            if (folded)
                throw new AssertionException(
                    $"'{text}' came out as '{laid.Replace("\n", "/")}' in a {cell} pixel cell. A " +
                    "short label folded in two buys glyph height and costs a reader more than it " +
                    "is worth -- a temperature split across two lines is not a number any more. " +
                    "See Painter.SplitCost.");
        }

        // The other side of the same price.
        var water = Fit("Water", 32);
        if (!water.Contains('\n'))
            throw new AssertionException(
                $"'Water' came out as '{water}' at 32 pixels, on one line. The price of a word " +
                "break is now high enough to refuse a fold that doubles the glyph height, which " +
                "is the case mid-word breaking was added for.");

        LabelCache.Clear();
    }

    /// <summary>
    /// Zooming in never makes a label smaller.
    ///
    /// <para><b>The defect.</b> Pricing a word break buys word integrity by trading away glyph
    /// height, and the trade is only available once the cell is wide enough to hold the longest
    /// word whole. So a name could shrink as the player zoomed <i>in</i>: at 48 screen pixels
    /// <c>Aqueous Manganese(II) Sulfate</c> took a glyph height of 7 with one word broken, and at
    /// 60 a whole-word arrangement appeared at 5 and won on breaks.</para>
    ///
    /// <para><b>The premise is asserted as well</b>, and it is what stops this test from passing
    /// by simply pricing breaks at nothing. A price of zero is monotone on every name measured, so
    /// a test that only checked for dips would be satisfied by throwing away the behaviour that
    /// keeps <c>Iron</c> whole. So this also checks the priced answer is still in use where the
    /// floor does not bind.</para>
    /// </summary>
    [GameTest]
    public static void ZoomingInNeverShrinksALabel(Region r)
    {
        var names = new[] { "Iron", "1727", "Water", "Carbon Dioxide", "Sulfuric Acid",
                            "Aqueous Manganese(II) Sulfate" };

        foreach (var name in names)
        {
            LabelCache.Clear();
            var heights = new List<int>();

            for (var i = 0; i < Magnifier.Ladder.Length; i++)
            {
                var cell = Magnifier.Ladder[i];
                var below = Magnifier.Ladder[Math.Max(0, i - 1)];
                var fitted = LabelCache.ChooseMonotone(
                    "", name, new Vector2(cell, cell), new Vector2(below, below),
                    int.MaxValue, 5, Painter.SplitCost);
                heights.Add(fitted.Font.GlyphHeight * fitted.Scale);
            }

            for (var i = 1; i < heights.Count; i++)
                if (heights[i] < heights[i - 1])
                    throw new AssertionException(
                        $"'{name}' shrank from a glyph height of {heights[i - 1]} at cell " +
                        $"{Magnifier.Ladder[i - 1]} to {heights[i]} at cell {Magnifier.Ladder[i]}. " +
                        $"The series was [{string.Join(",", heights)}]. Zooming in must never make " +
                        "text smaller. See LabelCache.ChooseMonotone.");
        }

        // The premise: the price is still doing its job where the floor does not bind.
        LabelCache.Clear();
        var iron = LabelCache.ChooseMonotone("", "Iron", new Vector2(32f, 32f),
                                             new Vector2(24f, 24f), int.MaxValue, 5,
                                             Painter.SplitCost);
        if (iron.Text.Contains('\n'))
            throw new AssertionException(
                $"'Iron' came out as '{iron.Text.Replace("\n", "/")}' at 32 pixels. The floor has " +
                "swallowed the word-break price, so this test is only asserting that a " +
                "size-maximising layout is monotone, which it always was.");

        LabelCache.Clear();
    }

    /// <summary>
    /// The panel really goes through it, and a screen of identical filters really is mostly hits.
    ///
    /// <para>This is the scenario the cache was built for: filters come in banks, so a magnifier
    /// pointed at a sorting array is a hundred cells carrying a handful of distinct labels. A unit
    /// test of the dictionary proves nothing about whether the draw path uses it.</para>
    /// </summary>
    [GameTest(RequiresDisplay = true)]
    public static IEnumerator APanelOfTheSameFilterLaysOutOnce()
    {
        yield return Session.Enter("flat");
        Session.CloseAllWindows();

        var field = Simulation.CurrentState?.Field;
        if (field == null)
            throw new AssertionException("no simulation field inside a session");

        var centre = new Vector2I(field.Width / 2, field.Height / 2);
        Session.PauseSimulation();

        // One filter, repeated: the same annotation text on every cell in view.
        var filter = BaseMaterial.ToMatchFilterOffset(Materials.GetBaseMaterialId("Water"));
        const int reach = 40;
        for (var dy = -reach; dy <= reach; dy++)
        for (var dx = -reach; dx <= reach; dx++)
        {
            field.Set(centre.X + dx, centre.Y + dy, filter);
            Session.MarkDirty(centre.X + dx, centre.Y + dy);
        }

        yield return View.LookAt(centre);
        yield return Cursor.Hover(centre);

        try
        {
            Settings.RequireAlt = false;
            yield return Wait.Frames(2);        // let the first frame populate it

            LabelCache.Clear();
            yield return Wait.Frames(1);

            var hits = LabelCache.Hits;
            var misses = LabelCache.Misses;

            if (hits + misses == 0)
                throw new AssertionException(
                    "the panel drew a screen of filters and asked the cache nothing, so the draw " +
                    "path is not going through it -- " + PixelInspectorApi.DescribeState());

            // Every cell is the same filter at the same zoom. The few misses are the distinct
            // boxes a cell can have, which is one here plus whatever the rim contributes.
            if (misses > hits)
                throw new AssertionException(
                    $"a panel of one repeated filter took {misses} misses against {hits} hits. " +
                    "The key is carrying something that varies per cell.");
        }
        finally
        {
            Settings.Reset();
            LabelCache.Clear();
        }

        Session.ResumeSimulation();
        yield return Session.Leave();
    }
}
