namespace Captly.Core.Speech;

/// <summary>A caption from the engine. Partials revise the current line. A final ends it.</summary>
public record CaptionEvent(string Text, bool IsFinal, TimeSpan StreamTime)
{
    /// <summary>How long the line's speech lasts, as far as it has been heard.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>How much of the stream the engine had heard when it raised this, so the line can be placed on a timeline.</summary>
    public TimeSpan HeardUpTo { get; init; }
}

public record EngineOptions(string ModelPath, ModelArchitecture Architecture)
{
    /// <summary>
    /// How much new audio to wait for between transcript updates. 750 ms cut Tiny's CPU by a fifth
    /// against 500 ms, for 84 ms more delay before the first words (D50).
    /// </summary>
    public TimeSpan UpdateInterval { get; init; } = TimeSpan.FromMilliseconds(750);
}

public enum ModelArchitecture { TinyStreaming, SmallStreaming, MediumStreaming }

/// <summary>Not thread-safe. <see cref="CaptionWorker"/> owns one on its inference thread.</summary>
public interface ISpeechEngine : IDisposable
{
    string Name { get; }
    void Load(EngineOptions options);

    /// <summary>Adds 16 kHz mono audio. Caption events are raised during this call.</summary>
    void Push(ReadOnlySpan<float> audio16kMono);

    /// <summary>Forgets the current stream, for a seek or track change. No events follow for the old audio.</summary>
    void Reset();

    event Action<CaptionEvent> Caption;
}
