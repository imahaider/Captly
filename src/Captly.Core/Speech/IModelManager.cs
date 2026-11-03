namespace Captly.Core.Speech;

public record ModelInfo(
    string Id,
    string DisplayName,
    int MillionParameters,
    long SizeOnDisk,
    ModelArchitecture Architecture,
    bool IsInstalled);

public interface IModelManager
{
    /// <summary>The catalogue, with <see cref="ModelInfo.IsInstalled"/> checked against the disk.</summary>
    IReadOnlyList<ModelInfo> Available { get; }

    string GetModelPath(string id);

    /// <summary>
    /// Downloads and verifies a model. Resumes after an interruption, retries network errors, and
    /// fetches a file again if it fails verification. Progress runs from 0 to 1.
    /// </summary>
    Task DownloadAsync(string id, IProgress<double>? progress, CancellationToken ct);
}
