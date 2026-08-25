using System;


namespace Inno.Editor.Interactions;

/// <summary>
/// Provides a lightweight fluent handle for one interaction area and optional target.
/// </summary>
public readonly struct EditorInteraction
{
    private readonly EditorInteractions m_interactions;

    internal EditorInteraction(EditorInteractions interactions, EditorAreaId area, object? target)
    {
        m_interactions = interactions;
        this.area = area;
        this.target = target;
    }

    /// <summary>Gets the stable interaction area.</summary>
    public EditorAreaId area { get; }

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
            ? EditorBuiltInInteractionIds.clearSelectionCommand
            : EditorBuiltInInteractionIds.selectCommand);

    /// <summary>Queries an action for this area and target.</summary>
    /// <param name="action">The stable action name.</param>
    /// <param name="argument">An optional action argument.</param>
    /// <returns>The current action state.</returns>
    internal EditorActionState Query(string action, object? argument = null)
        => m_interactions.Query(action, area.value, target, argument);

    /// <summary>Queries a command for this area and target.</summary>
    /// <param name="command">The command to query.</param>
    /// <returns>The current action state.</returns>
    public EditorActionState Query(EditorCommand command)
        => m_interactions.Query(command.id.value, area.value, target, argument: null);

    /// <summary>Queries a typed command for this area and target.</summary>
    /// <typeparam name="TArgument">The command argument type.</typeparam>
    /// <param name="command">The command to query.</param>
    /// <param name="argument">The typed command argument.</param>
    /// <returns>The current action state.</returns>
    public EditorActionState Query<TArgument>(EditorCommand<TArgument> command, TArgument argument)
        => m_interactions.Query(command.id.value, area.value, target, argument);

    /// <summary>Executes an action for this area and target.</summary>
    /// <param name="action">The stable action name.</param>
    /// <param name="argument">An optional action argument.</param>
    /// <returns><see langword="true"/> when a visible and enabled action executed; otherwise, <see langword="false"/>.</returns>
    internal bool Execute(string action, object? argument = null)
        => m_interactions.Execute(action, area.value, target, argument);

    /// <summary>Executes a command for this area and target.</summary>
    /// <param name="command">The command to execute.</param>
    /// <returns><see langword="true"/> when the command executed.</returns>
    public bool Execute(EditorCommand command)
        => m_interactions.Execute(command.id.value, area.value, target, argument: null);

    /// <summary>Executes a typed command for this area and target.</summary>
    /// <typeparam name="TArgument">The command argument type.</typeparam>
    /// <param name="command">The command to execute.</param>
    /// <param name="argument">The typed command argument.</param>
    /// <returns><see langword="true"/> when the command executed.</returns>
    public bool Execute<TArgument>(EditorCommand<TArgument> command, TArgument argument)
        => m_interactions.Execute(command.id.value, area.value, target, argument);

    /// <summary>Queues an action until the current UI traversal completes.</summary>
    /// <param name="action">The stable action name.</param>
    /// <param name="argument">An optional action argument.</param>
    internal void Enqueue(string action, object? argument = null)
        => m_interactions.Enqueue(action, area.value, target, argument);

    /// <summary>Queues a command until the current UI traversal completes.</summary>
    /// <param name="command">The command to queue.</param>
    public void Enqueue(EditorCommand command)
        => m_interactions.Enqueue(command.id.value, area.value, target, argument: null);

    /// <summary>Queues a typed command until the current UI traversal completes.</summary>
    /// <typeparam name="TArgument">The command argument type.</typeparam>
    /// <param name="command">The command to queue.</param>
    /// <param name="argument">The typed command argument.</param>
    public void Enqueue<TArgument>(EditorCommand<TArgument> command, TArgument argument)
        => m_interactions.Enqueue(command.id.value, area.value, target, argument);

    /// <summary>Presents an active action in place of this target's normal content.</summary>
    /// <param name="action">The stable action name.</param>
    /// <param name="argument">Optional presentation data supplied by the current view.</param>
    /// <returns><see langword="true"/> when the action presented content; otherwise, <see langword="false"/>.</returns>
    internal bool Present(string action, object? argument = null)
        => m_interactions.Present(action, area.value, target, argument);

    /// <summary>Presents a typed command in place of this target's normal content.</summary>
    /// <typeparam name="TArgument">The presentation argument type.</typeparam>
    /// <param name="command">The presentation command.</param>
    /// <param name="argument">The typed presentation data.</param>
    /// <returns><see langword="true"/> when the action presented content.</returns>
    public bool Present<TArgument>(EditorCommand<TArgument> command, TArgument argument)
        => m_interactions.Present(command.id.value, area.value, target, argument);

    /// <summary>Gets whether an action owns an active multi-frame operation for this target.</summary>
    /// <param name="action">The stable action name.</param>
    /// <returns><see langword="true"/> when the action is active for this exact target; otherwise, <see langword="false"/>.</returns>
    internal bool IsActive(string action)
        => m_interactions.IsActive(action, area.value, target);

    /// <summary>Gets whether a command owns active work for this target.</summary>
    /// <param name="command">The command to inspect.</param>
    /// <returns><see langword="true"/> when the command is active for this target.</returns>
    public bool IsActive(EditorCommand command)
        => m_interactions.IsActive(command.id.value, area.value, target);

    /// <summary>Builds the complete contextual menu for this area and target.</summary>
    /// <returns>The immutable resolved menu model.</returns>
    public EditorMenuModel BuildMenu() => m_interactions.BuildMenu(area.value, target);

    /// <summary>Resolves the shortcut displayed for an action in this area.</summary>
    /// <param name="action">The stable action name.</param>
    /// <param name="gesture">The resolved gesture when successful.</param>
    /// <returns><see langword="true"/> when a compatible shortcut exists; otherwise, <see langword="false"/>.</returns>
    internal bool TryGetShortcut(string action, out HotKeyGesture gesture)
        => m_interactions.TryGetShortcut(action, area.value, target, out gesture);

    /// <summary>Resolves the shortcut displayed for a command in this area.</summary>
    /// <param name="command">The command whose shortcut is requested.</param>
    /// <param name="gesture">The resolved gesture when successful.</param>
    /// <returns><see langword="true"/> when a compatible shortcut exists.</returns>
    public bool TryGetShortcut(EditorCommand command, out HotKeyGesture gesture)
        => m_interactions.TryGetShortcut(command.id.value, area.value, target, out gesture);

    /// <summary>Begins a managed drag originating from this area.</summary>
    /// <param name="data">The managed source data.</param>
    /// <returns>The runtime-owned drag token.</returns>
    public Guid BeginDrag(EditorDragData data)
        => m_interactions.BeginDrag(area.value, data);

    /// <summary>Queries this handle as a drop target.</summary>
    /// <param name="token">The active managed drag token.</param>
    /// <param name="placement">The requested placement relative to the target.</param>
    /// <returns>The drop compatibility and visual state.</returns>
    public EditorDropStatus QueryDrop(
        Guid token,
        EditorDropPlacement placement = EditorDropPlacement.None)
        => m_interactions.QueryDrop(token, area.value, target, placement);

    /// <summary>Delivers a managed drop to this target.</summary>
    /// <param name="token">The active managed drag token.</param>
    /// <param name="placement">The requested placement relative to the target.</param>
    /// <returns>The completed drop result.</returns>
    public EditorDropResult Drop(
        Guid token,
        EditorDropPlacement placement = EditorDropPlacement.None)
        => m_interactions.Drop(token, area.value, target, placement);
}
