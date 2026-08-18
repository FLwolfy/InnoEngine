using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

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

    private FileSystemWatcher? m_watcher;
    private CancellationTokenSource? m_debounceCancellation;
    private ScriptCompilationResult? m_pendingCompilation;
    private AssemblyModuleHandle? m_scriptModule;
    private long m_generation;
    private bool m_disposed;

    /// <summary>
    /// Creates a script manager for one project.
    /// </summary>
    public ScriptManager(ScriptManagerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.projectRootDirectory))
            throw new ArgumentException("Project root directory is required.", nameof(options));
        if (options.debounceMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Debounce duration cannot be negative.");
        m_options = new ScriptManagerOptions
        {
            projectRootDirectory = Path.GetFullPath(options.projectRootDirectory),
            autoCompile = options.autoCompile,
            debounceMilliseconds = options.debounceMilliseconds
        };
    }

    /// <summary>Gets whether a compilation currently owns the compiler gate.</summary>
    public bool isCompiling { get; private set; }

    /// <summary>Gets the most recently completed compilation.</summary>
    public ScriptCompilationResult? lastCompilation { get; private set; }

    /// <summary>Occurs after a complete game/editor compilation attempt.</summary>
    public event Action<ScriptCompilationResult>? CompilationCompleted;

    /// <summary>
    /// Generates IDE files, starts file observation, and requests the initial compilation.
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        Directory.CreateDirectory(m_options.assetDirectory);
        Directory.CreateDirectory(m_options.outputDirectory);
        GenerateProjectFiles();
        if (m_watcher is null)
        {
            m_watcher = new FileSystemWatcher(m_options.assetDirectory)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            m_watcher.Changed += OnProjectFileChanged;
            m_watcher.Created += OnProjectFileChanged;
            m_watcher.Deleted += OnProjectFileChanged;
            m_watcher.Renamed += OnProjectFileChanged;
        }
        if (m_options.autoCompile)
            RequestCompile();
    }

    /// <summary>
    /// Debounces and schedules compilation without mutating runtime state on the watcher thread.
    /// </summary>
    public void RequestCompile()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        CancellationTokenSource cancellation;
        lock (m_sync)
        {
            m_debounceCancellation?.Cancel();
            m_debounceCancellation?.Dispose();
            cancellation = new CancellationTokenSource();
            m_debounceCancellation = cancellation;
        }
        _ = CompileAfterDelayAsync(cancellation.Token);
    }

    /// <summary>
    /// Compiles both script assemblies and queues a successful generation for main-thread activation.
    /// </summary>
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
            isCompiling = true;
            long generation = Interlocked.Increment(ref m_generation);
            ScriptCompilationResult result;
            try
            {
                ScriptProjectGenerator.Generate(m_options);
                result = await ScriptCompiler
                    .CompileAsync(m_options, generation, effectiveCancellation)
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
                        exception.Message,
                        filePath: null,
                        line: 0,
                        column: 0)],
                    outputDirectory: null,
                    loadRequest: null);
            }
            lock (m_sync)
            {
                lastCompilation = result;
                m_pendingCompilation = result.success ? result : null;
            }
            CompilationCompleted?.Invoke(result);
            return result;
        }
        finally
        {
            isCompiling = false;
            m_compileGate.Release();
        }
    }

    /// <summary>
    /// Applies the latest successful compilation at a caller-controlled main-thread safe point.
    /// </summary>
    public bool ApplyPendingReload()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ScriptCompilationResult? pending;
        lock (m_sync)
        {
            pending = m_pendingCompilation;
            m_pendingCompilation = null;
        }
        if (pending?.loadRequest is not AssemblyLoadRequest request)
            return false;

        if (m_scriptModule is not AssemblyModuleHandle handle)
        {
            m_scriptModule = AssemblyManager.Load(request);
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
        }
        catch
        {
            migration.RollbackStructure();
            reload.Rollback();
            migration.RestorePreviousState();
            throw;
        }
        return true;
    }

    /// <summary>
    /// Generates standard SDK-style game/editor projects and a solution for IDE tooling.
    /// </summary>
    public void GenerateProjectFiles()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ScriptProjectGenerator.Generate(m_options);
    }

    /// <summary>
    /// Stops observation and unloads the active script module.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        m_lifetimeCancellation.Cancel();
        if (m_watcher is not null)
        {
            m_watcher.EnableRaisingEvents = false;
            m_watcher.Changed -= OnProjectFileChanged;
            m_watcher.Created -= OnProjectFileChanged;
            m_watcher.Deleted -= OnProjectFileChanged;
            m_watcher.Renamed -= OnProjectFileChanged;
            m_watcher.Dispose();
        }
        lock (m_sync)
        {
            m_debounceCancellation?.Cancel();
            m_debounceCancellation?.Dispose();
            m_debounceCancellation = null;
            m_pendingCompilation = null;
        }
        if (m_scriptModule is AssemblyModuleHandle handle && AssemblyManager.isInitialized)
            _ = AssemblyManager.Unload(handle);
        m_lifetimeCancellation.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task CompileAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(m_options.debounceMilliseconds, cancellationToken).ConfigureAwait(false);
            await CompileAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void OnProjectFileChanged(object sender, FileSystemEventArgs args)
    {
        if (m_disposed || !IsScriptInput(args.FullPath))
            return;
        RequestCompile();
    }

    private static bool IsScriptInput(string path)
        => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
           path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
           path.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) ||
           path.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase);
}
