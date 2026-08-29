using System;

namespace Inno.Rendering.Assets;

/// <summary>
/// Converts supported artist texture sources into a validated portable runtime container.
/// </summary>
public interface ITextureTargetCompiler
{
    /// <summary>
    /// Compiles one source texture into an uncompressed KTX artifact with a complete mip chain.
    /// </summary>
    /// <param name="sourcePath">Absolute path to a supported source image.</param>
    /// <param name="colorSpace">Sampling color-space contract.</param>
    /// <returns>Complete KTX bytes suitable for a backend-neutral texture container upload.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the target compiler rejects the source.</exception>
    byte[] CompileKtx(string sourcePath, TextureColorSpace colorSpace);
}
