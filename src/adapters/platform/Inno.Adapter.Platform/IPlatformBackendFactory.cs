using Inno.Platform;

namespace Inno.Adapter.Platform;

/// <summary>
/// Creates backend-neutral platform applications from explicit backend selections.
/// </summary>
public interface IPlatformBackendFactory
{
    /// <summary>
    /// Creates a new platform application for the selected backend.
    /// </summary>
    /// <param name="backend">
    /// Built-in platform backend selected by the composition root.
    /// </param>
    /// <returns>
    /// A caller-owned backend-neutral platform application.
    /// </returns>
    /// <exception cref="System.NotSupportedException">
    /// Thrown when the catalog does not contain the selected backend.
    /// </exception>
    IPlatformApplication CreateApplication(PlatformBackend backend);
}
