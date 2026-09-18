using Godot;
using PixelArt;
using Atomcraft;
using Atomcraft.TestHarness;

namespace PixelInspector.Test;

/// <summary>
/// What the mod costs, measured rather than asserted.
///
/// <para><b>Use <c>[GameBenchmark]</c>, not <c>[GameTest]</c>.</b> A benchmark is a measurement,
/// not a verdict: a timing threshold on a shared desktop is a flaky test, and the number worth
/// having is the one in <c>results.jsonl</c>, where a <c>benchmark</c> record carries it for
/// trending.</para>
///
/// <para>They only run when asked for: <c>./run-tests.sh -- --atomtest-bench</c>. Measure under
/// <c>--release</c>: a Debug assembly carries <c>DebuggableAttribute</c> with
/// <c>DisableOptimizations</c>, which turns the JIT off for it entirely.</para>
///
/// <para>What is measured is the per-cell lookup, because that is the only thing in this mod
/// that scales with anything. The default radius walks 169 cells per frame; at 60 frames a
/// second with Alt held that is about 10,000 lookups a second, and the numbers here say what
/// that costs.</para>
/// </summary>
public static class Benchmarks
{
    private const int Reps = 200_000;

    /// <summary>
    /// The three cases a cell can be, against each other: ordinary terrain (the overwhelming
    /// majority), a machine from the prebuilt table, and a programmed filter, which is the only
    /// one that does work beyond an array index.
    /// </summary>
    [GameBenchmark]
    public static void LookingUpACell(Region r)
    {
        var field = Simulation.CurrentState!.Field;

        r.Set(2, 2, "Granite");
        r.Set(4, 2, "Conveyor Left");
        r.SetRaw(6, 2, BaseMaterial.ToMatchFilterOffset(Materials.GetBaseMaterialId("Water")));

        var terrain = (r.OriginX + 2, r.OriginY + 2);
        var machine = (r.OriginX + 4, r.OriginY + 2);
        var filter = (r.OriginX + 6, r.OriginY + 2);

        // Measure rather than Compare: the harness's Compare takes exactly two variants and
        // reports their ratio, and the interesting comparison here is three-way.
        Bench.Measure("pixelinspector.lookup.terrain", Reps, () => Lookup(field, terrain));
        Bench.Measure("pixelinspector.lookup.machine", Reps, () => Lookup(field, machine));
        Bench.Measure("pixelinspector.lookup.filter", Reps, () => Lookup(field, filter));
    }

    /// <summary>
    /// Laying out a label, which is the dominant per-cell cost the panel has and the one that just
    /// grew.
    ///
    /// <para><b>Why this is worth a number.</b> The panel lays out every cell it draws, every
    /// frame -- 361 of them at the default zoom -- and asking for <c>Breaking.Anywhere</c> widens
    /// the candidate set the layout searches, because a one-word name gains arrangements it did
    /// not have. Against 16ms of frame budget, 361 layouts have about 44 microseconds each before
    /// the panel is the reason the game is slow.</para>
    ///
    /// <para>Compared against space-only wrapping rather than measured alone, since the question
    /// is not what it costs but what the extra candidates cost.</para>
    /// </summary>
    [GameBenchmark]
    public static void LayingOutALabel(Region r)
    {
        var box = new Vector2(Magnifier.DefaultCell, Magnifier.DefaultCell);

        Bench.Compare("pixelinspector.layout", LayoutReps,
            "words",
            () => LabelLayout.Choose("", "Sulfuric Acid", box,
                                     maxBody: 16, maxLines: 5, breaking: Breaking.Words),
            "anywhere",
            () => LabelLayout.Choose("", "Sulfuric Acid", box,
                                     maxBody: 16, maxLines: 5, breaking: Breaking.Anywhere));
    }

    /// <summary>Fewer than the lookup benchmarks: a layout is orders of magnitude dearer.</summary>
    private const int LayoutReps = 20000;

