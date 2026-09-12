using System;
using System.Linq;
using Inno.Core.Graphs;
using Inno.Rendering.Shaders;
using UI = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.ShaderEditor;

internal sealed partial class ShaderEditorCanvas
{
    private void RepairPorts(GraphNodeRecord node)
    {
        GraphEndpoint[] missing = draft.missingPorts.Where(endpoint => endpoint.nodeId == node.id).ToArray();
        if (missing.Length == 0) return;
        if (UI.SmallButton("Repair Ports…")) UI.OpenPopup("##repair-ports");
        if (!UI.BeginPopup("##repair-ports")) return;
        try
        {
            UI.TextWrapped("Removed ports retain their connections. Choose a replacement explicitly; occupied inputs are not overwritten.");
            foreach (GraphEndpoint endpoint in missing)
            {
                ShaderNodePort old = Port(endpoint);
                UI.PushID(endpoint.portId.value);
                try
                {
                    UI.TextUnformatted(endpoint.portId.value + " · " + old.type.id);
                    if (!UI.BeginCombo("##replacement", "Reconnect to…")) continue;
                    foreach (ShaderNodePort replacement in draft.ports[node.id])
                    {
                        var target = new GraphEndpoint(node.id, new(replacement.id));
                        if (replacement.direction != old.direction || draft.missingPorts.Contains(target)) continue;
                        bool occupied = replacement.direction == GraphPortDirection.Input && Controller.document.edges.Any(edge => edge.input == target);
                        bool compatible = old.type.id == "missing" || replacement.type.id == "any" || old.type.IsEquivalentTo(replacement.type);
                        UI.BeginDisabled(occupied || !compatible);
                        bool selected = UI.Selectable(replacement.id + (occupied ? " · connected" : !compatible ? " · different type" : ""));
                        UI.EndDisabled();
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
                    UI.EndCombo();
                }
                finally { UI.PopID(); }
            }
        }
        finally { UI.EndPopup(); }
    }
}
