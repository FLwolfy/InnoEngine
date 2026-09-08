using Inno.Platform;

namespace Inno.Adapter.Input;

/// <summary>
/// Creates host-level input event sources from explicit backend selections.
/// </summary>
public interface IInputBackendFactory
{
    /// <summary>
    /// Creates an input event source associated with one primary window.
    /// </summary>
    /// <param name="backend">
    /// Built-in input backend selected by the composition root.
    /// </param>
    /// <param name="window">
    /// Primary platform window whose input is accepted by the source.
    /// </param>
    /// <returns>
    /// A caller-owned backend-neutral input event source.
    /// </returns>
    /// <exception cref="System.NotSupportedException">
    /// Thrown when the catalog does not contain the selected backend or the platform is incompatible.
    /// </exception>
    IInputEventSource CreateEventSource(InputBackend backend, IPlatformWindow window);
}
