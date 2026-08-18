using System;
using System.Numerics;

using Inno.Core.Logging;
using Inno.Editor.Core;
using Inno.Editor.ImGui;
using Inno.Editor.Inspection;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panels;

/// <summary>
/// Draws the registered inspector for the current editor selection.
/// </summary>
public sealed class InspectorPanel : EditorPanel
{
    /// <summary>
    /// Creates the panel.
    /// </summary>
    public InspectorPanel()
        : base("editor.inspector", "Inspector")
    {
    }

    /// <inheritdoc />
    public override void OnRender(EditorContext context)
    {
        object? target = context.selection.selectedTarget;
        if (target is null)
        {
            ImGuiWidget.Hint("Select an asset or scene object.");
            return;
        }

        try
        {
            if (!InspectorDrawerRegistry.Draw(context, target))
            {
                ImGuiWidget.Hint($"No inspector drawer is registered for {target.GetType().Name}.");
            }
        }
        catch (Exception exception)
        {
            NativeImGui.TextColored(
                new Vector4(1f, 0.35f, 0.35f, 1f),
                $"Inspector failed: {exception.Message}");
            Log.Error("Inspector failed for target '{0}': {1}", target.GetType().FullName ?? target.GetType().Name, exception);
        }
    }
}
