using System;
using System.Diagnostics;

using Inno.Core.Events;
using Inno.Editor.Core;
using Inno.Editor.Core.Commands;
using Inno.Editor.Core.DragDrop;
using Inno.Editor.Core.Menus;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.Renderers;
using Inno.Editor.ImGui.Widgets;
using Inno.Editor.Interactions.Commands;
using Inno.Editor.Interactions.DragDrop;
using Inno.Editor.Interactions.Menus;
using Inno.Editor.Interactions.Panels;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Interactions;

/// <summary>
/// Hosts the complete attribute-discovered editor extension generation.
/// Feature code never registers extensions with this runtime manually.
/// </summary>
public sealed class EditorRuntime : IDisposable
{
    private readonly Stopwatch m_timer = Stopwatch.StartNew();
    private readonly EditorContext m_context;
    private readonly EditorExtensionCatalog m_catalog;
    private readonly EditorActionRouter m_actions;
    private readonly EditorMenuCatalog m_menus;
    private readonly EditorDropRouter m_drops;
    private readonly EditorModalHost m_modals = new();
    private bool m_started;
    private bool m_disposed;

    /// <summary>
    /// Creates an editor runtime that discovers and coordinates one project's active extension generation.
    /// </summary>
    /// <param name="projectDirectory">The project root containing the Asset and Library directories.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="projectDirectory"/> is empty.</exception>
    public EditorRuntime(string projectDirectory)
    {
        m_context = new RuntimeContext(this, projectDirectory);
        m_catalog = new EditorExtensionCatalog(m_context);
        m_actions = new EditorActionRouter(m_catalog, m_context);
        m_menus = new EditorMenuCatalog(m_catalog, m_actions);
        m_drops = new EditorDropRouter(m_catalog);
    }

    /// <summary>Gets the active shared editor context.</summary>
    public EditorContext context => m_context;

    /// <summary>Gets the number of discovered dockable panels.</summary>
    public int panelCount => m_catalog.extensions.panels.Length;

