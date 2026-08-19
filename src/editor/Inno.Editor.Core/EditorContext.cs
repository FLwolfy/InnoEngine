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
    public void Focus(Type surface, object? target = null)
    {
        focusedSurface = surface ?? throw new ArgumentNullException(nameof(surface));
        focusedTarget = target;
    }

    /// <summary>Queries an action for a target on an interaction surface.</summary>
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

    /// <summary>Builds a complete menu for a surface and target.</summary>
    public EditorMenuModel BuildMenu(Type surface, object? target = null)
        => OnBuildMenu(new EditorMenuContext(this, surface, target));

    /// <summary>Resolves the shortcut displayed for an action on a surface.</summary>
    public bool TryGetShortcut(string actionId, Type surface, out HotKeyGesture gesture)
        => OnTryGetShortcut(actionId, surface, out gesture);

    /// <summary>Dispatches a keyboard event against the currently focused context.</summary>
    public bool DispatchShortcut(Inno.Core.Events.KeyPressedEvent keyEvent)
        => OnDispatchShortcut(keyEvent, focusedSurface, focusedTarget ?? selection.selectedTarget);

    /// <summary>Begins a managed drag session.</summary>
    public Guid BeginDrag(EditorDragContext context) => OnBeginDrag(context);

    /// <summary>Resolves managed data for an active drag token.</summary>
    public bool TryGetDragData(Guid token, out EditorDragData? data)
        => OnTryGetDragData(token, out data);

    /// <summary>Evaluates a managed drop target.</summary>
    public EditorDropStatus QueryDrop(Guid token, EditorDropContext context)
        => OnQueryDrop(token, context);

    /// <summary>Delivers a managed drop.</summary>
    public EditorDropResult Drop(Guid token, EditorDropContext context)
        => OnDrop(token, context);

    /// <summary>Queries an action through the active runtime.</summary>
    protected abstract EditorActionState OnQuery(string actionId, EditorActionContext context);

    /// <summary>Executes an action through the active runtime.</summary>
    protected abstract bool OnExecute(string actionId, EditorActionContext context);

    /// <summary>Queues an action through the active runtime.</summary>
    protected abstract void OnEnqueue(string actionId, EditorActionContext context);

    /// <summary>Builds a menu through the active runtime.</summary>
    protected abstract EditorMenuModel OnBuildMenu(EditorMenuContext context);

    /// <summary>Resolves a shortcut through the active runtime.</summary>
    protected abstract bool OnTryGetShortcut(string actionId, Type surface, out HotKeyGesture gesture);

    /// <summary>Dispatches a keyboard event through the active runtime.</summary>
    protected abstract bool OnDispatchShortcut(
        Inno.Core.Events.KeyPressedEvent keyEvent,
        Type surface,
        object? target);

    /// <summary>Begins a drag through the active runtime.</summary>
    protected abstract Guid OnBeginDrag(EditorDragContext context);

    /// <summary>Resolves drag data through the active runtime.</summary>
    protected abstract bool OnTryGetDragData(Guid token, out EditorDragData? data);

    /// <summary>Queries a drop through the active runtime.</summary>
    protected abstract EditorDropStatus OnQueryDrop(Guid token, EditorDropContext context);

    /// <summary>Delivers a drop through the active runtime.</summary>
    protected abstract EditorDropResult OnDrop(Guid token, EditorDropContext context);

}
