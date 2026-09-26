using System;
using System.Collections.Generic;
using Inno.Core.Diagnostics;
using Inno.Editor.Core;
using Inno.Editor.ImGui.ImGuiWidget;
using Inno.Editor.Rendering;
using Inno.Rendering;

namespace Inno.Editor.Panel.ShaderEditor;

/// <summary>
/// Displays transient draft-check progress and publishes completed diagnostics to the shared Console.
/// </summary>
/// <param name="documents">
/// The shader editor documents value used to initialize this instance.
/// </param>
[EditorModal("shader.check", "Checking Shader", order: 220)]
internal sealed class ShaderCheckModal(ShaderEditorDocuments documents) : EditorModal
{
    private const string C_DIAGNOSTIC_GROUP = "Shader Check";
    private string m_status = "Checking the current Shader draft…";

    /// <summary>
    /// Gets whether this value is visible.
    /// </summary>
public override bool isVisible => documents.TryGetCheckDraft(out _);

    /// <summary>
    /// Renders this feature using the current editor presentation context.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
protected override void OnDraw(EditorContext context)
    {
        _ = context;
        if (documents.TryGetCheckDraft(out ShaderEditorDocuments.Draft? draft))
        {
            EditorShaderDraftCompilationSnapshot snapshot = documents.Check(draft);
            if (snapshot.state == EditorShaderCompilationState.Compiling)
            {
                m_status = "Checking the current Shader draft…";
            }
            else
            {
                Publish(draft, snapshot);
                m_status = snapshot.state == EditorShaderCompilationState.Succeeded
                    ? "Shader check completed."
                    : "Shader check failed. See Console diagnostics.";
                documents.CloseCheck();
            }
        }

        // Default modal policy is shared with script compilation: the same fixed width,
        // centered placement, blocking behavior, fade timing and content-sized height.
        ImGuiWidget.WrappedText(m_status);
    }

    private static void Publish(
        ShaderEditorDocuments.Draft draft,
        EditorShaderDraftCompilationSnapshot snapshot)
    {
        var diagnostics = new List<Diagnostic>(Math.Max(1, snapshot.diagnostics.Count));
        for (int index = 0; index < snapshot.diagnostics.Count; index++)
        {
            ShaderDiagnostic shader = snapshot.diagnostics[index];
            DiagnosticLocation? location = shader.location is ShaderSourceLocation source
                ? new(source.assetPath, source.line, source.column)
                : null;
            diagnostics.Add(shader.severity switch
            {
                DiagnosticSeverity.Info => Diagnostic.Info(shader.code, shader.message, location),
                DiagnosticSeverity.Warning => Diagnostic.Warning(shader.code, shader.message, location),
                DiagnosticSeverity.Error => Diagnostic.Error(shader.code, shader.message, location),
                _ => Diagnostic.Error(shader.code, shader.message, location)
            });
        }

        if (diagnostics.Count == 0)
        {
            diagnostics.Add(snapshot.state == EditorShaderCompilationState.Succeeded
                ? Diagnostic.Info("SHADER_CHECK_SUCCEEDED", "Shader check completed without diagnostics.")
                : Diagnostic.Error("SHADER_CHECK_FAILED", "Shader check failed without a compiler diagnostic."));
        }
        Diagnostics.Set(draft.id, C_DIAGNOSTIC_GROUP, diagnostics, draft.path.ToString());
    }
}
