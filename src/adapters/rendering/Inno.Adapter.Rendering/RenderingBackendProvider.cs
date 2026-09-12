using Inno.Rendering;

namespace Inno.Adapter.Rendering;

/// <summary>Supplies one rendering implementation registered by a composition owner.</summary>
public abstract class RenderingBackendProvider
{
    /// <summary>Gets the stable implementation identity paired with its authoring tools.</summary>
    public abstract RenderingBackendId id { get; }

    /// <summary>Creates a caller-owned device using backend-neutral surface options.</summary>
    /// <param name="options">Validated surface and device creation options.</param>
    /// <returns>A new device owned by the caller.</returns>
    public abstract IRenderDevice CreateDevice(RenderingBackendOptions options);
}
