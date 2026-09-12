using System.Collections.Generic;
using Inno.Rendering.Assets;

namespace Inno.Adapter.Rendering;

/// <summary>
/// Creates offline rendering compilers paired with a selected runtime rendering backend.
/// </summary>
public interface IRenderingAuthoringBackendFactory
{
    /// <summary>Gets the runtime backend identities supported by this authoring composition.</summary>
    IReadOnlyList<RenderingBackendId> supportedBackends { get; }

    /// <summary>
    /// Creates the shader compiler toolchain paired with the selected rendering backend.
    /// </summary>
    /// <param name="backend">
    /// Stable rendering backend identity selected by the authoring composition root.
    /// </param>
    /// <returns>
    /// A target compiler compatible with devices created for the same backend.
    /// </returns>
    IShaderCompilerToolchain CreateShaderCompilerToolchain(RenderingBackendId backend);

    /// <summary>
    /// Creates the texture compiler paired with the selected rendering backend.
    /// </summary>
    /// <param name="backend">
    /// Stable rendering backend identity selected by the authoring composition root.
    /// </param>
    /// <returns>
    /// A texture target compiler compatible with devices created for the same backend.
    /// </returns>
    ITextureTargetCompiler CreateTextureTargetCompiler(RenderingBackendId backend);
}
