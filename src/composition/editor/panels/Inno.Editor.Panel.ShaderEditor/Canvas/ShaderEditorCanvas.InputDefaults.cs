using System;
using System.Linq;
using Inno.Core.Graphs;
using Inno.Rendering.Shaders;
using Widget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using UI = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.ShaderEditor;

internal sealed partial class ShaderEditorCanvas
{
    private void DrawInputDefault(GraphNodeRecord node, ShaderNodePort port)
    {
        var endpoint = new GraphEndpoint(node.id, new(port.id));
        GraphEdgeRecord? edge = Controller.document.edges.FirstOrDefault(value => value.input == endpoint);
        UI.PushID(port.id);
        try
        {
            UI.TextUnformatted(port.id + " · " + port.type.id);
            if (edge is not null)
            {
                Widget.Hint("From " + edge.output.nodeId.value + "." + edge.output.portId.value + " · stored defaults are inactive");
                return;
            }
            if (node.definitionId == ShaderGraphDocument.outputDefinitionId || draft.missingPorts.Contains(endpoint))
            { Widget.Hint("Connect an available output to this input."); return; }
            if (m_inspectionNodes is { Length: > 1 } selected && selected.Any(value => !draft.ports[value.id].Any(candidate =>
                candidate.id == port.id && candidate.type.IsEquivalentTo(port.type)) || Controller.document.edges.Any(connection => connection.input == new GraphEndpoint(value.id, new(port.id)))))
            { Widget.Hint("Selected inputs have different types or connections; edit them separately."); return; }
            ShaderGraphLiteral zero;
            try { zero = ShaderGraphLiteral.Zero(port.type); }
            catch (NotSupportedException) { Widget.Hint("This resource or effect input requires an explicit connection."); return; }
            string key = ShaderGraphDocument.inputDefaultPrefix + port.id;
            if (!node.TryGetValue(key, out var encoded))
            {
                if (!port.required) Widget.Hint("No override · this input is defined by its node or Target.");
                if (UI.SmallButton(port.required ? "Use Zero Default" : "Override Default with Zero")) Set(node, key, zero);
                return;
            }
            ShaderGraphLiteral literal;
            System.Collections.Generic.IReadOnlyList<string> scalarTypes;
            try
            {
                literal = ShaderGraphDocument.Decode<ShaderGraphLiteral>(encoded!, owner.serialization, owner.context);
                scalarTypes = literal.GetScalarTypes();
                if (!literal.type.CreateType().IsEquivalentTo(port.type))
                    throw new InvalidOperationException("The port type changed. Its previous default remains preserved.");
            }
            catch (Exception error) when ((error is InvalidOperationException or ArgumentException or FormatException or NotSupportedException)
                && Inno.Core.Execution.RetirementPendingException.Find(error) is null)
            {
                Widget.Hint(error.Message);
                if (UI.SmallButton("Reset Default to Current Type")) Set(node, key, zero);
                return;
            }
            for (int index = 0; index < scalarTypes.Count; index++)
            {
                UI.PushID(index);
                string label = scalarTypes.Count == 1 ? "Value" : scalarTypes.Count <= 4 && port.type.fields.Count == 0 && port.type.elementType is null
                    ? new[] { "X", "Y", "Z", "W" }[index] : "Component " + index;
                uint bits = literal.scalarBits[index];
                bool changed;
                switch (scalarTypes[index])
                {
                    case "float":
                        float number = BitConverter.UInt32BitsToSingle(bits);
                        changed = Widget.CompactDragFloat(label, ref number, 0.01f);
                        bits = BitConverter.SingleToUInt32Bits(number);
                        break;
                    case "int":
                        int signed = unchecked((int)bits);
                        changed = UI.InputInt(label, ref signed); bits = unchecked((uint)signed);
                        break;
                    case "uint":
                        string text = bits.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        changed = UI.InputText(label, ref text, 32) && uint.TryParse(text, out bits);
                        break;
                    default:
                        bool boolean = bits != 0;
                        changed = UI.Checkbox(label, ref boolean); bits = boolean ? 1u : 0u;
                        break;
                }
                Gesture();
                if (changed) { literal.scalarBits[index] = bits; Set(node, key, literal, true); }
                UI.PopID();
            }
            if (UI.SmallButton(port.required ? "Require Connection" : "Use Node / Target Default"))
            {
                using var transaction = owner.interactions.history.BeginTransaction("Remove Input Default");
                foreach (GraphNodeRecord target in m_inspectionNodes ?? [node]) Controller.RemoveNodeValue(target.id, key);
                transaction.Commit(); owner.Changed(draft);
            }
        }
        finally { UI.PopID(); }
    }
}
