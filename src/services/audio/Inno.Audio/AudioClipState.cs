namespace Inno.Audio;

/// <summary>
/// Describes asynchronous preparation of a backend-owned clip generation.
/// </summary>
public enum AudioClipState
{
    /// <summary>
    /// Native preparation is still in progress and the clip must not start yet.
    /// </summary>
    Preparing,
    /// <summary>
    /// The retained native data source is ready for playback.
    /// </summary>
    Ready,
    /// <summary>
    /// Preparation failed; the clip must be destroyed and callers notified.
    /// </summary>
    Failed
}
