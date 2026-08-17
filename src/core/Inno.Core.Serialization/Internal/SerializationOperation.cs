using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Inno.Core.Serialization;

internal sealed class SerializationOperation
{
    private readonly List<Action> m_completionCallbacks = [];
    private readonly HashSet<object> m_scheduledObjects = new(ReferenceComparer.instance);
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
        m_capturePaths.Clear();
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        internal static ReferenceComparer instance { get; } = new();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
