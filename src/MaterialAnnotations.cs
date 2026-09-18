using PixelArt;
using Atomcraft;

namespace PixelInspector;

/// <summary>
/// The annotation for every material, worked out once, and the handful that can only be
/// answered per cell.
///
/// <para><b>Built once, read per pixel.</b> Almost every annotation is a property of the
/// material and not of the cell, so the whole vocabulary collapses into one array indexed by
/// material id and a per-pixel lookup costs one bounds check and one array read. That is what
/// makes a pass over the cells around the cursor affordable on every frame Alt is held.</para>
///
/// <para><b>What cannot go in the array.</b> A programmed match filter, nonmatch filter or
/// sensor is not stored as its own material: the game writes the <i>target's</i> id with a
/// quadrant bit set (<c>BaseMaterial.ToMatchFilterOffset</c> and friends), so a filter set to
/// Water and one set to Sand are different ids, both outside the array, and both <i>drawn</i>
/// in the same flat filter colour. The game does name the target, in the hover box, for the one
/// pixel under the cursor (<c>HoveredMaterialHint</c> appends it for any non-normal id); what
/// this mod adds is reading a whole bank of them at once without moving the cursor over each.
/// The programmable filter is the other case, and the one the hover box does <i>not</i> cover:
/// it takes its target from whichever material sits immediately left or right of it in the
/// world, which is live state and changes without the cell changing.</para>
/// </summary>
public static class MaterialAnnotations
{
    private static Annotation[] _table = Array.Empty<Annotation>();

    /// <summary>
    /// The programmable filter's id, resolved by name because the game's own constant for it is
    /// dead.
    ///
    /// <para><c>Materials.PROGRAMMABLE_FILTER</c> is declared and never assigned -- every other
    /// named id in that class is set from <c>GetBaseMaterialId</c> during init, and this one was
    /// missed -- so it holds 0, which is a perfectly good id belonging to an unrelated material.
    /// Using it would have annotated whichever material happens to sort first as a programmable
    /// filter and never annotated the real one, with no error anywhere. Found by
    /// <c>CellTests.AProgrammableFilterNamesItsNeighbours</c>; tracked by
    /// <c>RetirementTests.TheGamesProgrammableFilterConstantIsStillDead</c>.</para>
    ///
    /// <para>-1 until the table is built, and -1 if the material is ever renamed, which is a
    /// value no cell can hold.</para>
    /// </summary>
    private static short _programmableFilter = -1;

    /// <summary>The name the programmable filter is registered under; see <c>Strings.PROGRAMMABLE_FILTER</c>.</summary>
    private const string ProgrammableFilterName = "Programmable Filter";

    /// <summary>Whether the table has been built, which happens at the end of <c>Materials.Init</c>.</summary>
    public static bool Ready => _table.Length > 0;

    /// <summary>How many of the game's materials this mod has something to say about.</summary>
    public static int Annotated { get; private set; }

    /// <summary>
    /// Works out the annotation for every registered material.
    ///
    /// <para>Run from a postfix on <c>Materials.Init</c>, which is after the shipped
    /// <c>AllMaterials.json</c>, after <c>user://Materials/</c>, and after the mod loader has
    /// injected every mod's materials -- so another mod's machines get whatever the rules make
    /// of their names, on the same terms as the game's own.</para>
    /// </summary>
    public static void Build()
    {
        var count = Materials.Count;
        var table = new Annotation[Math.Max(0, (int)count)];
        var annotated = 0;

        for (short id = 0; id < count; id++)
        {
            var material = Materials.GetBaseMaterial(id);
            if (material == null)
                continue;
            // TurnsOnInto is "this can be switched on", which is the same statement as "this
            // is currently off". It is the one on/off signal the game gives consistently; see
            // Annotation.Off for why IsOn is not usable and AnnotationRules.For for the two
            // families whose names do not say it.
            var annotation = AnnotationRules.For(material.Name, material.TurnsOnInto.HasValue);
            table[id] = annotation;
            if (annotation.Any)
                annotated++;
        }

        _table = table;
        Annotated = annotated;

        _programmableFilter = Materials.GetBaseMaterialId(ProgrammableFilterName);
        if (_programmableFilter < 0)
            Log.Warn($"the game no longer has a material named '{ProgrammableFilterName}', so " +
                     "programmable filters will not be labelled with what sets them. Everything " +
                     "else is unaffected.");
    }

