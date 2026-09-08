using Inno.Rendering;

namespace Inno.Adapter.Rendering;

/// <summary>
/// Creates runtime rendering devices from one backend selection.
/// </summary>
public interface IRenderingBackendFactory
{
    /// <summary>
    /// Creates a rendering device for the supplied primary presentation surface.
    /// </summary>
    /// <param name="backend">
    /// Built-in rendering backend selected by the composition root.
    /// </param>
    /// <param name="options">
    /// Backend-neutral rendering options.
    /// </param>
    /// <returns>
    /// A caller-owned backend-neutral rendering device.
    /// </returns>
    IRenderDevice CreateDevice(RenderingBackend backend, RenderingBackendOptions options);
}
