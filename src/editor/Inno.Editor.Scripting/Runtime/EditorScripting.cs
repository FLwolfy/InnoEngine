using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Inno.Core.Logging;
using Inno.Core.Reflection;
using Inno.Editor.Core;
using Inno.Engine.Scene;

namespace Inno.Editor.Scripting;

/// <summary>
/// Owns script compilation and activation for one editor project.
/// </summary>
[EditorModule("editor-scripting", order: 100)]
internal sealed class EditorScripting : EditorModule
{
    private ScriptManager? m_manager;
    private Task<ScriptCompilationResult>? m_compilation;
    private WeakReference<GameScene>[] m_diagnosticScenes = [];
    private long m_diagnosticTypeCacheVersion = -1;
    private bool m_hideCompilationOnNextUpdate;
    private bool m_showCompilation;

    /// <inheritdoc />
    public override bool blocksFollowingUpdates => isCompiling;

    /// <summary>
    /// Gets whether script compilation is currently active.
    /// </summary>
    internal bool isCompiling
        => m_showCompilation ||
           m_compilation is not null ||
           m_manager?.isCompiling == true ||
           m_manager?.isUnloadVerificationPending == true ||
           m_manager?.isCompilationPending == true;

    internal bool isAvailable => m_manager is not null;

    /// <summary>
    /// Gets the current compiler progress.
    /// </summary>
    internal float progress => m_manager?.compilationProgress ?? 0f;

    /// <summary>
    /// Gets the current compiler stage.
    /// </summary>
    internal string status => m_manager?.compilationStatus ?? "Waiting for script changes.";

    internal void RecompileScripting()
        => QueueReload(static manager => manager.RecompileScripting());

    internal void ReloadScripting()
        => QueueReload(static manager => manager.ReloadScripting());

    internal void ReloadPlugins()
        => QueueReload(static manager => manager.ReloadPlugins());

    internal void ReconcileSceneDiagnostics(bool force = false)
    {
        IReadOnlyList<GameScene> scenes = SceneManager.loadedScenes;
        long typeCacheVersion = TypeCacheManager.isInitialized
            ? TypeCacheManager.current.version
            : -1;
        bool scenesChanged = scenes.Count != m_diagnosticScenes.Length;
        if (!scenesChanged)
        {
            for (int i = 0; i < scenes.Count; i++)
            {
                if (m_diagnosticScenes[i].TryGetTarget(out GameScene? trackedScene) &&
                    ReferenceEquals(scenes[i], trackedScene))
                {
                    continue;
                }
                scenesChanged = true;
                break;
            }
        }
        if (!force && !scenesChanged && typeCacheVersion == m_diagnosticTypeCacheVersion)
            return;

        ScriptDiagnosticPublisher.PublishMissingSceneElements();
        var sceneReferences = new WeakReference<GameScene>[scenes.Count];
        for (int i = 0; i < scenes.Count; i++)
            sceneReferences[i] = new WeakReference<GameScene>(scenes[i]);
        m_diagnosticScenes = sceneReferences;
        m_diagnosticTypeCacheVersion = typeCacheVersion;
    }

    /// <inheritdoc />
    protected override void OnStart(EditorContext context)
    {
        m_manager = new ScriptManager(new ScriptManagerOptions
        {
            projectRootDirectory = context.projectDirectory
        });
        m_manager.Start();
        if (m_manager.TryCompilePending(out Task<ScriptCompilationResult>? compilation))
        {
            m_compilation = compilation;
            m_showCompilation = true;
        }
    }

    /// <inheritdoc />
    protected override void OnUpdate(EditorContext context)
    {
        if (m_hideCompilationOnNextUpdate)
        {
            m_hideCompilationOnNextUpdate = false;
            m_showCompilation = false;
        }
        CompleteCompilation();
        ReconcileSceneDiagnostics();
        if (m_manager is null || m_compilation is not null)
            return;
        if (m_manager.isUnloadVerificationPending)
        {
            AdvanceUnloadVerification();
            return;
        }
        if (!context.isFocused)
            return;
        if (m_manager.TryCompilePending(out Task<ScriptCompilationResult>? compilation))
        {
            m_compilation = compilation;
            m_showCompilation = true;
        }
    }

    /// <inheritdoc />
    protected override void OnStop(EditorContext context)
        => DisposeManager();

    /// <inheritdoc />
    protected override void OnDispose()
        => DisposeManager();

    private void CompleteCompilation()
    {
        Task<ScriptCompilationResult>? compilation = m_compilation;
        if (compilation is null || !compilation.IsCompleted || m_manager is null)
            return;
        try
        {
            ScriptCompilationResult result = compilation.GetAwaiter().GetResult();
            ScriptDiagnosticPublisher.PublishCompilation(result);
            if (result.success)
                _ = m_manager.ApplyPendingReload();
        }
        catch (Exception exception)
        {
            ScriptDiagnosticPublisher.PublishReloadFailure(exception);
            Log.Error("Script assembly reload failed: {0}", exception);
        }
        finally
        {
            ReconcileSceneDiagnostics(force: true);
            m_compilation = null;
            m_hideCompilationOnNextUpdate = m_manager?.isUnloadVerificationPending != true;
        }
    }

    private void AdvanceUnloadVerification()
    {
        ScriptManager? manager = m_manager;
        if (manager is null || !manager.AdvanceUnloadVerification(out Exception? failure))
            return;
        if (failure is not null)
        {
            Log.Error(
                "Script reload committed, but retired assembly unload verification failed: {0}",
                failure);
        }
        m_hideCompilationOnNextUpdate = true;
    }

    private void QueueReload(Action<ScriptManager> request)
    {
        ScriptManager? manager = m_manager;
        if (manager is null)
            return;
        request(manager);
        m_hideCompilationOnNextUpdate = false;
        m_showCompilation = true;
    }

    private void DisposeManager()
    {
        if (m_manager is null)
        {
            ScriptDiagnosticPublisher.ClearAll();
            return;
        }
        m_manager.Dispose();
        m_manager = null;
        m_compilation = null;
        m_diagnosticScenes = [];
        m_diagnosticTypeCacheVersion = -1;
        m_hideCompilationOnNextUpdate = false;
        m_showCompilation = false;
        ScriptDiagnosticPublisher.ClearAll();
    }
}
