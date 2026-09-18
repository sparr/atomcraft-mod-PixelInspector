using Atomcraft;
using Godot;

namespace PixelInspector;

/// <summary>
/// Swallows a click made while the panel is up and the pointer is over interface that would eat
/// it, so the click does nothing at all rather than something unintended.
///
/// <para><b>What it is for.</b> The magnifier sits in the middle of the screen, in the band between
/// the health bar and the toolbar, and a player reading it is looking at the panel rather than at
/// what is physically beneath the pointer. The panel already warns -- the marker around the centre
/// pixel turns thick and red when a click would not reach the world, see <see cref="WorldInput"/>
/// -- but a warning does not stop the click landing on whatever toolbar slot or inventory button
/// is under the cursor. Reading a machine and changing your tool by accident is a poor trade.</para>
///
/// <para><b>How.</b> A full-screen <c>Control</c> with <c>MouseFilter.Stop</c> on a canvas layer
/// above the game's interface. Godot picks GUI input from the topmost layer down, so while that
/// control is live it takes the click and consumes it, and nothing beneath ever sees it. The
/// control carries no script and overrides nothing, which matters here: a mod is compiled without
/// Godot's source generators, so a subclass of ours would never have its virtuals called. Setting
/// a property on a plain instance needs none of that.</para>
///
/// <para><b>It cannot latch itself on.</b> The obvious hazard with a full-screen control is a
/// feedback loop -- the guard makes something hovered, that counts as blocked, so the guard stays
/// on over the world as well. It cannot happen: <c>HoverSystem</c> tracks only controls that call
/// <c>SetHoveredControl</c> on themselves, and <c>Gameplay.MouseIsOverHUD</c> is computed from the
/// HUD's own rectangles. Neither can see a node of ours.</para>
///
/// <para><b>One frame of lag, in the safe direction.</b> The guard is set from the magnifier's draw
/// pass, which runs after the game has processed input, so it takes effect on the next frame.
/// Moving off the interface and clicking within the same frame could still be swallowed; a click
/// getting through on the frame the pointer arrives over the toolbar is the same window the other
/// way. Both are a sixtieth of a second, on a guard whose whole job is a click the player did not
/// mean to make.</para>
///
/// <para><b>Off by every route out.</b> A control that swallows clicks and is left on is a game
/// with an unusable interface, so it comes off when the panel is down, when the setting is off,
/// when the mod is switched off, and from the mod's state reset.</para>
/// </summary>
internal static class ClickGuard
{
    /// <summary>
    /// Above Pixel Art's canvases, which are at 126 and 128, and far above the game's interface.
    /// Input picking runs from the top layer down, so this has to be the top of it.
    /// </summary>
    private const int Layer = 400;

    private static CanvasLayer? _layer;
    private static Control? _blocker;

    /// <summary>Whether clicks are being swallowed right now. For the state readout and a test.</summary>
    internal static bool Active { get; private set; }

    /// <summary>
    /// Whether the guard should be on, given the settings and what is under the pointer. Separate
    /// from the node handling so a test can ask it without a scene tree.
    /// </summary>
    internal static bool Wanted =>
        Settings.BlockClicksOverUI && PixelInspectorApi.Enabled && Painter.Showing
        && WorldInput.Blocked;

    /// <summary>
    /// Brings the guard into line with <see cref="Wanted"/>. Called once a frame from the
    /// magnifier's pass, which is what guarantees it is reconsidered every frame rather than left
    /// wherever it happened to be.
    /// </summary>
    internal static void Sync()
    {
        var want = Wanted;

        if (!want)
        {
            if (Active)
                Clear();
            return;
        }

        if (Active && _blocker != null && GodotObject.IsInstanceValid(_blocker))
            return;

        if (!Ensure())
            return;

        _blocker!.MouseFilter = Control.MouseFilterEnum.Stop;
        _blocker.Visible = true;
        Active = true;
    }

    /// <summary>
    /// Stops swallowing clicks. Safe when nothing was ever built, and called from the mod's state
    /// reset as well as from <see cref="Sync"/> -- a fault between raising the guard and lowering
    /// it would otherwise leave the interface dead with nothing to suggest why.
    /// </summary>
    internal static void Clear()
    {
        Active = false;
        if (_blocker == null || !GodotObject.IsInstanceValid(_blocker))
            return;
        _blocker.MouseFilter = Control.MouseFilterEnum.Ignore;
        _blocker.Visible = false;
    }

    /// <summary>
    /// Builds the layer and the control, once. False when there is no scene tree to build in,
    /// which is every headless frame.
    /// </summary>
    private static bool Ensure()
    {
        if (_blocker != null && GodotObject.IsInstanceValid(_blocker))
            return true;

        var root = Game.Instance?.GetTree()?.Root;
        if (root == null)
            return false;

        _layer = new CanvasLayer { Name = "PixelInspector.ClickGuard", Layer = Layer };
        _blocker = new Control
        {
            Name = "Blocker",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        // Full rect, so it covers whatever size the window is and follows a resize.
        _blocker.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(_blocker);
        root.AddChild(_layer);
        return true;
    }

    /// <summary>Drops the nodes, so a test can rebuild rather than inherit one.</summary>
    internal static void Forget()
    {
        Clear();
        if (_layer != null && GodotObject.IsInstanceValid(_layer))
            _layer.QueueFree();
        _layer = null;
        _blocker = null;
    }
}
