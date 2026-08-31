using System;

using Inno.Core.Diagnose;

namespace Inno.Editor.Application;

internal sealed class EditorProjectDiagnosticPublisher : IDisposable
{
    private const string C_PERSISTENCE_GROUP = "Project State Persistence";

    private string m_state = string.Empty;

    internal bool hasPersistenceFailure => !string.IsNullOrEmpty(m_state);

    internal bool PublishPersistenceFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        string state = exception.ToString();
        if (string.Equals(m_state, state, StringComparison.Ordinal))
            return false;
        Diagnostics.Set(
            C_PERSISTENCE_GROUP,
            Diagnostic.Error("EDITOR-PERSISTENCE", exception.Message));
        m_state = state;
        return true;
    }

    internal void ResolvePersistence()
    {
        if (string.IsNullOrEmpty(m_state))
            return;
        Diagnostics.Clear(C_PERSISTENCE_GROUP);
        m_state = string.Empty;
    }

    public void Dispose()
        => ResolvePersistence();
}
