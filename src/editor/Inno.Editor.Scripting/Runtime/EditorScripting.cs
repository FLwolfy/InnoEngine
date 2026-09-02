using System;
using System.Threading.Tasks;

using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Plugins.Authoring;
using Inno.Extensibility.Modules;
using Inno.Core.Logging;
using Inno.Core.Settings;
using Inno.Editor.Core;
using Inno.Scripting.Compiler;
using Inno.Scripting.Reload;

namespace Inno.Editor.Scripting;

/// <summary>
/// Owns script compilation and activation for one editor project.
/// </summary>
[EditorModule("editor-scripting", order: 100)]
internal sealed class EditorScripting : EditorModule, IEditorScriptCompilation
{
    private readonly AssetPipeline m_assets;
    private readonly ModuleHost m_modules;
    private readonly PluginEnvironment m_plugins;
    private readonly ProjectSettingsStore m_settings;
    private readonly EditorReloadCoordinator m_reloads;
    private readonly ScriptCompiler m_compiler;
    private readonly Logger m_log;
    private ScriptReloadHost? m_manager;
    private Task<ScriptCompilationResult>? m_compilation;
    private ScriptCompilationTicket? m_compilationTicket;
    private ScriptCompilationTicket? m_currentTicket;
    private bool m_blockFollowingUpdates;
    private bool m_hideCompilationOnNextUpdate;
    private bool m_showCompilation;
    private string? m_activationFailure;
    private long m_nextRequestId;

    internal EditorScripting(
        AssetPipeline assets,
        PluginEnvironment plugins,
        ModuleHost modules,
        ProjectSettingsStore settings,
        ScriptCompiler compiler,
        EditorReloadCoordinator reloads,
        LogRouter logs)
    {
        m_assets = assets ?? throw new ArgumentNullException(nameof(assets));
        m_plugins = plugins ?? throw new ArgumentNullException(nameof(plugins));
        m_modules = modules ?? throw new ArgumentNullException(nameof(modules));
        m_settings = settings ?? throw new ArgumentNullException(nameof(settings));
        m_compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        m_reloads = reloads ?? throw new ArgumentNullException(nameof(reloads));
        ArgumentNullException.ThrowIfNull(logs);
        m_log = logs.CreateLogger<EditorScripting>();
    }

    /// <summary>
    /// Gets whether blocks following updates is enabled for this implementation.
    /// </summary>
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

    /// <summary>
    /// Gets the current lifecycle state observed by callers.
    /// </summary>
    public EditorScriptCompilationState state
    {
        get
        {
            ScriptReloadHost? manager = m_manager;
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

    /// <summary>
    /// Gets the most recently activated script compilation result.
    /// </summary>
    public ScriptCompilationResult? lastCompilation => m_manager?.lastCompilation;

    /// <summary>
    /// Creates a compilation ticket for the newest observable source generation.
    /// </summary>
    /// <returns>
    /// The validated iscript compilation ticket that represents the completed operation.
    /// </returns>
    public IScriptCompilationTicket RequestCompilation()
    {
        m_currentTicket?.MarkSuperseded();
        var ticket = new ScriptCompilationTicket(
            ++m_nextRequestId,
            "Script compilation is queued.");
        m_currentTicket = ticket;
        ScriptReloadHost? manager = m_manager;
        if (manager is null)
        {
            ticket.MarkFailed(result: null, "Project scripting is not available.");
            return ticket;
        }
        QueueReload(static value => value.RecompileScripting(), supersedeCurrentTicket: false);
        return ticket;
    }

    /// <summary>
    /// Gets the ticket for the newest requested compilation, or null when idle.
    /// </summary>
    public IScriptCompilationTicket? currentTicket => m_currentTicket;

    internal void RecompileScripting()
        => QueueReload(static manager => manager.RecompileScripting(), supersedeCurrentTicket: true);

    internal void ReloadScripting()
        => QueueReload(static manager => manager.ReloadScripting(), supersedeCurrentTicket: true);

    internal void ReloadPlugins()
        => QueueReload(static manager => manager.ReloadPlugins(), supersedeCurrentTicket: true);

    internal void CancelCompilation()
        => m_manager?.CancelCompilation();

    /// <summary>
    /// Initializes this feature when its owning runtime becomes active.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    protected override void OnStart(EditorContext context)
    {
        m_manager = new ScriptReloadHost(
            new ScriptReloadOptions(),
            m_compiler,
            m_assets,
            m_plugins,
            m_modules,
            m_settings,
            m_reloads);
        m_manager.Start();
        if (m_manager.TryCompilePending(out Task<ScriptCompilationResult>? compilation))
        {
            m_compilation = compilation;
            BeginCompilationTicket();
            m_showCompilation = true;
        }
    }

    /// <summary>
    /// Advances this feature using the current runtime state.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
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
            BeginCompilationTicket();
            m_showCompilation = true;
        }
    }