    /// <summary>
    /// Builds, validates, and activates the current TypeCache extension generation.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown after the runtime has been disposed.</exception>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (m_started)
            return;
        _ = m_catalog.extensions;
        m_started = true;
    }

    /// <summary>
    /// Flushes queued actions and updates every active feature module for one editor frame.
    /// </summary>
    /// <param name="deltaTime">The elapsed frame time in seconds.</param>
    /// <param name="totalTime">The absolute editor runtime in seconds.</param>
    /// <param name="isFocused">Whether an editor viewport currently owns application focus.</param>
    /// <exception cref="ObjectDisposedException">Thrown after the runtime has been disposed.</exception>
    public void Update(float deltaTime, float totalTime, bool isFocused)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (!m_started)
            Start();
        m_context.frameDeltaTime = deltaTime;
        m_context.totalTime = totalTime;
        m_context.isFocused = isFocused;
        m_actions.Flush();
        m_catalog.UpdateModules();
    }

    /// <summary>
    /// Draws the main menu, all open panels, queued interactions, and active modals for one ImGui frame.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown after the runtime has been disposed.</exception>
    public void Draw()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        EditorExtensionCatalog.Snapshot extensions = m_catalog.extensions;
        double now = m_timer.Elapsed.TotalSeconds;
        bool blocksInteraction = m_modals.Update(extensions.modals, now);

        _ = NativeImGui.DockSpaceOverViewport();
        if (blocksInteraction)
            NativeImGui.BeginDisabled(true);
        EditorMenuRenderer.MainMenu(new EditorMenuContext(
            m_context,
            typeof(EditorSurface.MainMenu)));
        DrawPanels(extensions);
        if (blocksInteraction)
            NativeImGui.EndDisabled();

        m_actions.Flush();
        m_modals.Draw(m_context, extensions.modals, now);
    }

    /// <summary>
    /// Dispatches an unhandled keyboard event through attribute-declared contextual shortcuts.
    /// </summary>
    /// <param name="keyEvent">The keyboard event received from the application event stream.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="keyEvent"/> is <see langword="null"/>.</exception>
    public void HandleKeyPressed(KeyPressedEvent keyEvent)
    {
        ArgumentNullException.ThrowIfNull(keyEvent);
        if (m_modals.Update(m_catalog.extensions.modals, m_timer.Elapsed.TotalSeconds))
            return;
        if (m_context.DispatchShortcut(keyEvent))
            keyEvent.HandleInGlobal();
    }

    private EditorActionState Query(string actionId, EditorActionContext context)
        => m_actions.Query(actionId, context);

    private bool Execute(string actionId, EditorActionContext context)
        => m_actions.Execute(actionId, context);

    private void Enqueue(string actionId, EditorActionContext context)
        => m_actions.Enqueue(actionId, context);

    private bool TryGetInteraction<TState>(
        string actionId,
        EditorActionContext context,
        out EditorActionInteraction<TState>? interaction)
        => m_actions.TryGetInteraction(actionId, context, out interaction);

    private EditorMenuModel BuildMenu(EditorMenuContext context)
        => m_menus.Build(context);

    private bool TryGetShortcut(string actionId, Type surface, out HotKeyGesture gesture)
        => m_actions.TryGetShortcut(actionId, surface, out gesture);

    private bool DispatchShortcut(KeyPressedEvent keyEvent, Type surface, object? target)
        => m_actions.DispatchShortcut(keyEvent, surface, target);

    private Guid BeginDrag(EditorDragContext context) => m_drops.Begin(context);

    private bool TryGetDragData(Guid token, out EditorDragData? data)
        => m_drops.TryGetData(token, out data);

    private EditorDropStatus QueryDrop(Guid token, EditorDropContext context)
        => m_drops.Query(token, context);

    private EditorDropResult Drop(Guid token, EditorDropContext context)
        => m_drops.Drop(token, context);

    /// <summary>Stops modules and releases the active extension generation.</summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        m_actions.Clear();
        m_drops.Cancel();
        m_modals.Clear();
        _ = m_context.Select(typeof(EditorSurface.Global), null);
        m_catalog.Shutdown();
        m_catalog.Dispose();
        GC.SuppressFinalize(this);
    }

    private void DrawPanels(EditorExtensionCatalog.Snapshot extensions)
    {
        for (int i = 0; i < extensions.panels.Length; i++)
        {
            EditorExtensionCatalog.PanelRegistration registration = extensions.panels[i];
            if (!registration.panel.isOpen)
                continue;
            bool isOpen = registration.panel.isOpen;
            ImGuiWidget.PanelWindow(registration.attribute.title, ref isOpen, () =>
            {
                registration.panel.Draw(m_context);
                if (NativeImGui.IsWindowFocused(Inno.Native.ImGui.ImGuiFocusedFlags.RootAndChildWindows))
                {
                    m_context.Focus(
                        registration.type,
                        m_context.selection.selectedTarget);
                }
            });
            registration.panel.isOpen = isOpen;
        }
    }

    private sealed class RuntimeContext : EditorContext
    {
        private readonly EditorRuntime m_runtime;

        internal RuntimeContext(EditorRuntime runtime, string projectDirectory)
            : base(projectDirectory)
        {
            m_runtime = runtime;
        }

        protected override EditorActionState OnQuery(string actionId, EditorActionContext context)
            => m_runtime.Query(actionId, context);

        protected override bool OnExecute(string actionId, EditorActionContext context)
            => m_runtime.Execute(actionId, context);

        protected override void OnEnqueue(string actionId, EditorActionContext context)
            => m_runtime.Enqueue(actionId, context);

        protected override bool OnTryGetInteraction<TState>(
            string actionId,
            EditorActionContext context,
            out EditorActionInteraction<TState>? interaction)
            => m_runtime.TryGetInteraction(actionId, context, out interaction);

        protected override EditorMenuModel OnBuildMenu(EditorMenuContext context)
            => m_runtime.BuildMenu(context);

        protected override bool OnTryGetShortcut(
            string actionId,
            Type surface,
            out HotKeyGesture gesture)
            => m_runtime.TryGetShortcut(actionId, surface, out gesture);

        protected override bool OnDispatchShortcut(
            KeyPressedEvent keyEvent,
            Type surface,
            object? target)
            => m_runtime.DispatchShortcut(keyEvent, surface, target);

        protected override Guid OnBeginDrag(EditorDragContext context)
            => m_runtime.BeginDrag(context);

        protected override bool OnTryGetDragData(Guid token, out EditorDragData? data)
            => m_runtime.TryGetDragData(token, out data);

        protected override EditorDropStatus OnQueryDrop(Guid token, EditorDropContext context)
            => m_runtime.QueryDrop(token, context);

        protected override EditorDropResult OnDrop(Guid token, EditorDropContext context)
            => m_runtime.Drop(token, context);
    }
}
