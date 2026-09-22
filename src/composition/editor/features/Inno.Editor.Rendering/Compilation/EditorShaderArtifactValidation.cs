using System;
using System.Collections.Generic;
using Inno.Rendering;

namespace Inno.Editor.Rendering;

/// <summary>Contains active-device validation state for one immutable Editor shader candidate.</summary>
/// <param name="state">Pending, successful, or failed validation state.</param>
/// <param name="diagnostics">Detached device validation diagnostics.</param>
public sealed record EditorShaderArtifactValidationSnapshot(
    EditorShaderCompilationState state,
    IReadOnlyList<ShaderDiagnostic> diagnostics);

/// <summary>Queues immutable shader candidates for validation at the active device's next frame safety point.</summary>
public interface IEditorShaderArtifactValidator
{
    /// <summary>Gets the capability snapshot used to compile candidates for this validator.</summary>
    GraphicsCapabilities capabilities { get; }

    /// <summary>Requests or reads validation for an exact document revision and immutable artifact.</summary>
    /// <param name="documentId">Stable Editor document identity.</param>
    /// <param name="revision">Exact document revision that produced the artifact.</param>
    /// <param name="artifact">Complete immutable candidate compiled for the active device.</param>
    /// <returns>Current detached validation state.</returns>
    EditorShaderArtifactValidationSnapshot Request(
        Guid documentId,
        ulong revision,
        RenderShaderArtifact artifact);

    /// <summary>Forgets pending and completed validation state owned by one document.</summary>
    /// <param name="documentId">Closing or invalidated document identity.</param>
    void Release(Guid documentId);
}
