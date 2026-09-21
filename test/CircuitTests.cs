using System.Collections;
using Atomcraft;
using Atomcraft.TestHarness;
using Godot;

using Session = Atomcraft.TestHarness.Session;

namespace PixelInspector.Test;

/// <summary>
/// The circuit links: which neighbours a pixel is wired to, and the lines drawn for them.
/// </summary>
public static class CircuitTests
{
    /// <summary>
    /// A wire in a run links to the wires either side of it, and an isolated wire links to nothing.
    /// </summary>
    [GameTest]
    public static void AWireLinksAlongItsRun(Region r)
    {
        if (!Circuit.Ready)
            throw new AssertionException("the circuit table was never built; see LifecyclePatches");

        var field = Simulation.CurrentState!.Field;
        r.Set(2, 2, "Copper Wire Off");
        r.Set(3, 2, "Copper Wire Off");
        r.Set(4, 2, "Copper Wire Off");
        r.Set(8, 8, "Copper Wire Off");          // on its own

        Span<Vector2I> links = stackalloc Vector2I[8];

        var middle = Circuit.LinksAt(field, r.OriginX + 3, r.OriginY + 2, links);
        if (middle != 2)
            throw new AssertionException(
                $"a wire between two wires links to {middle} neighbours, expected 2. An unpowered " +
                "wire still has a bus: Wire Off carries TurnsOnInto and conducts the moment it is " +
                "energised. See Circuit.");

        var alone = Circuit.LinksAt(field, r.OriginX + 8, r.OriginY + 8, links);
        if (alone != 0)
            throw new AssertionException($"a wire with no neighbours links to {alone}");
    }

    /// <summary>
    /// Wire colours do not mix, but a device with no channel talks to every colour.
    ///
    /// <para>The channel test in <c>TurnOnOrOff</c> only fires when BOTH sides carry a
    /// <c>WireIndex</c>, which is what makes a Mirror a universal bridge and a lightbulb drivable
    /// from any bus.</para>
    /// </summary>
    [GameTest]
    public static void ColoursDoNotMixButAChannellessDeviceTalksToAll(Region r)
    {
        var field = Simulation.CurrentState!.Field;
        r.Set(2, 2, "Copper Wire Off");
        r.Set(3, 2, "Zinc Wire Off");            // a different channel
        r.Set(2, 3, "Mirror Off");               // no channel at all

        Span<Vector2I> links = stackalloc Vector2I[8];
        var n = Circuit.LinksAt(field, r.OriginX + 2, r.OriginY + 2, links);

        var toZinc = false;
        var toMirror = false;
        for (var i = 0; i < n; i++)
        {
            if (links[i] == new Vector2I(1, 0)) toZinc = true;
            if (links[i] == new Vector2I(0, 1)) toMirror = true;
        }

        if (toZinc)
            throw new AssertionException(
                "a copper wire links to a zinc one. Both carry a WireIndex and they differ, so " +
                "TurnOnOrOff refuses; drawing a line there claims a connection that cannot pass " +
                "a signal.");
        if (!toMirror)
            throw new AssertionException(
                "a copper wire does not link to a Mirror. A Mirror carries no WireIndex, so the " +
                "channel test never fires and every colour drives it -- it is the one intended " +
                "cross-channel bridge.");
    }

    /// <summary>
    /// A sensor drives exactly one neighbour, and it is the one the game's own scan order picks.
    ///
    /// <para>That order comes from a permutation table fixed at startup, and it runs down-left
    /// first. Surrounding a sensor with identical wire is what makes the choice observable: every
    /// neighbour is equally drivable, so only the order decides.</para>
    /// </summary>
    [GameTest]
    public static void ASensorDrivesOneNeighbourAndTheOrderDecidesWhich(Region r)
    {
        var field = Simulation.CurrentState!.Field;
        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
            r.Set(4 + dx, 4 + dy, "Copper Wire Off");
        r.Set(4, 4, "Sensor");

        Span<Vector2I> links = stackalloc Vector2I[8];
        var n = Circuit.LinksAt(field, r.OriginX + 4, r.OriginY + 4, links);

        if (n != 1)
            throw new AssertionException(
                $"a sensor ringed by wire links to {n} neighbours, expected 1. Sensors drive the " +
                "first drivable neighbour and stop; only wires, switches, oscillators and heating " +
                "elements reach all eight.");

        // Down-left, with +y pointing down.
        if (links[0] != new Vector2I(-1, 1))
            throw new AssertionException(
                $"a sensor drives {links[0]}, expected (-1, 1). The scan order is read from the " +
                "game's own Utils.GetAdjacent rather than restated here, so this failing means " +
                "the permutation changed and the line now points at the wrong cell.");
    }

