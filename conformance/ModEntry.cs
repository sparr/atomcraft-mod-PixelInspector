using Atomcraft.TestHarness;

namespace PixelInspectorConformance;

/// <summary>
/// Entry point for the conformance suite.
///
/// <para><b>This mod names no other mod.</b> It depends on the harness and nothing else, which
/// is the point: it asserts properties of the <i>game</i> that any mod showing a player what a
/// pixel is programmed to has to rest on, so it can be installed beside PixelInspector, beside a
/// rival that does the same job differently, or beside neither. That separates "is the problem
/// solved?" from "does my code work?".</para>
///
/// <para>A failure here is <b>bad</b> news, unlike a retirement test: these are properties the
/// mod depends on rather than defects it works around.</para>
/// </summary>
public static class ModEntry
{
    public const string ModId = "PixelInspectorConformance";
    public const string HarnessVersion = "0.4";

    public static void Initialize()
    {
        Harness.RequireVersion(HarnessVersion);
        Log.Info("loaded");
    }
}
