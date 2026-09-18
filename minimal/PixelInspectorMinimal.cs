// The whole idea of PixelInspector in one file.
//
// Hold Alt, and every programmed match filter within a few cells of the cursor is labelled with
// the material it is set to. That is the mod's core claim: the setting is in the world, the game
// draws none of it, and putting it on the pixel is a page of code.
//
// NOT BUILT, and deliberately outside src/ so the SDK's **/*.cs glob cannot sweep it in -- two
// [HarmonyPatch] classes on one method would apply the effect twice. Drop it into a project of
// its own to run it.
//
// It is also the last place in this project that draws for itself. The real mod's drawing moved
// to the PixelArt library, so everything below -- the canvas layer, the projection, the render
// hook, the guard -- is there rather than here. That makes this file worth more, not less: it is
// now the honest answer to "what is that library actually doing for me", in about ninety lines.
//
// WHAT THE REAL MOD ADDS, and why each piece exists rather than being scaffolding:
//
//   * The other 240 machines. This handles match filters; AnnotationRules also reads nonmatch
//     filters, sensors, the programmable filter's neighbours, gates, latches, thermostats,
//     oscillators, dispensers, phase filters, and the direction of every conveyor, pump, gun,
//     laser, balance and projectile. That is nearly all of the mod's source, and all of it is a
//     pure function from a material name, which is what makes it testable without a game.
//
//   * A library. The real mod takes a Canvas from PixelArt and registers one pass on it; the
//     per-frame hook, the canvas layer, the fault latch, the headless check and the release of
//     the server resources at exit are all that library's, shared with every other mod that
//     draws. What this file spends sixty lines on, the real mod spends one line on.
//
//   * A layout. The real mod decides, against the cell on the frame it draws, which of three
//     bitmap fonts to use, how many whole screen pixels each font pixel becomes, whether to put
//     the "=" above the name rather than beside it, whether to wrap the name across lines, and
//     how much of it to show at all -- so a zoomed-in cell shows the whole name and a zoomed-out
//     one shows as much as fits and marks the cut with an ellipsis. This draws one size, one
//     line, always.
//
//   * The game's own square, drop and cloud behind the phase filters, borrowed from
//     res://Art/material_swatch_*.png.
//
//   * A bitmap font. The engine's fallback font, used below, is a vector font rasterised at
//     whatever size it is asked for, and at one simulation cell per label that size is about 12
//     screen pixels -- where every glyph outline lands on fractional pixel boundaries and comes
//     out a grey smudge. PixelFont draws 3x5, 5x7 and 9x13 glyphs at whole scales from whole
//     positions through a nearest filter, so each font pixel is an exact block of screen pixels.
//     Run this file and then the real mod, zoomed in, and the difference is the whole reason
//     that file is 600 lines.
//
//   * A prebuilt table, so the per-cell cost is an array index rather than a string parse.
//
//   * A fault latch. Godot logs an exception from a per-frame hook on every frame with no
//     backpressure; one throwing hook made a 1.3 million line godot.log in ninety seconds. The
//     try/catch below is the shape of it, without the latch that stops the second report.
//
//   * Settings, a kill switch, and the direction nubs.
//
// It is otherwise honest: this really does work, and the projection, the canvas layer and the
// render hook are the same three ideas the real mod uses.

using Atomcraft;
using Godot;
using HarmonyLib;

namespace PixelInspectorMinimal;

public static class ModEntry
{
    public static void Initialize() => new Harmony("PixelInspectorMinimal").PatchAll();
}

[HarmonyPatch(typeof(Gameplay), nameof(Gameplay.Process))]
internal static class Draw
{
    private const int Radius = 6;
    private const float TileSize = 8f;   // world units per cell; see Utils.TileposToGlobal

    private static CanvasLayer? _layer;
    private static Rid _item;

    private static void Postfix()
    {
        try
        {
            if (!EnsureCanvas())
                return;
            RenderingServer.CanvasItemClear(_item);

            if (!Input.IsKeyPressed(Key.Alt) || !Game.InSession)
                return;

            var cam = Client.FollowCam;
            var field = Simulation.CurrentState?.Field;
            var viewport = Game.CanvasLayer?.GetViewport().GetVisibleRect().Size;
            if (cam == null || field == null || viewport == null || Game.World == null)
                return;

            var font = ThemeDB.Singleton.FallbackFont;
            var mouse = Game.World.GetGlobalMousePosition().GlobalToTileposI();
            var cell = TileSize * cam.Zoom.X;

            for (var y = mouse.Y - Radius; y <= mouse.Y + Radius; y++)
            for (var x = mouse.X - Radius; x <= mouse.X + Radius; x++)
            {
                // A programmed filter is not its own material: the game writes the TARGET's id
                // into the cell with a quadrant bit set, and then draws every one of them in the
                // same flat colour. This is the whole trick.
                var id = field.Get(x, y);
                if (!BaseMaterial.IsMatchFilterOffset(id))
                    continue;

                var target = Materials.GetBaseMaterial(BaseMaterial.BaseId(id));
                if (target == null)
                    continue;
                var label = target.Name;

                // The inverse of the game's own Utils.ScreenPositionToWorldPosition, so this
                // agrees with the mapping the game uses to decide which cell the mouse is over.
                var world = new Vector2(x * TileSize, y * TileSize);
                var screen = (world - cam.GlobalPosition) * cam.Zoom + viewport.Value * 0.5f;

                font.DrawString(_item, new Vector2(screen.X, screen.Y + cell),
                                "=" + label, HorizontalAlignment.Left,
                                width: -1f, fontSize: 12, modulate: Colors.White);
            }
        }
        catch (System.Exception e)
        {
            GD.PrintErr($"[PixelInspectorMinimal] {e}");
        }
    }

    private static bool EnsureCanvas()
    {
        if (DisplayServer.GetName() == "headless")
            return false;
        if (_layer != null && GodotObject.IsInstanceValid(_layer))
            return true;

        var root = Game.Instance?.GetTree()?.Root;
        if (root == null)
            return false;

        // A plain CanvasLayer plus a server canvas item, because a mod is compiled without
        // Godot's source generators and a CanvasItem subclass of ours would never have its
        // _Draw called.
        _layer = new CanvasLayer { Name = "PixelInspectorMinimal", Layer = 126 };
        root.AddChild(_layer);
        _item = RenderingServer.CanvasItemCreate();
        RenderingServer.CanvasItemSetParent(_item, _layer.GetCanvas());
        return true;
    }
}
