using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.Events;

namespace Inno.Editor.HotKeys;

/// <summary>
/// Maps keyboard gestures to editor commands and dispatches them to contextual handlers.
/// </summary>
public sealed class EditorHotKeyMap : IDisposable
{
    private readonly Dictionary<string, HotKeyGesture> m_bindings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<CommandHandler>> m_handlers = new(StringComparer.Ordinal);
    private long m_nextHandlerId;
    private bool m_isDisposed;

    /// <summary>
    /// Assigns a gesture to a command.
    /// </summary>
    /// <param name="commandId">Stable command identifier.</param>
    /// <param name="gesture">Keyboard gesture.</param>
    public void Bind(string commandId, HotKeyGesture gesture)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        m_bindings[commandId] = gesture;
    }

    /// <summary>
    /// Registers a contextual command handler.
    /// </summary>
    /// <param name="commandId">Stable command identifier.</param>
    /// <param name="execute">Action to invoke.</param>
    /// <param name="canExecute">Optional availability predicate.</param>
    /// <param name="priority">Higher-priority handlers are considered first.</param>
    /// <returns>A token that unregisters the handler.</returns>
    public IDisposable Register(
        string commandId,
        Action execute,
        Func<bool>? canExecute = null,
        int priority = 0)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentNullException.ThrowIfNull(execute);
        if (!m_handlers.TryGetValue(commandId, out List<CommandHandler>? handlers))
        {
            handlers = [];
            m_handlers.Add(commandId, handlers);
        }

        var handler = new CommandHandler(++m_nextHandlerId, execute, canExecute, priority);
        handlers.Add(handler);
        handlers.Sort(static (left, right) => right.priority.CompareTo(left.priority));
        return new Registration(this, commandId, handler.id);
    }

    /// <summary>
    /// Dispatches a key press to the first available handler for the matching command.
    /// </summary>
    /// <param name="keyEvent">Keyboard event.</param>
    /// <returns><see langword="true"/> when a command handler ran.</returns>
    public bool Process(KeyPressedEvent keyEvent)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(keyEvent);
        foreach ((string commandId, HotKeyGesture gesture) in m_bindings)
        {
            if (!gesture.Matches(keyEvent) || !m_handlers.TryGetValue(commandId, out List<CommandHandler>? handlers))
                continue;
            CommandHandler[] snapshot = handlers.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                CommandHandler handler = snapshot[i];
                if (handler.canExecute is not null && !handler.canExecute())
                    continue;
                handler.execute();
                return true;
            }
        }
        return false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (m_isDisposed)
            return;
        m_isDisposed = true;
        m_bindings.Clear();
        m_handlers.Clear();
    }

    private void Unregister(string commandId, long handlerId)
    {
        if (m_isDisposed || !m_handlers.TryGetValue(commandId, out List<CommandHandler>? handlers))
            return;
        _ = handlers.RemoveAll(handler => handler.id == handlerId);
        if (handlers.Count == 0)
            m_handlers.Remove(commandId);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(m_isDisposed, this);
    }

    private sealed record CommandHandler(long id, Action execute, Func<bool>? canExecute, int priority);

    private sealed class Registration(EditorHotKeyMap owner, string commandId, long handlerId) : IDisposable
    {
        private EditorHotKeyMap? m_owner = owner;

        public void Dispose()
        {
            EditorHotKeyMap? currentOwner = m_owner;
            m_owner = null;
            currentOwner?.Unregister(commandId, handlerId);
        }
    }
}
