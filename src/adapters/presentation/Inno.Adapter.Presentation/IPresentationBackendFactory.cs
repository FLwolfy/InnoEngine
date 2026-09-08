namespace Inno.Adapter.Presentation;

/// <summary>
/// Creates host presentation contexts from explicit backend selections.
/// </summary>
public interface IPresentationBackendFactory
{
    /// <summary>
    /// Creates a presentation context over compatible platform and rendering adapters.
    /// </summary>
    /// <param name="backend">
    /// Built-in presentation backend selected by the composition root.
    /// </param>
    /// <param name="options">
    /// Host-owned resources and presentation policy.
    /// </param>
    /// <returns>
    /// A caller-owned backend-neutral presentation context.
    /// </returns>
    /// <exception cref="System.NotSupportedException">
    /// Thrown when the selected platform, rendering, and presentation backends are incompatible.
    /// </exception>
    IPresentationContext CreateContext(PresentationBackend backend, PresentationBackendOptions options);
}
