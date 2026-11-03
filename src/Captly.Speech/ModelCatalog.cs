using System.Text.Json;
using System.Text.Json.Serialization;
using Captly.Core.Speech;

namespace Captly.Speech;

public sealed record ModelFile(string Name, long Size, string Sha256);

public sealed record ModelEntry(
    string Id,
    string DisplayName,
    int MillionParameters,
    ModelArchitecture Architecture,
    string Folder,
    string BaseUrl,
    IReadOnlyList<ModelFile> Files)
{
    [JsonIgnore]
    public long TotalSize => Files.Sum(f => f.Size);
}

/// <summary>
/// The models Captly knows, read from models.json. That file pins each file's SHA-256, because
/// Moonshine's own manifest only has CRC32C. `Bench catalog` writes it.
/// </summary>
public static class ModelCatalog
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static IReadOnlyList<ModelEntry> Load()
    {
        using var stream = typeof(ModelCatalog).Assembly.GetManifestResourceStream("Captly.Speech.models.json")
            ?? throw new InvalidOperationException("models.json is not embedded.");
        return JsonSerializer.Deserialize<List<ModelEntry>>(stream, JsonOptions) ?? [];
    }
}
