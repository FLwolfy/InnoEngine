using System;
using System.Threading;

using Inno.Scripting.Compiler;

namespace Inno.Editor.Scripting;

internal sealed class ScriptCompilationTicket : IScriptCompilationTicket
{
    private ScriptCompilationResult? m_result;
    private string m_status;
    private int m_state;

    internal ScriptCompilationTicket(long requestId, string status)
    {
        this.requestId = requestId;
        m_status = status;
        m_state = (int)ScriptCompilationTicketState.Queued;
    }

    /// <summary>
    /// Gets the scalar measurement or identity associated with the current state.
    /// </summary>
    public long requestId { get; }

    /// <summary>
    /// Gets the current lifecycle state observed by callers.
    /// </summary>
    public ScriptCompilationTicketState state
        => (ScriptCompilationTicketState)Volatile.Read(ref m_state);

    /// <summary>
    /// Gets the current human-readable operation status.
    /// </summary>
    public string status => Volatile.Read(ref m_status);

    /// <summary>
    /// Gets the completed compilation result, or null while work is still pending.
    /// </summary>
    public ScriptCompilationResult? result => Volatile.Read(ref m_result);

    /// <summary>
    /// Gets whether this value is completed.
    /// </summary>
    public bool isCompleted
        => state is ScriptCompilationTicketState.Succeeded
            or ScriptCompilationTicketState.Failed
            or ScriptCompilationTicketState.Canceled
            or ScriptCompilationTicketState.Superseded;

    internal void MarkCompiling(string status)
        => Transition(ScriptCompilationTicketState.Compiling, status, result: null);

    internal void MarkSucceeded(ScriptCompilationResult result, string status)
        => Transition(ScriptCompilationTicketState.Succeeded, status, result);

    internal void MarkFailed(ScriptCompilationResult? result, string status)
        => Transition(ScriptCompilationTicketState.Failed, status, result);

    internal void MarkCanceled(string status)
        => Transition(ScriptCompilationTicketState.Canceled, status, result: null);

    internal void MarkSuperseded()
        => Transition(
            ScriptCompilationTicketState.Superseded,
            "A newer script compilation request replaced this ticket.",
            result: null);

    private void Transition(
        ScriptCompilationTicketState state,
        string status,
        ScriptCompilationResult? result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        ScriptCompilationTicketState current = this.state;
        if (current is ScriptCompilationTicketState.Succeeded
            or ScriptCompilationTicketState.Failed
            or ScriptCompilationTicketState.Canceled
            or ScriptCompilationTicketState.Superseded)
        {
            return;
        }
        if (result is not null)
            Volatile.Write(ref m_result, result);
        Volatile.Write(ref m_status, status);
        Volatile.Write(ref m_state, (int)state);
    }
}
