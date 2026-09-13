using System;
using Inno.Core.Diagnostics;
using Inno.Editor.Rendering;
using Inno.Rendering;

namespace Inno.Editor.Shaders;

/// <summary>Associates a reloadable preview implementation with one open Shader contract.</summary>
/// <param name="contractId">Non-empty domain-owned contract identity.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ShaderPreviewProviderAttribute(string contractId) : Attribute
{
    /// <summary>Gets the non-empty Shader contract consumed by this preview.</summary>
    public string contractId { get; } = string.IsNullOrWhiteSpace(contractId)
        ? throw new ArgumentException("A preview contract is required.", nameof(contractId)) : contractId;
}

/// <summary>Builds domain-owned preview geometry and pass inputs without changing a scene or canonical asset.</summary>
public abstract class ShaderPreviewProvider
{
    /// <summary>Creates one frame-local rendering layer through the ordinary Render Graph.</summary>
    /// <param name="context">Detached Material, immutable candidate and isolated diagnostics for this invocation.</param>
    /// <returns>A domain-selected pipeline and frame payload; no instance may be retained beyond the submitted frame.</returns>
    public abstract EditorViewportLayer CreateLayer(ShaderPreviewContext context);
}

/// <summary>Carries frame-only preview inputs; persistent provider state must retain stable values rather than this object.</summary>
public sealed class ShaderPreviewContext
{
    /// <summary>Creates a preview invocation from host-owned detached inputs.</summary>
    /// <param name="resourceId">Isolated material publication and viewport resource identity.</param>
    /// <param name="material">Detached values with a current Shader reference; do not modify the reference.</param>
    /// <param name="artifact">Compiled candidate, never published as a canonical Shader.</param>
    /// <param name="definition">Detached contract captured by the candidate.</param>
    /// <param name="diagnostics">Preview-local reporter; errors must not contaminate project diagnostics.</param>
    /// <param name="pixelWidth">Positive target width.</param>
    /// <param name="pixelHeight">Positive target height.</param>
    public ShaderPreviewContext(RenderPersistentResourceId resourceId, MaterialAsset material, RenderShaderArtifact artifact,
        ShaderDefinition definition, IDiagnosticReporter diagnostics, int pixelWidth, int pixelHeight)
    {
        if (!resourceId.isValid) throw new ArgumentException("A resource identity is required.", nameof(resourceId));
        ArgumentNullException.ThrowIfNull(material); ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(definition); ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelWidth); ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelHeight);
        this.resourceId = resourceId; this.material = material; this.artifact = artifact; this.definition = definition;
        this.diagnostics = diagnostics; this.pixelWidth = pixelWidth; this.pixelHeight = pixelHeight;
    }
    /// <summary>Gets the publication scope released by the host when this preview closes.</summary>
    public RenderPersistentResourceId resourceId { get; }
    /// <summary>Gets the invocation-local Material values.</summary>
    public MaterialAsset material { get; }
    /// <summary>Gets the complete immutable GPU candidate.</summary>
    public RenderShaderArtifact artifact { get; }
    /// <summary>Gets the candidate's exact contract, not the latest uncompiled source interface.</summary>
    public ShaderDefinition definition { get; }
    /// <summary>Gets the host-owned, isolated diagnostic producer.</summary>
    public IDiagnosticReporter diagnostics { get; }
    /// <summary>Gets target width in physical pixels.</summary>
    public int pixelWidth { get; }
    /// <summary>Gets target height in physical pixels.</summary>
    public int pixelHeight { get; }
}
