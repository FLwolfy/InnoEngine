using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets;
using Inno.Assets.Core;
using Inno.Assets.Plugins;
using Inno.Core.Assemblies;
using Inno.Core.Reflection;
using Inno.Core.Settings;
using Inno.Editor.Core;

namespace Inno.Editor.Scripting;

/// <summary>
/// Watches, compiles, and atomically activates one project's C# script assemblies.
/// </summary>
public sealed class ScriptManager : IDisposable
{
    private const float C_COMPILATION_PROGRESS_SHARE = 0.8f;
    private const float C_STAGING_PROGRESS = 0.86f;
    private const float C_MIGRATION_PROGRESS = 0.94f;
    private const float C_UNLOAD_VERIFICATION_PROGRESS = 0.97f;
    private const int C_MAX_UNLOAD_VERIFICATION_ATTEMPTS = 10;

    private readonly object m_sync = new();
    private readonly ScriptManagerOptions m_options;
    private readonly SemaphoreSlim m_compileGate = new(1, 1);
    private readonly CancellationTokenSource m_lifetimeCancellation = new();
    private readonly TaskCompletionSource<Exception?> m_disposalCompleted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationToken m_lifetimeToken;
    private readonly ScriptArtifactCache m_artifactCache;
    private readonly Func<CancellationToken, ValueTask>? m_compileGateProbe;
    private readonly List<UnloadObservation> m_unloadObservations = [];

    private PendingReload? m_pendingCompilation;
    private ScriptCompilationResult? m_lastCompilation;
    private AssemblyModuleHandle? m_pluginModule;
    private AssemblyModuleHandle? m_runtimeScriptModule;
    private AssemblyModuleHandle? m_editorScriptModule;
    private string? m_activeCompilationDirectory;
    private string? m_activePluginFingerprint;
    private string? m_activeRuntimeFingerprint;
    private string? m_activeEditorFingerprint;
    private string m_compilationStatus = "Waiting for script changes.";
    private long m_lastCompileRequestTimestamp;
    private float m_compilationProgress;
    private int m_unloadVerificationAttempt;
    private ScriptReloadRequest m_requestedReload;
    private bool m_initialCompileRequested;
    private int m_disposeStarted;
    private int m_isCompiling;
    private volatile bool m_disposed;

    /// <summary>
    /// Creates a script manager for one project.
    /// </summary>
    /// <param name="options">The project root, automatic compilation policy, and debounce configuration.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when the configured project root is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the configured debounce duration is negative.</exception>
    public ScriptManager(ScriptManagerOptions options)
        : this(options, compileGateProbe: null)
    {
    }

