using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets;
using Inno.Assets.Core;
using Inno.Core.Assemblies;
using Inno.Core.Framework;
using Inno.Core.Reflection;
using Inno.Engine.Scene.Assets;

namespace Inno.Editor.Scripting;

/// <summary>
/// Watches, compiles, and atomically activates one project's C# script assemblies.
/// </summary>
public sealed class ScriptManager : IDisposable
{
    private readonly object m_sync = new();
    private readonly ScriptManagerOptions m_options;
    private readonly SemaphoreSlim m_compileGate = new(1, 1);
    private readonly CancellationTokenSource m_lifetimeCancellation = new();
    private readonly ScriptArtifactCache m_artifactCache;
    private readonly Func<CancellationToken, ValueTask>? m_compileGateProbe;

    private ScriptCompilationResult? m_pendingCompilation;
    private ScriptCompilationResult? m_lastCompilation;
    private AssemblyModuleHandle? m_scriptModule;
    private string? m_activeCompilationDirectory;
    private string m_compilationStatus = "Waiting for script changes.";
    private long m_lastCompileRequestTimestamp;
    private float m_compilationProgress;
    private bool m_compileRequested;
    private bool m_initialCompileRequested;
    private int m_isCompiling;
    private bool m_disposed;

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
    }

    /// <summary>Gets whether a compilation currently owns the compiler gate.</summary>
    public bool isCompiling => Volatile.Read(ref m_isCompiling) != 0;

    /// <summary>Gets whether source or plugin changes are waiting to be compiled.</summary>
    public bool isCompilationPending
    {
        get
        {
            lock (m_sync)
                return m_compileRequested;
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
        if (m_options.autoCompile)
        {
            lock (m_sync)
            {
                m_compileRequested = true;
                m_initialCompileRequested = true;
                m_lastCompileRequestTimestamp = Environment.TickCount64;
            }
        }
    }

    /// <summary>
    /// Marks scripts as changed without compiling on the file-watcher thread.
    /// </summary>
    public void RequestCompile()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        lock (m_sync)
        {
            m_compileRequested = true;
            m_lastCompileRequestTimestamp = Environment.TickCount64;
        }
    }

    /// <summary>
    /// Starts a pending compilation after the configured quiet period has elapsed.
    /// </summary>
    /// <remarks>The initial automatic request is immediately ready and does not observe the debounce duration.</remarks>
    /// <param name="compilation">The started compilation task, or <see langword="null"/> when none was ready.</param>
    /// <returns><see langword="true"/> when a pending compilation was started.</returns>
    public bool TryCompilePending(out Task<ScriptCompilationResult>? compilation)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        lock (m_sync)
        {
            long elapsed = Environment.TickCount64 - m_lastCompileRequestTimestamp;
            if (!m_compileRequested ||
                isCompiling ||
                !m_initialCompileRequested && elapsed < m_options.debounceMilliseconds)
            {
                compilation = null;
                return false;
            }
            m_compileRequested = false;
            m_initialCompileRequested = false;
        }
        if (AssetManager.isInitialized)
        {
            AssetManager.Update();
            AssetManager.Rescan();
        }
        compilation = CompileAsync().AsTask();
        return true;
    }

    /// <summary>
    /// Compiles both script assemblies and queues a successful generation for main-thread activation.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels compilation without replacing the active script generation.</param>
    /// <returns>The complete diagnostics, output path, and activation request produced by the compilation attempt.</returns>
    /// <exception cref="ObjectDisposedException">Thrown after the manager has been disposed.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> or the manager lifetime token is canceled.</exception>
    public async ValueTask<ScriptCompilationResult> CompileAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            m_lifetimeCancellation.Token);
        CancellationToken effectiveCancellation = linkedCancellation.Token;
        await m_compileGate.WaitAsync(effectiveCancellation).ConfigureAwait(false);
        try
        {
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
                        SetCompilationProgress,
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
                    loadRequest: null);
            }
            lock (m_sync)
            {
                m_lastCompilation = result;
                if (result.success)
                    m_pendingCompilation = result;
            }
            SetCompilationProgress(
                1f,
                result.success ? "Script compilation completed." : "Script compilation failed.");
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
    public bool ApplyPendingReload()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ScriptCompilationResult? pending;
        lock (m_sync)
            pending = m_pendingCompilation;
        if (pending?.loadRequest is not AssemblyLoadRequest request)
            return false;

        if (m_scriptModule is not AssemblyModuleHandle handle)
        {
            m_scriptModule = AssemblyManager.Load(request);
            m_activeCompilationDirectory = pending.outputDirectory;
            _ = m_artifactCache.Collect([m_activeCompilationDirectory]);
            ScriptDiagnosticPublisher.ClearReload();
            CompletePendingReload(pending);
            return true;
        }

        using AssemblyReloadSession reload = AssemblyManager.BeginReload(handle, request);
        TypeCacheReloadContext typeReload = reload.context.GetContext<TypeCacheReloadContext>();
        ISceneReloadMigration migration = SceneReloadService.Capture(typeReload);
        migration.PrepareForActivation();
        if (Shell.isInitialized)
        {
            foreach (object retiredObject in migration.retiredObjects)
                Shell.instance.coroutineScheduler.StopAllCoroutines(retiredObject);
        }
        try
        {
            reload.Activate();
            migration.Apply();
            migration.Complete();
            _ = reload.Complete();
            m_activeCompilationDirectory = pending.outputDirectory;
            _ = m_artifactCache.Collect([m_activeCompilationDirectory]);
        }
        catch
        {
            migration.RollbackStructure();
            reload.Rollback();
            migration.RestorePreviousState();
            throw;
        }
        ScriptDiagnosticPublisher.PublishReload(migration.diagnostics);
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
    /// Stops Asset Database observation and unloads the active script module.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        m_lifetimeCancellation.Cancel();
        if (AssetManager.isInitialized)
            AssetManager.Changed -= OnAssetDatabaseChanged;
        lock (m_sync)
        {
            m_compileRequested = false;
            m_initialCompileRequested = false;
            m_pendingCompilation = null;
        }
        if (m_scriptModule is AssemblyModuleHandle handle && AssemblyManager.isInitialized)
            _ = AssemblyManager.Unload(handle);
        ScriptDiagnosticPublisher.ClearAll();
        m_lifetimeCancellation.Dispose();
        GC.SuppressFinalize(this);
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
            RequestCompile();
            return;
        }
    }

    private void SetCompilationProgress(float progress, string status)
    {
        Volatile.Write(ref m_compilationProgress, Math.Clamp(progress, 0f, 1f));
        Volatile.Write(ref m_compilationStatus, status);
    }

    private void CompletePendingReload(ScriptCompilationResult applied)
    {
        lock (m_sync)
        {
            if (ReferenceEquals(m_pendingCompilation, applied))
                m_pendingCompilation = null;
        }
    }

    private static bool IsScriptInput(string path)
        => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
           path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
           path.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) ||
           path.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase) ||
           path.EndsWith(".iasmdef", StringComparison.OrdinalIgnoreCase);
}
