using PixelArt;
using Atomcraft.TestHarness;

namespace PixelInspector.Test;

/// <summary>
/// Generic well-formedness, from the harness.
///
/// <para><b>Every mod should have this, and it is two lines.</b> It catches the mistakes that
/// produce no error at load and no crash, just a mod that quietly does less than it says: a
/// material field naming a material that does not exist, a manifest data path matching nothing
/// in the zip, an unregistered <c>ColorDelegate</c>, a missing translation, a craftable that
/// was added but never put in a category.</para>
///
/// <para>It reads the mod's own zip rather than the live registries, so it also works on a mod
/// that fails to load, which is when it is worth the most.</para>
/// </summary>
public static class ValidationTests
{
    [GameTest]
    public static void TheModIsWellFormed() => Validation.Check("PixelInspector");

    /// <summary>
    /// The test mod too. Its manifest can rot the same way -- a renamed <c>initClass</c>, a
    /// dependency on a module id that no longer exists -- and nothing else would notice.
    /// </summary>
    [GameTest]
    public static void TheTestModIsWellFormed() => Validation.Check("PixelInspector.Test");
}
