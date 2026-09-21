using Godot;
using PixelArt;

namespace PixelInspector;

/// <summary>
/// Keeps the panel up after Alt is let go, so it can be left on rather than held on.
///
/// <para><b>A tap puts it up; any release puts it down.</b> Press and let go inside
/// <see cref="Settings.AltTapSeconds"/> and the panel stays. Once it is staying, the next release
/// of Alt puts it down however long the key was held.</para>
///
/// <para><b>The asymmetry is the point.</b> With the panel down, a long press is a peek: the
/// player is holding Alt to look at something and wants it gone when they let go, which is what
/// the mod has always done and what the duration test protects. With the panel already up there
/// is nothing to peek at -- it is in front of them either way -- so a press can only be aimed at
/// dismissing it, and the length of that press says nothing.</para>
///
/// <para><b>On release rather than on press</b>, because the gesture is not finished until the
/// key comes back up. Deciding on the way down would mean deciding before the length of the
/// press is known, which is the one fact being read.</para>
///
/// <para><b>Polled once a frame rather than driven by a key event, and not for want of an
/// event.</b> <c>Gameplay._UnhandledInput</c> is a real Godot handler a postfix could take, and
/// it would deliver the release directly. It would also miss it: that handler sees only what no
/// Control consumed, and only while its node is in the tree and processing, so a release that
/// lands in a menu, in a focused text field, or between scenes never arrives. A missed release
/// is not one dropped frame of behaviour -- it leaves <c>_held</c> true with the key physically
/// up, and the flip then fires on some unrelated press minutes later. A poll cannot desync like
/// that: whatever was missed, the next frame compares the real key state against the last one
/// and settles. The cost of being right is a bitset lookup and a clock read, on a postfix that
/// already runs every frame for the click guard.</para>
///
/// <para>What polling gives up is a press and release inside one frame, which is 17ms at 60fps
/// against a human tap of several times that. That failure is the benign one as well: the tap
/// does nothing and the player taps again.</para>
///
/// <para>This is runtime state rather than a setting, so it belongs in the mod's reset; a panel
/// left stuck on would make the mod draw for every test after the one that stuck it.</para>
/// </summary>
internal static class StickyPanel
{
    private static bool _held;
    private static bool _stuck;
    private static ulong _pressedAt;

    /// <summary>Whether a tap has left the panel up with nothing being held.</summary>
    internal static bool Stuck => _stuck;

    /// <summary>
    /// Samples the modifier and the clock. Called once a frame from the cursor postfix, which
    /// runs whatever the panel is doing -- the magnifier's own pass would not do, since it is not
    /// called at all on the frames the panel is down, and the release that should bring it back
    /// up happens on exactly those frames.
    /// </summary>
    internal static void Sync() => Sync(Canvas.AltHeld, Time.GetTicksMsec());

    /// <summary>
    /// The decision, separated from reading the keyboard and the clock so a test can drive it. A
    /// test cannot hold a key down, and it cannot wait out a threshold without spending the time.
    /// </summary>
    internal static void Sync(bool altHeld, ulong nowMsec)
    {
        if (altHeld && !_held)
        {
            _pressedAt = nowMsec;
        }
        else if (!altHeld && _held)
        {
            // Down beats up: a release always dismisses, and only a short one engages.
            if (_stuck)
                _stuck = false;
            else
                _stuck = TapMsec > 0 && nowMsec - _pressedAt <= TapMsec;
        }

        _held = altHeld;
    }

    /// <summary>
    /// The threshold in milliseconds. Zero means no press is ever short enough, which is a player
    /// asking for the hold-only behaviour back rather than a degenerate case.
    /// </summary>
    private static ulong TapMsec => (ulong)Mathf.Max(0.0, Settings.AltTapSeconds * 1000.0);

    /// <summary>Drops the panel and forgets the key, for the mod's state reset.</summary>
    internal static void Clear()
    {
        _held = false;
        _stuck = false;
        _pressedAt = 0;
    }
}
