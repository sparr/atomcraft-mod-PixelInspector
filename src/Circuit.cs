using Atomcraft;
using Godot;

namespace PixelInspector;

/// <summary>How many neighbours a circuit pixel reaches.</summary>
internal enum Fan
{
    /// <summary>Not part of the signal system, or a gate, which draws its own wires.</summary>
    None,

    /// <summary>Every neighbour it can drive. Wires, switches, oscillators, heating elements.</summary>
    All,

    /// <summary>
    /// Exactly one: the first neighbour with somewhere to be driven to, in the game's own scan
    /// order. Sensors, buttons and temperature sensors.
    /// </summary>
    First,
}

/// <summary>
/// Which neighbours a pixel is wired to, from the rules in <c>CircuitRules/</c>.
///
/// <para><b>The one rule everything reduces to.</b> <c>BaseMaterial.TurnOnOrOff</c> refuses a
/// target that is air or out of bounds, refuses it when both sides carry a <c>WireIndex</c> and
/// the two differ, and otherwise drives it if it has a <c>TurnsOnInto</c> to be driven to. So a
/// link exists where the target can be turned on and the channels do not conflict. A pixel with no
/// <c>WireIndex</c> at all -- a lightbulb, a heating element, a Mirror -- talks to every
/// channel.</para>
///
/// <para><b>Fan-out is a property of the sender's class, not its data.</b> Only wires, switches,
/// oscillators and heating elements reach all eight neighbours. Sensors, buttons and temperature
/// sensors drive exactly one, and which one is not a choice: they walk
/// <c>Utils.GetAdjacent</c>, which indexes a permutation table fixed at startup, and take the
/// first neighbour that can be driven. That order is down-left, up, up-right, up-left, right,
/// left, down-right, down -- and this asks the game for it rather than restating it, so it stays
/// right if it ever changes.</para>
///
/// <para><b>An unpowered wire still shows its bus</b>, and that is deliberate. A <c>Wire Off</c>
/// has no <c>Step</c>, so at this instant it drives nothing; but it has a <c>TurnsOnInto</c> and
/// will conduct the moment it is energised. What is drawn is the circuit, not the current tick --
/// a bus that only appeared while lit would be invisible exactly when a player is trying to work
/// out why it is not.</para>
///
/// <para>Gates and latches are <see cref="Fan.None"/> on purpose. They drive one fixed cell, and
/// their drawn outline already carries its legs and output stub; a second line would say it
/// twice.</para>
/// </summary>
internal static class Circuit
{
    private static Fan[]? _fan;

    // The three materials a tagged id stands for. A programmed pixel's id is the id of the
    // material it WATCHES with a quadrant bit set, so the id resolves to the wrong material
    // entirely -- Sensor (Iron) reads back as Iron. These are what it should resolve to.
    private static short _sensor, _match, _nonmatch;

    /// <summary>
    /// Classifies every material once, from the behaviour class the game registered for it.
    /// Called from the <c>Materials.Init</c> postfix, after every mod's materials exist.
    /// </summary>
    internal static void Build(int materialCount)
    {
        var fan = new Fan[materialCount];
        for (var id = 0; id < materialCount; id++)
        {
            var material = ((short)id).ToMaterial();
            if (material == null)
                continue;
            fan[id] = Classify(material.GetType().Name);
        }
        _fan = fan;
        _sensor = Materials.GetBaseMaterialId("Sensor");
        _match = Materials.GetBaseMaterialId("Match Filter");
        _nonmatch = Materials.GetBaseMaterialId("Nonmatch Filter");
    }

