using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Plugins.Authoring;
using Inno.Extensibility.Modules;
using Inno.Extensibility.Types;
using Inno.Core.Settings;
using Inno.Scripting.Compiler;

namespace Inno.Scripting.Reload;

/// <summary>
/// Watches, compiles, and atomically activates one project's C# script assemblies.
/// </summary>
public sealed class ScriptReloadHost : IDisposable
{
    private const float C_COMPILATION_PROGRESS_SHARE = 0.8f;
    private const float C_STAGING_PROGRESS = 0.86f;
    private const float C_MIGRATION_PROGRESS = 0.94f;
    private const float C_UNLOAD_VERIFICATION_PROGRESS = 0.97f;
    private const int C_MAX_UNLOAD_VERIFICATION_ATTEMPTS = 10;

    private readonly object m_sync = new();
    private readonly AssetPipeline m_assets;
    private readonly ScriptCompiler m_compiler;
    private readonly ModuleHost m_modules;
    private readonly ScriptReloadOptions m_options;
    private readonly PluginEnvironment m_plugins;
    private readonly ProjectSettingsStore m_settings;
    private readonly IScriptReloadCoordinator m_reloads;
    private readonly SemaphoreSlim m_compileGate = new(1, 1);
    private readonly CancellationTokenSource m_lifetimeCancellation = new();
    private readonly TaskCompletionSource<Exception?> m_disposalCompleted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationToken m_lifetimeToken;
    private readonly List<UnloadObservation> m_unloadObservations = [];

    private CancellationTokenSource? m_activeCompilationCancellation;
    private ScriptCompilationResult? m_activeCompilation;
    private PendingReload? m_pendingCompilation;
    private ScriptCompilationResult? m_lastCompilation;
    private readonly Dictionary<string, AssemblyModuleHandle> m_pluginModules = new(StringComparer.Ordinal);
    private AssemblyModuleHandle? m_runtimeScriptModule;
    private AssemblyModuleHandle? m_editorScriptModule;
    private string? m_activeCompilationDirectory;
    private readonly Dictionary<string, string> m_activeModuleFingerprints = new(StringComparer.Ordinal);
    private string m_compilationStatus = "Waiting for script changes.";
    private long m_lastCompileRequestTimestamp;
    private long m_compilationStartedTimestamp;
    private TimeSpan m_lastCompilationElapsed;
    private float m_compilationProgress;
    private int m_unloadVerificationAttempt;
    private ScriptReloadRequest m_requestedReload;
    private bool m_initialCompileRequested;
    private int m_disposeStarted;
    private int m_isCompiling;
    private volatile bool m_disposed;

    /// <summary>
    /// Creates a reload host for one compiler and runtime module owner.
    /// </summary>
    /// <param name="options">
    /// The automatic compilation, debounce, and long-running warning policy.
    /// </param>
    /// <param name="compiler">
    /// The compiler that produces immutable candidate script generations.
    /// </param>
    /// <param name="assets">
    /// The authoring asset pipeline that supplies committed source artifacts.
    /// </param>
    /// <param name="plugins">
    /// The Plugin environment that owns active and candidate source generations.
    /// </param>
    /// <param name="modules">
    /// The module host that atomically activates compiled assemblies.
    /// </param>
    /// <param name="settings">
    /// The project settings store rebuilt with each committed Plugin generation.
    /// </param>
    /// <param name="reloads">
    /// The host-owned coordinator that commits dependent state together with script generations.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="options"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the configured debounce duration is negative.
    /// </exception>
    public ScriptReloadHost(
        ScriptReloadOptions options,
        ScriptCompiler compiler,
        AssetPipeline assets,
        PluginEnvironment plugins,
        ModuleHost modules,
        ProjectSettingsStore settings,
        IScriptReloadCoordinator reloads)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(reloads);
        if (options.debounceMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Debounce duration cannot be negative.");
        if (options.compilationWarningTimeout <= TimeSpan.Zero &&
            options.compilationWarningTimeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Compilation warning timeout must be positive or infinite.");
        }
        m_assets = assets;
        m_compiler = compiler;
        m_plugins = plugins;
        m_modules = modules;
        m_settings = settings;
        m_reloads = reloads;
        m_options = new ScriptReloadOptions
        {
            autoCompile = options.autoCompile,
            debounceMilliseconds = options.debounceMilliseconds,
            compilationWarningTimeout = options.compilationWarningTimeout
        };
        m_lifetimeToken = m_lifetimeCancellation.Token;
    }

