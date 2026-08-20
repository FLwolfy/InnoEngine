using System;

using Inno.Editor.Core.Commands;
using Inno.Editor.Core.DragDrop;
using Inno.Editor.Core.Menus;

namespace Inno.Editor.Core;

/// <summary>
/// Shared runtime context used by all editor panels.
/// </summary>
public abstract class EditorContext
{
    /// <summary>
    /// Creates the shared editor context.
    /// </summary>
    /// <param name="projectDirectory">Normalized project root directory.</param>
    protected EditorContext(string projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(projectDirectory))
            throw new ArgumentException("A project directory is required.", nameof(projectDirectory));
        this.projectDirectory = System.IO.Path.GetFullPath(projectDirectory);
    }

    /// <summary>
    /// Gets the shared selection state.
    /// </summary>
    public EditorSelectionState selection { get; } = new();

    /// <summary>
    /// Gets the normalized project root directory.
    /// </summary>
    public string projectDirectory { get; }

    /// <summary>
    /// Gets whether any editor viewport currently owns application focus.
    /// </summary>
    public bool isFocused { get; set; }

    /// <summary>Gets the surface that most recently owned keyboard focus.</summary>
    public Type focusedSurface { get; private set; } = typeof(EditorSurface.Global);

    /// <summary>Gets the target associated with the focused surface.</summary>
    public object? focusedTarget { get; private set; }

    /// <summary>
    /// Gets or sets the latest frame delta in seconds.
    /// </summary>
    public float frameDeltaTime { get; set; }

    /// <summary>
    /// Gets or sets the latest absolute runtime in seconds.
    /// </summary>
    public float totalTime { get; set; }

    /// <summary>Updates the surface used for contextual keyboard dispatch.</summary>
    /// <param name="surface">The interaction surface that currently owns keyboard focus.</param>
    /// <param name="target">The optional target associated with the focused surface.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="surface"/> is <see langword="null"/>.</exception>
    public void Focus(Type surface, object? target = null)
    {
        focusedSurface = surface ?? throw new ArgumentNullException(nameof(surface));
        focusedTarget = target;
    }

    /// <summary>Queries an action for a target on an interaction surface.</summary>
    /// <param name="actionId">The stable identifier of the action to query.</param>
    /// <param name="surface">The interaction surface issuing the query.</param>
    /// <param name="target">The optional object the action would operate on.</param>
    /// <param name="argument">An optional placement-specific argument supplied to the action.</param>
    /// <returns>The current visibility, availability, checked state, and optional display name of the best matching action.</returns>
    public EditorActionState Query(
        string actionId,
        Type surface,
        object? target = null,
        object? argument = null)
    {
        return OnQuery(
            actionId,
            new EditorActionContext(this, surface, target, argument));
    }

    /// <summary>Executes an action for a target on an interaction surface.</summary>
    /// <param name="actionId">The stable identifier of the action to execute.</param>
    /// <param name="surface">The interaction surface issuing the request.</param>
    /// <param name="target">The optional object the action operates on.</param>
    /// <param name="argument">An optional placement-specific argument supplied to the action.</param>
    /// <returns><see langword="true"/> when a visible and enabled matching action executed successfully; otherwise, <see langword="false"/>.</returns>
    public bool Execute(
        string actionId,
        Type surface,
        object? target = null,
        object? argument = null)
    {
        return OnExecute(
            actionId,
            new EditorActionContext(this, surface, target, argument));
    }

    /// <summary>Queues an action until the current UI traversal completes.</summary>
    /// <param name="actionId">The stable identifier of the action to enqueue.</param>
    /// <param name="surface">The interaction surface issuing the request.</param>
    /// <param name="target">The optional object the action operates on.</param>
    /// <param name="argument">An optional placement-specific argument supplied to the action.</param>
    public void Enqueue(
        string actionId,
        Type surface,
        object? target = null,
        object? argument = null)
    {
        OnEnqueue(
            actionId,
            new EditorActionContext(this, surface, target, argument));
    }

    /// <summary>
    /// Selects a target, or clears selection, through the built-in selection actions.
    /// </summary>
    /// <param name="surface">The interaction surface requesting the selection change.</param>
    /// <param name="target">The target to select, or <see langword="null"/> to clear selection.</param>
    /// <returns><see langword="true"/> when the corresponding selection action executed successfully; otherwise, <see langword="false"/>.</returns>
    public bool Select(Type surface, object? target)
        => target is null
            ? Execute(EditorActionIds.ClearSelection, surface)
            : Execute(EditorActionIds.Select, surface, target);

    /// <summary>
    /// Tries to resolve type-safe cross-frame state owned by the best matching editor action.
    /// </summary>
    /// <typeparam name="TState">The interaction state type expected by the caller.</typeparam>
    /// <param name="actionId">The stable identifier of the action that owns the interaction.</param>
    /// <param name="surface">The interaction surface used to resolve the action implementation.</param>
    /// <param name="target">The optional target associated with the interaction.</param>
    /// <param name="interaction">The active typed interaction when resolution succeeds.</param>
    /// <returns><see langword="true"/> when a matching action owns an active interaction for the supplied target; otherwise, <see langword="false"/>.</returns>
    public bool TryGetInteraction<TState>(
        string actionId,
        Type surface,
        object? target,
        out EditorActionInteraction<TState>? interaction)
        => OnTryGetInteraction(
            actionId,
            new EditorActionContext(this, surface, target),
            out interaction);

    /// <summary>Builds a complete menu for a surface and target.</summary>
    /// <param name="surface">The interaction surface whose menu placements should be collected.</param>
    /// <param name="target">The optional object the resulting menu operates on.</param>
    /// <returns>An immutable menu tree containing all visible static and dynamic placements.</returns>
    public EditorMenuModel BuildMenu(Type surface, object? target = null)
        => OnBuildMenu(new EditorMenuContext(this, surface, target));

    /// <summary>Resolves the shortcut displayed for an action on a surface.</summary>
    /// <param name="actionId">The stable identifier of the action.</param>
    /// <param name="surface">The interaction surface where the shortcut is presented.</param>
    /// <param name="gesture">The resolved keyboard gesture when the method succeeds.</param>
    /// <returns><see langword="true"/> when a compatible shortcut is registered; otherwise, <see langword="false"/>.</returns>
    public bool TryGetShortcut(string actionId, Type surface, out HotKeyGesture gesture)
        => OnTryGetShortcut(actionId, surface, out gesture);

    /// <summary>Dispatches a keyboard event against the currently focused context.</summary>
    /// <param name="keyEvent">The unhandled keyboard event to dispatch.</param>
    /// <returns><see langword="true"/> when a matching enabled action handled the event; otherwise, <see langword="false"/>.</returns>
    public bool DispatchShortcut(Inno.Core.Events.KeyPressedEvent keyEvent)
        => OnDispatchShortcut(keyEvent, focusedSurface, focusedTarget ?? selection.selectedTarget);

    /// <summary>Begins a managed drag session.</summary>
    /// <param name="context">The source surface and managed data for the drag operation.</param>
    /// <returns>The stable token used by the native UI payload for the active managed drag session.</returns>
    public Guid BeginDrag(EditorDragContext context) => OnBeginDrag(context);

    /// <summary>Resolves managed data for an active drag token.</summary>
    /// <param name="token">The active managed drag-session token.</param>
    /// <param name="data">The managed drag data when the token is current and its source remains valid.</param>
    /// <returns><see langword="true"/> when valid managed data was resolved; otherwise, <see langword="false"/>.</returns>
    public bool TryGetDragData(Guid token, out EditorDragData? data)
        => OnTryGetDragData(token, out data);

    /// <summary>Evaluates a managed drop target.</summary>
    /// <param name="token">The active managed drag-session token.</param>
    /// <param name="context">The target surface, target object, and requested placement.</param>
    /// <returns>The compatibility state and standard visual for the best matching drop handler.</returns>
    public EditorDropStatus QueryDrop(Guid token, EditorDropContext context)
        => OnQueryDrop(token, context);

    /// <summary>Delivers a managed drop.</summary>
    /// <param name="token">The active managed drag-session token.</param>
    /// <param name="context">The target surface, target object, and requested placement.</param>
    /// <returns>The observable result produced by the matching drop handler.</returns>
    public EditorDropResult Drop(Guid token, EditorDropContext context)
        => OnDrop(token, context);

    /// <summary>Queries an action through the active runtime.</summary>
    /// <param name="actionId">The stable identifier of the action to query.</param>
    /// <param name="context">The complete contextual action request.</param>
    /// <returns>The current state of the best matching action.</returns>
    protected abstract EditorActionState OnQuery(string actionId, EditorActionContext context);

    /// <summary>Executes an action through the active runtime.</summary>
    /// <param name="actionId">The stable identifier of the action to execute.</param>
    /// <param name="context">The complete contextual action request.</param>
    /// <returns><see langword="true"/> when the action executed successfully; otherwise, <see langword="false"/>.</returns>
    protected abstract bool OnExecute(string actionId, EditorActionContext context);

    /// <summary>Queues an action through the active runtime.</summary>
    /// <param name="actionId">The stable identifier of the action to enqueue.</param>
    /// <param name="context">The complete contextual action request.</param>
    protected abstract void OnEnqueue(string actionId, EditorActionContext context);

    /// <summary>Resolves active cross-frame state through the active runtime.</summary>
    /// <typeparam name="TState">The interaction state type expected by the caller.</typeparam>
    /// <param name="actionId">The stable identifier of the action that owns the interaction.</param>
    /// <param name="context">The complete contextual action request.</param>
    /// <param name="interaction">The active typed interaction when resolution succeeds.</param>
    /// <returns><see langword="true"/> when a matching active interaction exists; otherwise, <see langword="false"/>.</returns>
    protected abstract bool OnTryGetInteraction<TState>(
        string actionId,
        EditorActionContext context,
        out EditorActionInteraction<TState>? interaction);

    /// <summary>Builds a menu through the active runtime.</summary>
    /// <param name="context">The surface and optional target used to collect menu placements.</param>
    /// <returns>The complete immutable menu model for the context.</returns>
    protected abstract EditorMenuModel OnBuildMenu(EditorMenuContext context);

    /// <summary>Resolves a shortcut through the active runtime.</summary>
    /// <param name="actionId">The stable identifier of the action.</param>
    /// <param name="surface">The interaction surface where the shortcut is requested.</param>
    /// <param name="gesture">The resolved gesture when the method succeeds.</param>
    /// <returns><see langword="true"/> when a compatible shortcut exists; otherwise, <see langword="false"/>.</returns>
    protected abstract bool OnTryGetShortcut(string actionId, Type surface, out HotKeyGesture gesture);

    /// <summary>Dispatches a keyboard event through the active runtime.</summary>
    /// <param name="keyEvent">The keyboard event to dispatch.</param>
    /// <param name="surface">The surface that currently owns keyboard focus.</param>
    /// <param name="target">The optional target associated with the focused surface.</param>
    /// <returns><see langword="true"/> when an action handled the event; otherwise, <see langword="false"/>.</returns>
    protected abstract bool OnDispatchShortcut(
        Inno.Core.Events.KeyPressedEvent keyEvent,
        Type surface,
        object? target);

    /// <summary>Begins a drag through the active runtime.</summary>
    /// <param name="context">The source surface and managed drag data.</param>
    /// <returns>The token identifying the active managed drag session.</returns>
    protected abstract Guid OnBeginDrag(EditorDragContext context);

    /// <summary>Resolves drag data through the active runtime.</summary>
    /// <param name="token">The managed drag-session token.</param>
    /// <param name="data">The resolved drag data when the method succeeds.</param>
    /// <returns><see langword="true"/> when the token resolves to valid data; otherwise, <see langword="false"/>.</returns>
    protected abstract bool OnTryGetDragData(Guid token, out EditorDragData? data);

    /// <summary>Queries a drop through the active runtime.</summary>
    /// <param name="token">The managed drag-session token.</param>
    /// <param name="context">The complete drop-target context.</param>
    /// <returns>The compatibility state returned by the best matching drop handler.</returns>
    protected abstract EditorDropStatus OnQueryDrop(Guid token, EditorDropContext context);

    /// <summary>Delivers a drop through the active runtime.</summary>
    /// <param name="token">The managed drag-session token.</param>
    /// <param name="context">The complete drop-target context.</param>
    /// <returns>The result returned by the matching drop handler.</returns>
    protected abstract EditorDropResult OnDrop(Guid token, EditorDropContext context);

}
