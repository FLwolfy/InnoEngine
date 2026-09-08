namespace Inno.Adapter.Audio;

/// <summary>
/// Selects a built-in audio backend without exposing its implementation types.
/// </summary>
public enum AudioBackend
{
    /// <summary>
    /// Uses the MiniAudio implementation.
    /// </summary>
    MiniAudio = 0
}
