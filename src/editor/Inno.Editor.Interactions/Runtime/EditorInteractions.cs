using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Loader;

using Inno.Core.Events;
using Inno.Core.Identity;
using Inno.Core.Logging;
using Inno.Scripting.Api;
using Inno.Editor.Core;

namespace Inno.Editor.Interactions;

/// <summary>
/// Provides the single presentation-independent entry point for editor actions, menus, selection, focus, and drag-and-drop.
/// </summary>
public sealed class EditorInteractions : IEditorSelectionCoordinator, IEditorHistoryIsolation
{
    private readonly EditorContext m_editor;
    private readonly EditorHistory m_history;
    private readonly Logger m_log;
    private EditorActionRouter? m_actions;
    private EditorExtensionCatalog? m_catalog;
    private EditorMenuCatalog? m_menus;
    private EditorToolbarCatalog? m_toolbars;
    private EditorDropRouter? m_drops;
    private string m_focusedArea = EditorBuiltInInteractionIds.C_GLOBAL_AREA;
    private object? m_focusedTarget;
    private object? m_previousGenerationSelection;
    private object? m_previousGenerationFocus;
    private Guid? m_pendingSelectionId;
    private Guid? m_pendingFocusId;

    internal EditorInteractions(EditorContext editor, Logger log)
    {
        m_editor = editor ?? throw new ArgumentNullException(nameof(editor));
        m_log = log ?? throw new ArgumentNullException(nameof(log));
        m_history = new EditorHistory(new EditorHistoryOptions
        {
            cacheDirectory = Path.Combine(editor.projectDirectory, "Library", "Editor", "History")
        }, log);
        m_history.Attach(editor, this);
    }

    /// <summary>
    /// Gets the shared read-only editor selection state.
    /// </summary>
    public EditorSelectionState selection { get; } = new();

    object? IEditorSelectionCoordinator.selectedTarget => selection.selectedTarget;

    /// <summary>
    /// Gets the transactional Undo and Redo history owned by this editor runtime.
    /// </summary>
    public IEditorHistory history => m_history;

    /// <summary>
    /// Starts an isolated temporary Undo and Redo branch while retaining the current editing branch.
    /// </summary>
    /// <remarks>
    /// Disposing the returned scope releases every temporary operation and restores the retained branch.
    /// This host-level boundary is intended for transient editor sessions such as Play Mode.
    /// </remarks>
    /// <returns>
    /// A scope that restores the retained history branch when disposed.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown during an Undo, Redo, transaction, or another isolated branch.
    /// </exception>
    [ScriptingApiIgnore]
    public IDisposable BeginHistoryIsolation() => m_history.BeginIsolation();

    internal EditorHistory historyHost => m_history;

    /// <summary>
    /// Gets the area that most recently received keyboard focus.
    /// </summary>
    public string focusedArea => m_focusedArea;

    /// <summary>
    /// Gets the target associated with the focused area.
    /// </summary>
    public object? focusedTarget => m_focusedTarget;

    /// <summary>
    /// Creates a lightweight interaction handle for one area and optional target.
    /// </summary>
    /// <param name="area">
    /// The stable interaction area.
    /// </param>
    /// <param name="target">
    /// The optional object represented by the area.
    /// </param>
    /// <returns>
    /// A lightweight interaction handle.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="area"/> is empty.
    /// </exception>
    public EditorInteraction For(string area, object? target = null)
        => new(this, area, target);

    /// <summary>
    /// Replaces the editor selection after closing presentations owned by other targets.
    /// </summary>
    /// <param name="target">
    /// The target to select, or <see langword="null"/> to clear the selection.
    /// </param>
    public void SetSelection(object? target)
    {
        PrepareSelectionChange(target);
        if (target is null)
            selection.Clear();
        else
            selection.Select(target);
    }

    /// <summary>
    /// Resolves valid managed data for an active drag token.
    /// </summary>
    /// <param name="token">
    /// The runtime-owned drag token.
    /// </param>
    /// <param name="data">
    /// The managed drag data when resolution succeeds.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the token is current and valid; otherwise, <see langword="false"/>.
    /// </returns>
    public bool TryGetDragData(Guid token, out EditorDragData? data)
        => Drops.TryGetData(token, out data);

    /// <summary>
    /// Toggles one panel in the currently active extension generation.
    /// </summary>
    /// <param name="panelId">
    /// The stable panel identifier to resolve.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when an available panel was found and toggled.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="panelId"/> is empty.
    /// </exception>
    public bool TogglePanel(string panelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(panelId);
        return m_catalog?.TryTogglePanel(panelId) == true;
    }

    internal void Attach(EditorExtensionCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        m_catalog = catalog;
        m_actions = new EditorActionRouter(catalog, m_editor, this, m_log);
        m_menus = new EditorMenuCatalog(catalog, m_actions, m_log);
        m_toolbars = new EditorToolbarCatalog(catalog, m_actions);
        m_drops = new EditorDropRouter(catalog, m_log);
    }

    internal void Update()
    {
        ResolveGenerationTargets();
        Actions.Flush();
    }

    internal void PrepareGenerationTransition()
    {
        if (m_previousGenerationSelection is not null || m_previousGenerationFocus is not null)
            throw new InvalidOperationException("An interaction generation transition is already active.");

        m_previousGenerationSelection = selection.selectedTarget;
        m_previousGenerationFocus = m_focusedTarget;
        PrepareRetiringTarget(
            selection.selectedTarget,
            persistentId => m_pendingSelectionId = persistentId,
            selection.Clear);
        PrepareRetiringTarget(
            m_focusedTarget,
            persistentId => m_pendingFocusId = persistentId,
            () => m_focusedTarget = null);
        Actions.ResetTransientState();
        Drops.Cancel();
    }

