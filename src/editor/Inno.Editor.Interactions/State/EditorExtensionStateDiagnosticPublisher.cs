using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.Diagnose;

namespace Inno.Editor.Interactions;

internal sealed class EditorExtensionStateDiagnosticPublisher : IDisposable
{
    private const string C_CAPTURE_GROUP = "Editor State Capture";
    private const string C_RESTORE_GROUP = "Editor State Restore";
    private const string C_SAVE_GROUP = "Editor State Save";

    private readonly Dictionary<string, string> m_states = new(StringComparer.Ordinal);

    internal bool PublishCapture(IReadOnlyList<string> messages)
        => Publish(C_CAPTURE_GROUP, "EDITOR-STATE-CAPTURE", messages);

    internal bool PublishRestore(IReadOnlyList<string> messages)
        => Publish(C_RESTORE_GROUP, "EDITOR-STATE-RESTORE", messages);

    internal bool PublishSave(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return Publish(C_SAVE_GROUP, "EDITOR-STATE-SAVE", [exception.Message]);
    }

    internal void ResolveSave()
        => Resolve(C_SAVE_GROUP);

    public void Dispose()
    {
        foreach (string group in m_states.Keys.ToArray())
            Resolve(group);
    }

    private bool Publish(string group, string code, IReadOnlyList<string> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        string[] current = messages
            .Where(static message => !string.IsNullOrWhiteSpace(message))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (current.Length == 0)
        {
            Resolve(group);
            return false;
        }
        string state = string.Join('\n', current);
        if (m_states.TryGetValue(group, out string? previous) &&
            string.Equals(previous, state, StringComparison.Ordinal))
        {
            return false;
        }
        Diagnostics.Set(
            group,
            current.Select(message => Diagnostic.Error(code, message)));
        m_states[group] = state;
        return true;
    }

    private void Resolve(string group)
    {
        if (!m_states.Remove(group))
            return;
        Diagnostics.Clear(group);
    }
}
