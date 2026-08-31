using System;
using System.Threading.Tasks;

using Inno.Core.Logging;
using Inno.Editor.Core;

namespace Inno.Editor.Scripting;

/// <summary>
/// Owns script compilation and activation for one editor project.
/// </summary>
[EditorModule("editor-scripting", order: 100)]
internal sealed class EditorScripting : EditorModule
{
    private ScriptManager? m_manager;
    private Task<ScriptCompilationResult>? m_compilation;
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
        ScriptDiagnosticPublisher.ClearAll();
    }
}