    internal ScriptManager(
        ScriptManagerOptions options,
        Func<CancellationToken, ValueTask>? compileGateProbe)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.projectRootDirectory))
            throw new ArgumentException("Project root directory is required.", nameof(options));
        if (options.debounceMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Debounce duration cannot be negative.");
        m_options = new ScriptManagerOptions
        {
            projectRootDirectory = System.IO.Path.GetFullPath(options.projectRootDirectory),
            autoCompile = options.autoCompile,
            debounceMilliseconds = options.debounceMilliseconds
        };
        m_artifactCache = new ScriptArtifactCache(m_options.outputDirectory);
        m_compileGateProbe = compileGateProbe;
        m_lifetimeToken = m_lifetimeCancellation.Token;
    }

    /// <summary>Gets whether a compilation currently owns the compiler gate.</summary>
    public bool isCompiling => Volatile.Read(ref m_isCompiling) != 0;

    /// <summary>Gets whether source or plugin changes are waiting to be compiled.</summary>
    public bool isCompilationPending
    {
        get
        {
            lock (m_sync)
                return m_requestedReload != ScriptReloadRequest.None;
        }
    }

    /// <summary>Gets the current compilation progress in the inclusive range from zero to one.</summary>
    public float compilationProgress => Volatile.Read(ref m_compilationProgress);

    /// <summary>Gets a short description of the current compilation stage.</summary>
    public string compilationStatus => Volatile.Read(ref m_compilationStatus);

    /// <summary>Gets the most recently completed compilation.</summary>
    public ScriptCompilationResult? lastCompilation
    {
        get
        {
            lock (m_sync)
                return m_lastCompilation;
        }
    }

    internal bool isUnloadVerificationPending
    {
        get
        {
            lock (m_sync)
                return m_unloadObservations.Count != 0;
        }
    }

    /// <summary>
    /// Generates IDE files, subscribes to the Asset Database, and requests the initial compilation.
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (!AssetManager.isInitialized)
            throw new InvalidOperationException("ScriptManager requires AssetManager to be initialized first.");
        System.IO.Directory.CreateDirectory(m_options.assetDirectory);
        System.IO.Directory.CreateDirectory(m_options.outputDirectory);
        _ = m_artifactCache.Collect([]);
        AssetManager.Rescan();
        GenerateProjectFiles();
        AssetManager.Changed -= OnAssetDatabaseChanged;
        AssetManager.Changed += OnAssetDatabaseChanged;
        AssetManager.SourceMountsChanged -= OnSourceMountsChanged;
        AssetManager.SourceMountsChanged += OnSourceMountsChanged;
        PluginManager.ActivationCandidateChanged -= OnPluginActivationCandidateChanged;
        PluginManager.ActivationCandidateChanged += OnPluginActivationCandidateChanged;
        if (m_options.autoCompile)
        {
            lock (m_sync)
            {
                m_requestedReload = ScriptReloadRequest.ReloadPlugins;
                m_initialCompileRequested = true;
                m_lastCompileRequestTimestamp = Environment.TickCount64;
            }
            SetCompilationProgress(0f, "Initial plugin and scripting reload queued.");
        }
    }

    /// <summary>
    /// Queues incremental recompilation of changed script assemblies.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown after the manager has been disposed.</exception>
    public void RecompileScripting()
    {
        QueueReload(ScriptReloadRequest.Recompile);
    }

    /// <summary>
    /// Queues a complete rebuild of both scripting load contexts while retaining the plugin generation.
    /// Valid cached artifacts are reused.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown after the manager has been disposed.</exception>
    public void ReloadScripting() => QueueReload(ScriptReloadRequest.ReloadScripting);

    /// <summary>
    /// Queues replacement of the unified plugin generation and both dependent scripting generations.
    /// Valid script artifacts are reused when plugin reference fingerprints are unchanged.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown after the manager has been disposed.</exception>
    public void ReloadPlugins() => QueueReload(ScriptReloadRequest.ReloadPlugins);

    /// <summary>
    /// Starts a pending compilation after the configured quiet period has elapsed.
    /// </summary>
    /// <remarks>The initial automatic request is immediately ready and does not observe the debounce duration.</remarks>
    /// <param name="compilation">The started compilation task, or <see langword="null"/> when none was ready.</param>
    /// <returns><see langword="true"/> when a pending compilation was started.</returns>
    internal bool TryCompilePending(out Task<ScriptCompilationResult>? compilation)
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
        if (AssetManager.isInitialized)
        {
            AssetManager.Update();
            AssetManager.Rescan();
        }
        compilation = CompileAsync(request).AsTask();
        return true;
    }

    /// <summary>
    /// Compiles both script assemblies and queues a successful generation for main-thread activation.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels compilation without replacing the active script generation.</param>
    /// <returns>The complete diagnostics, output path, and activation request produced by the compilation attempt.</returns>
    /// <exception cref="ObjectDisposedException">Thrown after the manager has been disposed.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> or the manager lifetime token is canceled.</exception>
    internal ValueTask<ScriptCompilationResult> CompileAsync(CancellationToken cancellationToken = default)
        => CompileAsync(ScriptReloadRequest.Recompile, cancellationToken);

    private async ValueTask<ScriptCompilationResult> CompileAsync(
        ScriptReloadRequest request,
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
            if (m_compileGateProbe is not null)
                await m_compileGateProbe(effectiveCancellation).ConfigureAwait(false);
            SetCompilationProgress(0f, "Generating IDE project files...");
            ScriptCompilationResult result;
            try
            {
                ScriptProjectGenerator.Generate(m_options);
                result = await ScriptCompiler
                    .CompileAsync(
                        m_options,
                        (progress, status) => SetCompilationProgress(
                            progress * C_COMPILATION_PROGRESS_SHARE,
                            status),
                        effectiveCancellation)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (effectiveCancellation.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                result = new ScriptCompilationResult(
                    success: false,
                    [new ScriptDiagnostic(
                        "INNO0001",
                        ScriptDiagnosticSeverity.Error,
                        exception.ToString(),
                        filePath: null,
                        line: 0,
                        column: 0)],
                    outputDirectory: null,
                    reloadRequests: null);
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
                    else if (result.success)
                    {
                        m_pendingCompilation = new PendingReload(result, request);
                    }
                    SetCompilationProgress(
                        result.success ? C_COMPILATION_PROGRESS_SHARE : 1f,
                        m_requestedReload != ScriptReloadRequest.None
                            ? "Compilation superseded by a queued reload request."
                            : result.success
                                ? "Script compilation completed."
                                : "Script compilation failed.");
                }
            }
            return result;
        }
        finally
        {
            Volatile.Write(ref m_isCompiling, 0);
            m_compileGate.Release();
        }
    }

    /// <summary>
    /// Applies the latest successful compilation at a caller-controlled main-thread safe point.
    /// </summary>
    /// <returns><see langword="true"/> when a pending generation was loaded or reloaded successfully; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ObjectDisposedException">Thrown after the manager has been disposed.</exception>
    internal bool ApplyPendingReload()
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

        IReadOnlyList<AssemblyLoadRequest> requests = SelectReloadRequests(pending);
        if (requests.Count == 0)
        {
            PluginManager.ActivatePending();
            ProjectSettingsManager.RebuildCurrent();
            PluginManager.CommitPending();
            CompletePendingReload(pending);
            ScriptDiagnosticPublisher.ClearReload();
            EditorReloadCoordinator.RefreshDiagnostics();
            SetCompilationProgress(1f, "No scripting changes detected.");
            return false;
        }

        SetCompilationProgress(C_STAGING_PROGRESS, "Staging script reload candidates...");
        AssemblyModuleInfo[] retiringModules = AssemblyManager.modules
            .Where(module => requests.Any(request => string.Equals(
                request.moduleName,
                module.moduleName,
                StringComparison.Ordinal)))
            .ToArray();
        using AssemblyReloadSession reload = AssemblyManager.BeginReload(requests);
        SetCompilationProgress(C_MIGRATION_PROGRESS, "Migrating active editor state...");
        Action activateCandidate = static () =>
        {
            PluginManager.ActivatePending();
            if (AssetManager.isInitialized)
                AssetManager.Update();
            if (ProjectSettingsManager.isInitialized)
                ProjectSettingsManager.RebuildCurrent();
        };
        Action restorePrevious = static () =>
        {
            PluginManager.RollbackPending();
            if (AssetManager.isInitialized)
                AssetManager.Update();
            if (ProjectSettingsManager.isInitialized)
                ProjectSettingsManager.RebuildCurrent();
        };
        AssemblyUnloadMonitor unload = EditorReloadCoordinator.Execute(
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
        m_activeCompilationDirectory = pending.compilation.outputDirectory;
        _ = m_artifactCache.Collect([m_activeCompilationDirectory]);
        RefreshModuleHandles();
        UpdateActiveFingerprints(requests);
        PluginManager.CommitPending();
        SetCompilationProgress(
            retiringModules.Length == 0 ? 1f : C_UNLOAD_VERIFICATION_PROGRESS,
            retiringModules.Length == 0
                ? "Script reload completed."
                : "Script reload committed. Verifying retired assembly unload...");
        ScriptDiagnosticPublisher.ClearReload();
        EditorReloadCoordinator.RefreshDiagnostics();
        CompletePendingReload(pending);
        return true;
    }

    /// <summary>
    /// Generates standard SDK-style game/editor projects and a solution for IDE tooling.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown after the manager has been disposed.</exception>
    public void GenerateProjectFiles()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (AssetManager.isInitialized)
        {
            AssetManager.Update();
            AssetManager.Rescan();
        }
        ScriptProjectGenerator.Generate(m_options);
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
                if (AssetManager.isInitialized)
                {
                    AssetManager.Changed -= OnAssetDatabaseChanged;
                    AssetManager.SourceMountsChanged -= OnSourceMountsChanged;
                }
                PluginManager.ActivationCandidateChanged -= OnPluginActivationCandidateChanged;
                lock (m_sync)
                {
                    m_requestedReload = ScriptReloadRequest.None;
                    m_initialCompileRequested = false;
                    m_pendingCompilation = null;
                    m_unloadObservations.Clear();
                }
                if (AssemblyManager.isInitialized)
                {
                    AssemblyModuleHandle[] modules =
                    [
                        .. new[] { m_editorScriptModule, m_runtimeScriptModule, m_pluginModule }
                            .OfType<AssemblyModuleHandle>()
                    ];
                    if (modules.Length > 0)
                        _ = AssemblyManager.Unload(modules);
                    m_pluginModule = null;
                    m_runtimeScriptModule = null;
                    m_editorScriptModule = null;
                    if (modules.Length > 0 && AssetManager.isInitialized)
                        AssetManager.Rescan();
                }
                m_activeCompilationDirectory = null;
                ScriptDiagnosticPublisher.ClearAll();
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

    internal bool AdvanceUnloadVerification(out Exception? failure)
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
            ScriptDiagnosticPublisher.ClearUnload();
            return true;
        }

        failure = CreateUnloadVerificationFailure(retainedModules);
        SetCompilationProgress(
            1f,
            "Script reload committed, but retired assembly unload verification failed.");
        ScriptDiagnosticPublisher.PublishUnloadFailure(failure);
        return true;
    }

    private void OnAssetDatabaseChanged(AssetChangeSet changeSet)
    {
        if (m_disposed || !m_options.autoCompile)
            return;
        for (int i = 0; i < changeSet.changes.Count; i++)
        {
            AssetChange change = changeSet.changes[i];
            if (!IsScriptInput(change.relativePath) && !IsScriptInput(change.oldRelativePath))
                continue;
            QueueReload(IsPluginInput(change.relativePath) || IsPluginInput(change.oldRelativePath)
                ? ScriptReloadRequest.ReloadPlugins
                : ScriptReloadRequest.Recompile);
            return;
        }
    }

    private void OnSourceMountsChanged()
    {
        if (m_disposed || !m_options.autoCompile)
            return;
        if (PluginManager.hasPendingActivation)
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
        ScriptDiagnosticPublisher.ClearUnload();
    }

    private IReadOnlyList<AssemblyLoadRequest> SelectReloadRequests(PendingReload pending)
    {
        IReadOnlyList<AssemblyLoadRequest> requests = pending.compilation.reloadRequests;
        if (requests.Count == 0)
            return [];
        if (m_pluginModule is null || m_runtimeScriptModule is null || m_editorScriptModule is null)
            return requests;
        if (pending.request == ScriptReloadRequest.ReloadPlugins)
            return requests;
        if (pending.request == ScriptReloadRequest.ReloadScripting)
            return requests.Where(static request => request.domain == AssemblyDomain.InnoScripting).ToArray();
        if (string.Equals(
                pending.compilation.outputDirectory,
                m_activeCompilationDirectory,
                StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var compiled = pending.compilation.compiledAssemblies.ToHashSet(StringComparer.OrdinalIgnoreCase);
        AssemblyLoadRequest? runtime = requests.SingleOrDefault(static request =>
            request.domain == AssemblyDomain.InnoScripting && request.scope == AssemblyScope.Runtime);
        AssemblyLoadRequest? editor = requests.SingleOrDefault(static request =>
            request.domain == AssemblyDomain.InnoScripting && request.scope == AssemblyScope.Editor);
        if (runtime is not null &&
            (GetOwnedAssemblyNames(runtime).Any(compiled.Contains) ||
             !string.Equals(
                 ComputeRequestFingerprint(runtime),
                 m_activeRuntimeFingerprint,
                 StringComparison.Ordinal)))
        {
            return requests.Where(static request => request.domain == AssemblyDomain.InnoScripting).ToArray();
        }
        if (editor is not null &&
            (GetOwnedAssemblyNames(editor).Any(compiled.Contains) ||
             !string.Equals(
                 ComputeRequestFingerprint(editor),
                 m_activeEditorFingerprint,
                 StringComparison.Ordinal)))
        {
            return [editor];
        }
        return [];
    }

    private void RefreshModuleHandles()
    {
        IReadOnlyList<AssemblyModuleInfo> modules = AssemblyManager.modules;
        m_pluginModule = modules.Single(static module => module.moduleName == "ProjectPlugins").handle;
        m_runtimeScriptModule = modules.Single(static module => module.moduleName == "RuntimeScripts").handle;
        m_editorScriptModule = modules.Single(static module => module.moduleName == "EditorScripts").handle;
    }

    private void UpdateActiveFingerprints(IReadOnlyList<AssemblyLoadRequest> requests)
    {
        foreach (AssemblyLoadRequest request in requests)
        {
            string fingerprint = ComputeRequestFingerprint(request);
            if (request.domain == AssemblyDomain.InnoPlugin)
                m_activePluginFingerprint = fingerprint;
            else if (request.scope == AssemblyScope.Runtime)
                m_activeRuntimeFingerprint = fingerprint;
            else
                m_activeEditorFingerprint = fingerprint;
        }
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
            if (ReferenceEquals(m_pendingCompilation, applied))
                m_pendingCompilation = null;
        }
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
        ScriptReloadRequest request);

    private sealed record UnloadObservation(
        AssemblyUnloadMonitor monitor,
        IReadOnlyList<AssemblyModuleInfo> modules);

    private enum ScriptReloadRequest
    {
        None,
        Recompile,
        ReloadScripting,
        ReloadPlugins
    }
}
