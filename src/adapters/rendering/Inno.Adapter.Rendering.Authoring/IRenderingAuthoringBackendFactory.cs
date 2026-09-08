using Inno.Rendering.Assets;

namespace Inno.Adapter.Rendering;

/// <summary>
/// Creates offline rendering compilers paired with a selected runtime rendering backend.
/// </summary>
public interface IRenderingAuthoringBackendFactory
{
    /// <summary>
    /// Creates the shader compiler toolchain paired with the selected rendering backend.
    /// </summary>
    /// <param name="backend">
    /// Built-in rendering backend selected by the authoring composition root.
    /// </param>
    /// <returns>
    /// A target compiler compatible with devices created for the same backend.
    /// </returns>
    IShaderCompilerToolchain CreateShaderCompilerToolchain(RenderingBackend backend);

    /// <summary>
    /// Creates the texture compiler paired with the selected rendering backend.
    /// </summary>
    /// <param name="backend">
    /// Built-in rendering backend selected by the authoring composition root.
    /// </param>
    /// <returns>
    /// A texture target compiler compatible with devices created for the same backend.
    /// </returns>
    ITextureTargetCompiler CreateTextureTargetCompiler(RenderingBackend backend);
}
