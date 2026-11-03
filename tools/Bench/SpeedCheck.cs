using Captly.Core;
using Captly.Core.Speech;
using Captly.Speech;
using Captly.Windowing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Captly.Bench;

/// <summary>Runs the in-app speed test on every installed model, and shows what each result would recommend.</summary>
internal static class SpeedCheck
{
    public static int Run()
    {
        using var http = new HttpClient();
        var manager = new ModelManager(AppPaths.ForCurrentUser().ModelFolder, http, NullLogger<ModelManager>.Instance);
        var models = manager.Available;

        foreach (var adapter in GraphicsAdapters.List())
            Console.WriteLine($"Graphics card {adapter.Id}  {adapter.Name}  {adapter.DedicatedMemory / 1048576} MB dedicated");

        foreach (var model in models.Where(m => m.IsInstalled))
        {
            var result = SpeedTest.Run(model, manager.GetModelPath(model.Id), CancellationToken.None);
            var recommended = ModelRecommender.Recommend(models, result);
            var expected = string.Join("  ", models.Select(m => $"{m.DisplayName} {ModelRecommender.ExpectedRtf(result, m.MillionParameters):F2}"));
            Console.WriteLine($"{model.DisplayName}: RTF {result.RealTimeFactor:F3}  expected: {expected}  ->  recommends {recommended.ModelId}");
        }
        return 0;
    }
}
