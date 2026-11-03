using Captly.Core.Speech;

namespace Captly.Core.Tests;

public class ModelRecommenderTests
{
    private static readonly ModelInfo[] Models =
    [
        new("small", "Small", 123, 0, ModelArchitecture.SmallStreaming, false),
        new("tiny", "Tiny", 34, 0, ModelArchitecture.TinyStreaming, true),
        new("medium", "Medium", 245, 0, ModelArchitecture.MediumStreaming, false),
    ];

    private static BenchResult Tiny(double rtf) => new("tiny", 34, rtf);

    [Fact]
    public void WithoutASpeedTest_RecommendsTheSmallest()
    {
        Assert.Equal(new ModelRecommendation("tiny", null), ModelRecommender.Recommend(Models, null));
    }

    // D16's numbers from the Snapdragon test PC: Tiny 0.31, so Small is expected near 0.59.
    [Fact]
    public void TheTestPc_GetsTiny()
    {
        var recommendation = ModelRecommender.Recommend(Models, Tiny(0.31));

        Assert.Equal("tiny", recommendation.ModelId);
        Assert.Equal(0.31, recommendation.ExpectedRtf!.Value, 6);
    }

    [Fact]
    public void AFasterPc_GetsSmall()
    {
        Assert.Equal("small", ModelRecommender.Recommend(Models, Tiny(0.2)).ModelId);
    }

    [Fact]
    public void AVeryFastPc_GetsMedium()
    {
        Assert.Equal("medium", ModelRecommender.Recommend(Models, Tiny(0.1)).ModelId);
    }

    [Fact]
    public void ASlowPc_StillGetsTheSmallest()
    {
        var recommendation = ModelRecommender.Recommend(Models, Tiny(0.9));

        Assert.Equal("tiny", recommendation.ModelId);
        Assert.Equal(0.9, recommendation.ExpectedRtf!.Value, 6);
    }

    [Fact]
    public void ATestOfALargerModel_ScalesDown()
    {
        // Small measured at 0.57 on the test PC predicts Tiny near 0.30.
        var expected = ModelRecommender.ExpectedRtf(new BenchResult("small", 123, 0.57), 34);

        Assert.Equal(0.30, expected, 2);
    }

    [Fact]
    public void Scaling_MatchesTheMeasuredRatios_WithinFivePercent()
    {
        // D16: Tiny 0.31, Small 0.57, Medium 0.84.
        Assert.Equal(0.57, ModelRecommender.ExpectedRtf(Tiny(0.31), 123), 0.57 * 0.05);
        Assert.Equal(0.84, ModelRecommender.ExpectedRtf(Tiny(0.31), 245), 0.84 * 0.05);
    }

    [Fact]
    public void NoModels_IsAnError()
    {
        Assert.Throws<ArgumentException>(() => ModelRecommender.Recommend([], null));
    }
}
