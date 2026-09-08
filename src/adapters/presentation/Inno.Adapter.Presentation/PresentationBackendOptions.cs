using System;
using Inno.Platform;
using Inno.Rendering;
using Inno.Rendering.Assets;

namespace Inno.Adapter.Presentation;

/// <summary>
/// Supplies backend-neutral resources required to create one host presentation context.
/// </summary>
public sealed class PresentationBackendOptions
{
    private string m_assetSourceDirectory = string.Empty;

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
    /// Gets or sets the shader compiler paired with the selected rendering backend.
    /// </summary>
    public required ShaderCompiler shaderCompiler { get; set; }

    /// <summary>
    /// Gets or sets a writable source directory used for presentation shader diagnostics.
    /// </summary>
    public required string assetSourceDirectory
    {
        get => m_assetSourceDirectory;
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            m_assetSourceDirectory = value;
        }
    }

    /// <summary>
    /// Gets or sets optional presentation features requested by the host.
    /// </summary>
    public PresentationFeatures features { get; set; }
}
