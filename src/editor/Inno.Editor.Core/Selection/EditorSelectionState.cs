using System;
using System.Diagnostics.CodeAnalysis;

namespace Inno.Editor.Core;

/// <summary>
/// Stores editor-wide object selection state.
/// </summary>
public sealed class EditorSelectionState
{
    private object? m_selectedTarget;

    /// <summary>
    /// Gets the selected target, or <see langword="null"/> when nothing is selected.
    /// </summary>
    public object? selectedTarget => m_selectedTarget;

    /// <summary>
    /// Gets a monotonically increasing selection change version.
    /// </summary>
    public ulong version { get; private set; }

    /// <summary>
    /// Selects a target object.
    /// </summary>
    /// <param name="target">Target to select.</param>
    public void Select(object target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (Equals(m_selectedTarget, target))
        {
            return;
        }

        m_selectedTarget = target;
        version++;
    }

    /// <summary>
    /// Clears the current selection.
    /// </summary>
    public void Clear()
    {
        if (m_selectedTarget is null)
        {
            return;
        }

        m_selectedTarget = null;
        version++;
    }

    /// <summary>
    /// Tries to read the current target as a requested type.
    /// </summary>
    /// <typeparam name="TTarget">Requested target type.</typeparam>
    /// <param name="target">Typed target when successful.</param>
    /// <returns><see langword="true"/> when the current target is compatible.</returns>
    public bool TryGet<TTarget>([NotNullWhen(true)] out TTarget? target)
    {
        if (m_selectedTarget is TTarget typedTarget)
        {
            target = typedTarget;
            return true;
        }

        target = default;
        return false;
    }

}
