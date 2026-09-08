using Inno.Scripting.Compiler;

namespace Inno.Editor.Scripting;

/// <summary>
/// Exposes the immutable observer view of one exact script compilation and activation request.
/// </summary>
public interface IScriptCompilationTicket
{
    /// <summary>
    /// Gets the producer-local monotonic identifier of this request.
    /// </summary>
    long requestId { get; }

    /// <summary>
    /// Gets the current lifecycle state of this exact request.
    /// </summary>
    ScriptCompilationTicketState state { get; }

    /// <summary>
    /// Gets a human-readable description of the current or terminal state.
    /// </summary>
    string status { get; }

    /// <summary>
    /// Gets the completed compiler result, or <see langword="null"/> before the compiler finishes.
    /// </summary>
    ScriptCompilationResult? result { get; }

    /// <summary>
    /// Gets whether no further state transition is valid for this request.
    /// </summary>
    bool isCompleted { get; }
}
