using Inno.Platform;
using Inno.Rendering;

namespace Inno.Adapter.Presentation;

/// <summary>
/// Collects the validated presentation backend options values that configure one owned operation.
/// </summary>
public sealed class PresentationBackendOptions
{
    /// <summary>
    /// Gets or sets the platform application that owns presentation windows and events.
    /// </summary>
    public required IPlatformApplication platformApplication { get; set; }

    /// <summary>
    /// Gets or sets the primary presentation window.
    /// </summary>
    public required IPlatformWindow window { get; set; }

    /// <summary>
    /// Gets or sets the rendering device used to present host draw data.
    /// </summary>
    public required IRenderDevice renderDevice { get; set; }

    /// <summary>
    /// Gets or sets optional presentation features requested by the host.
    /// </summary>
    public PresentationFeatures features { get; set; }
}
