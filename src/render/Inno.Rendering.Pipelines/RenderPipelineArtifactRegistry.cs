using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Rendering.Core;
using Inno.Rendering.Assets;

namespace Inno.Rendering.Pipelines;

/// <summary>
/// Stores target-compiled shader candidates by stable asset or built-in operation identity.
/// </summary>
public sealed class RenderPipelineArtifactRegistry
{
    private readonly object m_lock = new();
    private readonly Dictionary<Guid, List<ShaderCandidate>> m_shaders = [];
    private readonly Dictionary<string, OperationCandidate> m_operations = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, TextureCandidate> m_textures = [];

    /// <summary>
    /// Installs a complete target artifact candidate for one imported shader and static variant.
    /// </summary>
    /// <param name="shader">Imported shader with a non-empty persistent identity.</param>
    /// <param name="artifact">Complete target artifact produced by the shared compiler.</param>
    public void InstallShader(ShaderAsset shader, CompiledShaderArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(shader);
        ArgumentNullException.ThrowIfNull(artifact);
        Guid persistentId = shader.identity.persistentId;
        if (persistentId == Guid.Empty)
        {
            throw new ArgumentException("A registered shader requires a persistent asset identity.", nameof(shader));
        }

        lock (m_lock)
        {
            if (!m_shaders.TryGetValue(persistentId, out List<ShaderCandidate>? candidates))
            {
                candidates = [];
                m_shaders.Add(persistentId, candidates);
            }

            candidates.RemoveAll(candidate => candidate.artifact.variant == artifact.variant);
            candidates.Add(new ShaderCandidate(shader.contentVersion, artifact));
        }
    }

    /// <summary>
    /// Installs a complete target artifact for one built-in or extension operation.
    /// </summary>
    /// <param name="operationId">Stable operation identity.</param>
    /// <param name="artifact">Complete target artifact produced by the shared compiler.</param>
    /// <param name="passName">Stable pass name selected from the artifact.</param>
    public void InstallOperation(
        string operationId,
        CompiledShaderArtifact artifact,
        string passName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentException.ThrowIfNullOrWhiteSpace(passName);
        if (!artifact.passes.Any(pass => string.Equals(pass.definition.name, passName, StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                $"Artifact '{artifact.shaderName}' has no pass named '{passName}'.",
                nameof(passName));
        }

        lock (m_lock)
        {
            m_operations[operationId] = new OperationCandidate(artifact, passName);
        }
    }

    /// <summary>
    /// Installs every fullscreen or compute pass that declares a stable pipeline-operation metadata value.
    /// </summary>
    /// <param name="artifact">Complete target artifact produced by the shared compiler.</param>
    /// <returns>The stable operation IDs installed from the artifact.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when an operation ID is empty or appears on a scene-material pass.
    /// </exception>
    public IReadOnlyList<string> InstallTaggedOperations(CompiledShaderArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        var installed = new List<string>();
        foreach (CompiledShaderPass pass in artifact.passes)
        {
            if (!pass.definition.tags.TryGetValue(
                    BuiltinShaderMetadataTags.PipelineOperation,
                    out string? operationId))
            {
                continue;
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
            bool compute = pass.stages.Any(static stage => stage.stage == ShaderStage.Compute);
            bool fullscreen = string.Equals(
                pass.definition.tag,
                BuiltinShaderPassTags.Fullscreen,
                StringComparison.Ordinal);
            if (!compute && !fullscreen)
            {
                throw new ArgumentException(
                    $"Shader pass '{pass.definition.name}' declares a pipeline operation but is neither Compute nor Fullscreen.",
                    nameof(artifact));
            }

            InstallOperation(operationId, artifact, pass.definition.name);
            installed.Add(operationId);
        }

        return installed;
    }

    /// <summary>
    /// Installs a device texture produced by the target texture build and upload path.
    /// </summary>
    /// <param name="texture">Imported texture with a non-empty persistent identity.</param>
    /// <param name="handle">Opaque texture owned by the active device generation.</param>
    public void InstallTexture(TextureAsset texture, PersistentTextureHandle handle)
    {
        ArgumentNullException.ThrowIfNull(texture);
        if (!handle.isValid)
        {
            throw new ArgumentException("An installed texture requires a valid device handle.", nameof(handle));
        }

        Guid persistentId = texture.identity.persistentId;
        if (persistentId == Guid.Empty)
        {
            throw new ArgumentException("A registered texture requires a persistent asset identity.", nameof(texture));
        }

        lock (m_lock)
        {
            m_textures[persistentId] = new TextureCandidate(texture.contentVersion, handle);
        }
    }

    /// <summary>Removes every candidate while leaving already-created GPU last-good resources untouched.</summary>
    public void Clear()
    {
        lock (m_lock)
        {
            m_shaders.Clear();
            m_operations.Clear();
            m_textures.Clear();
        }
    }

    internal bool TryGetShader(
        ShaderAsset shader,
        IReadOnlySet<string> enabledOptions,
        out ShaderCandidate candidate)
    {
        Guid persistentId = shader.identity.persistentId;
        lock (m_lock)
        {
            if (persistentId != Guid.Empty
                && m_shaders.TryGetValue(persistentId, out List<ShaderCandidate>? candidates))
            {
                ShaderCandidate? match = candidates.FirstOrDefault(value =>
                    value.artifact.variant.options.Values.ToHashSet(StringComparer.Ordinal)
                        .SetEquals(enabledOptions));
                if (match is not null)
                {
                    candidate = match;
                    return true;
                }
            }
        }

        candidate = null!;
        return false;
    }

    internal bool TryGetOperation(string operationId, out OperationCandidate candidate)
    {
        lock (m_lock)
        {
            return m_operations.TryGetValue(operationId, out candidate!);
        }
    }

    internal bool TryGetTexture(TextureAsset texture, out TextureCandidate candidate)
    {
        lock (m_lock)
        {
            return m_textures.TryGetValue(texture.identity.persistentId, out candidate!);
        }
    }

    internal sealed record ShaderCandidate(long sourceContentVersion, CompiledShaderArtifact artifact);

    internal sealed record OperationCandidate(CompiledShaderArtifact artifact, string passName);

    internal sealed record TextureCandidate(long sourceContentVersion, PersistentTextureHandle handle);
}
