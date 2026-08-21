using System;
using System.Threading.Tasks;

using Inno.Core.Logging;
using Inno.Editor.Core;

namespace Inno.Editor.Scripting;

/// <summary>Owns script compilation and activation for one editor project.</summary>
[EditorModule(order: 100)]
public sealed class ScriptingModule : EditorModule, IDisposable
{
    private ScriptManager? m_manager;
    private Task<ScriptCompilationResult>? m_compilation;
    private bool m_hideCompilationOnNextUpdate;
    private bool m_showCompilation;

    /// <inheritdoc />
    public override bool blocksFollowingUpdates => isCompiling;

    /// <summary>Gets whether script compilation is currently active.</summary>
    public bool isCompiling => m_showCompilation || m_compilation is not null || m_manager?.isCompiling == true;

    /// <summary>Gets the current compiler progress.</summary>
    public float progress => m_manager?.compilationProgress ?? 0f;

    /// <summary>Gets the current compiler stage.</summary>
    public string status => m_manager?.compilationStatus ?? "Waiting for script changes.";

    /// <inheritdoc />
    protected override void OnStart(EditorContext context)
    {
        m_manager = new ScriptManager(new ScriptManagerOptions
        {
            projectRootDirectory = context.projectDirectory
        });
        m_manager.CompilationCompleted += OnCompilationCompleted;
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
        if (m_manager is null || m_compilation is not null || !context.isFocused)
            return;
        if (m_manager.TryCompilePending(out Task<ScriptCompilationResult>? compilation))
        {
            m_compilation = compilation;
            m_showCompilation = true;
        }
    }

    /// <inheritdoc />
    protected override void OnStop(EditorContext context) => DisposeManager();

    /// <inheritdoc />
    public void Dispose()
    {
        DisposeManager();
        GC.SuppressFinalize(this);
    }

    private void CompleteCompilation()
    {
        Task<ScriptCompilationResult>? compilation = m_compilation;
        if (compilation is null || !compilation.IsCompleted || m_manager is null)
            return;
        try
        {
            ScriptCompilationResult result = compilation.GetAwaiter().GetResult();
            if (result.success)
                _ = m_manager.ApplyPendingReload();
        }
        catch (Exception exception)
        {
            Log.Error("Script reload failed: {0}", exception);
        }
        finally
        {
            m_compilation = null;
            m_hideCompilationOnNextUpdate = true;
        }
    }

    private void DisposeManager()
    {
        if (m_manager is null)
            return;
        m_manager.CompilationCompleted -= OnCompilationCompleted;
        m_manager.Dispose();
        m_manager = null;
        m_compilation = null;
        m_hideCompilationOnNextUpdate = false;
        m_showCompilation = false;
    }

    private static void OnCompilationCompleted(ScriptCompilationResult result)
    {
        foreach (ScriptDiagnostic diagnostic in result.diagnostics)
        {
            string location = diagnostic.filePath is null
                ? string.Empty
                : $"{diagnostic.filePath}({diagnostic.line},{diagnostic.column}): ";
            string message = $"{location}{diagnostic.id}: {diagnostic.message}";
            if (diagnostic.severity == ScriptDiagnosticSeverity.Error)
                Log.Error(message);
            else if (diagnostic.severity == ScriptDiagnosticSeverity.Warning)
                Log.Warn(message);
        }
    }
}
