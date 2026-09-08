namespace Inno.Editor.Scripting;

/// <summary>
/// Identifies the lifecycle state of one exact script compilation request.
/// </summary>
public enum ScriptCompilationTicketState
{
    /// <summary>
    /// The request is queued behind the active compiler operation.
    /// </summary>
    Queued,

    /// <summary>
    /// The request owns the compiler pipeline or is activating its candidate generation.
    /// </summary>
    Compiling,

    /// <summary>
    /// The request compiled and atomically activated its candidate generation.
    /// </summary>
    Succeeded,

    /// <summary>
    /// Compilation or candidate activation failed without replacing the active generation.
    /// </summary>
    Failed,

    /// <summary>
    /// The request was explicitly canceled before activation.
    /// </summary>
    Canceled,

    /// <summary>
    /// A newer request replaced this request before its candidate could activate.
    /// </summary>
    Superseded
}
