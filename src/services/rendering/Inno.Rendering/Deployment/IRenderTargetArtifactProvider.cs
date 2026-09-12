using System;

namespace Inno.Rendering;

/// <summary>
/// Resolves immutable, source-free target artifacts for the active rendering device.
/// </summary>
public interface IRenderTargetArtifactProvider
{
    /// <summary>Resolves the exact material contract stored with a compiled program using the current owner reference context.</summary>
    /// <param name="artifact">One immutable program publication, including its native-serialized runtime definition.</param>
    /// <returns>A detached definition belonging to this artifact, never the latest uncompiled source definition.</returns>
    /// <exception cref="InvalidOperationException">The publication cannot be decoded in the current owner generation.</exception>
    ShaderDefinition ReadShaderDefinition(RenderShaderArtifact artifact);

    /// <summary>
    /// Resolves the target shader matching one runtime asset, variant, and device capability snapshot.
    /// </summary>
    /// <param name="shader">
    /// The imported runtime shader description.
    /// </param>
    /// <param name="variant">
    /// The exact material keyword selection.
    /// </param>
    /// <param name="capabilities">
    /// The active device capabilities used to select a packaged backend target.
    /// </param>
    /// <param name="artifact">
    /// Receives the validated source-free artifact when it exists.
    /// </param>
    /// <returns>
    /// The current artifact availability. <see cref="RenderTargetArtifactStatus.Ready"/> guarantees that
    /// <paramref name="artifact"/> is non-null.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="shader"/> or <paramref name="capabilities"/> is <see langword="null"/>.
    /// </exception>
    RenderTargetArtifactStatus GetShaderArtifact(
        ShaderAsset shader,
        RenderShaderVariant variant,
        GraphicsCapabilities capabilities,
        out RenderShaderArtifact? artifact);

    /// <summary>
    /// Resolves the portable KTX artifact for one imported runtime texture.
    /// </summary>
    /// <param name="texture">
    /// Stable reference to the imported texture slot.
    /// </param>
    /// <param name="artifact">
    /// Receives immutable KTX bytes when the artifact exists and is non-empty.
    /// </param>
    /// <returns>
    /// The current artifact availability. <see cref="RenderTargetArtifactStatus.Ready"/> guarantees that
    /// <paramref name="artifact"/> is non-empty.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="texture"/> is invalid.
    /// </exception>
    RenderTargetArtifactStatus GetTextureArtifact(
        RenderTextureArtifactReference texture,
        out ReadOnlyMemory<byte> artifact);
}
