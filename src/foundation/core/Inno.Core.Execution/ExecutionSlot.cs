using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Inno.Core.Execution;

/// <summary>
/// Provides an isolated, revocable execution binding whose values never survive scope retirement.
/// </summary>
/// <typeparam name="TValue">
/// The explicitly selected service or immutable execution value.
/// </typeparam>
public sealed class ExecutionSlot<TValue>
{
    private readonly AsyncLocal<Binding?> m_current = new();
    private readonly string m_name;

    /// <summary>
    /// Creates a slot without binding a process-global service.
    /// </summary>
    /// <param name="name">
    /// The diagnostic name used when access is invalid.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The name is empty.
    /// </exception>
    public ExecutionSlot(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        m_name = name;
    }

    /// <summary>
    /// Gets the current live value, rejecting expired or wrong-thread bindings.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No live binding exists on the permitted thread.
    /// </exception>
    public TValue current => TryGet(out TValue? value)
        ? value
        : throw new InvalidOperationException($"No active '{m_name}' execution scope is available to this caller.");

    /// <summary>
    /// Binds a value until the returned scope is retired in strict last-in-first-out order.
    /// </summary>
    /// <param name="value">
    /// The service or value owned by the enclosing lifetime.
    /// </param>
    /// <param name="threadAffine">
    /// Whether access and retirement require the entering thread.
    /// </param>
    /// <returns>
    /// A scope that revokes inherited bindings and clears their strong references on retirement.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// The value is null.
    /// </exception>
    public IDisposable Enter(TValue value, bool threadAffine = false)
    {
        ArgumentNullException.ThrowIfNull(value);
        var binding = new Binding(this, value, m_current.Value, threadAffine);
        m_current.Value = binding;
        return binding;
    }

    /// <summary>
    /// Temporarily masks this slot without exposing an enclosing binding.
    /// </summary>
    /// <returns>
    /// A strict scope that restores the enclosing binding when retired.
    /// </returns>
    public IDisposable Suspend()
    {
        var binding = new Binding(this, default!, m_current.Value, false, assigned: false);
        m_current.Value = binding;
        return binding;
    }

    /// <summary>
    /// Tries to read the current binding without falling through an expired child scope.
    /// </summary>
    /// <param name="value">
    /// Receives the live value, or the default value when access is unavailable.
    /// </param>
    /// <returns>
    /// Whether a live binding is available on this thread.
    /// </returns>
    public bool TryGet([MaybeNullWhen(false)] out TValue value)
    {
        Binding? binding = m_current.Value;
        if (binding is not null)
            return binding.TryGet(out value);
        value = default;
        return false;
    }

    private sealed class Binding : IDisposable
    {
        private readonly ExecutionSlot<TValue> m_slot;
        private readonly object m_sync = new();
        private readonly int m_threadId;
        private readonly bool m_assigned;
        private Binding? m_parent;
        private TValue m_value;
        private bool m_active = true;

        internal Binding(ExecutionSlot<TValue> slot, TValue value, Binding? parent, bool threadAffine, bool assigned = true)
        {
            m_slot = slot;
            m_value = value;
            m_parent = parent;
            m_threadId = threadAffine ? Environment.CurrentManagedThreadId : 0;
            m_assigned = assigned;
        }

        internal bool TryGet([MaybeNullWhen(false)] out TValue value)
        {
            lock (m_sync)
            {
                if (m_active && m_assigned && (m_threadId == 0 || m_threadId == Environment.CurrentManagedThreadId))
                {
                    value = m_value;
                    return true;
                }
                value = default;
                return false;
            }
        }

        /// <summary>
        /// Revokes this binding only at the top of its execution stack and clears retained values.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The binding is retired out of order or on an invalid thread.
        /// </exception>
        public void Dispose()
        {
            lock (m_sync)
            {
                if (!m_active)
                    return;
                if (!ReferenceEquals(m_slot.m_current.Value, this))
                    throw new InvalidOperationException($"'{m_slot.m_name}' scopes must retire in last-in-first-out order.");
                if (m_threadId != 0 && m_threadId != Environment.CurrentManagedThreadId)
                    throw new InvalidOperationException($"'{m_slot.m_name}' must retire on its owner thread.");
                m_slot.m_current.Value = m_parent;
                m_active = false;
                m_value = default!;
                m_parent = null;
            }
        }
    }
}
