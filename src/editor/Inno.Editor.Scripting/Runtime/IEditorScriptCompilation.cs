namespace Inno.Editor.Scripting;

/// <summary>Identifies whether the active project script generation can safely start game simulation.</summary>
public enum EditorScriptCompilationState
{
    /// <summary>The script service has not produced its initial active generation.</summary>
    Initializing,

    /// <summary>A requested script generation is compiling, activating, or completing unload verification.</summary>
    Compiling,

    /// <summary>The most recent compilation succeeded and its generation is active.</summary>
    Ready,

    /// <summary>The most recent compilation failed, so game simulation must not start.</summary>
    Failed
}

/// <summary>Exposes the minimal script-generation readiness contract required by editor workflows.</summary>
public interface IEditorScriptCompilation
{
    /// <summary>Gets the current script-generation readiness state.</summary>
    EditorScriptCompilationState state { get; }

    /// <summary>Gets a human-readable description of current compiler work or readiness.</summary>
    string status { get; }

    /// <summary>
    /// Gets the most recently completed compilation, or <see langword="null"/> before the first attempt completes.
    /// </summary>
    ScriptCompilationResult? lastCompilation { get; }
}
