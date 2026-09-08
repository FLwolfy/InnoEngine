using System;

using Inno.Core.Identity;
using Inno.Scene;

namespace Inno.Editor.Panel.Inspector;

/// <summary>
/// Owns the transient target retained by the Inspector lock control.
/// </summary>
internal sealed class InspectorLockControl
{
    private WeakReference<object>? m_collectibleTarget;
    private object? m_lockedTarget;
    private Guid m_lockedIdentity;
    private bool m_hasLockedIdentity;
    private bool m_isLocked;

    /// <summary>
    /// Resolves the target that the Inspector should present this frame.
    /// </summary>
    /// <param name="selectedTarget">
    /// The current global editor selection.
    /// </param>
    /// <returns>
    /// The retained target while locked; otherwise, the current valid global selection.
    /// </returns>
    internal object? Resolve(object? selectedTarget)
    {
        object? lockedTarget = m_isLocked ? ResolveLockedTarget() : null;
        if (m_isLocked && !IsValid(lockedTarget))
        {
            Clear();
        }

        object? target = m_isLocked ? lockedTarget : selectedTarget;
        return IsValid(target) ? target : null;
    }

    /// <summary>
    /// Gets whether the Inspector is currently retaining a target independently from global selection.
    /// </summary>
    internal bool isLocked => m_isLocked;

    /// <summary>
    /// Toggles target retention using the target currently displayed by the Inspector.
    /// </summary>
    /// <param name="displayedTarget">
    /// The current valid Inspector target to retain when locking.
    /// </param>
    internal void Toggle(object displayedTarget)
    {
        if (m_isLocked)
        {
            Clear();
            return;
        }

        if (!IsValid(displayedTarget))
            return;
        m_isLocked = true;
        if (displayedTarget is IdentityObject identityObject)
        {
            m_lockedIdentity = identityObject.identity.persistentId;
            m_hasLockedIdentity = true;
            return;
        }

        if (displayedTarget.GetType().Assembly.IsCollectible)
        {
            m_collectibleTarget = new WeakReference<object>(displayedTarget);
            return;
        }

        m_lockedTarget = displayedTarget;
    }

    private void Clear()
    {
        m_collectibleTarget = null;
        m_lockedTarget = null;
        m_lockedIdentity = Guid.Empty;
        m_hasLockedIdentity = false;
        m_isLocked = false;
    }

    private object? ResolveLockedTarget()
    {
        if (m_hasLockedIdentity)
        {
            return IdentityAllocator.hasCurrent
                ? IdentityAllocator.current.Get<IdentityObject>(m_lockedIdentity)
                : null;
        }

        if (m_collectibleTarget is not null)
            return m_collectibleTarget.TryGetTarget(out object? target) ? target : null;
        return m_lockedTarget;
    }

    private static bool IsValid(object? target)
        => target is not null && target is not EngineObject { isDestroyed: true };
}
