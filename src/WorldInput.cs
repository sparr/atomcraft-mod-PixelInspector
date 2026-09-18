using Atomcraft;

namespace PixelInspector;

/// <summary>
/// Whether a click would reach the world right now, or be eaten by the interface.
///
/// <para><b>Why the panel has to say.</b> The magnifier draws a magnified view of the world under
/// the cursor and goes on drawing it wherever the cursor is, including over the toolbar, the
/// inventory backing and any open window. The view stays perfectly truthful about what is under
/// there and completely silent about the fact that clicking will do nothing to it, which is the
/// worst combination: it looks exactly like a pixel you are about to mine.</para>
///
/// <para><b>The three conditions are the game's own.</b> <c>InputManager</c> gates every tool on
/// <c>!(tool == null | MouseIsOverHUD) &amp;&amp; HoverSystem.CurrentHoveredControl == null</c>,
/// and <c>Gameplay</c> gates world interaction on an open window besides. Those are read here
/// rather than restated: <c>MouseIsOverHUD</c> in particular is a hand-maintained list of
/// rectangles, so anything that reimplements it is wrong the day the HUD changes shape.</para>
/// </summary>
internal static class WorldInput
{
    /// <summary>
    /// Whether the pointer is over something that would swallow a click.
    ///
    /// <para>Never throws and answers false if it cannot tell, since the ring this drives is a
    /// warning: showing it when there is no problem is worse than missing one, and an exception
    /// from a per-frame draw is worse than both.</para>
    /// </summary>
    internal static bool Blocked
    {
        get
        {
            try
            {
                return Gameplay.MouseIsOverHUD
                    || HoverSystem.CurrentHoveredControl != null
                    || Gameplay.CurrentWindowId != WindowId.None;
            }
            catch
            {
                return false;
            }
        }
    }
}
