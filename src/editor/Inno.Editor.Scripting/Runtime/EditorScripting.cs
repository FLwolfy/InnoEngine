using System;
using System.Threading.Tasks;

using Inno.Assets.Plugins;
using Inno.Core.Logging;
using Inno.Editor.Core;

namespace Inno.Editor.Scripting;

/// <summary>
/// Owns script compilation and activation for one editor project.
/// </summary>
[EditorModule("editor-scripting", order: 100)]
internal sealed class EditorScripting : EditorModule, IEditorScriptCompilation
{
    private ScriptManager? m_manager;
    private Task<ScriptCompilationResult>? m_compilation;
    private bool m_blockFollowingUpdates;
    private bool m_hideCompilationOnNextUpdate;
    private bool m_showCompilation;
    private string? m_activationFailure;

    /// <inheritdoc />
    public override bool blocksFollowingUpdates => m_blockFollowingUpdates;

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

    /// <inheritdoc />
    public EditorScriptCompilationState state
    {
        get
        {
            ScriptManager? manager = m_manager;
            if (manager is null)
                return EditorScriptCompilationState.Initializing;
            if (isCompiling)
                return EditorScriptCompilationState.Compiling;
            if (m_activationFailure is not null)
                return EditorScriptCompilationState.Failed;
            return manager.lastCompilation switch
            {
                { success: true } => EditorScriptCompilationState.Ready,
                { success: false } => EditorScriptCompilationState.Failed,
                _ => EditorScriptCompilationState.Initializing
            };
        }
    }

    /// <summary>
    /// Gets the current compiler progress.
    /// </summary>
    internal float progress => m_manager?.compilationProgress ?? 0f;

    /// <summary>
    /// Gets the current compiler stage.
    /// </summary>
    public string status
        => m_activationFailure ?? m_manager?.compilationStatus ?? "Initializing project scripting.";

    /// <inheritdoc />
    public ScriptCompilationResult? lastCompilation => m_manager?.lastCompilation;

    internal void RecompileScripting()
        => QueueReload(static manager => manager.RecompileScripting());

    internal void ReloadScripting()
        => QueueReload(static manager => manager.ReloadScripting());

    internal void ReloadPlugins()
        => QueueReload(static manager => manager.ReloadPlugins());

    internal void CancelCompilation()
        => m_manager?.CancelCompilation();

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
        m_blockFollowingUpdates = false;
        if (m_hideCompilationOnNextUpdate)
        {
            m_hideCompilationOnNextUpdate = false;
            m_showCompilation = false;
        }
        CompleteCompilation();
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
            {
                m_blockFollowingUpdates = true;
                _ = m_manager.ApplyPendingReload();
                m_activationFailure = null;
            }
            else
                PluginManager.RollbackPending();
        }
        catch (OperationCanceledException)
        {
            PluginManager.RollbackPending();
            Log.Info("Script compilation was canceled; the active generation was retained.");
        }
        catch (Exception exception)
        {
            PluginManager.RollbackPending();
            m_activationFailure = $"Script generation activation failed: {exception.Message}";
            ScriptDiagnosticPublisher.PublishReloadFailure(exception);
            Log.Error("Script assembly reload failed: {0}", exception);
        }
        finally
        {
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
        m_hideCompilationOnNextUpdate = false;
        m_showCompilation = false;
        m_blockFollowingUpdates = false;
        m_activationFailure = null;
        ScriptDiagnosticPublisher.ClearAll();
    }
}
