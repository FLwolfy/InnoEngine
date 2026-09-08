using System;

namespace Inno.Core.Coroutines;

/// <summary>
/// Opaque handle returned by
/// <see cref="CoroutineScheduler.StartCoroutine(System.Collections.IEnumerator)"/>
/// or <see cref="CoroutineScheduler.StartCoroutine(object,System.Collections.IEnumerator)"/>.
/// </summary>
public readonly struct CoroutineHandle
{
    private readonly long m_id;
    private readonly WeakReference<CoroutineScheduler>? m_schedulerRef;

    internal CoroutineHandle(long id, WeakReference<CoroutineScheduler> schedulerRef)
    {
        m_id = id;
        m_schedulerRef = schedulerRef;
    }

    internal long id => m_id;

    /// <summary>
    /// Gets whether this handle currently points to a live coroutine
    /// in a still-alive scheduler.
    /// </summary>
    public bool isValid
    {
        get
        {
            if (m_id <= 0 || m_schedulerRef is null)
            {
                return false;
            }

            return m_schedulerRef.TryGetTarget(out CoroutineScheduler? scheduler)
                   && scheduler.IsHandleValid(m_id);
        }
    }

    internal bool IsOwnedBy(CoroutineScheduler scheduler)
    {
        return m_id > 0
               && m_schedulerRef is not null
               && m_schedulerRef.TryGetTarget(out CoroutineScheduler? target)
               && ReferenceEquals(target, scheduler);
    }
}
