using Atomcraft;
using Godot;
using HarmonyLib;
using PixelArt;

namespace PixelInspector;

/// <summary>
/// Moves the game's hover box out from under the cursor and puts it beside the magnifier, and
/// fades the cursor so it is not sitting on the thing it is pointing at.
///
/// <para><b>Nothing is drawing into the box.</b> <c>HoveredMaterialHint</c> is an ordinary Godot
/// Control with two child nodes, a <c>Label</c> and a <c>Background</c>, and a mod that adds a
/// line to it sets <c>Label.Text</c> rather than drawing anywhere. Children are positioned
/// relative to their parent, so moving the box moves everything in it, including lines another
/// mod appended, with no coordination at all.</para>
///
/// <para><b>Which is why this is a pass and not a patch.</b> <c>Gameplay.Process</c> calls
/// <c>HoveredMaterialHint.Process</c>, and Pixel Art's shared frame is a postfix on
/// <c>Gameplay.Process</c>. So by the time this runs, the box has positioned itself, the game has
/// filled it in, and every other mod's postfix has already appended and resized. No
/// <c>Priority.Last</c>, no <c>[HarmonyAfter]</c>, and no race with a mod that also claims to go
/// last.</para>
///
/// <para>The box is anchored by its <b>left</b> edge, which is what makes the above true rather
/// than merely convenient: anchoring the right edge would need the box's final width, and that is
/// only known after every contributor has had its say.</para>
/// </summary>
internal static class HudPlacement
{
    /// <summary>Screen pixels between the magnifier and the box.</summary>
    private const float Gap = 16f;

    /// <summary>
    /// Puts the box beside the panel. Called once a frame from the magnifier's pass, after the
    /// game and every other mod have finished with it.
    /// </summary>
    internal static void PlaceHoverBox(Rect2 panel)
    {
        var hint = Gameplay.Instance?.HoveredMaterialHint;
        if (hint == null || !hint.Visible)
            return;

        // To the right of the panel by default, and to the left when the box would run off the
        // screen. The same flip the game does against the cursor, done against the panel.
        //
        // Measured from the Background child rather than from the hint itself. The hint is a
        // bare Control whose own rect bears no relation to what is drawn: it came back about 500
        // pixels wide for a three line tooltip, which flipped the box to the far left of the
        // screen and put it through the middle of the performance HUD. Background is the nine
        // patch that is actually the box, it is the node the game itself resizes to fit the
        // text, and it is reachable by name, so no reflection and nothing private.
        var viewport = ViewGeometry.ViewportSize;
        var box = hint.GetNodeOrNull<Control>("Background");
        var width = box?.GetRect().Size.X ?? 0f;
        var x = panel.End.X + Gap;
        if (x + width > viewport.X)
            x = panel.Position.X - Gap - width;

        hint.GlobalPosition = new Vector2(Mathf.Round(x), Mathf.Round(panel.Position.Y));
    }
}

/// <summary>
/// Hides the mouse cursor where it would be drawn in front of the magnifier.
///
/// <para><b>A cursor cannot be put behind anything.</b> It is composited by the operating system
/// after the frame is finished -- <c>Cursors.Process</c> hands a texture to
/// <c>Input.SetCustomMouseCursor</c> and the compositor draws it over whatever the game produced
/// -- so there is no layer to move it to and no z-order to lose. Not drawing it is the only way it
/// gets out from in front of the panel.</para>
///
/// <para><b>Which is affordable because the panel says everything the cursor did.</b> The ring
/// marks the pixel a click will act on, and the single lit pixel inside that ring marks where in
/// it the pointer is sitting, to one screen pixel. Between them a player knows more about where
/// they are aiming than the cursor was telling them, which is why this went from fading the cursor
/// to removing it.</para>
///
/// <para>Only where it overlaps, so a player pointing at the edge of the screen still has a
/// cursor. That is the literal reading of "behind the panel": present where the panel is not,
/// absent where the panel would be over it.</para>
///
/// <para>Restored the moment either condition stops holding, and by <c>ResetState</c> as well --
/// <c>Input.MouseMode</c> is process-wide, so a mod that switches off or faults while holding it
/// hidden would leave the player without a cursor in the menus.</para>
/// </summary>
[HarmonyPatch(typeof(Cursors), nameof(Cursors.Process))]
internal static class CursorPatch
{
    private static bool _hidden;

    /// <summary>What the patch decided on the last frame, for a test to assert on.</summary>
    internal static bool Hidden => _hidden;

    /// <summary>
    /// Whether the pointer is inside the panel right now.
    ///
    /// <para>Both are in the render target's coordinates rather than the window's, which is the
    /// only reason this comparison is meaningful: the panel is laid out against
    /// <c>ViewGeometry.ViewportSize</c> and the viewport reports the mouse in the same space, so
    /// neither has to know what the frame is being rescaled by on its way to the screen.</para>
    /// </summary>
    private static bool OverPanel()
    {
        var viewport = Game.CanvasLayer?.GetViewport();
        return viewport != null && WouldHide(viewport.GetMousePosition());
    }

    /// <summary>
    /// The whole decision, as a function of where the pointer is. Separated from reading the
    /// pointer so a test can ask it about a position rather than having to put a real mouse there,
    /// which needs a display and a settled warp.
    /// </summary>
    internal static bool WouldHide(Vector2 pointer) =>
        Settings.QuietCursor && ZoomKeys.Claiming && Magnifier.PanelRect().HasPoint(pointer);

    /// <summary>Hides it without waiting for a frame, so a test can assert the reset undoes it.</summary>
    internal static void HideForTest()
    {
        _hidden = true;
        Input.MouseMode = Input.MouseModeEnum.Hidden;
    }

    /// <summary>Gives the cursor back. Called from the mod's state reset.</summary>
    internal static void Restore()
    {
        if (!_hidden)
            return;
        _hidden = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    [HarmonyPostfix]
    internal static void AfterProcess()
    {
        // The click guard rides along here rather than in the magnifier's draw pass, and the
        // reason is the whole hazard in miniature: that pass does not run when the panel is down,
        // so a guard raised on the last frame Alt was held would never be told to come off, and
        // the interface would stay dead until something else reset the mod. This runs every frame
        // whatever the panel is doing.
        // Before the guard, not after: the guard asks whether the panel is up, and on the frame
        // a release flips that the two would otherwise disagree for one frame.
        StickyPanel.Sync();
        ClickGuard.Sync();

        var hide = OverPanel();
        if (hide == _hidden)
            return;                 // assigned only on a change: MouseMode is process-wide state

        _hidden = hide;
        Input.MouseMode = hide ? Input.MouseModeEnum.Hidden : Input.MouseModeEnum.Visible;
    }
}
