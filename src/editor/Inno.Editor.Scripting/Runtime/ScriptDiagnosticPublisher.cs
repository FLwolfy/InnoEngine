using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.Diagnose;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Assets;

namespace Inno.Editor.Scripting;

internal static class ScriptDiagnosticPublisher
{
    private const string C_COMPILER_DIAGNOSTICS = "Script Compiler";
    private const string C_MISSING_SCENE_DIAGNOSTICS = "Missing Scene Scripts";
    private const string C_RELOAD_DIAGNOSTICS = "Script Reload";
    private const string C_UNLOAD_DIAGNOSTICS = "Script Unload";

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

    internal static void PublishMissingSceneElements()
    {
        var diagnostics = new List<Diagnostic>();
        foreach (GameScene scene in SceneManager.loadedScenes)
        {
            foreach (GameObject gameObject in scene.GetObjects())
            {
                foreach (MissingGameComponent missing in gameObject.GetComponents().OfType<MissingGameComponent>())
                {
                    diagnostics.Add(CreateMissingDiagnostic(
                        scene,
                        missing.identity.persistentId,
                        missing.missingTypeName));
                }
            }
            foreach (MissingGameSystem missing in scene.GetSystems().OfType<MissingGameSystem>())
            {
                diagnostics.Add(CreateMissingDiagnostic(
                    scene,
                    missing.identity.persistentId,
                    missing.missingTypeName));
            }
        }
        Diagnostics.Set(C_MISSING_SCENE_DIAGNOSTICS, diagnostics);
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

    internal static void ClearUnload()
        => Diagnostics.Clear(C_UNLOAD_DIAGNOSTICS);

    internal static void ClearAll()
    {
        Diagnostics.Clear(C_COMPILER_DIAGNOSTICS);
        Diagnostics.Clear(C_MISSING_SCENE_DIAGNOSTICS);
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

    private static Diagnostic CreateMissingDiagnostic(
        GameScene scene,
        Guid elementId,
        string missingTypeName)
        => Diagnostic.Warning(
            "INNOHR0002",
            $"'{missingTypeName}' is unavailable in scene '{scene.name}' " +
            $"({scene.identity.persistentId:D}), element {elementId:D}. Its identity and serialized state are preserved.");
}
