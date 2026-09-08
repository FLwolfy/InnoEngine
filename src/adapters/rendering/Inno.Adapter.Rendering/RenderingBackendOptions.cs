using Inno.Platform;
using Inno.Rendering;

namespace Inno.Adapter.Rendering;

/// <summary>
/// Configures backend-neutral rendering-device creation.
/// </summary>
public sealed class RenderingBackendOptions
{
    /// <summary>
    /// Gets or sets the primary platform window used as the presentation surface.
    /// </summary>
    public IPlatformWindow? window { get; set; }

    /// <summary>
    /// Gets or sets the preferred graphics API, or <see langword="null"/> for the backend default.
    /// </summary>
    public GraphicsApi? preferredGraphicsApi { get; set; }

    /// <summary>
    /// Gets or sets whether presentation waits for display synchronization.
    /// </summary>
    public bool verticalSync { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the primary backbuffer performs sRGB encoding.
    /// </summary>
    public bool sRgbBackbuffer { get; set; } = true;

    /// <summary>
    /// Gets or sets whether rendering must execute on the calling thread.
    /// </summary>
    public bool forceSingleThreaded { get; set; }
}
