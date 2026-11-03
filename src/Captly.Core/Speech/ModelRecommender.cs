namespace Captly.Core.Speech;

/// <summary>A speed test result. RTF is processing time over audio time, so under 1 keeps up.</summary>
public sealed record BenchResult(string ModelId, int MillionParameters, double RealTimeFactor);

/// <summary>The model that fits this PC, and the RTF it is expected to run at, when a speed test ran.</summary>
public sealed record ModelRecommendation(string ModelId, double? ExpectedRtf);

/// <summary>A graphics card, for the Speech Model screen. No model can use one yet (D3, D22).</summary>
public sealed record GraphicsAdapter(string Id, string Name, long DedicatedMemory);

/// <summary>
/// Picks the model for the setup screen and the "Recommended for this PC" badge (PLAN.md Phase 7).
/// Only the processor runs models today (D3), so this rests on one speed test of any model.
/// </summary>
public static class ModelRecommender
{
    /// <summary>
    /// The largest RTF a recommended model may have. It leaves most of a core free, and keeps
    /// partial captions quick. D16 measured Small at 0.57 on the test PC, so Tiny stays there.
    /// </summary>
    public const double Budget = 0.4;

    /// <summary>The largest model expected to fit the budget. Without a speed test, or if none fits, the smallest.</summary>
    public static ModelRecommendation Recommend(IReadOnlyList<ModelInfo> models, BenchResult? bench)
    {
        var smallest = models.MinBy(m => m.MillionParameters) ?? throw new ArgumentException("There are no models.", nameof(models));
        if (bench == null) return new ModelRecommendation(smallest.Id, null);

        var fits = models.Where(m => ExpectedRtf(bench, m.MillionParameters) <= Budget).MaxBy(m => m.MillionParameters) ?? smallest;
        return new ModelRecommendation(fits.Id, ExpectedRtf(bench, fits.MillionParameters));
    }

    /// <summary>
    /// Scales a measured RTF to another model size. Cost grows with the square root of the
    /// parameter count: D16 measured Small at 1.84 times Tiny and Medium at 2.71, where the root
    /// gives 1.90 and 2.68.
    /// </summary>
    public static double ExpectedRtf(BenchResult bench, int millionParameters) =>
        bench.RealTimeFactor * Math.Sqrt((double)millionParameters / bench.MillionParameters);
}
