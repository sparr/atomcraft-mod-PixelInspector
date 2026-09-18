using Atomcraft.TestHarness;

namespace PixelInspectorConformance;

/// <summary>Generic well-formedness of this suite's own zip and manifest.</summary>
public static class ValidationTests
{
    [GameTest]
    public static void TheConformanceModIsWellFormed() =>
        Validation.Check("PixelInspectorConformance");
}
