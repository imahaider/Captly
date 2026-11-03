using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Captly.Core.Speech;
using Captly.Speech;

namespace Captly.Bench;

/// <summary>
/// Writes models.json from Moonshine's own manifest and the files already on disk. Each file must
/// match the upstream size and CRC32C first, so a pinned SHA-256 is never taken from a bad copy.
/// </summary>
internal static unsafe class CatalogBuilder
{
    private sealed record Source(string Id, string DisplayName, int MillionParameters, ModelArchitecture Architecture, int NativeArch, string Folder);

    private static readonly Source[] Sources =
    [
        new("moonshine-tiny-streaming", "Tiny Streaming", 34, ModelArchitecture.TinyStreaming, 2, "tiny-streaming-en"),
        new("moonshine-small-streaming", "Small Streaming", 123, ModelArchitecture.SmallStreaming, 4, "small-streaming-en"),
        new("moonshine-medium-streaming", "Medium Streaming", 245, ModelArchitecture.MediumStreaming, 5, "medium-streaming-en"),
    ];

    [StructLayout(LayoutKind.Sequential)]
    private struct Option
    {
        public byte* Name;
        public byte* Value;
    }

    [DllImport("moonshine", EntryPoint = "moonshine_get_stt_dependencies")]
    private static extern int GetSttDependencies(byte* language, Option* options, ulong count, byte** json);

    [DllImport("moonshine", EntryPoint = "moonshine_free_buffer")]
    private static extern void FreeBuffer(void* pointer);

    public static int Run(string outputPath, string modelRoot)
    {
        var entries = new List<ModelEntry>();
        foreach (var source in Sources)
        {
            using var manifest = JsonDocument.Parse(Manifest(source.NativeArch));
            var group = manifest.RootElement.GetProperty("groups")[0];
            var files = new List<ModelFile>();
            foreach (var file in group.GetProperty("files").EnumerateArray())
            {
                var name = file.GetProperty("name").GetString()!;
                var bytes = File.ReadAllBytes(Path.Combine(modelRoot, source.Folder, name));
                var size = file.GetProperty("size").GetInt64();
                var checksum = file.GetProperty("checksum").GetString()!;
                var actual = Crc32C.Base64(bytes);
                if (bytes.Length != size || actual != checksum)
                {
                    Console.WriteLine($"{source.Id}/{name} does not match upstream: {actual} vs {checksum}");
                    return 1;
                }
                files.Add(new ModelFile(name, size, Convert.ToHexStringLower(SHA256.HashData(bytes))));
            }

            entries.Add(new ModelEntry(source.Id, source.DisplayName, source.MillionParameters, source.Architecture,
                source.Folder, group.GetProperty("base_url").GetString()!, files));
            Console.WriteLine($"{source.Id}: {files.Count} files match upstream");
        }

        File.WriteAllText(outputPath, JsonSerializer.Serialize(entries, ModelCatalog.JsonOptions) + Environment.NewLine);
        return 0;
    }

    private static string Manifest(int arch)
    {
        fixed (byte* language = "en\0"u8, key = "model_arch\0"u8, value = Encoding.UTF8.GetBytes(arch + "\0"))
        {
            var option = new Option { Name = key, Value = value };
            byte* json = null;
            var error = GetSttDependencies(language, &option, 1, &json);
            if (error != 0) throw new InvalidOperationException($"Manifest failed: {error}");
            var text = Marshal.PtrToStringUTF8((nint)json)!;
            FreeBuffer(json);
            return text;
        }
    }
}

internal static class Crc32C
{
    private static readonly uint[] Table = Enumerable.Range(0, 256).Select(i =>
    {
        var crc = (uint)i;
        for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? crc >> 1 ^ 0x82F63B78 : crc >> 1;
        return crc;
    }).ToArray();

    /// <summary>Base64 of the big-endian digest, as Moonshine's manifest writes it.</summary>
    public static string Base64(ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        foreach (var value in data) crc = Table[(byte)(crc ^ value)] ^ crc >> 8;
        crc = ~crc;
        return Convert.ToBase64String([(byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc]);
    }
}
