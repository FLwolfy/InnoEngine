using System.Linq;

using Inno.Core.Graphs;
using Inno.Rendering.Shaders;
using Widget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using ImGuiApi = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.ShaderEditor;

internal sealed partial class ShaderEditorCanvas
{
    private void DrawInputDefault(GraphNodeRecord node, ShaderNodePort port)
    {
        var endpoint = new GraphEndpoint(node.id, new(port.id));
        GraphEdgeRecord? edge = Controller.document.edges.FirstOrDefault(value => value.input == endpoint);
        ImGuiApi.PushID(port.id);
        try
        {
            if (edge is not null)
            {
                InspectorPortStatusRow(
                    "port." + port.id,
                    port,
                    null,
                    "From " + edge.output.nodeId.value + "." + edge.output.portId.value);
                return;
            }
            if (node.definitionId == ShaderGraphDocument.outputDefinitionId || draft.missingPorts.Contains(endpoint))
            {
                InspectorPortStatusRow("port." + port.id, port, null, "Connect an available output");
                return;
            }
            if (m_inspectionNodes is { Length: > 1 } selected && selected.Any(value =>
                    !draft.ports[value.id].Any(candidate =>
                        candidate.id == port.id && candidate.type.IsEquivalentTo(port.type)) ||
                    Controller.document.edges.Any(connection =>
                        connection.input == new GraphEndpoint(value.id, new(port.id)))))
            {
                InspectorPortStatusRow("port." + port.id, port, null, "Mixed connections or types");
                return;
            }

            InspectorPortStatusRow(
                "port." + port.id,
                port,
                null,
                port.required
                    ? "Required · Connect a Constant or compatible output"
                    : "Optional · Zero when unconnected");
        }
        finally
        {
            ImGuiApi.PopID();
        }
    }

    private static void InspectorPortStatusRow(
        string id,
        ShaderNodePort port,
        string? component,
        string status)
        => Widget.PropertyRow(
            "shader." + id,
            PortLabel(port, component),
            () => Widget.MetadataValue(
                DisplayType(port.type.id),
                status,
                "Shader value type · " + port.type.id));

    private static string PortLabel(ShaderNodePort port, string? component)
        => component is null ? port.id : port.id + " " + component;

    private static string DisplayType(string typeId)
        => Widget.NicifyName(typeId);
}
