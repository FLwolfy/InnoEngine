using System;
using System.Collections.Generic;

namespace Inno.Rendering.Assets;

/// <summary>
/// Describes the artifact selected after evaluating a candidate compilation.
/// </summary>
public sealed class ShaderArtifactSelection
{
    /// <summary>
    /// Creates an artifact selection result.
    /// </summary>
    /// <param name="artifact">Selected candidate or last-good artifact.</param>
    /// <param name="candidateSucceeded">Whether the candidate replaced active state.</param>
    /// <param name="usingLastGood">Whether a previous artifact was preserved.</param>
    /// <param name="diagnostics">Candidate diagnostics.</param>
    public ShaderArtifactSelection(
        CompiledShaderArtifact? artifact,
        bool candidateSucceeded,
        bool usingLastGood,
        IReadOnlyList<ShaderDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        this.artifact = artifact;
        this.candidateSucceeded = candidateSucceeded;
        this.usingLastGood = usingLastGood;
        this.diagnostics = diagnostics;
    }

    /// <summary>Gets the selected candidate or last-good artifact.</summary>
    public CompiledShaderArtifact? artifact { get; }

    /// <summary>Gets whether the candidate replaced active state.</summary>
    public bool candidateSucceeded { get; }

    /// <summary>Gets whether a previous artifact was preserved.</summary>
    public bool usingLastGood { get; }

    /// <summary>Gets candidate diagnostics.</summary>
    public IReadOnlyList<ShaderDiagnostic> diagnostics { get; }
}

/// <summary>
/// Atomically preserves last-good CPU shader artifacts by asset, target and variant.
/// </summary>
public sealed class ShaderLastGoodStore
{
    private readonly object m_gate = new();
    private readonly Dictionary<ArtifactKey, CompiledShaderArtifact> m_artifacts = [];

    /// <summary>
    /// Commits a complete candidate or returns the current last-good artifact after failure.
    /// </summary>
    /// <param name="shaderId">Persistent shader asset identity.</param>
    /// <param name="targetKey">Stable target cache key.</param>
    /// <param name="variant">Static keyword variant.</param>
    /// <param name="candidate">Candidate compilation result.</param>
    /// <returns>The artifact safe to use for rendering.</returns>
    public ShaderArtifactSelection Select(
        Guid shaderId,
        string targetKey,
        ShaderVariantKey variant,
        ShaderCompilationResult candidate)
    {
        if (shaderId == Guid.Empty)
        {
            throw new ArgumentException("A persistent shader identity is required.", nameof(shaderId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(targetKey);
        ArgumentNullException.ThrowIfNull(candidate);
        var key = new ArtifactKey(shaderId, targetKey, variant.value);
        lock (m_gate)
        {
            if (candidate.succeeded)
            {
                m_artifacts[key] = candidate.artifact!;
                return new ShaderArtifactSelection(
                    candidate.artifact,
                    candidateSucceeded: true,
                    usingLastGood: false,
                    candidate.diagnostics);
            }

            m_artifacts.TryGetValue(key, out CompiledShaderArtifact? lastGood);
            return new ShaderArtifactSelection(
                lastGood,
                candidateSucceeded: false,
                usingLastGood: lastGood is not null,
                candidate.diagnostics);
        }
    }

    /// <summary>
    /// Removes all CPU artifacts associated with one persistent shader.
    /// </summary>
    /// <param name="shaderId">Persistent shader asset identity.</param>
    /// <returns>The number of removed variants.</returns>
    public int Remove(Guid shaderId)
    {
        if (shaderId == Guid.Empty)
        {
            return 0;
        }

        lock (m_gate)
        {
            int removed = 0;
            foreach (ArtifactKey key in new List<ArtifactKey>(m_artifacts.Keys))
            {
                if (key.shaderId == shaderId && m_artifacts.Remove(key))
                {
                    removed++;
                }
            }

            return removed;
        }
    }

    private readonly record struct ArtifactKey(Guid shaderId, string targetKey, string variantKey);
}