    /// <summary>
    /// Gets whether a compilation currently owns the compiler gate.
    /// </summary>
    public bool isCompiling => Volatile.Read(ref m_isCompiling) != 0;

    /// <summary>
    /// Gets whether source or plugin changes are waiting to be compiled.
    /// </summary>
    public bool isCompilationPending
    {
        get
        {
            lock (m_sync)
                return m_requestedReload != ScriptReloadRequest.None;
        }
    }

    /// <summary>
    /// Gets the current compilation progress in the inclusive range from zero to one.
    /// </summary>
    public float compilationProgress => Volatile.Read(ref m_compilationProgress);

    /// <summary>
    /// Gets a short description of the current compilation stage.
    /// </summary>
    public string compilationStatus
    {
        get
        {
            string status = Volatile.Read(ref m_compilationStatus);
            return isCompilationTakingLong
                ? $"Long-running compilation ({compilationElapsed.TotalSeconds:F1}s). {status}"
                : status;
        }
    }

    /// <summary>
    /// Gets the elapsed duration of the active or most recently completed compilation.
    /// </summary>
    public TimeSpan compilationElapsed
    {
        get
        {
            long started = Volatile.Read(ref m_compilationStartedTimestamp);
            return isCompiling && started != 0
                ? TimeSpan.FromMilliseconds(Math.Max(0, Environment.TickCount64 - started))
                : m_lastCompilationElapsed;
        }
    }

    /// <summary>
    /// Gets whether the active compilation exceeded the configured warning duration.
    /// </summary>
    public bool isCompilationTakingLong
        => isCompiling &&
           m_options.compilationWarningTimeout != Timeout.InfiniteTimeSpan &&
           compilationElapsed >= m_options.compilationWarningTimeout;

    /// <summary>
    /// Gets the most recently completed compilation.
    /// </summary>
    public ScriptCompilationResult? lastCompilation
    {
        get
        {
            lock (m_sync)
                return m_lastCompilation;
        }
    }

    /// <summary>
    /// Gets whether retired collectible assembly generations are still being verified for unload.
    /// </summary>
    public bool isUnloadVerificationPending
    {
        get
        {
            lock (m_sync)
                return m_unloadObservations.Count != 0;
        }
    }

