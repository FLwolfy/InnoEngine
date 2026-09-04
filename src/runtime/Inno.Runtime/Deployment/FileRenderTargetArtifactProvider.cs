using System;
using System.IO;
using Inno.Rendering;

namespace Inno.Runtime;

/// <summary>
/// Reads immutable render target artifacts from one verified materialized content deployment.
/// </summary>
public sealed class FileRenderTargetArtifactProvider : IRenderTargetArtifactProvider
{
    private readonly string m_contentRoot;

    /// <summary>
    /// Creates a provider rooted at one source-free runtime content directory.
    /// </summary>
    /// <param name="contentRoot">
    /// The verified directory materialized from the deployed content pack.
    /// </param>
    /// <exception cref="DirectoryNotFoundException">
    /// Thrown when the content root does not exist.
    /// </exception>
    public FileRenderTargetArtifactProvider(string contentRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        m_contentRoot = Path.GetFullPath(contentRoot);
        if (!Directory.Exists(m_contentRoot))
            throw new DirectoryNotFoundException($"Runtime content root '{m_contentRoot}' does not exist.");
    }

    /// <summary>
    /// Loads and validates one packaged shader target artifact when it exists.
    /// </summary>
    /// <param name="shader">
    /// The imported backend-neutral shader asset.
    /// </param>
    /// <param name="variant">
    /// The exact material keyword selection.
    /// </param>
    /// <param name="capabilities">
    /// The active graphics capability snapshot.
    /// </param>
    /// <param name="artifact">
    /// Receives the decoded artifact when present.
    /// </param>
    /// <returns>
    /// <see cref="RenderTargetArtifactStatus.Ready"/> when the deployed artifact exists; otherwise,
    /// <see cref="RenderTargetArtifactStatus.Unavailable"/>.
    /// </returns>
    /// <exception cref="InvalidDataException">
    /// Thrown when a deployed artifact exists but is corrupt or mismatched.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="shader"/> or <paramref name="capabilities"/> is <see langword="null"/>.
    /// </exception>
    public RenderTargetArtifactStatus GetShaderArtifact(
        ShaderAsset shader,
        RenderShaderVariant variant,
        GraphicsCapabilities capabilities,
        out RenderShaderArtifact? artifact)
    {
        ArgumentNullException.ThrowIfNull(shader);
        ArgumentNullException.ThrowIfNull(capabilities);
        string path = Resolve(RenderTargetArtifactPath.GetShaderPath(
            shader.identity.persistentId,
            capabilities.backend,
            variant));
        if (!File.Exists(path))
        {
            artifact = null;
            return RenderTargetArtifactStatus.Unavailable;
        }
        try
        {
            ShaderDefinition definition = shader.definition
                ?? throw new InvalidDataException(
                    $"Runtime shader '{shader.assetPath}' has no committed definition.");
            artifact = RenderShaderArtifactCodec.Decode(
                File.ReadAllBytes(path),
                definition.name,
                variant);
            return RenderTargetArtifactStatus.Ready;
        }
        catch (Exception exception) when (exception is not InvalidDataException)
        {
            throw new InvalidDataException(
                $"Deployed shader artifact '{path}' failed integrity validation.",
                exception);
        }
    }

    /// <summary>
    /// Loads one packaged portable texture artifact when it exists.
    /// </summary>
    /// <param name="texture">
    /// The imported texture description.
    /// </param>
    /// <param name="artifact">
    /// Receives immutable KTX bytes when present.
    /// </param>
    /// <returns>
    /// <see cref="RenderTargetArtifactStatus.Ready"/> when the deployed artifact exists; otherwise,
    /// <see cref="RenderTargetArtifactStatus.Unavailable"/>.
    /// </returns>
    /// <exception cref="InvalidDataException">
    /// Thrown when a deployed texture artifact is empty.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="texture"/> is <see langword="null"/>.
    /// </exception>
    public RenderTargetArtifactStatus GetTextureArtifact(
        TextureAsset texture,
        out ReadOnlyMemory<byte> artifact)
    {
        ArgumentNullException.ThrowIfNull(texture);
        string path = Resolve(RenderTargetArtifactPath.GetTexturePath(texture.identity.persistentId));
        if (!File.Exists(path))
        {
            artifact = ReadOnlyMemory<byte>.Empty;
            return RenderTargetArtifactStatus.Unavailable;
        }
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0)
            throw new InvalidDataException($"Deployed texture artifact '{path}' is empty.");
        artifact = bytes;
        return RenderTargetArtifactStatus.Ready;
    }

    private string Resolve(string relativePath)
    {
        string result = Path.GetFullPath(Path.Combine(m_contentRoot, relativePath));
        string prefix = Path.TrimEndingDirectorySeparator(m_contentRoot) + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!result.StartsWith(prefix, comparison))
            throw new InvalidDataException("A target artifact path escaped the runtime content root.");
        return result;
    }
}
