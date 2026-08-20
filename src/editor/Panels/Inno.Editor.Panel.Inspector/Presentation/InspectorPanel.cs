using System;
using Inno.Core.Logging;
using Inno.Editor.Core;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using Inno.Editor.Interactions;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Inspector;

/// <summary>
/// Draws the registered inspector for the current editor selection.
/// </summary>
[EditorPanel("scene.inspector", "Inspector", order: 200)]
public sealed class InspectorPanel : EditorPanel
{
    private readonly SceneInspectionModule m_inspection;
    private readonly EditorInteractions m_interactions;

    /// <summary>
    /// Creates the panel.
    /// </summary>
    /// <param name="inspection">The scene inspection module that owns drawer registries and property rendering.</param>
    /// <param name="interactions">The active editor interaction entry point.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="inspection"/> or <paramref name="interactions"/> is <see langword="null"/>.</exception>
    public InspectorPanel(
        SceneInspectionModule inspection,
        EditorInteractions interactions)
    {
        m_inspection = inspection ?? throw new ArgumentNullException(nameof(inspection));
        m_interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
    }

    /// <inheritdoc />
    public override void Draw(EditorContext context)
    {
        object? target = m_interactions.selection.selectedTarget;
        if (target is null)
        {
            EditorWidget.Hint("Select an asset or scene object.");
            return;
        }

        try
        {
            if (!m_inspection.Draw(context, target))
            {
                EditorWidget.Hint($"No inspector drawer is registered for {target.GetType().Name}.");
            }
        }
        catch (Exception exception)
        {
            NativeImGui.TextColored(
                EditorPalette.error,
                $"Inspector failed: {exception.Message}");
            Log.Error("Inspector failed for target '{0}': {1}", target.GetType().FullName ?? target.GetType().Name, exception);
        }
    }
}
