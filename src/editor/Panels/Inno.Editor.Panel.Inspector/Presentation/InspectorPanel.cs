using System;
using System.Numerics;

using Inno.Core.Logging;
using Inno.Editor.Core;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using Inno.Editor.Inspection;
using Inno.Editor.Interactions;
using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Inspector;

/// <summary>
/// Draws the registered inspector for the current editor selection.
/// </summary>
[EditorPanel("scene.inspector", "Inspector", order: 200)]
internal sealed class InspectorPanel : EditorPanel
{
    private readonly SceneInspectionModule m_inspection;
    private readonly EditorInteractions m_interactions;
    private readonly InspectorTargetHeader m_targetHeader;
    private string m_failureState = string.Empty;

    /// <inheritdoc />
    public override bool useWindowPadding => false;

    /// <summary>
    /// Creates the panel.
    /// </summary>
    /// <param name="inspection">The scene inspection module that owns drawer registries and property rendering.</param>
    /// <param name="interactions">The active editor interaction entry point.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="inspection"/> or <paramref name="interactions"/> is <see langword="null"/>.</exception>
    internal InspectorPanel(
        SceneInspectionModule inspection,
        EditorInteractions interactions)
    {
        m_inspection = inspection ?? throw new ArgumentNullException(nameof(inspection));
        m_interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
        m_targetHeader = new InspectorTargetHeader();
    }

    /// <inheritdoc />
    public override void Draw(EditorContext context)
    {
        if (NativeImGui.BeginChild(
                "##InspectorScrollRegion",
                Vector2.Zero,
                ImGuiChildFlags.None,
                ImGuiWindowFlags.NoSavedSettings))
        {
            EditorWidget.ConstrainedContent(
                "##InspectorContent",
                () => DrawContent(context));
        }
        NativeImGui.EndChild();
    }

    private void DrawContent(EditorContext context)
    {
        object? target = m_targetHeader.Resolve(m_interactions.selection.selectedTarget);
        if (target is null)
        {
            m_failureState = string.Empty;
            EditorWidget.Hint("Select an asset or scene object.");
            return;
        }

        if (!m_inspection.TryResolve(
                context,
                target,
                out IInspectionDrawer? drawer,
                out InspectionDrawContext? drawContext) ||
            drawer is null ||
            drawContext is null)
        {
            m_failureState = string.Empty;
            EditorWidget.Hint($"No inspector drawer is registered for {target.GetType().Name}.");
            return;
        }

        m_targetHeader.Draw(drawer, drawContext);
        try
        {
            drawer.Draw(drawContext);
            m_failureState = string.Empty;
        }
        catch (Exception exception)
        {
            EditorWidget.ColoredText(
                EditorPalette.error,
                $"Inspector failed: {exception.Message}");
            string failureState = $"{target.GetType().FullName}:{exception}";
            if (!string.Equals(m_failureState, failureState, StringComparison.Ordinal))
            {
                Log.Error(
                    "Inspector failed for target '{0}': {1}",
                    target.GetType().FullName ?? target.GetType().Name,
                    exception);
                m_failureState = failureState;
            }
        }
    }
}
