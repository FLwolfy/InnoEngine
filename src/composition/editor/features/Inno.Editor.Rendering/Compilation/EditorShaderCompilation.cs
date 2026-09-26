using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Graphs;
using Inno.Rendering;

namespace Inno.Editor.Rendering;

/// <summary>
/// Identifies native compilation state independently of document persistence.
/// </summary>
public enum EditorShaderCompilationState
{
    /// <summary>
    /// The latest semantic candidate is being compiled.
    /// </summary>
    Compiling,
    /// <summary>
    /// The latest semantic candidate compiled successfully.
    /// </summary>
    Succeeded,
    /// <summary>
    /// The latest semantic candidate failed.
    /// </summary>
    Failed
}

/// <summary>
/// Contains detached compiler state without retaining assets, tasks or extension instances.
/// </summary>
/// <param name="state">
/// Latest requested semantic candidate state.
/// </param>
/// <param name="usingLastGood">
/// Whether rendering still uses a previous successful candidate.
/// </param>
/// <param name="diagnostics">
/// Immutable latest completed compiler diagnostics.
/// </param>
public sealed record EditorShaderCompilationSnapshot(EditorShaderCompilationState state, bool usingLastGood,
    IReadOnlyList<ShaderDiagnostic> diagnostics);

/// <summary>
/// Contains a preview-only immutable candidate, never registered as a canonical asset artifact.
/// </summary>
/// <param name="state">
/// Latest draft compilation state.
/// </param>
/// <param name="usingLastGood">
/// Whether the preview artifact belongs to an earlier successful draft.
/// </param>
/// <param name="diagnostics">
/// Latest detached preview diagnostics, separate from project diagnostics.
/// </param>
/// <param name="artifact">
/// Preview-only complete shader candidate, or null before the first success.
/// </param>
public sealed record EditorShaderDraftCompilationSnapshot(EditorShaderCompilationState state, bool usingLastGood,
    IReadOnlyList<ShaderDiagnostic> diagnostics, RenderShaderArtifact? artifact);

/// <summary>
/// Schedules Editor shader compilation for the active device without exposing native handles to authoring panels.
/// </summary>
public sealed class EditorShaderCompilation
{
    private readonly EditorRenderTargetArtifactProvider m_provider;
    private readonly GraphicsCapabilities m_capabilities;
    private readonly IEditorShaderArtifactValidator? m_validator;
    /// <summary>
    /// Pairs the authoring artifact owner with the active device's immutable capability snapshot.
    /// </summary>
    /// <param name="provider">
    /// Host-owned shader compilation service.
    /// </param>
    /// <param name="capabilities">
    /// Current device capability snapshot.
    /// </param>
    public EditorShaderCompilation(EditorRenderTargetArtifactProvider provider, GraphicsCapabilities capabilities)
    { m_provider = provider ?? throw new ArgumentNullException(nameof(provider)); m_capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities)); }

    /// <summary>
    /// Pairs the authoring artifact owner with active-device validation at frame safety points.
    /// </summary>
    /// <param name="provider">
    /// Host-owned shader compilation service.
    /// </param>
    /// <param name="validator">
    /// Host-owned active-device artifact validator.
    /// </param>
    public EditorShaderCompilation(EditorRenderTargetArtifactProvider provider, IEditorShaderArtifactValidator validator)
    {
        m_provider = provider ?? throw new ArgumentNullException(nameof(provider));
        m_validator = validator ?? throw new ArgumentNullException(nameof(validator));
        m_capabilities = validator.capabilities;
    }
    /// <summary>
    /// Requests current compilation and reads its explicit last-good status.
    /// </summary>
    /// <param name="shader">
    /// Current generation shader asset, not retained by the service.
    /// </param>
    /// <param name="variant">
    /// Exact static keyword selection.
    /// </param>
    /// <returns>
    /// A detached status snapshot.
    /// </returns>
    public EditorShaderCompilationSnapshot Request(ShaderAsset shader, RenderShaderVariant variant)
        => m_provider.RequestShaderCompilation(shader, variant, m_capabilities);

    /// <summary>
    /// Reads the canonical Shader candidate for an isolated Material preview without modifying Material values.
    /// </summary>
    /// <param name="shader">
    /// Current owner Shader reference.
    /// </param>
    /// <param name="variant">
    /// Exact Material keyword selection.
    /// </param>
    /// <returns>
    /// Compilation status and immutable last-good artifact when available.
    /// </returns>
    public EditorShaderDraftCompilationSnapshot RequestArtifact(ShaderAsset shader, RenderShaderVariant variant)
    {
        EditorShaderCompilationSnapshot status = Request(shader, variant);
        m_provider.GetShaderArtifact(shader, variant, m_capabilities, out RenderShaderArtifact? artifact);
        return new(status.state, status.usingLastGood, status.diagnostics, artifact);
    }

    /// <summary>
    /// Decodes the exact contract captured with a candidate using the current owner's references.
    /// </summary>
    /// <param name="artifact">
    /// Complete candidate, not the latest uncompiled asset definition.
    /// </param>
    /// <returns>
    /// A detached current-generation definition.
    /// </returns>
    public ShaderDefinition ReadDefinition(RenderShaderArtifact artifact) => m_provider.ReadShaderDefinition(artifact);

    /// <summary>
    /// Compiles a detached document through the same graph toolchain in an isolated preview cache.
    /// </summary>
    /// <param name="documentId">
    /// Preview owner identity, independent of canonical artifact lookup.
    /// </param>
    /// <param name="graph">
    /// Current draft, never modified or retained.
    /// </param>
    /// <param name="revision">
    /// Monotonic draft revision, including Undo/Redo.
    /// </param>
    /// <param name="variant">
    /// Exact static keyword selection.
    /// </param>
    /// <returns>
    /// Preview-only state and artifact; calling this method cannot publish to Scene/Game.
    /// </returns>
    public EditorShaderDraftCompilationSnapshot RequestDraft(Guid documentId, GraphDocument graph, ulong revision, RenderShaderVariant variant)
    {
        EditorShaderDraftCompilationSnapshot compiled = m_provider.RequestDraft(
            documentId,
            graph,
            revision,
            variant,
            m_capabilities);
        if (m_validator is null || compiled.state != EditorShaderCompilationState.Succeeded
            || compiled.artifact is null)
            return compiled;

        EditorShaderArtifactValidationSnapshot validated = m_validator.Request(
            documentId,
            revision,
            compiled.artifact);
        IReadOnlyList<ShaderDiagnostic> diagnostics = compiled.diagnostics
            .Concat(validated.diagnostics)
            .ToArray();
        return validated.state switch
        {
            EditorShaderCompilationState.Compiling => new(validated.state, false, diagnostics, null),
            EditorShaderCompilationState.Failed => new(validated.state, false, diagnostics, null),
            _ => new(validated.state, false, diagnostics, compiled.artifact)
        };
    }

    /// <summary>
    /// Cancels a document's preview work and retires its cached candidate without touching canonical artifacts.
    /// </summary>
    /// <param name="documentId">
    /// Closing preview owner identity.
    /// </param>
    public void ReleaseDraft(Guid documentId)
    {
        m_validator?.Release(documentId);
        m_provider.ReleaseDraft(documentId);
    }
}
