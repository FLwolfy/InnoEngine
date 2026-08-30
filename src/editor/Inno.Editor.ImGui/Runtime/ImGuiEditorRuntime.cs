using System;
using System.Collections.Generic;
using System.Diagnostics;

using Inno.Core.Events;
using Inno.Editor.Core;
using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using Inno.Editor.Interactions;
using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.ImGui;

/// <summary>
/// Presents the backend-independent editor interaction runtime through ImGui.
/// </summary>
public sealed class ImGuiEditorRuntime : EditorRuntime
{
    private readonly Stopwatch m_timer = Stopwatch.StartNew();
    private readonly EditorInteractionRuntime m_runtime;
    private readonly EditorModalHost m_modals = new();
    private bool m_disposed;

    /// <summary>
    /// Creates an ImGui editor runtime over an existing project context.
    /// </summary>
    /// <param name="context">The shared editor context that owns project settings and frame state.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="context"/> is <see langword="null"/>.
    /// </exception>
    public ImGuiEditorRuntime(EditorContext context)
        : this(context, Array.Empty<object>())
    {
    }

    /// <summary>Creates an ImGui editor runtime with stable host-owned extension services.</summary>
    /// <param name="context">The shared editor context that owns project settings and frame state.</param>
    /// <param name="hostServices">Services available to discovered extension constructors.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="context"/> or <paramref name="hostServices"/> is <see langword="null"/>.
    /// </exception>
    public ImGuiEditorRuntime(EditorContext context, IEnumerable<object> hostServices)
        : base(context ?? throw new ArgumentNullException(nameof(context)))
    {
        ArgumentNullException.ThrowIfNull(hostServices);
        ImGuiIOPtr io = NativeImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.InnoOverlayScrollbars;
        m_runtime = new EditorInteractionRuntime(context, hostServices);
    }

    /// <summary>Gets the active presentation-independent interaction entry point.</summary>
    public EditorInteractions interactions => m_runtime.interactions;

    /// <summary>Gets the number of active dockable panels.</summary>
    public int panelCount => m_runtime.panelCount;

    /// <inheritdoc />
    public override void Start() => m_runtime.Start();

    /// <inheritdoc />
    public override void Update(EditorFrame frame) => m_runtime.Update(frame);

    /// <summary>
    /// Captures all stateful active modules and panels and flushes their project state to disk.
    /// </summary>
    /// <exception cref="ObjectDisposedException">
    /// Thrown after this runtime has been disposed.
    /// </exception>
    public void SaveState()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        m_runtime.SaveState();
    }

    /// <summary>
    /// Freezes automatic extension-state persistence and writes the final project state before editor
    /// modules begin shutting down.
    /// </summary>
    /// <remarks>
    /// This operation is idempotent and prevents module teardown from overwriting the saved
    /// module sections with transient empty state.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">
    /// Thrown after this runtime has been disposed.
    /// </exception>
    public void PrepareShutdown()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        m_runtime.PrepareShutdown();
    }

    /// <summary>Draws the complete editor frame through ImGui.</summary>
    public void Draw()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        EditorWidget.ApplyPendingStyle();
        IReadOnlyList<EditorModalExtension> modals = m_runtime.modals;
        double now = m_timer.Elapsed.TotalSeconds;
        bool blocksInteraction = m_modals.Update(modals, now);

        DrawDockSpace();
        if (blocksInteraction)
            NativeImGui.BeginDisabled(true);
        try
        {
            EditorMenuRenderer.MainMenu(interactions.For(ImGuiInteractionIds.C_MAIN_MENU_AREA));
            DrawPanels(m_runtime.panels);
        }
        finally
        {
            if (blocksInteraction)
                NativeImGui.EndDisabled();
        }

        m_runtime.Flush();
        // Menu actions can change modal visibility after the pre-draw transition update.
        modals = m_runtime.modals;
        _ = m_modals.Update(modals, now);
        m_modals.Draw(context, modals, now);
    }

    private static void DrawDockSpace()
    {
        NativeImGui.PushStyleColor(ImGuiCol.ResizeGripHovered, EditorPalette.accentHovered);
        NativeImGui.PushStyleColor(ImGuiCol.ResizeGripActive, EditorPalette.accentActive);
        try
        {
            _ = NativeImGui.DockSpaceOverViewport();
        }
        finally
        {
            NativeImGui.PopStyleColor(2);
        }
    }

    /// <summary>Dispatches an unhandled keyboard event through contextual shortcuts.</summary>
    /// <param name="keyEvent">The keyboard event received from the application event stream.</param>
    public void HandleKeyPressed(KeyPressedEvent keyEvent)
    {
        ArgumentNullException.ThrowIfNull(keyEvent);
        if (m_modals.Update(m_runtime.modals, m_timer.Elapsed.TotalSeconds))
            return;
        m_runtime.HandleKeyPressed(keyEvent);
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        m_modals.Clear();
        m_runtime.Dispose();
        GC.SuppressFinalize(this);
    }

    private void DrawPanels(IReadOnlyList<EditorPanelExtension> panels)
    {
        for (int i = 0; i < panels.Count; i++)
        {
            EditorPanelExtension extension = panels[i];
            if (!extension.isOpen || !extension.TryGetWindowPresentation(
                    out bool useWindowPadding,
                    out bool allowScrolling))
                continue;
            bool isOpen = extension.isOpen;
            ImGuiWindowFlags flags = ImGuiWindowFlags.NoCollapse;
            if (!allowScrolling)
                flags |= ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
            EditorWidget.PanelWindow(extension.title, ref isOpen, () =>
            {
                if (extension.Draw(context) &&
                    NativeImGui.IsWindowFocused(Inno.Native.ImGui.ImGuiFocusedFlags.RootAndChildWindows))
                {
                    interactions.For(
                        $"panel/{extension.id}",
                        interactions.selection.selectedTarget).Focus();
                }
            }, flags, useWindowPadding);
            extension.isOpen = isOpen;
        }
    }
}
