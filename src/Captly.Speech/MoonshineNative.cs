using System.Runtime.InteropServices;
using System.Text;

namespace Captly.Speech;

/// <summary>
/// Bindings for core/moonshine-c-api.h at tag v0.1.5. Struct layouts must match the header of the
/// DLL that loads, so update both together.
/// </summary>
internal static unsafe partial class MoonshineNative
{
    private const string Library = "moonshine";

    public const int HeaderVersion = 30000;
    public const uint ArchTinyStreaming = 2;
    public const uint ArchSmallStreaming = 4;
    public const uint ArchMediumStreaming = 5;

    [StructLayout(LayoutKind.Sequential)]
    public struct Option
    {
        public byte* Name;
        public byte* Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Line
    {
        public byte* Text;
        public float* AudioData;
        public nuint AudioDataCount;
        public float StartTime;
        public float Duration;
        public ulong Id;
        public sbyte IsComplete;
        public sbyte IsUpdated;
        public sbyte IsNew;
        public sbyte HasTextChanged;
        public sbyte HaveSpeakersChanged;
        public void* SpeakerSpans;
        public ulong SpeakerSpanCount;
        public uint LastTranscriptionLatencyMs;
        public void* Words;
        public ulong WordCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Transcript
    {
        public Line* Lines;
        public ulong LineCount;
    }

    [DllImport(Library, EntryPoint = "moonshine_get_version")]
    public static extern int GetVersion();

    [DllImport(Library, EntryPoint = "moonshine_error_to_string")]
    public static extern byte* ErrorToString(int error);

    [DllImport(Library, EntryPoint = "moonshine_load_transcriber_from_files")]
    public static extern int LoadTranscriber(byte* path, uint arch, Option* options, ulong optionCount, int version);

    [DllImport(Library, EntryPoint = "moonshine_free_transcriber")]
    public static extern void FreeTranscriber(int transcriber);

    [DllImport(Library, EntryPoint = "moonshine_create_stream")]
    public static extern int CreateStream(int transcriber, uint flags);

    [DllImport(Library, EntryPoint = "moonshine_free_stream")]
    public static extern int FreeStream(int transcriber, int stream);

    [DllImport(Library, EntryPoint = "moonshine_start_stream")]
    public static extern int StartStream(int transcriber, int stream);

    [DllImport(Library, EntryPoint = "moonshine_stop_stream")]
    public static extern int StopStream(int transcriber, int stream);

    [DllImport(Library, EntryPoint = "moonshine_transcribe_add_audio_to_stream")]
    public static extern int AddAudio(int transcriber, int stream, float* audio, ulong length, int sampleRate, uint flags);

    [DllImport(Library, EntryPoint = "moonshine_transcribe_stream")]
    public static extern int TranscribeStream(int transcriber, int stream, uint flags, Transcript** transcript);

    [DllImport("ucrtbase", EntryPoint = "_putenv_s", CharSet = CharSet.Ansi)]
    private static extern int PutEnv(string name, string value);

    public static string Text(byte* utf8) => utf8 == null ? "" : Marshal.PtrToStringUTF8((nint)utf8) ?? "";

    public static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text + "\0");

    /// <summary>
    /// Runs ONNX Runtime on one thread, which is far faster here than its default pool (DECISIONS.md
    /// D4). Moonshine reads this with getenv, so it has to reach the C runtime's own copy of the
    /// environment before the library loads.
    /// </summary>
    public static void ForceSingleThread()
    {
        const string name = "MOONSHINE_ORT_SINGLE_THREAD";
        Environment.SetEnvironmentVariable(name, "1");
        if (OperatingSystem.IsWindows()) PutEnv(name, "1");
    }
}
