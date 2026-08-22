using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Inspector;

/// <summary>
/// Owns the transient target retained by the Inspector lock control.
/// </summary>
internal sealed class InspectorLockControl
{
    private object? m_lockedTarget;
    private bool m_isLocked;

    /// <summary>
    /// Resolves the target that the Inspector should present this frame.
    /// </summary>
    /// <param name="selectedTarget">The current global editor selection.</param>
    /// <returns>
    /// The retained target while locked; otherwise, the current valid global selection.
    /// </returns>
    internal object? Resolve(object? selectedTarget)
    {
        if (m_isLocked && !IsValid(m_lockedTarget))
        {
            m_isLocked = false;
            m_lockedTarget = null;
        }

        object? target = m_isLocked ? m_lockedTarget : selectedTarget;
        return IsValid(target) ? target : null;
    }

    /// <summary>
    /// Gets whether the Inspector is currently retaining a target independently from global selection.
    /// </summary>
    internal bool isLocked => m_isLocked;

    /// <summary>
    /// Toggles target retention using the target currently displayed by the Inspector.
    /// </summary>
    /// <param name="displayedTarget">The current valid Inspector target to retain when locking.</param>
    internal void Toggle(object displayedTarget)
    {
        if (m_isLocked)
        {
            m_isLocked = false;
            m_lockedTarget = null;
            return;
        }

        if (!IsValid(displayedTarget))
            return;
        m_isLocked = true;
        m_lockedTarget = displayedTarget;
    }

    private static bool IsValid(object? target)
        => target is not null && target is not EngineObject { isDestroyed: true };
}
