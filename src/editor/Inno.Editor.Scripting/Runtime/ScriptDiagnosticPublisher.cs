using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.Diagnostics;
using Inno.Engine.Scene.Assets;

namespace Inno.Editor.Scripting;

internal static class ScriptDiagnosticPublisher
{
    private static readonly DiagnosticSource COMPILER_SOURCE = new(
        "editor.scripting.compiler",
        "Script Compiler");
    private static readonly DiagnosticSource RELOAD_SOURCE = new(
        "editor.scripting.reload",
        "Script Reload");

    internal static void PublishCompilation(ScriptCompilationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        DiagnosticManager.Publish(
            COMPILER_SOURCE,
            result.diagnostics.Select(CreateCompilationDiagnostic));
    }

    internal static void PublishReload(IReadOnlyList<SceneReloadDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        DiagnosticManager.Publish(
            RELOAD_SOURCE,
            diagnostics.Select(static diagnostic => new Diagnostic(
                diagnostic.severity == SceneReloadDiagnosticSeverity.Warning
                    ? DiagnosticSeverity.Warning
                    : DiagnosticSeverity.Error,
                diagnostic.code,
                diagnostic.message)));
    }

    internal static void PublishReloadFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        DiagnosticManager.Publish(
            RELOAD_SOURCE,
            [new Diagnostic(DiagnosticSeverity.Error, "INNO-RELOAD", exception.ToString())]);
    }

    internal static void ClearReload()
        => DiagnosticManager.Clear(RELOAD_SOURCE);

    internal static void ClearAll()
    {
        DiagnosticManager.Clear(COMPILER_SOURCE);
        DiagnosticManager.Clear(RELOAD_SOURCE);
    }

    private static Diagnostic CreateCompilationDiagnostic(ScriptDiagnostic diagnostic)
    {
        DiagnosticLocation? location = string.IsNullOrWhiteSpace(diagnostic.filePath)
            ? null
            : new DiagnosticLocation(
                diagnostic.filePath,
                diagnostic.line,
                diagnostic.column);
        return new Diagnostic(
            diagnostic.severity switch
            {
                ScriptDiagnosticSeverity.Info => DiagnosticSeverity.Info,
                ScriptDiagnosticSeverity.Warning => DiagnosticSeverity.Warning,
                ScriptDiagnosticSeverity.Error => DiagnosticSeverity.Error,
                _ => DiagnosticSeverity.Error
            },
            diagnostic.id,
            diagnostic.message,
            location);
    }
}
