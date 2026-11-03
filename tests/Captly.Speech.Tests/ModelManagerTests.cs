using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Captly.Core.Speech;
using Microsoft.Extensions.Logging.Abstractions;

namespace Captly.Speech.Tests;

public sealed class ModelManagerTests : IDisposable
{
    private const string Id = "test-model";
    private static readonly byte[] Weights = RandomNumberGenerator.GetBytes(200_000);
    private static readonly byte[] Tokens = Encoding.ASCII.GetBytes("hello tokens");

    private readonly string _root = Directory.CreateTempSubdirectory("captly-models-").FullName;
    private readonly FakeServer _server = new();
    private readonly List<TimeSpan> _delays = [];
    private readonly HttpClient _http;

    public ModelManagerTests()
    {
        _http = new HttpClient(_server);
        _server.Files["weights.ort"] = Weights;
        _server.Files["tokenizer.bin"] = Tokens;
    }

    public void Dispose()
    {
        _http.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private static ModelFile Pin(string name, byte[] bytes) =>
        new(name, bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)));

    private static readonly ModelEntry Entry = new(Id, "Test", 1, ModelArchitecture.TinyStreaming, "test-folder",
        "https://example.test/model", [Pin("weights.ort", Weights), Pin("tokenizer.bin", Tokens)]);

    private ModelManager Manager() => new(_root, _http, NullLogger<ModelManager>.Instance, [Entry],
        (wait, _) => { _delays.Add(wait); return Task.CompletedTask; });

    private string FilePath(string name) => Path.Combine(_root, "test-folder", name);

    private Task Download(IProgress<double>? progress = null) =>
        Manager().DownloadAsync(Id, progress, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Download_InstallsEveryFile()
    {
        Assert.False(Manager().Available[0].IsInstalled);

        await Download();

        Assert.True(Manager().Available[0].IsInstalled);
        Assert.Equal(Weights, File.ReadAllBytes(FilePath("weights.ort")));
        Assert.Equal(Tokens, File.ReadAllBytes(FilePath("tokenizer.bin")));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "test-folder"), "*.part"));
    }

    [Fact]
    public async Task Progress_RisesToOne()
    {
        var seen = new List<double>();

        await Download(new SyncProgress(seen.Add));

        Assert.Equal(1, seen[^1]);
        Assert.Equal(seen.Order(), seen);
    }

    [Fact]
    public async Task Interrupted_ResumesWithARangeRequest()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath("x"))!);
        File.WriteAllBytes(FilePath("weights.ort.part"), Weights[..50_000]);

        await Download();

        Assert.Equal(50_000, _server.RangeStarts["weights.ort"]);
        Assert.Equal(Weights, File.ReadAllBytes(FilePath("weights.ort")));
    }

    [Fact]
    public async Task ServerIgnoringRange_StartsTheFileAgain()
    {
        _server.SupportsRange = false;
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath("x"))!);
        File.WriteAllBytes(FilePath("weights.ort.part"), Weights[..50_000]);

        await Download();

        Assert.Equal(Weights, File.ReadAllBytes(FilePath("weights.ort")));
    }

    [Fact]
    public async Task ConnectionDropped_RetriesAndResumes()
    {
        _server.CutOffAfter["weights.ort"] = new Queue<int>([60_000]);

        await Download();

        Assert.Equal(60_000, _server.RangeStarts["weights.ort"]);
        Assert.Equal(Weights, File.ReadAllBytes(FilePath("weights.ort")));
        Assert.Equal([TimeSpan.FromSeconds(1)], _delays);
    }

    [Fact]
    public async Task ServerErrors_BackOffThenSucceed()
    {
        _server.FailuresLeft = 3;

        await Download();

        Assert.True(Manager().Available[0].IsInstalled);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)], _delays);
    }

    [Fact]
    public async Task NetworkDown_GivesUpWithoutInstalling()
    {
        _server.FailuresLeft = 100;

        await Assert.ThrowsAsync<HttpRequestException>(() => Download());

        Assert.Equal(4, _delays.Count);
        Assert.False(Manager().Available[0].IsInstalled);
    }

    [Fact]
    public async Task CorruptDownload_IsFetchedAgain()
    {
        var bad = (byte[])Weights.Clone();
        bad[100] ^= 0xFF;
        _server.Corrupt["weights.ort"] = new Queue<byte[]>([bad]);

        await Download();

        Assert.Equal(Weights, File.ReadAllBytes(FilePath("weights.ort")));
        Assert.Equal(2, _server.Requests["weights.ort"]);
    }

    [Fact]
    public async Task AlwaysCorrupt_FailsAndNeverInstallsTheBadFile()
    {
        var bad = (byte[])Weights.Clone();
        bad[100] ^= 0xFF;
        _server.Corrupt["weights.ort"] = new Queue<byte[]>([bad, bad, bad]);

        await Assert.ThrowsAsync<ModelVerificationException>(() => Download());

        Assert.False(File.Exists(FilePath("weights.ort")));
        Assert.False(Manager().Available[0].IsInstalled);
    }

    [Fact]
    public async Task BadPartialFromAnEarlierRun_IsReplaced()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath("x"))!);
        File.WriteAllBytes(FilePath("weights.ort.part"), new byte[Weights.Length]);

        await Download();

        Assert.Equal(Weights, File.ReadAllBytes(FilePath("weights.ort")));
    }

    [Fact]
    public async Task AlreadyInstalled_DownloadsNothing()
    {
        await Download();
        _server.Requests.Clear();

        await Download();

        Assert.Empty(_server.Requests);
    }

    [Fact]
    public async Task DeletedFile_ShowsAsNotInstalledAndOnlyThatFileReturns()
    {
        await Download();
        File.Delete(FilePath("tokenizer.bin"));
        Assert.False(Manager().Available[0].IsInstalled);
        _server.Requests.Clear();

        await Download();

        Assert.True(Manager().Available[0].IsInstalled);
        Assert.Equal(["tokenizer.bin"], _server.Requests.Keys);
    }

    [Fact]
    public async Task Cancelled_StopsAndIsNotRetried()
    {
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Manager().DownloadAsync(Id, null, cancel.Token));

        Assert.Empty(_delays);
    }

    [Fact]
    public void UnknownModel_IsRejected() => Assert.Throws<ArgumentException>(() => Manager().GetModelPath("nope"));

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    private sealed class FakeServer : HttpMessageHandler
    {
        public Dictionary<string, byte[]> Files { get; } = [];
        public Dictionary<string, Queue<byte[]>> Corrupt { get; } = [];
        public Dictionary<string, Queue<int>> CutOffAfter { get; } = [];
        public Dictionary<string, long> RangeStarts { get; } = [];
        public Dictionary<string, int> Requests { get; } = [];
        public bool SupportsRange { get; set; } = true;
        public int FailuresLeft { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (FailuresLeft > 0)
            {
                FailuresLeft--;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            var name = request.RequestUri!.Segments[^1];
            Requests[name] = Requests.GetValueOrDefault(name) + 1;
            var body = Corrupt.TryGetValue(name, out var bad) && bad.Count > 0 ? bad.Dequeue() : Files[name];

            var start = SupportsRange ? request.Headers.Range?.Ranges.Single().From ?? 0 : 0;
            if (start > 0) RangeStarts[name] = start;

            var content = body[(int)start..];
            if (CutOffAfter.TryGetValue(name, out var cuts) && cuts.Count > 0) content = content[..cuts.Dequeue()];

            var response = new HttpResponseMessage(start > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content),
            };
            if (start > 0) response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, body.Length - 1, body.Length);
            return Task.FromResult(response);
        }
    }
}
