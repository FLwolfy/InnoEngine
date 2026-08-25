using System;

namespace Inno.Editor.Interactions;

/// <summary>
/// Provides a lightweight fluent handle for one interaction area and optional target.
/// </summary>
public readonly struct EditorInteraction
{
    private readonly EditorInteractions m_interactions;

    internal EditorInteraction(EditorInteractions interactions, string area, object? target)
    {
        m_interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
        ArgumentException.ThrowIfNullOrWhiteSpace(area);
        this.area = area;
        this.target = target;
    }

    /// <summary>Gets the stable interaction area.</summary>
    public string area { get; }

    /// <summary>Gets the optional target represented by this handle.</summary>
    public object? target { get; }

    /// <summary>Gets whether this handle's target is the current editor selection.</summary>
    public bool isSelected => target is not null &&
                              Equals(m_interactions.selection.selectedTarget, target);

    /// <summary>Marks this area and target as the active keyboard context.</summary>
    public void Focus() => m_interactions.Focus(area, target);

    /// <summary>Selects this target, or clears selection when the target is <see langword="null"/>.</summary>
    /// <returns><see langword="true"/> when the built-in selection action executed; otherwise, <see langword="false"/>.</returns>
    public bool Select()
        => Execute(target is null
            ? EditorBuiltInInteractionIds.C_CLEAR_SELECTION
            : EditorBuiltInInteractionIds.C_SELECT);

    /// <summary>Queries an action for this area and target.</summary>
    /// <param name="action">The stable action name.</param>
    /// <param name="argument">An optional action argument.</param>
    /// <returns>The current action state.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="action"/> is empty.</exception>
    public EditorActionState Query(string action, object? argument = null)
        => m_interactions.Query(action, area, target, argument);

    /// <summary>Executes an action for this area and target.</summary>
    /// <param name="action">The stable action name.</param>
    /// <param name="argument">An optional action argument.</param>
    /// <returns><see langword="true"/> when a visible and enabled action executed; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="action"/> is empty.</exception>
    public bool Execute(string action, object? argument = null)
        => m_interactions.Execute(action, area, target, argument);

    /// <summary>Queues an action until the current UI traversal completes.</summary>
    /// <param name="action">The stable action name.</param>
    /// <param name="argument">An optional action argument.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="action"/> is empty.</exception>
    public void Enqueue(string action, object? argument = null)
        => m_interactions.Enqueue(action, area, target, argument);

    /// <summary>Presents an active action in place of this target's normal content.</summary>
    /// <param name="action">The stable action name.</param>
    /// <param name="argument">Optional presentation data supplied by the current view.</param>
    /// <returns><see langword="true"/> when the action presented content; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="action"/> is empty.</exception>
    public bool Present(string action, object? argument = null)
        => m_interactions.Present(action, area, target, argument);

    /// <summary>Gets whether an action owns an active multi-frame operation for this target.</summary>
    /// <param name="action">The stable action name.</param>
    /// <returns><see langword="true"/> when the action is active for this exact target; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="action"/> is empty.</exception>
    public bool IsActive(string action)
        => m_interactions.IsActive(action, area, target);

    /// <summary>Builds the complete contextual menu for this area and target.</summary>
    /// <returns>The immutable resolved menu model.</returns>
    public EditorMenuModel BuildMenu() => m_interactions.BuildMenu(area, target);

    /// <summary>Resolves the shortcut displayed for an action in this area.</summary>
    /// <param name="action">The stable action name.</param>
    /// <param name="gesture">The resolved gesture when successful.</param>
    /// <returns><see langword="true"/> when a compatible shortcut exists; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="action"/> is empty.</exception>
    public bool TryGetShortcut(string action, out HotKeyGesture gesture)
        => m_interactions.TryGetShortcut(action, area, target, out gesture);

    /// <summary>Begins a managed drag originating from this area.</summary>
    /// <param name="data">The managed source data.</param>
    /// <returns>The runtime-owned drag token.</returns>
    public Guid BeginDrag(EditorDragData data)
        => m_interactions.BeginDrag(area, data);

    /// <summary>Queries this handle as a drop target.</summary>
    /// <param name="token">The active managed drag token.</param>
    /// <param name="placement">The requested placement relative to the target.</param>
    /// <returns>The drop compatibility and visual state.</returns>
    public EditorDropStatus QueryDrop(
        Guid token,
        EditorDropPlacement placement = EditorDropPlacement.None)
        => m_interactions.QueryDrop(token, area, target, placement);

    /// <summary>Delivers a managed drop to this target.</summary>
    /// <param name="token">The active managed drag token.</param>
    /// <param name="placement">The requested placement relative to the target.</param>
    /// <returns>The completed drop result.</returns>
    public EditorDropResult Drop(
        Guid token,
        EditorDropPlacement placement = EditorDropPlacement.None)
        => m_interactions.Drop(token, area, target, placement);
}
