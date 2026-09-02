using System;
using System.Linq;

using Inno.Core.Diagnostics;
using Inno.Scripting.Compiler;

namespace Inno.Editor.Scripting;

internal static class ScriptDiagnosticPublisher
{
    private const string C_COMPILER_DIAGNOSTICS = "Script Compiler";
    private const string C_IDE_PROJECTION_DIAGNOSTICS = "Script IDE Projection";
    private const string C_RELOAD_DIAGNOSTICS = "Script Reload";
    private const string C_UNLOAD_DIAGNOSTICS = "Script Unload";

    internal static void PublishCompilation(ScriptCompilationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Diagnostics.Set(
            C_COMPILER_DIAGNOSTICS,
            result.diagnostics.Select(CreateCompilationDiagnostic));
    }

    internal static void PublishReloadFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Diagnostics.Set(
            C_RELOAD_DIAGNOSTICS,
            Diagnostic.Error("INNO-RELOAD", exception.ToString()));
    }

    internal static void PublishIdeProjectionFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Diagnostics.Set(
            C_IDE_PROJECTION_DIAGNOSTICS,
            Diagnostic.Warning("INNO-IDE-PROJECTION", exception.ToString()));
    }

    internal static void PublishUnloadFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Diagnostics.Set(
            C_UNLOAD_DIAGNOSTICS,
            Diagnostic.Error("INNO-ALC-UNLOAD", exception.Message));
    }

    internal static void ClearReload()
        => Diagnostics.Clear(C_RELOAD_DIAGNOSTICS);

    internal static void ClearIdeProjection()
        => Diagnostics.Clear(C_IDE_PROJECTION_DIAGNOSTICS);

    internal static void ClearUnload()
        => Diagnostics.Clear(C_UNLOAD_DIAGNOSTICS);

    internal static void ClearAll()
    {
        Diagnostics.Clear(C_COMPILER_DIAGNOSTICS);
        Diagnostics.Clear(C_IDE_PROJECTION_DIAGNOSTICS);
        Diagnostics.Clear(C_RELOAD_DIAGNOSTICS);
        Diagnostics.Clear(C_UNLOAD_DIAGNOSTICS);
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