    /// <summary>
    /// Subscribes to committed asset changes and queues a cache-aware initial compilation request.
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (!m_assets.isInitialized)
            throw new InvalidOperationException("ScriptReloadHost requires AssetPipeline to be initialized first.");
        m_assets.Update();
        m_assets.Changed -= OnAssetDatabaseChanged;
        m_assets.Changed += OnAssetDatabaseChanged;
        m_assets.SourceMountsChanged -= OnSourceMountsChanged;
        m_assets.SourceMountsChanged += OnSourceMountsChanged;
        m_plugins.ActivationCandidateChanged -= OnPluginActivationCandidateChanged;
        m_plugins.ActivationCandidateChanged += OnPluginActivationCandidateChanged;
        if (m_options.autoCompile)
        {
            lock (m_sync)
            {
                bool hasActiveScripting = m_modules.isInitialized &&
                    m_modules.modules.Any(static module => module.domain == AssemblyDomain.InnoScripting);
                m_requestedReload = m_plugins.hasPendingActivation
                    ? ScriptReloadRequest.ReloadPlugins
                    : hasActiveScripting
                        ? ScriptReloadRequest.Recompile
                        : ScriptReloadRequest.ReloadScripting;
                m_initialCompileRequested = true;
                m_lastCompileRequestTimestamp = Environment.TickCount64;
            }
            SetCompilationProgress(0f, "Initial scripting cache probe queued.");
        }
    }

    /// <summary>
    /// Requests cancellation of the active compilation without changing the active script generation.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when an active compilation received the request.
    /// </returns>
    /// <exception cref="ObjectDisposedException">
    /// Thrown after the manager has been disposed.
    /// </exception>
    public bool CancelCompilation()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        CancellationTokenSource? cancellation;
        lock (m_sync)
            cancellation = m_activeCompilationCancellation;
        if (cancellation is null)
            return false;
        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        SetCompilationProgress(compilationProgress, "Canceling script compilation...");
        return true;
    }

    /// <summary>
    /// Queues incremental recompilation of changed script assemblies.
    /// </summary>
    /// <exception cref="ObjectDisposedException">
    /// Thrown after the manager has been disposed.
    /// </exception>
    public void RecompileScripting()
    {
        QueueReload(ScriptReloadRequest.Recompile);
    }

    /// <summary>
    /// Queues a complete rebuild of both scripting load contexts while retaining the plugin generation.
    /// Valid cached artifacts are reused.
    /// </summary>
    /// <exception cref="ObjectDisposedException">
    /// Thrown after the manager has been disposed.
    /// </exception>
    public void ReloadScripting() => QueueReload(ScriptReloadRequest.ReloadScripting);

    /// <summary>
    /// Queues replacement of the unified plugin generation and both dependent scripting generations.
    /// Valid script artifacts are reused when plugin reference fingerprints are unchanged.
    /// </summary>
    /// <exception cref="ObjectDisposedException">
    /// Thrown after the manager has been disposed.
    /// </exception>
    public void ReloadPlugins() => QueueReload(ScriptReloadRequest.ReloadPlugins);

    /// <summary>
    /// Starts a pending compilation after the configured quiet period has elapsed.
    /// </summary>
    /// <remarks>
    /// The initial automatic request is immediately ready and does not observe the debounce duration.
    /// </remarks>
    /// <param name="compilation">
    /// The started compilation task, or <see langword="null"/> when none was ready.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a pending compilation was started.
    /// </returns>
    public bool TryCompilePending(out Task<ScriptCompilationResult>? compilation)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ScriptReloadRequest request;
        lock (m_sync)
        {
            long elapsed = Environment.TickCount64 - m_lastCompileRequestTimestamp;
            if (m_requestedReload == ScriptReloadRequest.None ||
                isCompiling ||
                !m_initialCompileRequested && elapsed < m_options.debounceMilliseconds)
            {
                compilation = null;
                return false;
            }
            request = m_requestedReload;
            m_requestedReload = ScriptReloadRequest.None;
            m_initialCompileRequested = false;
        }
        if (m_assets.isInitialized)
            m_assets.Update();
        PluginUnavailabilityPlan? unavailability = request == ScriptReloadRequest.ReloadPlugins
            ? CapturePluginUnavailabilityPlan()
            : null;
        compilation = CompileAsync(request, unavailability).AsTask();
        return true;
    }

    private async ValueTask<ScriptCompilationResult> CompileAsync(
        ScriptReloadRequest request,
        PluginUnavailabilityPlan? unavailability,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            m_lifetimeToken);
        CancellationToken effectiveCancellation = linkedCancellation.Token;
        await m_compileGate.WaitAsync(effectiveCancellation).ConfigureAwait(false);
        try
        {
            effectiveCancellation.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(m_disposed, this);
            Volatile.Write(ref m_isCompiling, 1);
            Volatile.Write(ref m_compilationStartedTimestamp, Environment.TickCount64);
            lock (m_sync)
                m_activeCompilationCancellation = linkedCancellation;
            SetCompilationProgress(0f, "Preparing script compilation...");
            ScriptCompilationResult result;
            try
            {
                result = await m_compiler
                    .CompileAuthoringGenerationAsync(
                        new ScriptProgressObserver((progress, status) => SetCompilationProgress(
                            progress * C_COMPILATION_PROGRESS_SHARE,
                            status)),
                        effectiveCancellation)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (effectiveCancellation.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                result = ScriptCompilationResult.Failure(
                    new ScriptDiagnostic(
                        "INNO0001",
                        ScriptDiagnosticSeverity.Error,
                        exception.ToString(),
                        filePath: null,
                        line: 0,
                        column: 0));
            }
            lock (m_sync)
            {
                if (!m_disposed)
                {
                    m_lastCompilation = result;
                    if (m_requestedReload != ScriptReloadRequest.None)
                    {
                        if (request > m_requestedReload)
                            m_requestedReload = request;
                        m_pendingCompilation = null;
                    }
                    else if (result.success || unavailability is not null)
                    {
                        m_pendingCompilation = new PendingReload(
                            result,
                            request,
                            result.success ? null : unavailability);
                    }
                    SetCompilationProgress(
                        result.success ? C_COMPILATION_PROGRESS_SHARE : 1f,
                        m_requestedReload != ScriptReloadRequest.None
                            ? "Compilation superseded by a queued reload request."
                            : result.success
                                ? "Script compilation completed."
                                : unavailability is not null
                                    ? "Compilation failed after a Plugin availability change; the unavailable " +
                                      "generation is ready to activate."
                                : "Script compilation failed.");
                }
            }
            return result;
        }
        finally
        {
            long started = Interlocked.Exchange(ref m_compilationStartedTimestamp, 0);
            if (started != 0)
            {
                m_lastCompilationElapsed = TimeSpan.FromMilliseconds(
                    Math.Max(0, Environment.TickCount64 - started));
            }
            lock (m_sync)
            {
                if (ReferenceEquals(m_activeCompilationCancellation, linkedCancellation))
                    m_activeCompilationCancellation = null;
            }
            Volatile.Write(ref m_isCompiling, 0);
            m_compileGate.Release();
        }
    }

    /// <summary>
    /// Applies the latest compiled generation, or commits a Plugin-unavailability generation when changed
    /// Plugin code cannot produce a replacement, at a caller-controlled main-thread safe point.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when modules were replaced or retired; otherwise, <see langword="false"/>.
    /// </returns>
    /// <exception cref="ObjectDisposedException">
    /// Thrown after the manager has been disposed.
    /// </exception>
    public bool ApplyPendingReload()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        PendingReload? pending;
        lock (m_sync)
        {
            pending = m_pendingCompilation;
            if (pending is not null && m_requestedReload != ScriptReloadRequest.None)
            {
                if (pending.request > m_requestedReload)
                    m_requestedReload = pending.request;
                m_pendingCompilation = null;
                pending = null;
            }
        }
        if (pending is null)
            return false;

        ReloadPlan plan = SelectReloadPlan(pending);
        if (plan.requests.Count == 0 && plan.removedModuleNames.Count == 0)
        {
            m_plugins.ActivatePending();
            m_settings.RebuildCurrent();
            m_plugins.CommitPending();
            CompletePendingReload(pending);
            m_reloads.RefreshDiagnostics();
            SetCompilationProgress(1f, "No scripting changes detected.");
            return false;
        }

        SetCompilationProgress(C_STAGING_PROGRESS, "Staging script reload candidates...");
        AssemblyModuleInfo[] retiringModules = m_modules.modules
            .Where(module => plan.requests.Any(request => string.Equals(
                    request.moduleName,
                    module.moduleName,
                    StringComparison.Ordinal)) ||
                plan.removedModuleNames.Contains(module.moduleName, StringComparer.Ordinal))
            .ToArray();
        using AssemblyReloadSession reload = m_modules.BeginReload(
            plan.requests,
            plan.removedModuleNames);
        SetCompilationProgress(C_MIGRATION_PROGRESS, "Migrating active editor state...");
        Action activateCandidate = () =>
        {
            m_plugins.ActivatePending();
            if (m_assets.isInitialized)
                m_assets.Update();
            if (m_settings.isInitialized)
                m_settings.RebuildCurrent();
        };
        Action restorePrevious = () =>
        {
            m_plugins.RollbackPending();
            if (m_assets.isInitialized)
                m_assets.Update();
            if (m_settings.isInitialized)
                m_settings.RebuildCurrent();
        };
        AssemblyUnloadMonitor unload = m_reloads.Execute(
            reload,
            activateCandidate,
            restorePrevious);
        if (retiringModules.Length > 0)
        {
            lock (m_sync)
            {
                m_unloadObservations.Add(new UnloadObservation(
                    unload,
                    retiringModules));
                m_unloadVerificationAttempt = 0;
            }
        }
        m_activeCompilationDirectory = pending.unavailability is null
            ? pending.compilation.outputDirectory
            : null;
        _ = m_compiler.CollectArtifacts(m_activeCompilationDirectory is null
            ? Array.Empty<string?>()
            : [m_activeCompilationDirectory]);
        RefreshModuleHandles();
        UpdateActiveFingerprints(plan);
        m_plugins.CommitPending();
        string completedStatus = pending.unavailability is null
            ? "Script reload completed."
            : "Plugin availability change committed. Unavailable scene types are preserved as Missing until their " +
              "Stable IDs return.";
        SetCompilationProgress(
            retiringModules.Length == 0 ? 1f : C_UNLOAD_VERIFICATION_PROGRESS,
            retiringModules.Length == 0
                ? completedStatus
                : completedStatus + " Verifying retired assembly unload...");
        m_reloads.RefreshDiagnostics();
        CompletePendingReload(pending);
        return true;
    }

    /// <summary>
    /// Generates standard SDK-style game/editor projects and a solution for IDE tooling.
    /// </summary>
    /// <exception cref="ObjectDisposedException">
    /// Thrown after the manager has been disposed.
    /// </exception>
    public void GenerateProjectFiles()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (m_assets.isInitialized)
        {
            m_assets.Update();
            m_assets.Rescan();
        }
        ScriptCompilationResult? activeCompilation;
        lock (m_sync)
            activeCompilation = m_activeCompilation;
        m_compiler.GenerateProjectFiles(activeCompilation);
    }

    /// <summary>
    /// Cancels and waits for active compilation work, stops Asset Database observation, and unloads
    /// the active script module.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref m_disposeStarted, 1) != 0)
        {
            Exception? previousFailure = m_disposalCompleted.Task.GetAwaiter().GetResult();
            if (previousFailure is not null)
                ExceptionDispatchInfo.Capture(previousFailure).Throw();
            return;
        }
        Exception? disposalFailure = null;
        try
        {
            m_disposed = true;
            m_lifetimeCancellation.Cancel();
            m_compileGate.Wait();
            try
            {
                if (m_assets.isInitialized)
                {
                    m_assets.Changed -= OnAssetDatabaseChanged;
                    m_assets.SourceMountsChanged -= OnSourceMountsChanged;
                }
                m_plugins.ActivationCandidateChanged -= OnPluginActivationCandidateChanged;
                lock (m_sync)
                {
                    m_requestedReload = ScriptReloadRequest.None;
                    m_initialCompileRequested = false;
                    m_pendingCompilation = null;
                    m_unloadObservations.Clear();
                }
                if (m_modules.isInitialized)
                {
                    AssemblyModuleHandle[] modules =
                    [
                        .. new[] { m_editorScriptModule, m_runtimeScriptModule }
                            .OfType<AssemblyModuleHandle>(),
                        .. m_pluginModules.Values
                    ];
                    if (modules.Length > 0)
                        _ = m_modules.Unload(modules);
                    m_pluginModules.Clear();
                    m_runtimeScriptModule = null;
                    m_editorScriptModule = null;
                    if (modules.Length > 0 && m_assets.isInitialized)
                        m_assets.Rescan();
                }
                m_activeCompilationDirectory = null;
                m_activeCompilation = null;
            }
            finally
            {
                m_compileGate.Release();
                m_lifetimeCancellation.Dispose();
            }
            GC.SuppressFinalize(this);
        }
        catch (Exception exception)
        {
            disposalFailure = exception;
            throw;
        }
        finally
        {
            m_disposalCompleted.TrySetResult(disposalFailure);
        }
    }

    /// <summary>
    /// Advances cooperative unload verification for retired script generations.
    /// </summary>
    /// <param name="failure">
    /// Receives the retention failure after all verification attempts, otherwise <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when no additional verification frame is required.
    /// </returns>
    public bool AdvanceUnloadVerification(out Exception? failure)
    {
        failure = null;
        int attempt;
        lock (m_sync)
        {
            if (m_disposed)
                return true;
            if (m_unloadObservations.Count == 0)
                return true;
            attempt = ++m_unloadVerificationAttempt;
        }

        SetCompilationProgress(
            C_UNLOAD_VERIFICATION_PROGRESS,
            $"Script reload committed. Forcing garbage collection to verify retired assemblies " +
            $"({attempt}/{C_MAX_UNLOAD_VERIFICATION_ATTEMPTS})...");
        ForceUnloadCollection();

        AssemblyModuleInfo[]? retainedModules = null;
        lock (m_sync)
        {
            for (int i = m_unloadObservations.Count - 1; i >= 0; i--)
            {
                if (m_unloadObservations[i].monitor.isCompleted)
                    m_unloadObservations.RemoveAt(i);
            }

            if (m_unloadObservations.Count == 0)
            {
                m_unloadVerificationAttempt = 0;
                SetCompilationProgress(1f, "Script reload and retired assembly unload completed.");
            }
            else if (attempt >= C_MAX_UNLOAD_VERIFICATION_ATTEMPTS)
            {
                retainedModules = m_unloadObservations
                    .SelectMany(static observation => observation.modules)
                    .GroupBy(static module =>
                        (module.moduleName, module.domain, module.scope, module.generation))
                    .Select(static group => group.First())
                    .OrderBy(static module => module.domain)
                    .ThenBy(static module => module.scope)
                    .ThenBy(static module => module.moduleName, StringComparer.Ordinal)
                    .ThenBy(static module => module.generation)
                    .ToArray();
                m_unloadObservations.Clear();
                m_unloadVerificationAttempt = 0;
            }
            else
            {
                return false;
            }
        }

        if (retainedModules is null)
        {
            return true;
        }

        failure = CreateUnloadVerificationFailure(retainedModules);
        SetCompilationProgress(
            1f,
            "Script reload committed, but retired assembly unload verification failed.");
        return true;
    }

    private void OnAssetDatabaseChanged(AssetChangeSet changeSet)
    {
        if (m_disposed || !m_options.autoCompile)
            return;
        for (int i = 0; i < changeSet.changes.Count; i++)
        {
            AssetChange change = changeSet.changes[i];
            string path = change.assetPath.ToString();
            string previousPath = change.previousAssetPath?.ToString() ?? string.Empty;
            if (!IsScriptInput(path) && !IsScriptInput(previousPath))
                continue;
            QueueReload(IsPluginInput(path) || IsPluginInput(previousPath)
                ? ScriptReloadRequest.ReloadPlugins
                : ScriptReloadRequest.Recompile);
            return;
        }
    }

    private void OnSourceMountsChanged()
    {
        if (m_disposed || !m_options.autoCompile)
            return;
        if (m_plugins.hasPendingActivation)
            return;
        QueueReload(ScriptReloadRequest.ReloadPlugins);
    }

    private void OnPluginActivationCandidateChanged()
    {
        if (m_disposed || !m_options.autoCompile)
            return;
        QueueReload(ScriptReloadRequest.ReloadPlugins);
    }

    private void SetCompilationProgress(float progress, string status)
    {
        Volatile.Write(ref m_compilationProgress, Math.Clamp(progress, 0f, 1f));
        Volatile.Write(ref m_compilationStatus, status);
    }

    private void QueueReload(ScriptReloadRequest request)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        lock (m_sync)
        {
            if (request > m_requestedReload)
                m_requestedReload = request;
            m_lastCompileRequestTimestamp = Environment.TickCount64;
        }
        SetCompilationProgress(0f, request switch
        {
            ScriptReloadRequest.ReloadPlugins => "Plugin and scripting reload queued.",
            ScriptReloadRequest.ReloadScripting => "Full scripting reload queued.",
            _ => "Scripting recompile queued."
        });
    }

    private ReloadPlan SelectReloadPlan(PendingReload pending)
    {
        if (pending.unavailability is not null)
        {
            return new ReloadPlan(
                Array.Empty<AssemblyLoadRequest>(),
                pending.unavailability.removedModuleNames);
        }

        IReadOnlyList<AssemblyLoadRequest> requests = pending.compilation.activationRequests;
        string[] candidatePlugins = requests
            .Where(static request => request.domain == AssemblyDomain.InnoPlugin)
            .Select(static request => request.moduleName)
            .ToArray();
        string[] removedPlugins = pending.request == ScriptReloadRequest.ReloadPlugins
            ? m_pluginModules.Keys.Except(candidatePlugins, StringComparer.Ordinal).ToArray()
            : [];
        if (m_runtimeScriptModule is null || m_editorScriptModule is null ||
            candidatePlugins.Any(plugin => !m_pluginModules.ContainsKey(plugin)))
        {
            return new ReloadPlan(requests, removedPlugins);
        }
        if (pending.request == ScriptReloadRequest.ReloadPlugins)
            return new ReloadPlan(requests, removedPlugins);
        if (pending.request == ScriptReloadRequest.ReloadScripting)
        {
            return new ReloadPlan(
                requests.Where(static request => request.domain == AssemblyDomain.InnoScripting).ToArray(),
                []);
        }
        if (string.Equals(
                pending.compilation.outputDirectory,
                m_activeCompilationDirectory,
                StringComparison.OrdinalIgnoreCase))
        {
            return new ReloadPlan([], []);
        }

        var compiled = pending.compilation.compiledAssemblyNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = requests
            .Where(request => GetOwnedAssemblyNames(request).Any(compiled.Contains) ||
                              !m_activeModuleFingerprints.TryGetValue(
                                  request.moduleName,
                                  out string? activeFingerprint) ||
                              !string.Equals(
                                  ComputeRequestFingerprint(request),
                                  activeFingerprint,
                                  StringComparison.Ordinal))
            .Select(static request => request.moduleName)
            .ToHashSet(StringComparer.Ordinal);
        if (selected.Any(name => requests.Any(request =>
                request.domain == AssemblyDomain.InnoPlugin &&
                string.Equals(request.moduleName, name, StringComparison.Ordinal))))
        {
            bool changed;
            do
            {
                changed = false;
                foreach (AssemblyLoadRequest request in requests.Where(static request =>
                             request.domain == AssemblyDomain.InnoPlugin))
                {
                    if (!selected.Contains(request.moduleName) && request.upstreamModuleNames.Any(selected.Contains))
                        changed |= selected.Add(request.moduleName);
                }
            }
            while (changed);
            selected.UnionWith(requests
                .Where(static request => request.domain == AssemblyDomain.InnoScripting)
                .Select(static request => request.moduleName));
        }
        AssemblyLoadRequest? runtime = requests.SingleOrDefault(static request =>
            request.domain == AssemblyDomain.InnoScripting && request.scope == AssemblyScope.Runtime);
        if (runtime is not null && selected.Contains(runtime.moduleName))
        {
            AssemblyLoadRequest? editor = requests.SingleOrDefault(static request =>
                request.domain == AssemblyDomain.InnoScripting && request.scope == AssemblyScope.Editor);
            if (editor is not null)
                selected.Add(editor.moduleName);
        }
        return new ReloadPlan(
            requests.Where(request => selected.Contains(request.moduleName)).ToArray(),
            []);
    }

    private void RefreshModuleHandles()
    {
        IReadOnlyList<AssemblyModuleInfo> modules = m_modules.modules;
        m_pluginModules.Clear();
        foreach (AssemblyModuleInfo module in modules.Where(static module =>
                     module.domain == AssemblyDomain.InnoPlugin &&
                     module.moduleName.StartsWith("Plugin.", StringComparison.Ordinal)))
        {
            m_pluginModules.Add(module.moduleName, module.handle);
        }
        m_runtimeScriptModule = modules.SingleOrDefault(static module =>
            module.moduleName == "RuntimeScripts")?.handle;
        m_editorScriptModule = modules.SingleOrDefault(static module =>
            module.moduleName == "EditorScripts")?.handle;
    }

    private void UpdateActiveFingerprints(ReloadPlan plan)
    {
        foreach (string removed in plan.removedModuleNames)
            m_activeModuleFingerprints.Remove(removed);
        foreach (AssemblyLoadRequest request in plan.requests)
            m_activeModuleFingerprints[request.moduleName] = ComputeRequestFingerprint(request);
    }

    private static IEnumerable<string> GetOwnedAssemblyNames(AssemblyLoadRequest request)
        => new[] { request.mainAssemblyPath }
            .Concat(request.preloadAssemblyPaths)
            .Select(static path => System.Reflection.AssemblyName.GetAssemblyName(path).Name ?? string.Empty);

    private static string ComputeRequestFingerprint(AssemblyLoadRequest request)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string path in new[] { request.mainAssemblyPath }
                     .Concat(request.preloadAssemblyPaths)
                     .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase))
        {
            byte[] name = System.Text.Encoding.UTF8.GetBytes(System.IO.Path.GetFileName(path));
            hash.AppendData(name);
            hash.AppendData(System.IO.File.ReadAllBytes(path));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private void CompletePendingReload(PendingReload applied)
    {
        lock (m_sync)
        {
            m_activeCompilation = applied.unavailability is null
                ? applied.compilation
                : null;
            if (ReferenceEquals(m_pendingCompilation, applied))
                m_pendingCompilation = null;
        }
    }

    private PluginUnavailabilityPlan? CapturePluginUnavailabilityPlan()
    {
        if (!m_plugins.hasPendingActivation || m_pluginModules.Count == 0)
            return null;

        Dictionary<string, string> candidatePluginHashes = m_plugins.compilationPlugins
            .Where(static plugin => plugin.containsCode)
            .ToDictionary(
                static plugin => plugin.manifest.pluginId,
                static plugin => plugin.contentHash,
                StringComparer.Ordinal);
        var removedModuleNames = m_plugins.activePlugins
            .Where(static plugin => plugin.containsCode)
            .Where(plugin => !candidatePluginHashes.TryGetValue(
                                 plugin.manifest.pluginId,
                                 out string? candidateHash) ||
                             !string.Equals(
                                 plugin.contentHash,
                                 candidateHash,
                                 StringComparison.Ordinal))
            .Select(static plugin => "Plugin." + plugin.manifest.pluginId)
            .Where(m_pluginModules.ContainsKey)
            .ToHashSet(StringComparer.Ordinal);
        if (removedModuleNames.Count == 0)
            return null;

        AssemblyModuleInfo[] activeModules = m_modules.modules.ToArray();
        bool changed;
        do
        {
            changed = false;
            foreach (AssemblyModuleInfo module in activeModules)
            {
                if (removedModuleNames.Contains(module.moduleName) ||
                    !module.upstreamModuleNames.Any(removedModuleNames.Contains))
                {
                    continue;
                }
                changed |= removedModuleNames.Add(module.moduleName);
            }
        }
        while (changed);

        return new PluginUnavailabilityPlan(
            removedModuleNames
                .OrderBy(static moduleName => moduleName, StringComparer.Ordinal)
                .ToArray());
    }

    private static bool IsScriptInput(string path)
        => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
           path.EndsWith(".iasmdef", StringComparison.OrdinalIgnoreCase);

    private static bool IsPluginInput(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !IsScriptInput(path))
            return false;
        try
        {
            return AssetPath.Parse(path).source != AssetSourceId.project;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static InvalidOperationException CreateUnloadVerificationFailure(
        IReadOnlyList<AssemblyModuleInfo> modules)
    {
        string retained = string.Join(
            ", ",
            modules.Select(static module =>
                $"{module.moduleName} ({module.domain}/{module.scope}, generation {module.generation})"));
        return new InvalidOperationException(
            $"Script reload was committed, but the following retired assembly contexts remained reachable " +
            $"after {C_MAX_UNLOAD_VERIFICATION_ATTEMPTS} forced garbage-collection attempts: {retained}. " +
            "A retained Type, object, delegate, extension, task, subscription, or thread is preventing " +
            "collectible AssemblyLoadContext unload. The active generation remains committed.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ForceUnloadCollection()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private sealed record PendingReload(
        ScriptCompilationResult compilation,
        ScriptReloadRequest request,
        PluginUnavailabilityPlan? unavailability = null);

    private sealed record PluginUnavailabilityPlan(
        IReadOnlyList<string> removedModuleNames);

    private sealed record ReloadPlan(
        IReadOnlyList<AssemblyLoadRequest> requests,
        IReadOnlyList<string> removedModuleNames);

    private sealed record UnloadObservation(
        AssemblyUnloadMonitor monitor,
        IReadOnlyList<AssemblyModuleInfo> modules);

    private sealed class ScriptProgressObserver(Action<float, string> report)
        : IProgress<ScriptCompilationProgress>
    {
        /// <summary>
        /// Publishes one progress update to the receiving workflow.
        /// </summary>
        /// <param name="value">
        /// The concrete value read or transformed by this operation.
        /// </param>
        public void Report(ScriptCompilationProgress value)
            => report(value.fraction, value.stage);
    }

    private enum ScriptReloadRequest
    {
        None,
        Recompile,
        ReloadScripting,
        ReloadPlugins
    }
}