    /// <summary>
    /// A programmed sensor is wired like a bare one.
    ///
    /// <para><b>The trap this covers.</b> A programmed pixel stores the id of the material it
    /// WATCHES with a quadrant bit set, so the ordinary lookup resolves it to that material: a
    /// <c>Sensor (Iron)</c> reads back as Iron, classifies as inert, and draws nothing -- while
    /// in the game it drives a neighbour exactly as a bare sensor does. The same mis-resolution
    /// applies when such a pixel is the target rather than the sender.</para>
    /// </summary>
    [GameTest]
    public static void AProgrammedSensorIsWiredLikeABareOne(Region r)
    {
        var field = Simulation.CurrentState!.Field;
        var watching = BaseMaterial.ToSensorOffset(Materials.GetBaseMaterialId("Iron"));

        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
            r.Set(4 + dx, 4 + dy, "Copper Wire Off");
        r.SetRaw(4, 4, watching);

        if (Circuit.Of(watching) != Fan.First)
            throw new AssertionException(
                $"a programmed sensor classifies as {Circuit.Of(watching)}. Its id carries a " +
                "quadrant tag and resolves to the material it watches unless that is undone.");

        Span<Vector2I> links = stackalloc Vector2I[8];
        var n = Circuit.LinksAt(field, r.OriginX + 4, r.OriginY + 4, links);
        if (n != 1)
            throw new AssertionException(
                $"a programmed sensor ringed by wire links to {n} neighbours, expected 1");
    }

    /// <summary>
    /// A gate draws no link line, because its own outline already carries its legs.
    /// </summary>
    [GameTest]
    public static void AGateDrawsNoSeparateLink(Region r)
    {
        var field = Simulation.CurrentState!.Field;
        r.Set(2, 2, "And Gate (Up) (On)");
        r.Set(2, 3, "Copper Wire Off");

        Span<Vector2I> links = stackalloc Vector2I[8];
        if (Circuit.LinksAt(field, r.OriginX + 2, r.OriginY + 2, links) != 0)
            throw new AssertionException(
                "a gate reports circuit links. Its drawn outline already has legs and an output " +
                "stub; a line as well would say it twice.");
    }

    /// <summary>
    /// The whole vocabulary in one scene, photographed.
    ///
    /// <para>Every kind of circuit pixel the mod draws a link for, plus the cases where it must
    /// NOT draw one. The colour pairs are deliberately <b>adjacent</b>: two buses a few cells
    /// apart prove nothing, because nothing was ever going to join them. Touching them is the
    /// only arrangement where an absent line is evidence.</para>
    /// </summary>
    [GameTest(RequiresDisplay = true)]
    public static IEnumerator TheWholeVocabularyIsPhotographed()
    {
        yield return Session.Enter("flat");
        Session.CloseAllWindows();

        var field = Simulation.CurrentState!.Field;
        var c = new Vector2I(field.Width / 2, field.Height / 2);
        Session.PauseSimulation();

        // The scene is laid out around its own origin, nudged so it sits centred in the panel.
        const int shiftX = 2;

        void Put(int dx, int dy, string name)
        {
            dx += shiftX;
            var id = Materials.GetBaseMaterialId(name);
            if (id <= 0)
                throw new AssertionException($"no material named '{name}'");
            field.Set(c.X + dx, c.Y + dy, id);
            Session.MarkDirty(c.X + dx, c.Y + dy);
        }
        void Raw(int dx, int dy, short id)
        {
            dx += shiftX;
            field.Set(c.X + dx, c.Y + dy, id);
            Session.MarkDirty(c.X + dx, c.Y + dy);
        }
        void Run(int x0, int y, int len, string name)
        {
            for (var i = 0; i < len; i++) Put(x0 + i, y, name);
        }

        // Channels that TOUCH. Two buses a few cells apart prove nothing, because nothing was
        // ever going to join them; touching is the only arrangement where an absent line is
        // evidence. Copper against zinc, then aluminum, silver and heat resistant in a stack.
        Run(-12, -12, 6, "Copper Wire Off");
        Run(-12, -11, 6, "Zinc Wire Off");
        Run(-12, -9, 5, "Aluminum Wire Off");
        Run(-12, -8, 5, "Silver Wire Off");
        Run(-12, -7, 5, "Heat Resistant Wire Off");

        // A Mirror carries no channel, so it bridges the two it sits between.
        Run(-3, -12, 3, "Copper Wire Off");
        Put(0, -12, "Mirror Off");
        Run(1, -12, 3, "Gold Wire Off");

        // Corner contact is contact: the same colour links diagonally, two colours still do not.
        Put(-3, -9, "Copper Wire Off");
        Put(-2, -8, "Copper Wire Off");
        Put(2, -9, "Copper Wire Off");
        Put(3, -8, "Zinc Wire Off");

        // Sources that drive all eight neighbours, each feeding a bus.
        Put(-12, -5, "Switch On");             Run(-11, -5, 4, "Copper Wire Off");
        Put(-12, -3, "Switch Off");            Run(-11, -3, 4, "Copper Wire Off");
        Put(-12, -1, "2 Step Oscillator (On)"); Run(-11, -1, 4, "Copper Wire Off");

        // Sources that drive exactly one neighbour. Ringing each in wire shows which one the
        // game's own scan order picks.
        void Ring(int cx, int cy, string middle, short raw = 0)
        {
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
                Put(cx + dx, cy + dy, "Copper Wire Off");
            if (raw != 0) Raw(cx, cy, raw); else Put(cx, cy, middle);
        }
        Ring(-12, 2, "Sensor");
        Ring(-8, 2, "Heat Resistant Sensor");
        Ring(-4, 2, "", BaseMaterial.ToSensorOffset(Materials.GetBaseMaterialId("Iron")));
        Ring(0, 2, "Button Off");
        Ring(4, 2, "Temperature Sensor (1000)");

        // A heating element drives its bus; a cooling element only receives from one.
        Put(-12, 5, "Heating Element (1000)"); Run(-11, 5, 4, "Copper Wire Off");
        Run(-4, 5, 4, "Copper Wire Off");      Put(0, 5, "Cooling Element (Off)");

        // Receivers. Each is driven by the wire on its left and drives nothing itself, so exactly
        // one line appears, and it belongs to the wire.
        var receivers = new[]
        {
            "Door", "Electrode Off", "Cowbell (Off)", "Lightbulb (Off)",
            "Gun (Right) (Off)", "Igniter (Off)", "Copper Wall",
        };
        for (var i = 0; i < receivers.Length; i++)
        {
            Put(-12 + i * 3, 8, "Copper Wire Off");
            Put(-11 + i * 3, 8, receivers[i]);
        }

        // Gates and latches draw their own wiring in the icon, so they must add no line. The wire
        // beneath each one still reaches up toward it.
        Put(-12, 11, "And Gate (Up) (On)");      Put(-12, 12, "Copper Wire Off");
        Put(-9, 11, "Or Gate (Right) (On)");     Put(-9, 12, "Copper Wire Off");
        Put(-6, 11, "Xor Gate (Down) (Off)");    Put(-6, 12, "Copper Wire Off");
        Put(-3, 11, "Not Gate (Left) (On)");     Put(-3, 12, "Copper Wire Off");
        Put(0, 11, "Latch (Right) (On) (Rest)"); Put(0, 12, "Copper Wire Off");

        yield return View.LookAt(c);
        yield return View.SetZoom(1.5f);
        yield return Cursor.Hover(c);

        try
        {
            Settings.RequireAlt = false;
            while (Magnifier.Cell > 16) Magnifier.Step(-1);   // widest view, to fit the scene
            yield return Wait.Frames(3);

            var image = Game.CanvasLayer.GetViewport().GetTexture()?.GetImage();
            if (image == null)
                throw new AssertionException("the viewport handed back no image");
            Artifacts.WriteBytes("pixelinspector-circuit.png", image.SavePngToBuffer());
        }
        finally
        {
            Settings.Reset();
            Magnifier.Reset();
        }

        Session.ResumeSimulation();
        yield return Session.Leave();
    }

