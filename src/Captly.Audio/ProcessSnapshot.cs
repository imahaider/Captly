using Captly.Core.Audio;

namespace Captly.Audio;

/// <summary>Every process and its parent, for code outside this library such as the window tracker.</summary>
public static class ProcessSnapshot
{
    public static ProcessTree Take() => Processes.Snapshot();
}
