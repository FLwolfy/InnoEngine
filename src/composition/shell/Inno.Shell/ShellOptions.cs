using Inno.Adapter;
using Inno.Platform;
using Inno.Rendering;

namespace Inno.Shell;

/// <summary>
/// Configures backend selection and primary-window policy for a composition shell.
/// </summary>
public sealed class ShellOptions
{
    /// <summary>
    /// Gets or sets the complete backend selection used by the shell and its derived product host.
    /// </summary>
    public AdapterSelection adapters { get; set; } = AdapterSelection.defaultValue;

    /// <summary>
    /// Gets or sets primary-window creation options.
    /// </summary>
    public PlatformWindowOptions window { get; set; } = new();

    /// <summary>
    /// Gets or sets the preferred graphics API, or <see langword="null"/> for the rendering-backend default.
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
    public bool forceSingleThreadedRendering { get; set; }
}
