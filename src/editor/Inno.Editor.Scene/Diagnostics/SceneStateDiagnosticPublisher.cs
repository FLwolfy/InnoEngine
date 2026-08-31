using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.Diagnose;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Assets;

namespace Inno.Editor.Scene;

internal static class SceneStateDiagnosticPublisher
{
    private const string C_MISSING_ELEMENTS = "Missing Scene Scripts";
    private const string C_RELOAD = "Scene Reload";

    internal static void PublishMissingElements()
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
        Diagnostics.Set(C_MISSING_ELEMENTS, diagnostics);
    }

    internal static void PublishReload(IReadOnlyList<SceneReloadDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        Diagnostics.Set(
            C_RELOAD,
            diagnostics.Select(static diagnostic => diagnostic.severity switch
            {
                SceneReloadDiagnosticSeverity.Warning => Diagnostic.Warning(
                    diagnostic.code,
                    diagnostic.message),
                _ => Diagnostic.Error(diagnostic.code, diagnostic.message)
            }));
    }

    internal static void ClearAll()
    {
        Diagnostics.Clear(C_MISSING_ELEMENTS);
        Diagnostics.Clear(C_RELOAD);
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
