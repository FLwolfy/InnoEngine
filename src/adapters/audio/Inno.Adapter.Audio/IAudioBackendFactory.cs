using Inno.Audio;

namespace Inno.Adapter.Audio;

/// <summary>
/// Creates backend-neutral audio devices from explicit backend selections.
/// </summary>
public interface IAudioBackendFactory
{
    /// <summary>
    /// Creates a new audio device for one runtime audio generation.
    /// </summary>
    /// <param name="backend">
    /// Built-in audio backend selected by the composition root.
    /// </param>
    /// <param name="options">
    /// Backend-neutral device options.
    /// </param>
    /// <returns>
    /// A caller-owned backend-neutral audio device.
    /// </returns>
    /// <exception cref="System.NotSupportedException">
    /// Thrown when the catalog does not contain the selected backend.
    /// </exception>
    IAudioDevice CreateDevice(AudioBackend backend, AudioBackendOptions options = default);
}
