using Inno.Adapter.Rendering;

namespace Inno.Adapter.Presentation;

/// <summary>
/// Extends the runtime adapter catalog with tooling and presentation factories required by authoring products.
/// </summary>
public interface IAuthoringAdapterCatalog : IAdapterCatalog
{
    /// <summary>
    /// Gets the rendering toolchain factory used to compile authoring artifacts.
    /// </summary>
    IRenderingAuthoringBackendFactory renderingAuthoring { get; }

    /// <summary>
    /// Gets the graphical host-presentation factory used by authoring products.
    /// </summary>
    IPresentationBackendFactory presentation { get; }
}