    /// <summary>
    /// Stops this feature before its owning runtime releases the active generation.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    protected override void OnStop(EditorContext context)
        => DisposeManager();

    /// <summary>
    /// Releases resources retained by this feature after it has stopped.
    /// </summary>
    protected override void OnDispose()
        => DisposeManager();

    private void CompleteCompilation()
    {
        Task<ScriptCompilationResult>? compilation = m_compilation;
        if (compilation is null || !compilation.IsCompleted || m_manager is null)
            return;
        ScriptCompilationTicket? ticket = m_compilationTicket;
        try
        {
            ScriptCompilationResult result = compilation.GetAwaiter().GetResult();
            ScriptDiagnosticPublisher.PublishCompilation(result);
            if (result.success)
            {
                m_blockFollowingUpdates = true;
                _ = m_manager.ApplyPendingReload();
                m_activationFailure = null;
                string completionStatus = GenerateIdeProjection()
                    ? "Script generation compiled and activated successfully."
                    : "Script generation activated, but IDE project generation failed.";
                ticket?.MarkSucceeded(result, completionStatus);
            }
            else
            {
                bool activatedUnavailableGeneration = m_manager.ApplyPendingReload();
                if (activatedUnavailableGeneration)
                {
                    m_blockFollowingUpdates = true;
                    m_activationFailure = null;
                    _ = GenerateIdeProjection();
                    ticket?.MarkFailed(
                        result,
                        "Script compilation failed after a Plugin availability change; unavailable types were " +
                        "preserved as Missing.");
                }
                else
                {
                    m_plugins.RollbackPending();
                    _ = GenerateIdeProjection();
                    ticket?.MarkFailed(result, "Script compilation failed; the active generation was retained.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            m_plugins.RollbackPending();
            ticket?.MarkCanceled("Script compilation was canceled; the active generation was retained.");
            m_log.Write(
                LogLevel.Info,
                "Script compilation was canceled; the active generation was retained.");
        }
        catch (Exception exception)
        {
            m_plugins.RollbackPending();
            m_activationFailure = $"Script generation activation failed: {exception.Message}";
            ticket?.MarkFailed(
                ticket.result,
                $"Script generation activation failed: {exception.Message}");
            ScriptDiagnosticPublisher.PublishReloadFailure(exception);
            m_log.Write(LogLevel.Error, "Script assembly reload failed: {0}", [exception]);
        }
        finally
        {
            m_compilation = null;
            m_compilationTicket = null;
            m_hideCompilationOnNextUpdate = m_manager?.isUnloadVerificationPending != true;
        }
    }

    private void AdvanceUnloadVerification()
    {
        ScriptReloadHost? manager = m_manager;
        if (manager is null || !manager.AdvanceUnloadVerification(out Exception? failure))
            return;
        if (failure is not null)
        {
            ScriptDiagnosticPublisher.PublishUnloadFailure(failure);
            m_log.Write(
                LogLevel.Error,
                "Script reload committed, but retired assembly unload verification failed: {0}",
                [failure]);
        }
        else
        {
            ScriptDiagnosticPublisher.ClearUnload();
        }
        m_hideCompilationOnNextUpdate = true;
    }

    private bool GenerateIdeProjection()
    {
        try
        {
            ScriptReloadHost manager = m_manager
                ?? throw new InvalidOperationException("Project scripting is not available.");
            manager.GenerateProjectFiles();
            ScriptDiagnosticPublisher.ClearIdeProjection();
            return true;
        }
        catch (Exception exception)
        {
            ScriptDiagnosticPublisher.PublishIdeProjectionFailure(exception);
            m_log.Write(
                LogLevel.Warn,
                "Script generation activated, but IDE project generation failed: {0}",
                [exception]);
            return false;
        }
    }

    private void QueueReload(Action<ScriptReloadHost> request, bool supersedeCurrentTicket)
    {
        ScriptReloadHost? manager = m_manager;
        if (manager is null)
            return;
        if (supersedeCurrentTicket)
            m_currentTicket?.MarkSuperseded();
        request(manager);
        m_hideCompilationOnNextUpdate = false;
        m_showCompilation = true;
    }

    private void BeginCompilationTicket()
    {
        ScriptCompilationTicket? ticket = m_currentTicket;
        if (ticket is null || ticket.state != ScriptCompilationTicketState.Queued)
            return;
        m_compilationTicket = ticket;
        ticket.MarkCompiling("Compiling and validating the requested script generation.");
    }

    private void DisposeManager()
    {
        if (m_manager is null)
        {
            ScriptDiagnosticPublisher.ClearAll();
            return;
        }
        m_manager.Dispose();
        m_currentTicket?.MarkCanceled("Project scripting stopped before this request could activate.");
        m_manager = null;
        m_compilation = null;
        m_compilationTicket = null;
        m_hideCompilationOnNextUpdate = false;
        m_showCompilation = false;
        m_blockFollowingUpdates = false;
        m_activationFailure = null;
        ScriptDiagnosticPublisher.ClearAll();
    }
}
