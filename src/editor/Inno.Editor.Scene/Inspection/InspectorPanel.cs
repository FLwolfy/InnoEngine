using System;
using Inno.Core.Logging;
using Inno.Editor.Core;
using Inno.Editor.Core.Panels;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.Widgets;
using Inno.Editor.Scene.Inspection;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Scene.Inspection;

/// <summary>
/// Draws the registered inspector for the current editor selection.
/// </summary>
[EditorPanel("scene.inspector", "Inspector", order: 200)]
public sealed class InspectorPanel : EditorPanel
{
    private readonly SceneInspectionModule m_inspection;

    /// <summary>
    /// Creates the panel.
    /// </summary>
    /// <param name="inspection">The scene inspection module that owns drawer registries and property rendering.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="inspection"/> is <see langword="null"/>.</exception>
    public InspectorPanel(SceneInspectionModule inspection)
    {
        m_inspection = inspection ?? throw new ArgumentNullException(nameof(inspection));
    }

    /// <inheritdoc />
    public override void Draw(EditorContext context)
    {
        object? target = context.selection.selectedTarget;
        if (target is null)
        {
            ImGuiWidget.Hint("Select an asset or scene object.");
            return;
        }

        try
        {
            if (!m_inspection.Draw(context, target))
            {
                ImGuiWidget.Hint($"No inspector drawer is registered for {target.GetType().Name}.");
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
