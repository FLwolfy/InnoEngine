using Inno.Adapter.Audio;
using Inno.Adapter.Input;
using Inno.Adapter.Platform;
using Inno.Adapter.Rendering;
using Inno.Adapter.Storage;

namespace Inno.Adapter;

/// <summary>
/// Exposes factories for every replaceable host backend family without exposing implementations.
/// </summary>
public interface IAdapterCatalog
{
    /// <summary>
    /// Gets the platform backend factory.
    /// </summary>
    IPlatformBackendFactory platform { get; }

    /// <summary>
    /// Gets the input backend factory.
    /// </summary>
    IInputBackendFactory input { get; }

    /// <summary>
    /// Gets the application-storage backend factory.
    /// </summary>
    IStorageBackendFactory storage { get; }

    /// <summary>
    /// Gets the rendering backend factory.
    /// </summary>
    IRenderingBackendFactory rendering { get; }

    /// <summary>
    /// Gets the audio backend factory.
    /// </summary>
    IAudioBackendFactory audio { get; }

}
