using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Captly.Core.Speech;
using Microsoft.Extensions.Logging;

namespace Captly.Speech;

public sealed class ModelVerificationException(string message) : Exception(message);

/// <summary>
/// Downloads models into per-model folders. Each file is fetched to a .part file, so an interrupted
/// download resumes where it stopped, and it only takes its real name once its SHA-256 matches.
/// A finished model gets a marker, so start-up never has to hash hundreds of megabytes.
/// </summary>
public sealed class ModelManager(
    string root,
    HttpClient http,
    ILogger<ModelManager> logger,
    IReadOnlyList<ModelEntry>? catalog = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null) : IModelManager
{
    private const string VerifiedMarker = ".verified";
    private const int MaxAttempts = 5;
    private const int MaxVerifyAttempts = 2;
    private const int BufferSize = 1 << 16;

    private readonly IReadOnlyList<ModelEntry> _catalog = catalog ?? ModelCatalog.Load();
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    public IReadOnlyList<ModelInfo> Available => _catalog
        .Select(m => new ModelInfo(m.Id, m.DisplayName, m.MillionParameters, m.TotalSize, m.Architecture, IsInstalled(m)))
        .ToList();

    public string GetModelPath(string id) => Path.Combine(root, Find(id).Folder);

    public async Task DownloadAsync(string id, IProgress<double>? progress, CancellationToken ct)
    {
        var model = Find(id);
        if (IsInstalled(model))
        {
            progress?.Report(1);
            return;
        }

        var folder = GetModelPath(id);
        Directory.CreateDirectory(folder);
        var total = model.TotalSize;
        long finished = 0;

        foreach (var file in model.Files)
        {
            var path = Path.Combine(folder, file.Name);
            if (!await IsIntactAsync(path, file, ct))
                await FetchVerifiedAsync(model, file, path, done => progress?.Report((double)(finished + done) / total), ct);

            finished += file.Size;
            progress?.Report((double)finished / total);
        }

        await File.WriteAllTextAsync(Path.Combine(folder, VerifiedMarker), model.Id, ct);
        logger.LogInformation("Model {Id} installed in {Folder}", id, folder);
    }

    private ModelEntry Find(string id) => _catalog.FirstOrDefault(m => m.Id == id)
        ?? throw new ArgumentException($"Unknown model: {id}", nameof(id));

    // Checked by size too, so a deleted or truncated file shows as not installed.
    private bool IsInstalled(ModelEntry model)
    {
        var folder = Path.Combine(root, model.Folder);
        if (!File.Exists(Path.Combine(folder, VerifiedMarker))) return false;
        return model.Files.All(f =>
        {
            var info = new FileInfo(Path.Combine(folder, f.Name));
            return info.Exists && info.Length == f.Size;
        });
    }

    private async Task FetchVerifiedAsync(ModelEntry model, ModelFile file, string path, Action<long> progress, CancellationToken ct)
    {
        var part = path + ".part";
        for (var attempt = 1; ; attempt++)
        {
            await FetchWithRetriesAsync($"{model.BaseUrl}/{file.Name}", part, file.Size, progress, ct);
            if (await HashMatchesAsync(part, file, ct))
            {
                File.Move(part, path, overwrite: true);
                return;
            }

            logger.LogWarning("{File} failed its checksum on attempt {Attempt}", file.Name, attempt);
            File.Delete(part);
            if (attempt >= MaxVerifyAttempts)
                throw new ModelVerificationException($"{file.Name} failed verification {attempt} times.");
        }
    }

    private async Task FetchWithRetriesAsync(string url, string part, long size, Action<long> progress, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await FetchAsync(url, part, size, progress, ct);
                return;
            }
            catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                if (attempt >= MaxAttempts) throw;
                var wait = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                logger.LogWarning(e, "Download of {Url} failed on attempt {Attempt}, retrying in {Wait}", url, attempt, wait);
                await _delay(wait, ct);
            }
        }
    }

    private async Task FetchAsync(string url, string part, long size, Action<long> progress, CancellationToken ct)
    {
        var existing = File.Exists(part) ? new FileInfo(part).Length : 0;

        // More than the file holds can only be a bad leftover.
        if (existing > size)
        {
            File.Delete(part);
            existing = 0;
        }
        if (existing == size) return;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0) request.Headers.Range = new RangeHeaderValue(existing, null);

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            File.Delete(part);
            throw new IOException($"Server refused to resume {url}");
        }
        response.EnsureSuccessStatusCode();

        // A plain 200 means the server ignored the range and is sending the whole file.
        var resuming = response.StatusCode == HttpStatusCode.PartialContent;
        await using var output = new FileStream(part, resuming ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
        await using var body = await response.Content.ReadAsStreamAsync(ct);

        var buffer = new byte[BufferSize];
        var done = resuming ? existing : 0;
        progress(done);
        int read;
        while ((read = await body.ReadAsync(buffer, ct)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            done += read;
            progress(done);
        }

        if (done < size) throw new IOException($"Connection closed after {done} of {size} bytes from {url}");
    }

    private static async Task<bool> IsIntactAsync(string path, ModelFile file, CancellationToken ct) =>
        File.Exists(path) && new FileInfo(path).Length == file.Size && await HashMatchesAsync(path, file, ct);

    private static async Task<bool> HashMatchesAsync(string path, ModelFile file, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexStringLower(hash) == file.Sha256;
    }
}
