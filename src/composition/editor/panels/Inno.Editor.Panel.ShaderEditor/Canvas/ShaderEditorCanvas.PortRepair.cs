using System;
using System.Linq;
using Inno.Core.Graphs;
using Inno.Rendering.Shaders;
using ImGuiApi = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.ShaderEditor;

internal sealed partial class ShaderEditorCanvas
{
    private void RepairPorts(GraphNodeRecord node)
    {
        GraphEndpoint[] missing = draft.missingPorts.Where(endpoint => endpoint.nodeId == node.id).ToArray();
        if (missing.Length == 0) return;
        if (ImGuiApi.SmallButton("Repair Ports…")) ImGuiApi.OpenPopup("##repair-ports");
        if (!ImGuiApi.BeginPopup("##repair-ports")) return;
        try
        {
            ImGuiApi.TextWrapped("Removed ports retain their connections. Choose a replacement explicitly; occupied inputs are not overwritten.");
            foreach (GraphEndpoint endpoint in missing)
            {
                ShaderNodePort old = Port(endpoint);
                ImGuiApi.PushID(endpoint.portId.value);
                try
                {
                    ImGuiApi.TextUnformatted(endpoint.portId.value + " · " + old.type.id);
                    if (!ImGuiApi.BeginCombo("##replacement", "Reconnect to…")) continue;
                    foreach (ShaderNodePort replacement in draft.ports[node.id])
                    {
                        var target = new GraphEndpoint(node.id, new(replacement.id));
                        if (replacement.direction != old.direction || draft.missingPorts.Contains(target)) continue;
                        bool occupied = replacement.direction == GraphPortDirection.Input && Controller.document.edges.Any(edge => edge.input == target);
                        bool compatible = old.type.id == "missing" || replacement.type.id == "any" || old.type.IsEquivalentTo(replacement.type);
                        ImGuiApi.BeginDisabled(occupied || !compatible);
                        bool selected = ImGuiApi.Selectable(replacement.id + (occupied ? " · connected" : !compatible ? " · different type" : ""));
                        ImGuiApi.EndDisabled();
                        if (!selected) continue;
                        GraphDocument candidate = Controller.document.Clone();
                        foreach (GraphEdgeRecord edge in candidate.edges.Where(edge => edge.input == endpoint || edge.output == endpoint).ToArray())
                        {
                            candidate.RemoveEdge(edge.id);
                            candidate.AddEdge(new(edge.id, edge.output == endpoint ? target : edge.output, edge.input == endpoint ? target : edge.input));
                        }
                        // Keep the retired declaration in metadata for a future source restoration and Undo.
                        candidate.FindNode(node.id)!.SetValue(ShaderEditorDocuments.C_PORT_SNAPSHOT,
                            ShaderGraphDocument.Encode(draft.portSnapshots[node.id], owner.serialization, owner.context));
                        Controller.ReplaceDocument(candidate, "Reconnect Shader Port");
                        owner.Changed(draft);
                        break;
                    }
                    ImGuiApi.EndCombo();
                }
                finally { ImGuiApi.PopID(); }
            }
        }
        finally { ImGuiApi.EndPopup(); }
    }
}
