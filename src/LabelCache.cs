using Godot;
using PixelArt;

namespace PixelInspector;

/// <summary>
/// Remembers the layout chosen for a label, so a panel full of the same machine lays out once
/// rather than once a cell.
///
/// <para><b>Why this is worth having.</b> Duplicate machines are the normal case, not a corner:
/// filters come in banks, conveyors in runs, and a player looking at a sorting array through the
/// magnifier is looking at a few distinct labels repeated across a hundred cells. Laying out is by
/// far the dearest thing done per cell -- measured at 6.5 microseconds against about 0.04 for a
/// material lookup -- and asking for <c>Breaking.Anywhere</c> made it three and a half times
/// dearer again, because a one-word name gains arrangements it did not have.</para>
///
/// <para><b>The key is everything the answer depends on</b> and nothing else. The mark and the
/// body are the text; the box is the cell after the arrows have taken their bite, so two cells of
/// the same zoom with the same arrows share an entry and one with an extra arrow does not; and the
/// body ceiling is a setting a player can change while the game runs; the price of a word break
/// is a constant today and keyed anyway, since a stale layout after someone makes it a setting
/// would be a confusing bug for the sake of one field. The line budget is a constant and the
/// breaking mode is fixed here, so neither can vary.</para>
///
/// <para>The box is rounded to whole pixels for the key, which loses nothing: it is built from an
/// integer cell size and an arrow reach that is already rounded, so the same cell at the same zoom
/// produces the same integers exactly rather than approximately.</para>
/// </summary>
internal static class LabelCache
{
    private readonly record struct Key(string Mark, string Body, int Width, int Height,
                                      int MaxBody, float SplitCost);

    private static readonly Dictionary<Key, FittedText> Fits = new();

    /// <summary>
    /// How many entries before the whole thing is dropped and rebuilt.
    ///
    /// <para>Far above what a session reaches -- 244 annotated materials against eight zoom rungs
    /// and a handful of arrow shapes -- so this is a backstop against a case not thought of rather
    /// than an expected event. Dropping everything is right for a backstop: evicting the least
    /// used would need bookkeeping on every hit, which is the path this exists to keep cheap.</para>
    /// </summary>
    private const int Capacity = 4096;

    /// <summary>Hits and misses since the last reset, for a test and for the state readout.</summary>
    internal static int Hits { get; private set; }

    /// <summary>See <see cref="Hits"/>.</summary>
    internal static int Misses { get; private set; }

    /// <summary>How many layouts are remembered.</summary>
    internal static int Count => Fits.Count;

    /// <summary>
    /// The layout for a label, from the cache when it has been asked before.
    /// </summary>
    internal static FittedText Choose(string mark, string body, Vector2 box, int maxBody,
                                     int maxLines, float splitCost)
    {
        var key = new Key(mark, body,
                          Mathf.RoundToInt(box.X), Mathf.RoundToInt(box.Y), maxBody, splitCost);

        if (Fits.TryGetValue(key, out var remembered))
        {
            Hits++;
            return remembered;
        }

        Misses++;
        var fitted = LabelLayout.Choose(mark, body, box, maxBody: maxBody, maxLines: maxLines,
                                        breaking: Breaking.Anywhere, wordSplitCost: splitCost);

        if (Fits.Count >= Capacity)
            Fits.Clear();
        Fits[key] = fitted;
        return fitted;
    }

    /// <summary>
    /// The layout for a label, never smaller than the same label got one zoom rung down.
    ///
    /// <para><b>The problem this solves.</b> Pricing a word break buys word integrity by trading
    /// away glyph height -- that is the whole mechanism, and it is what keeps <c>Iron</c> whole
    /// instead of folded. But the trade is available at some cell sizes and not others, because a
    /// whole-word arrangement only becomes possible once the cell is wide enough to hold the
    /// longest word. So a name could get <i>smaller</i> as the player zoomed in: at 48 screen
    /// pixels <c>Aqueous Manganese(II) Sulfate</c> took a glyph height of 7 with one word broken,
    /// and at 60 a whole-word arrangement became available at 5 and won on breaks. Zooming in and
    /// watching the text shrink is wrong however good the arrangement is.</para>
    ///
    /// <para><b>Why it cannot be fixed by choosing a better price.</b> Measured across the ladder
    /// on six names: a price of zero never dips, because the comparison is then purely "bigger
    /// wins" and the set of arrangements that fit only grows as the cell does. Every price above
    /// zero dips somewhere -- the library's own default of 0.4 dips on this name too. The trade
    /// that keeps a short label whole is the same trade that lets a long one shrink, so no single
    /// number has both properties.</para>
    ///
    /// <para><b>So the fix is not a per-cell rule at all.</b> Monotonicity is a property of the
    /// <i>ladder</i>, and the ladder is this mod's, not the library's -- nothing inside a single
    /// call to <c>Choose</c> can know what the previous rung showed. Here it is one comparison: if
    /// the priced answer is smaller than what the rung below would have given, take the
    /// size-maximising answer instead, which is monotone by construction and never below that
    /// floor. Short labels keep their whole-word arrangement, because for them the priced answer
    /// is never the smaller one.</para>
    /// </summary>
    /// <param name="floorBox">
    /// The same label's box one zoom rung down. The layout there is what this must not go below.
    /// </param>
    internal static FittedText ChooseMonotone(string mark, string body, Vector2 box, Vector2 floorBox,
                                              int maxBody, int maxLines, float splitCost)
    {
        var priced = Choose(mark, body, box, maxBody, maxLines, splitCost);

        // Size-maximising, which is what "monotone" means here: with breaks priced at nothing the
        // comparison is bigger-wins, and a larger box can only widen the field of arrangements
        // that fit.
        var floor = Choose(mark, body, floorBox, maxBody, maxLines, 0f);
        if (Height(priced) >= Height(floor))
            return priced;

        return Choose(mark, body, box, maxBody, maxLines, 0f);
    }

    /// <summary>A rendering's glyph height in screen pixels, which is what "bigger" means.</summary>
    private static int Height(in FittedText fitted) => fitted.Font.GlyphHeight * fitted.Scale;

    /// <summary>
    /// Forgets everything. Called from the mod's state reset, and needed there rather than merely
    /// tidy: a test that changes a setting the key does not carry -- the text scale a label is
    /// drawn at, or a font the library swapped -- would otherwise read a stale answer.
    /// </summary>
    internal static void Clear()
    {
        Fits.Clear();
        Hits = 0;
        Misses = 0;
    }
}
