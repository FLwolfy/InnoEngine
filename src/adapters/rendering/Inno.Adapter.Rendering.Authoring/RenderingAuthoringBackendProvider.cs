using Inno.Rendering.Assets;

namespace Inno.Adapter.Rendering;

/// <summary>Supplies authoring tools paired with one runtime rendering implementation.</summary>
public abstract class RenderingAuthoringBackendProvider
{
    /// <summary>Gets the stable runtime implementation identity served by these tools.</summary>
    public abstract RenderingBackendId id { get; }

    /// <summary>Creates the shader toolchain for this implementation.</summary>
    /// <returns>A compiler compatible with devices registered under the same identity.</returns>
    public abstract IShaderCompilerToolchain CreateShaderCompilerToolchain();

    /// <summary>Creates the texture compiler for this implementation.</summary>
    /// <returns>A compiler compatible with devices registered under the same identity.</returns>
    public abstract ITextureTargetCompiler CreateTextureTargetCompiler();
}
