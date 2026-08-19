using System;
using System.Numerics;

using Inno.Editor.Core;
using Inno.Editor.Core.DragDrop;
using Inno.Editor.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Scene.Hierarchy;

internal sealed class HierarchyDropVisual
{
    internal EditorDropPlacement GetScenePlacement(in TreeNodeResult result, float mouseY)
        => mouseY >= (result.min.Y + result.max.Y) * 0.5f
            ? EditorDropPlacement.After
            : EditorDropPlacement.Before;

    internal EditorDropPlacement GetObjectPlacement(in TreeNodeResult result, float mouseY)
    {
        float height = MathF.Max(1f, result.max.Y - result.min.Y);
        float relativeY = (mouseY - result.min.Y) / height;
        if (relativeY < 0.25f)
            return EditorDropPlacement.Before;
        return relativeY > 0.75f ? EditorDropPlacement.After : EditorDropPlacement.Into;
    }

    internal void Draw(in TreeNodeResult result, EditorDropVisual visual)
    {
        switch (visual)
        {
            case EditorDropVisual.InsertBefore:
                ImGuiWidget.InsertionLine(result.min.X, result.max.X, result.min.Y);
                break;
            case EditorDropVisual.InsertAfter:
                ImGuiWidget.InsertionLine(result.min.X, result.max.X, result.max.Y);
                break;
            case EditorDropVisual.Highlight:
            {
                Vector2 highlightMin = result.contentMin;
                highlightMin.X = MathF.Max(
                    result.min.X,
                    highlightMin.X - NativeImGui.GetStyle().ItemInnerSpacing.X);
                ImGuiWidget.DropTargetHighlight(highlightMin, result.max);
                break;
            }
        }
    }
}