    internal void RollbackGenerationTransition()
    {
        m_pendingSelectionId = null;
        m_pendingFocusId = null;
        if (m_previousGenerationSelection is object previousSelection)
            selection.Select(previousSelection);
        else
            selection.Clear();
        m_focusedTarget = m_previousGenerationFocus;
        m_previousGenerationSelection = null;
        m_previousGenerationFocus = null;
    }

    internal void CompleteGenerationTransition()
    {
        m_previousGenerationSelection = null;
        m_previousGenerationFocus = null;
    }

    internal void Shutdown(
        IReadOnlyList<EditorExtensionCatalog.ActionRegistration> actions)
    {
        Actions.Clear(actions);
        Drops.Cancel();
        m_catalog = null;
        selection.Clear();
        m_focusedArea = EditorBuiltInInteractionIds.C_GLOBAL_AREA;
        m_focusedTarget = null;
        m_pendingSelectionId = null;
        m_pendingFocusId = null;
        m_previousGenerationSelection = null;
        m_previousGenerationFocus = null;
        m_history.Dispose();
    }

    internal bool DispatchShortcut(KeyPressedEvent keyEvent)
        => Actions.DispatchShortcut(
            keyEvent,
            m_focusedArea,
            m_focusedTarget ?? selection.selectedTarget);

    internal void Focus(string area, object? target)
    {
        m_focusedArea = area;
        m_focusedTarget = target;
    }

    internal void PrepareSelectionChange(object? target)
        => Actions.LosePresentationExcept(target);

    internal EditorActionState Query(
        string action,
        string area,
        object? target,
        object? argument)
        => Actions.Query(action, CreateActionContext(area, target, argument));

    internal bool Execute(
        string action,
        string area,
        object? target,
        object? argument)
        => Actions.Execute(action, CreateActionContext(area, target, argument));

    internal void Enqueue(
        string action,
        string area,
        object? target,
        object? argument)
        => Actions.Enqueue(action, CreateActionContext(area, target, argument));

    internal bool Present(string action, string area, object? target, object? argument)
        => Actions.Present(action, CreateActionContext(area, target, argument));

    internal bool IsActive(string action, string area, object? target)
        => Actions.IsActive(action, CreateActionContext(area, target, null));

    internal EditorMenuModel BuildMenu(string area, object? target)
        => Menus.Build(new EditorMenuContext(m_editor, this, area, target));

    internal EditorToolbarModel BuildToolbar(string area, object? target)
        => Toolbars.Build(CreateActionContext(area, target, argument: null));

    internal bool TryGetShortcut(
        string action,
        string area,
        object? target,
        out HotKeyGesture gesture)
        => Actions.TryGetShortcut(action, area, target, out gesture);

    internal Guid BeginDrag(string area, EditorDragData data)
    {
        if (m_catalog is null)
            throw new InvalidOperationException("Editor interactions are not attached to a runtime.");
        _ = m_catalog.extensions;
        return Drops.Begin(new EditorDragContext(m_editor, this, area, data));
    }

    internal EditorDropStatus QueryDrop(
        Guid token,
        string area,
        object? target,
        EditorDropPlacement placement)
    {
        if (target is null ||
            !Drops.TryGetData(token, out EditorDragData? data) ||
            data is null)
            return EditorDropStatus.rejected;
        return Drops.Query(token, new EditorDropContext(
            m_editor,
            this,
            area,
            data,
            target,
            placement));
    }

    internal EditorDropResult Drop(
        Guid token,
        string area,
        object? target,
        EditorDropPlacement placement)
    {
        if (target is null ||
            !Drops.TryGetData(token, out EditorDragData? data) ||
            data is null)
            return EditorDropResult.rejected;
        return Drops.Drop(token, new EditorDropContext(
            m_editor,
            this,
            area,
            data,
            target,
            placement));
    }

    private EditorActionRouter Actions => m_actions
        ?? throw new InvalidOperationException("Editor interactions are not attached to a runtime.");

    private EditorMenuCatalog Menus => m_menus
        ?? throw new InvalidOperationException("Editor interactions are not attached to a runtime.");

    private EditorDropRouter Drops => m_drops
        ?? throw new InvalidOperationException("Editor interactions are not attached to a runtime.");

    private EditorToolbarCatalog Toolbars => m_toolbars
        ?? throw new InvalidOperationException("Editor interactions are not attached to a runtime.");

    private EditorActionContext CreateActionContext(
        string area,
        object? target,
        object? argument)
        => new(m_editor, this, area, target, argument);

    private void ResolveGenerationTargets()
    {
        if (m_pendingSelectionId is Guid selectionId)
        {
            m_pendingSelectionId = null;
            IdentityObject? replacement = IdentityAllocator.hasCurrent
                ? IdentityAllocator.current.Get<IdentityObject>(selectionId)
                : null;
            if (replacement is not null)
                selection.Select(replacement);
            else
                selection.Clear();
        }
        if (m_pendingFocusId is Guid focusId)
        {
            m_pendingFocusId = null;
            m_focusedTarget = IdentityAllocator.hasCurrent
                ? IdentityAllocator.current.Get<IdentityObject>(focusId)
                : null;
        }
    }

    private static void PrepareRetiringTarget(
        object? target,
        Action<Guid> retainIdentity,
        Action clear)
    {
        if (target is null ||
            !target.GetType().Assembly.IsCollectible &&
            AssemblyLoadContext.GetLoadContext(target.GetType().Assembly)?.IsCollectible != true)
        {
            return;
        }
        if (target is IdentityObject identityObject)
            retainIdentity(identityObject.identity.persistentId);
        clear();
    }
}
