using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.Diagnose;
using Inno.Engine.Scene.Assets;

namespace Inno.Editor.Scripting;

internal static class ScriptDiagnosticPublisher
{
    private const string C_COMPILER_DIAGNOSTICS = "Script Compiler";
    private const string C_RELOAD_DIAGNOSTICS = "Script Reload";

    internal static void PublishCompilation(ScriptCompilationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Diagnostics.Set(
            C_COMPILER_DIAGNOSTICS,
            result.diagnostics.Select(CreateCompilationDiagnostic));
    }

    internal static void PublishReload(IReadOnlyList<SceneReloadDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        Diagnostics.Set(
            C_RELOAD_DIAGNOSTICS,
            diagnostics.Select(static diagnostic => diagnostic.severity switch
            {
                SceneReloadDiagnosticSeverity.Warning => Diagnostic.Warning(diagnostic.code, diagnostic.message),
                _ => Diagnostic.Error(diagnostic.code, diagnostic.message)
            }));
    }

    internal static void PublishReloadFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Diagnostics.Set(
            C_RELOAD_DIAGNOSTICS,
            Diagnostic.Error("INNO-RELOAD", exception.ToString()));
    }

    internal static void ClearReload()
        => Diagnostics.Clear(C_RELOAD_DIAGNOSTICS);

    internal static void ClearAll()
    {
        Diagnostics.Clear(C_COMPILER_DIAGNOSTICS);
        Diagnostics.Clear(C_RELOAD_DIAGNOSTICS);
    }

    private static Diagnostic CreateCompilationDiagnostic(ScriptDiagnostic diagnostic)
    {
        DiagnosticLocation? location = string.IsNullOrWhiteSpace(diagnostic.filePath)
            ? null
            : new DiagnosticLocation(
                diagnostic.filePath,
                diagnostic.line,
                diagnostic.column);
        return diagnostic.severity switch
        {
            ScriptDiagnosticSeverity.Info => Diagnostic.Info(
                diagnostic.id,
                diagnostic.message,
                location),
            ScriptDiagnosticSeverity.Warning => Diagnostic.Warning(
                diagnostic.id,
                diagnostic.message,
                location),
            ScriptDiagnosticSeverity.Error => Diagnostic.Error(
                diagnostic.id,
                diagnostic.message,
                location),
            _ => Diagnostic.Error(
                diagnostic.id,
                diagnostic.message,
                location)
        };
    }
}