    /// <summary>Drops the table, so a new session rebuilds rather than indexing a stale one.</summary>
    public static void Forget()
    {
        _table = Array.Empty<Annotation>();
        Annotated = 0;
        _programmableFilter = -1;
    }

    /// <summary>
    /// What to draw on the cell at <paramref name="x"/>, <paramref name="y"/>.
    ///
    /// <para>Takes the field rather than just the id because two of the answers depend on
    /// neighbours. Out of bounds, empty, and unannotated cells all come back as
    /// <see cref="Annotation.None"/>.</para>
    /// </summary>
    public static Annotation At(SimField field, int x, int y)
    {
        var id = field.Get(x, y);
        if (id == -1 || id == -2)
            return Annotation.None;

        // A programmed filter or sensor: the quadrant bits say which of the three it is and
        // the low bits are the target's own material id.
        if (!BaseMaterial.IsNormal(id))
        {
            var target = Label(BaseMaterial.BaseId(id));
            if (BaseMaterial.IsMatchFilterOffset(id))
                return new Annotation(Kind.Filter, icon: MarkIcon.Check, body: target);
            if (BaseMaterial.IsNonMatchFilterOffset(id))
                return new Annotation(Kind.Filter, icon: MarkIcon.Cross, body: target);
            if (BaseMaterial.IsSensorOffset(id))
                return new Annotation(Kind.Filter, mark: AnnotationRules.Sense, body: target);
            return Annotation.None;
        }

        // The programmable filter passes whatever matches either of its horizontal neighbours,
        // and moves it vertically. Nothing about the cell says which materials those are, so
        // they are read here, on the frame the annotation is drawn.
        if (id == _programmableFilter)
            return new Annotation(Kind.Filter, icon: MarkIcon.Check,
                                  body: ProgrammedBy(field, x, y));

        return id >= 0 && id < _table.Length ? _table[id] : Annotation.None;
    }

    /// <summary>
    /// The two materials a programmable filter is currently set by, as one label: one name,
    /// two joined by a slash, or nothing at all when both sides are empty and the filter
    /// therefore passes nothing.
    /// </summary>
    private static string ProgrammedBy(SimField field, int x, int y)
    {
        var left = Label(field.Get(x - 1, y));
        var right = Label(field.Get(x + 1, y));

        if (left.Length == 0)
            return right;
        if (right.Length == 0 || right == left)
            return left;
        return left + "/" + right;
    }

    /// <summary>
    /// How a filter's or sensor's target is written: its name, whole.
    ///
    /// <para><b>Nothing is cut here.</b> How much of the name fits depends on how far the view is
    /// zoomed in, which is not known until the frame is drawn, so the whole name is carried and
    /// <see cref="LabelLayout"/> wraps or cuts it against the cell it is about to occupy. Cutting
    /// at this point instead would fix a character budget at startup and throw away, on every
    /// frame, room the player had zoomed in to create.</para>
    ///
    /// <para>The material's <c>Formula</c> was used here first, and it is a worse answer than it
    /// looks. It is compact -- 54% of the shipped formulas are two characters -- but only 1041 of
    /// 1915 materials have one at all, so more than a third of targets fell through to the name
    /// anyway and the label's meaning depended on which. And a formula is not what a player
    /// selected the filter with: the material-select window, the guide and the hover box all say
    /// "Water", so a pixel reading <c>=H2O</c> asks them to translate.</para>
    /// </summary>
    private static string Label(short id)
    {
        if (id < 0)
            return "";
        var material = Materials.GetBaseMaterial(id);
        return material?.Name ?? "";
    }
}