    /// <summary>
    /// What a pixel actually is, for an id that may carry a quadrant tag.
    ///
    /// <para>A programmed sensor or filter is stored as the id of the material it watches with a
    /// bit set, so reading it back through the ordinary lookup gives that material: a
    /// <c>Sensor (Iron)</c> comes back as Iron, is classified as inert, and draws no link at all
    /// even though a programmed sensor drives a cell exactly as a bare one does. Mapping the tag
    /// to the material the pixel really is fixes both what it sends and what it can be sent.</para>
    /// </summary>
    private static short Real(short id)
    {
        if (BaseMaterial.IsNormal(id))
            return id;
        if (BaseMaterial.IsSensorOffset(id))
            return _sensor;
        if (BaseMaterial.IsMatchFilterOffset(id))
            return _match;
        if (BaseMaterial.IsNonMatchFilterOffset(id))
            return _nonmatch;
        return id;
    }

    /// <summary>Forgets the table, so a reload rebuilds it.</summary>
    internal static void Clear() => _fan = null;

    /// <summary>Whether the table has been built.</summary>
    internal static bool Ready => _fan != null;

    /// <summary>
    /// The fan-out of a behaviour class.
    ///
    /// <para>By class rather than by name or by data: the data says what a pixel becomes, and only
    /// the class says how far it reaches. The heating and temperature families are registered one
    /// class per setting -- <c>HeatingElementMaterial_1000</c> and friends -- so those match on a
    /// prefix.</para>
    /// </summary>
    private static Fan Classify(string cls) => cls switch
    {
        // Conduct to all eight. Wire Off has no Step and drives nothing this tick, but it is part
        // of the bus; see the note on the class.
        "WireOnMaterial" or "WireOffMaterial" or "WireTurningOffMaterial" => Fan.All,
        "SwitchOnMaterial" or "SwitchOffMaterial" => Fan.All,
        "PulsarOnMaterial" or "PulsarOffMaterial" => Fan.All,
        "SensorMaterial" => Fan.First,
        "ButtonOnMaterial" or "ButtonOffMaterial" or "ButtonFadingMaterial" => Fan.First,
        _ when cls.StartsWith("HeatingElementMaterial", StringComparison.Ordinal) => Fan.All,
        _ when cls.StartsWith("TemperatureSensorMaterial", StringComparison.Ordinal) => Fan.First,
        _ => Fan.None,
    };

    /// <summary>The fan-out of a material, or <see cref="Fan.None"/> before the table exists.</summary>
    internal static Fan Of(short id)
    {
        var fan = _fan;
        if (fan == null)
            return Fan.None;
        var real = Real(id);
        return real >= 0 && real < fan.Length ? fan[real] : Fan.None;
    }

    /// <summary>
    /// Fills <paramref name="into"/> with the offsets of the neighbours this cell is wired to, and
    /// returns how many. Never more than eight.
    /// </summary>
    internal static int LinksAt(SimField field, int x, int y, Span<Vector2I> into)
    {
        var fan = Of(field.Get(x, y));
        if (fan == Fan.None)
            return 0;

        var sender = Real(field.Get(x, y)).ToMaterial();
        if (sender == null)
            return 0;

        Span<Vector2I> around = stackalloc Vector2I[8];
        Utils.GetAdjacent(x, y, around);

        var found = 0;
        for (var i = 0; i < 8; i++)
        {
            var at = around[i];
            if (!Drives(sender, field, at.X, at.Y))
                continue;

            into[found++] = new Vector2I(at.X - x, at.Y - y);
            if (fan == Fan.First)
                break;              // one cell only, and this is the one the game would pick
        }
        return found;
    }

    /// <summary>
    /// Whether <paramref name="sender"/> would drive the pixel at these coordinates, by the three
    /// tests in <c>TurnOnOrOff</c>.
    /// </summary>
    private static bool Drives(BaseMaterial sender, SimField field, int x, int y)
    {
        var raw = field.Get(x, y);
        if (raw == -1 || raw == -2)
            return false;                       // air, or outside the world

        var material = Real(raw).ToMaterial();
        if (material?.TurnsOnInto == null)
            return false;                       // nothing to be driven to

        // The channel test fires only when BOTH sides have a wire index. A device with none --
        // a lightbulb, a heating element, a Mirror -- talks to every colour.
        return sender.WireIndex is not { } from || material.WireIndex is not { } to || from == to;
    }
}
