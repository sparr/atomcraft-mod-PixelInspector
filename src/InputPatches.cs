using Atomcraft;
using Godot;
using HarmonyLib;
using PixelArt;

namespace PixelInspector;

/// <summary>
/// The magnifier's zoom keys, and the camera zoom they have to be taken away from.
///
/// <para><b>Alt plus <c>=</c> and <c>-</c>, which are already spoken for.</b> The game binds
/// <c>KB_ZoomIncrease</c> to <c>=</c> and <c>KB_ZoomDecrease</c> to <c>-</c>, and
/// <c>InputManager</c> tests them with <c>Input.IsActionPressed</c>, which does not look at
/// modifiers. So holding Alt does not stop the camera from zooming, and adding a modifier to a
/// binding of our own would not either: the game's binding still matches. The only way to have
/// those keys is to take them.</para>
///
/// <para>Reusing the game's own actions rather than adding bindings is deliberate. A player who
/// has rebound zoom to something else gets the magnifier on their keys too, with nothing to
/// configure and nothing to collide.</para>
/// </summary>
internal static class ZoomKeys
{
    private const string Increase = "KB_ZoomIncrease";
    private const string Decrease = "KB_ZoomDecrease";

    /// <summary>
    /// Whether the magnifier is claiming the zoom keys this frame.
    ///
    /// <para>Only while it is actually on screen, so a player who never holds Alt keeps the
    /// camera zoom they have always had, and switching the mod off hands the keys straight
    /// back.</para>
    /// </summary>
    internal static bool Claiming => PixelInspectorApi.Enabled && Painter.Showing;

    /// <summary>
    /// Steps the magnifier when either key is newly pressed. Called once a frame from the
    /// magnifier's own canvas pass.
    ///
    /// <para><c>IsActionJustPressed</c> rather than <c>IsActionPressed</c>: the camera zooms
    /// continuously while its key is held, which is right for a camera and wrong for a ladder of
    /// eight rungs. One press, one rung.</para>
    /// </summary>
    internal static void Poll()
    {
        if (!Claiming)
            return;
        if (InputMap.HasAction(Increase) && Input.IsActionJustPressed(Increase))
            Magnifier.Step(+1);
        if (InputMap.HasAction(Decrease) && Input.IsActionJustPressed(Decrease))
            Magnifier.Step(-1);
    }
}

/// <summary>
/// Stops the camera zooming while the magnifier has the zoom keys.
///
/// <para><b>A prefix returning false skips the original</b>, which is the whole mechanism. The
/// target is <c>FollowCam.IncreaseZoom</c> and <c>DecreaseZoom</c> rather than
/// <c>InputManager.Process</c>, which does all of the game's input in one method and would take
/// everything with it.</para>
///
/// <para><b>The hazard worth knowing:</b> Harmony still runs postfixes when a prefix has skipped
/// the original, so a mod whose postfix assumes the original ran can misbehave. Zoooom patches
/// exactly these two methods with a paired prefix and postfix that bias its zoom target down and
/// back up; it is safe in either ordering, because the pair is balanced and guarded on its own
/// flag. That was checked by reading it rather than assumed, and it is the kind of thing to
/// re-check if another mod starts patching these.</para>
///
/// <para>It suppresses slightly more than the two keys: any call to either method while the
/// magnifier is up, which also covers the game's <c>Z</c>-held fast zoom. Matching the game's
/// own action checks exactly would mean duplicating them, for a case nobody reaches.</para>
/// </summary>
[HarmonyPatch]
internal static class CameraZoomSuppressionPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(FollowCam), nameof(FollowCam.IncreaseZoom))]
    internal static bool BeforeIncrease() => !ZoomKeys.Claiming;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(FollowCam), nameof(FollowCam.DecreaseZoom))]
    internal static bool BeforeDecrease() => !ZoomKeys.Claiming;
}
