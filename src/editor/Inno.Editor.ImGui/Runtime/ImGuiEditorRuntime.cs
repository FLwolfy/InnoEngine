using System;
using System.Collections.Generic;
using System.Diagnostics;

using Inno.Core.Events;
using Inno.Editor.Core;
using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using Inno.Editor.Interactions;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.ImGui;

/// <summary>
/// Presents the backend-independent editor interaction runtime through ImGui.
/// </summary>
public sealed class ImGuiEditorRuntime : Inno.Editor.Core.EditorRuntime
{
    private readonly Stopwatch m_timer = Stopwatch.StartNew();
    private readonly EditorInteractionRuntime m_runtime;
    private readonly EditorModalHost m_modals = new();
    private bool m_disposed;

    /// <summary>Creates an ImGui editor runtime for one project.</summary>
    /// <param name="projectDirectory">The project root containing Assets and Library.</param>
    public ImGuiEditorRuntime(string projectDirectory)
        : base(new EditorContext(projectDirectory))
    {
        m_runtime = new EditorInteractionRuntime(context);
    }

    /// <summary>
    /// Creates an ImGui editor runtime over an existing project context.
    /// </summary>
    /// <param name="context">The shared editor context that owns project settings and frame state.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="context"/> is <see langword="null"/>.
    /// </exception>
    public ImGuiEditorRuntime(EditorContext context)
        : base(context ?? throw new ArgumentNullException(nameof(context)))
    {
        m_runtime = new EditorInteractionRuntime(context);
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
    /// Captures all active editor workspace providers and flushes their project state to disk.
    /// </summary>
    /// <exception cref="ObjectDisposedException">
    /// Thrown after this runtime has been disposed.
    /// </exception>
    public void SaveWorkspace()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        m_runtime.SaveWorkspace();
    }

    /// <summary>
    /// Freezes automatic workspace persistence and writes the final project state before editor
    /// modules begin shutting down.
    /// </summary>
    /// <remarks>
    /// This operation is idempotent and prevents module teardown from overwriting the saved
    /// workspace with transient empty state.
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

        _ = NativeImGui.DockSpaceOverViewport();
        if (blocksInteraction)
            NativeImGui.BeginDisabled(true);
        EditorMenuRenderer.MainMenu(interactions.For(EditorAreas.MainMenu));
        DrawPanels(m_runtime.panels);
        if (blocksInteraction)
            NativeImGui.EndDisabled();

        m_runtime.Flush();
        m_modals.Draw(context, modals, now);
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
            if (!extension.panel.isOpen)
                continue;
            bool isOpen = extension.panel.isOpen;
            EditorWidget.PanelWindow(extension.title, ref isOpen, () =>
            {
                extension.panel.Draw(context);
                if (NativeImGui.IsWindowFocused(Inno.Native.ImGui.ImGuiFocusedFlags.RootAndChildWindows))
                {
                    interactions.For(
                        $"panel/{extension.id}",
                        interactions.selection.selectedTarget).Focus();
                }
            }, useWindowPadding: extension.panel.useWindowPadding);
            extension.panel.isOpen = isOpen;
        }
    }
}