    /// <summary>
    /// The line's geometry matches the gate art: the same margin, the same wire width.
    /// </summary>
    [GameTest]
    public static void TheLinesMatchTheGateArt(Region r)
    {
        foreach (var cell in Magnifier.Ladder)
        {
            var border = CircuitLines.Border(cell);
            var expected = Mathf.Max(1f, Mathf.Floor(cell / 16f));
            if (!Mathf.IsEqualApprox(border, expected))
                throw new AssertionException(
                    $"at cell {cell} a link stops {border} from the edge but a gate is trimmed by " +
                    $"{expected}; the two have to agree or a wire and a gate leave different margins");

            var w = CircuitLines.Width(cell);
            if (w % 2 != 0)
                throw new AssertionException(
                    $"at cell {cell} the wire width is {w}, which is odd. A line down the centre " +
                    "of an even cell sits on a half coordinate, and only an even width straddles it.");
        }
    }

    /// <summary>
    /// The line contrasts with the pixel it is on, at every lightness, and keeps its hue.
    /// </summary>
    [GameTest]
    public static void TheTintContrastsAndKeepsTheHue(Region r)
    {
        foreach (var v in new[] { 0.0f, 0.2f, 0.45f, 0.55f, 0.8f, 1.0f })
        {
            var under = Color.FromHsv(0.58f, 0.7f, v);
            var line = CircuitLines.Tint(under);

            if (Mathf.Abs(line.V - under.V) < 0.3f)
                throw new AssertionException(
                    $"on a pixel of value {v:0.00} the line is {line.V:0.00}, too close to read. " +
                    "The value is pushed away from wherever the pixel is rather than inverted, " +
                    "because inverting a mid grey gives another mid grey.");

            if (Mathf.Abs(line.H - under.H) > 0.02f)
                throw new AssertionException(
                    $"the line shifted hue from {under.H:0.00} to {line.H:0.00}; it should move " +
                    "only lightness, so it reads as part of the pixel rather than laid over it");
        }
    }
}
