using Inno.Core.Graphs;
using Inno.Rendering.Shaders;
using Widget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using UI = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.ShaderEditor;

internal sealed partial class ShaderEditorCanvas
{
    private void DrawTarget()
    {
        string current = ShaderGraphDocument.ReadTarget(Controller.document, owner.serialization, owner.context);
        UI.BeginDisabled(draft.readOnly);
        try
        {
            if (!Widget.BeginBoundedCombo("Target", current.Length == 0 ? "Explicit Stages" : current)) return;
            try
            {
                if (UI.Selectable("Explicit Stages", current.Length == 0)) Assign("");
                if (owner.targets is { } targets)
                    foreach (string id in targets.ids)
                        if (UI.Selectable(id, current == id)) Assign(id);
            }
            finally { UI.EndCombo(); }
        }
        finally { UI.EndDisabled(); }
        void Assign(string id)
        {
            if (id == current) return;
            GraphDocument candidate = Controller.document.Clone();
            ShaderGraphDocument.SetTarget(candidate, id, owner.serialization, owner.context);
            Controller.ReplaceDocument(candidate, "Change Shader Target");
            owner.Changed(draft);
        }
    }
}