    /// <summary>
    /// The cache against the layout it stands in front of, which is the whole of its case.
    ///
    /// <para>The panel lays out every named machine it draws, every frame, and duplicates are the
    /// normal case rather than a corner: filters come in banks and conveyors in runs. If a hit is
    /// not far cheaper than a miss the cache is only a dictionary in the way.</para>
    /// </summary>
    [GameBenchmark]
    public static void CachingALayout(Region r)
    {
        var box = new Vector2(Magnifier.DefaultCell, Magnifier.DefaultCell);

        // Warmed, so the measured path is the hit rather than the first miss.
        LabelCache.Clear();
        LabelCache.Choose("", "Sulfuric Acid", box, int.MaxValue, 5, 0.8f);

        Bench.Compare("pixelinspector.layoutcache", LayoutReps,
            "uncached",
            () => LabelLayout.Choose("", "Sulfuric Acid", box,
                                     maxBody: int.MaxValue, maxLines: 5,
                                     breaking: Breaking.Anywhere),
            "cached",
            () => LabelCache.Choose("", "Sulfuric Acid", box, int.MaxValue, 5, 0.8f));

        LabelCache.Clear();
    }

    /// <summary>
    /// Drawing a gate, baked against walked.
    ///
    /// <para>The glyph is a thirteen by thirteen grid, so a walk touches 169 cells per gate per
    /// frame and rotates every coordinate as it goes -- and the answer depends only on the glyph,
    /// the facing and the zoom, none of which change between one gate and the next. A panel of
    /// gates was computing one answer over and over.</para>
    ///
    /// <para>Measured against the bake being thrown away each time, which is what walking cost.
    /// Merging runs of lit cells into single rectangles is part of the saving and is only
    /// affordable because it happens once.</para>
    /// </summary>
    [GameBenchmark]
    public static void DrawingAGate(Region r)
    {
        var canvas = Canvas.For("pixelinspector.bench");
        var cell = new Rect2(0f, 0f, Magnifier.DefaultCell, Magnifier.DefaultCell);

        GateArt.Clear();
        GateArt.Draw(canvas, cell, GateKind.And, Aim.Up, Colors.White, Colors.White);

        Bench.Compare("pixelinspector.gate", GateReps,
            "cold",
            () =>
            {
                GateArt.Clear();
                GateArt.Draw(canvas, cell, GateKind.And, Aim.Up, Colors.White, Colors.White);
            },
            "baked",
            () => GateArt.Draw(canvas, cell, GateKind.And, Aim.Up, Colors.White, Colors.White));

        GateArt.Clear();
    }

    /// <summary>A gate draw is cheap; enough reps to see past the noise.</summary>
    private const int GateReps = 20000;

    /// <summary>
    /// What warming the gate art costs, split into its two halves.
    ///
    /// <para>The question this answers is whether the bake needs to be moved off the first frame
    /// that draws a gate -- spread over several frames at load, say. That is only worth doing if
    /// the whole job is an appreciable slice of a 16ms frame, so the whole job is what gets
    /// measured: every gate, every zoom rung, all four facings, plus the decode of the packed
    /// blob on its own so the two are not confused.</para>
    /// </summary>
    [GameBenchmark]
    public static void WarmingTheGateArt(Region r)
    {
        GateArt.Clear();
        var sprites = GateArt.DecodeOnly();
        GateArt.Clear();

        Bench.Measure("pixelinspector.gate.decode", 2000, () =>
        {
            GateArt.Clear();
            GateArt.DecodeOnly();
        });

        var baked = 0;
        Bench.Measure("pixelinspector.gate.warmall", 200, () =>
        {
            GateArt.Clear();
            baked = GateArt.WarmAll();
        });

        Log.Info($"GATEWARM sprites={sprites} baked={baked}");
        GateArt.Clear();
    }

    /// <summary>
    /// Deciding what a name means, which happens once per material at startup and never again.
    ///
    /// <para>Measured anyway because it is the part of the mod most likely to grow: every new
    /// rule is another string comparison on a path that runs 1,900 times while the game is
    /// already showing a loading screen. Knowing the per-name cost is how to tell whether that
    /// stays true.</para>
    /// </summary>
    [GameBenchmark]
    public static void DecidingWhatANameMeans()
    {
        // "terrain" is the case that matters: it is what 1,900 of the ~1,915 names cost, and
        // it is the one that walks the whole rule list before answering "nothing".
        Bench.Compare("pixelinspector.rules", 50_000,
            "machine", () => AnnotationRules.For("Heating Element (Turning Off) (1500)"),
            "terrain", () => AnnotationRules.For("Granite"));
    }

    private static void Lookup(SimField field, (int X, int Y) at) =>
        PixelInspectorApi.At(field, at.X, at.Y);
}
