using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Inno.Core.Serialization;

internal sealed class SerializationOperation
{
    private readonly List<Action> m_completionCallbacks = [];
    private readonly HashSet<object> m_scheduledObjects = new(ReferenceComparer.instance);
    private readonly List<object> m_scheduledObjectOrder = [];
    private readonly Dictionary<object, string> m_capturePaths = new(ReferenceComparer.instance);
    private bool m_isActive = true;

    internal SerializationOperation(SerializationContext context)
    {
        this.context = context;
    }

    internal SerializationContext context { get; }

    internal void EnsureActive()
    {
        if (!m_isActive)
            throw new InvalidOperationException("The serialization reader or writer is no longer active.");
    }

    internal void AddCompletionCallback(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        EnsureActive();
        m_completionCallbacks.Add(callback);
    }

    internal void ScheduleRestoredObject(ISerializable value)
    {
        EnsureActive();
        if (!m_scheduledObjects.Add(value))
            return;
        m_scheduledObjectOrder.Add(value);

        Action? callback = ReflectionMetadata.CreateRestoreCallback(value);
        if (callback is not null)
            m_completionCallbacks.Add(callback);
    }

    internal void EnterCapture(object value, string path)
    {
        EnsureActive();
        if (m_capturePaths.TryGetValue(value, out string? existingPath))
        {
            throw new InvalidOperationException(
                $"Serialization cycle detected at '{path}'. The same object is already being written at '{existingPath}'.");
        }

        m_capturePaths.Add(value, path);
    }

    internal Checkpoint CreateCheckpoint()
    {
        EnsureActive();
        return new Checkpoint(m_completionCallbacks.Count, m_scheduledObjectOrder.Count);
    }

    internal void Rollback(Checkpoint checkpoint)
    {
        EnsureActive();
        if (checkpoint.callbackCount < 0 ||
            checkpoint.callbackCount > m_completionCallbacks.Count ||
            checkpoint.scheduledObjectCount < 0 ||
            checkpoint.scheduledObjectCount > m_scheduledObjectOrder.Count)
        {
            throw new InvalidOperationException("The serialization operation checkpoint is invalid.");
        }

        if (m_completionCallbacks.Count > checkpoint.callbackCount)
        {
            m_completionCallbacks.RemoveRange(
                checkpoint.callbackCount,
                m_completionCallbacks.Count - checkpoint.callbackCount);
        }
        for (int i = m_scheduledObjectOrder.Count - 1; i >= checkpoint.scheduledObjectCount; i--)
        {
            m_scheduledObjects.Remove(m_scheduledObjectOrder[i]);
            m_scheduledObjectOrder.RemoveAt(i);
        }
    }

    internal void ExitCapture(object value)
    {
        if (m_isActive)
            m_capturePaths.Remove(value);
    }

    internal void Complete()
    {
        EnsureActive();
        try
        {
            for (int i = 0; i < m_completionCallbacks.Count; i++)
                m_completionCallbacks[i]();
        }
        finally
        {
            m_isActive = false;
            m_completionCallbacks.Clear();
            m_scheduledObjects.Clear();
            m_scheduledObjectOrder.Clear();
            m_capturePaths.Clear();
        }
    }

    internal void Fail()
    {
        if (!m_isActive)
            return;

        m_isActive = false;
        m_completionCallbacks.Clear();
        m_scheduledObjects.Clear();
        m_scheduledObjectOrder.Clear();
        m_capturePaths.Clear();
    }

    internal readonly record struct Checkpoint(int callbackCount, int scheduledObjectCount);

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        internal static ReferenceComparer instance { get; } = new();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
