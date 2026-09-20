using System;
using System.Linq;
using Inno.Assets.Pipeline;
using Inno.Core.Graphs;
using Inno.Native.ImGui;
using Inno.Rendering;
using Inno.Rendering.Assets;
using Inno.Rendering.Shaders;
using Widget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using UI = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.ShaderEditor;

internal sealed partial class ShaderEditorCanvas
{
    private void Controls(GraphNodeRecord node)
    {
        if (owner.drawers?.TryDraw(node.definitionId, new(node, owner.serialization, owner.context,
            (key, value, continuous) => SetEncoded(node, key, value, continuous), owner.previews, m_inspection!, draft.readOnly)) == true) return;
        switch (node.definitionId)
        {
            case "inno.shader.constant":
                Scalar(node);
                break;
            case "inno.shader.binary":
                Choice(node, "operation", "add", ["add", "subtract", "multiply", "divide", "minimum", "maximum", "equal", "less-than"]);
                Choice(node, "type", "float", ["float", "float2", "float3", "float4", "int", "uint"]);
                break;
            case "inno.shader.construct":
                Choice(node, "type", "float4", ["float2", "float3", "float4", "int2", "int3", "int4", "uint2", "uint3", "uint4", "float3x3", "float4x4"]);
                break;
            case "inno.shader.select":
                Choice(node, "type", "float", ["float", "float2", "float3", "float4", "int", "uint", "bool"]);
                break;
            case "inno.shader.reroute":
                InspectorRow("reroute.type", "Type", () =>
                    UI.TextDisabled(Read(node, "valueType", new ShaderGraphType { id = "float" }).CreateType().id));
                Widget.Hint("Forwards the complete port type without changing the value.");
                break;
            case "inno.shader.extract":
                Choice(node, "type", "float4", ["float2", "float3", "float4", "float3x3", "float4x4"]);
                int index = Read(node, "index", 0);
                InspectorRow("extract.component", "Component", () =>
                {
                    if (UI.InputInt("##component", ref index) && index >= 0) Set(node, "index", index);
                });
                break;
            case "inno.shader.sample":
                Choice(node, "type", "sampled-texture2d", ["sampled-texture2d", "sampled-texture2d-array", "sampled-texture3d", "sampled-texture-cube"]);
                bool level = Read(node, "explicitLevel", false);
                InspectorRow("sample.explicit-level", "Explicit LOD", () =>
                {
                    if (UI.Checkbox("##explicit-level", ref level)) Set(node, "explicitLevel", level);
                });
                break;
            case "inno.shader.stage-input":
                Input(node);
                break;
            case "inno.shader.source":
                Source(node);
                break;
            case ShaderGraphNodes.callDefinitionId:
                GraphCall(node);
                break;
            case ShaderGraphNodes.inputDefinitionId:
                GraphInputs(node);
                break;
            case ShaderGraphNodes.outputDefinitionId:
                GraphOutputs(node);
                break;
            case ShaderGraphDocument.outputDefinitionId:
                Output(node);
                break;
            case "inno.shader.storage-load":
            case "inno.shader.storage-store":
            case "inno.shader.storage-atomic-add":
                ShaderGraphType resource = Read(node, "resource", new ShaderGraphType { isStorage = true,
                    storageElement = new() { id = "float4" }, access = RenderStorageAccess.ReadWrite });
                InspectorRow("storage.contract", "Contract", () =>
                {
                    if (UI.Button("Storage Contract…")) UI.OpenPopup("##storage");
                });
                if (StoragePopup(ref resource)) Set(node, "resource", resource);
                UI.TextWrapped("Connect then → after to order memory effects.");
                break;
            case "inno.shader.discard":
                UI.TextWrapped("Discard fragments where condition is true.");
                break;
            default:
                UI.TextDisabled("Extension node");
                break;
        }
    }

    private void GraphCall(GraphNodeRecord node)
    {
        Guid selected = Read(node, "sourceId", Guid.Empty);
        string path = Read(node, "sourcePath", "");
        ShaderGraphNodeInterface nodeInterface = Read(node, ShaderGraphNodes.interfaceKey, new ShaderGraphNodeInterface());
        InspectorRow("graph-node.asset", "Node Graph", () =>
        {
            if (!Widget.BeginBoundedCombo("##graph-node", selected == Guid.Empty ? "Choose Shader node…" : System.IO.Path.GetFileName(path))) return;
            try
            {
                foreach (AssetFileEntry entry in owner.assets.GetFileSystemEntries(includeDirectories: false)
                             .Where(static entry => entry.extension == ".ishader")
                             .OrderBy(static entry => entry.assetPath.ToString(), StringComparer.Ordinal))
                {
                    Guid id = owner.AssetId(entry);
                    if (id == draft.id) continue;
                    ShaderGraphNodeInterface candidate;
                    try { candidate = owner.LoadGraphNodeInterface(id); }
                    catch (Exception failure) when ((failure is InvalidOperationException or ArgumentException or FormatException)
                        && Inno.Core.Execution.RetirementPendingException.Find(failure) is null) { continue; }
                    if (!UI.Selectable(candidate.displayName + "  ·  " + entry.assetPath.localPath, id == selected)) continue;
                    using var transaction = owner.interactions.history.BeginTransaction("Assign Graph Node");
                    Set(node, "sourceId", id);
                    Set(node, "sourcePath", entry.assetPath.ToString());
                    Set(node, ShaderGraphNodes.interfaceKey, candidate);
                    transaction.Commit();
                    selected = id;
                    path = entry.assetPath.ToString();
                    nodeInterface = candidate;
                }
            }
            finally { UI.EndCombo(); }
        });
        Widget.DrawItemTooltip(path);
        InspectorRow("graph-node.kind", "Kind", () => UI.TextDisabled(nodeInterface.kind.ToString()));
        InspectorRow("graph-node.effect", "Effect", () => UI.TextDisabled(nodeInterface.effect.ToString()));
        if (nodeInterface.kind == ShaderGraphNodeKind.DomainOutput)
            InspectorRow("graph-node.role", "Target Role", () => UI.TextDisabled(nodeInterface.role));
        RepairPorts(node);
    }

    private void GraphInputs(GraphNodeRecord node)
    {
        GraphInterfaceMetadata();
        ShaderGraphNodeInputSettings settings = Read(node, ShaderGraphDocument.settingsKey, new ShaderGraphNodeInputSettings());
        GraphPorts(node, settings.ports, inputs: true, ports =>
        {
            settings.ports = ports;
            Set(node, ShaderGraphDocument.settingsKey, settings);
        });
    }

    private void GraphOutputs(GraphNodeRecord node)
    {
        GraphInterfaceMetadata();
        ShaderGraphNodeOutputSettings settings = Read(node, ShaderGraphDocument.settingsKey, new ShaderGraphNodeOutputSettings());
        GraphPorts(node, settings.ports, inputs: false, ports =>
        {
            settings.ports = ports;
            Set(node, ShaderGraphDocument.settingsKey, settings);
        });
    }

    private void GraphInterfaceMetadata()
    {
        ShaderGraphNodeSettings settings = ShaderGraphNodes.ReadSettings(
            Controller.document, owner.serialization, owner.context);
        string displayName = settings.displayName;
        InspectorRow("node-interface.name", "Node Name", () =>
        {
            bool changed = UI.InputText("##name", ref displayName, 256);
            Gesture();
            if (changed) { settings.displayName = displayName; SetGraphNodeSettings(settings, true); }
        });
        string createPath = settings.createPath;
        InspectorRow("node-interface.catalog", "Catalog", () =>
        {
            bool changed = UI.InputText("##catalog", ref createPath, 256);
            Gesture();
            if (changed) { settings.createPath = createPath; SetGraphNodeSettings(settings, true); }
        });
        int createOrder = settings.createOrder;
        InspectorRow("node-interface.order", "Order", () =>
        {
            bool changed = UI.InputInt("##order", ref createOrder);
            Gesture();
            if (changed) { settings.createOrder = createOrder; SetGraphNodeSettings(settings, true); }
        });
        InspectorRow("node-interface.kind", "Kind", () =>
        {
            if (!Widget.BeginBoundedCombo("##kind", settings.kind.ToString())) return;
            try
            {
                foreach (ShaderGraphNodeKind kind in Enum.GetValues<ShaderGraphNodeKind>())
                    if (UI.Selectable(kind.ToString(), settings.kind == kind))
                    { settings.kind = kind; SetGraphNodeSettings(settings); }
            }
            finally { UI.EndCombo(); }
        });
        if (settings.kind == ShaderGraphNodeKind.Function)
        {
            InspectorRow("node-interface.effect", "Effect", () =>
            {
                if (!Widget.BeginBoundedCombo("##effect", settings.effect.ToString())) return;
                try
                {
                    foreach (ShaderGraphNodeEffect effect in Enum.GetValues<ShaderGraphNodeEffect>())
                        if (UI.Selectable(effect.ToString(), settings.effect == effect))
                        { settings.effect = effect; SetGraphNodeSettings(settings); }
                }
                finally { UI.EndCombo(); }
            });
        }
        if (settings.kind == ShaderGraphNodeKind.DomainOutput)
        {
            string role = settings.role;
            InspectorRow("node-interface.role", "Target Role", () =>
            {
                bool changed = UI.InputText("##role", ref role, 256);
                Gesture();
                if (changed) { settings.role = role; SetGraphNodeSettings(settings, true); }
            });
        }
    }

    private void SetGraphNodeSettings(ShaderGraphNodeSettings settings, bool continuous = false)
    {
        GraphDocument candidate = Controller.document.Clone();
        ShaderGraphNodes.WriteSettings(candidate, settings, owner.serialization, owner.context);
        Controller.ReplaceDocument(candidate, "Edit Graph Node Interface", continuous ? draft.valueGesture : null);
        owner.Changed(draft);
    }

    private void GraphPorts(GraphNodeRecord node, ShaderGraphNodePortDefinition[] ports, bool inputs,
        Action<ShaderGraphNodePortDefinition[]> apply)
    {
        UI.SeparatorText(inputs ? "Inputs" : "Outputs");
        for (int index = 0; index < ports.Length; index++)
        {
            UI.PushID(index);
            try
            {
                ShaderGraphNodePortDefinition port = ports[index];
                string id = port.id;
                InspectorRow("node-port.id", "Port " + (index + 1), () =>
                {
                    bool changed = UI.InputText("##id", ref id, 128);
                    Gesture();
                    if (changed)
                    {
                        ShaderGraphNodePortDefinition[] changedPorts = ports.ToArray();
                        changedPorts[index] = new() { id = id, type = port.type, required = port.required };
                        apply(changedPorts);
                    }
                });
                InspectorRow("node-port.type", "Type", () =>
                {
                    if (!Widget.BeginBoundedCombo("##type", port.type.id)) return;
                    try
                    {
                        foreach (string type in new[] { "float", "float2", "float3", "float4", "int", "int2", "int3", "int4", "uint", "uint2", "uint3", "uint4", "bool", "float3x3", "float4x4", "sampled-texture2d", "sampled-texture2d-array", "sampled-texture3d", "sampled-texture-cube" })
                            if (UI.Selectable(type, port.type.id == type))
                            {
                                ShaderGraphNodePortDefinition[] changedPorts = ports.ToArray();
                                changedPorts[index] = new() { id = port.id, type = new() { id = type }, required = port.required };
                                apply(changedPorts);
                            }
                    }
                    finally { UI.EndCombo(); }
                });
                if (inputs)
                {
                    bool required = port.required;
                    InspectorRow("node-port.required", "Required", () =>
                    {
                        if (!UI.Checkbox("##required", ref required)) return;
                        ShaderGraphNodePortDefinition[] changedPorts = ports.ToArray();
                        changedPorts[index] = new() { id = port.id, type = port.type, required = required };
                        apply(changedPorts);
                    });
                }
                InspectorRow("node-port.remove", "", () =>
                {
                    if (UI.SmallButton("Remove")) apply(ports.Where((_, item) => item != index).ToArray());
                });
            }
            finally { UI.PopID(); }
        }
        if (CenteredAddButton(inputs ? "Add Input" : "Add Output"))
        {
            string prefix = inputs ? "input" : "output";
            string id = prefix;
            for (int suffix = 2; ports.Any(port => port.id == id); suffix++) id = prefix + suffix;
            apply([.. ports, new() { id = id, type = new() { id = "float" }, required = false }]);
        }
        RepairPorts(node);
    }

    private void Scalar(GraphNodeRecord node)
    {
        string type = Read(node, "type", "float");
        InspectorRow("constant.type", "Type", () =>
        {
            if (!Widget.BeginBoundedCombo("##constant-type", type)) return;
            try
            {
                foreach (string candidate in new[] { "float", "int", "uint", "bool" })
                    if (UI.Selectable(candidate, type == candidate))
                    {
                        using var transaction = owner.interactions.history.BeginTransaction("Change Constant Type");
                        Set(node, "type", candidate);
                        switch (candidate)
                        {
                            case "float": Set(node, "value", 0f); break;
                            case "int": Set(node, "value", 0); break;
                            case "uint": Set(node, "value", 0u); break;
                            case "bool": Set(node, "value", false); break;
                        }
                        transaction.Commit();
                        type = candidate;
                    }
            }
            finally { UI.EndCombo(); }
        });
        switch (type)
        {
            case "float":
                float number = Read(node, "value", 0f);
                InspectorRow("constant.value", "Value", () =>
                {
                    bool changed = Widget.CompactDragFloat("##value", ref number, 0.01f);
                    Gesture();
                    if (changed) Set(node, "value", number, true);
                });
                break;
            case "bool":
                bool boolean = Read(node, "value", false);
                InspectorRow("constant.value", "Value", () =>
                {
                    if (UI.Checkbox("##value", ref boolean)) Set(node, "value", boolean);
                });
                break;
            case "int":
                int integer = Read(node, "value", 0);
                InspectorRow("constant.value", "Value", () =>
                {
                    bool integerChanged = UI.InputInt("##value", ref integer);
                    Gesture();
                    if (integerChanged) Set(node, "value", integer, true);
                });
                break;
            case "uint":
                string unsigned = Read(node, "value", 0u).ToString(System.Globalization.CultureInfo.InvariantCulture);
                InspectorRow("constant.value", "Value", () =>
                {
                    bool unsignedChanged = UI.InputText("##value", ref unsigned, 32, ImGuiInputTextFlags.CharsDecimal);
                    Gesture();
                    if (unsignedChanged && uint.TryParse(unsigned, out uint value)) Set(node, "value", value, true);
                });
                break;
        }
    }

    private void Source(GraphNodeRecord node)
    {
        Guid selected = Read(node, "sourceId", Guid.Empty);
        string path = Read(node, "sourcePath", "");
        string selectedFunction = Read(node, "function", "");
        InspectorRow("source.library", "Library", () =>
        {
            if (!Widget.BeginBoundedCombo("##library", selected == Guid.Empty ? "Choose source library…" : System.IO.Path.GetFileName(path))) return;
            try
            {
                foreach (AssetFileEntry entry in owner.assets.GetFileSystemEntries(includeDirectories: false)
                    .Where(static entry => entry.assetPath.localPath.EndsWith(".ishadersource", StringComparison.OrdinalIgnoreCase)))
                    if (UI.Selectable(entry.assetPath.ToString(), owner.AssetId(entry) == selected))
                    {
                        using var transaction = owner.interactions.history.BeginTransaction("Assign Shader Function");
                        Set(node, "sourceId", owner.AssetId(entry));
                        Set(node, "sourcePath", entry.assetPath.ToString());
                        if (owner.assets.TryLoad(entry.assetPath, out ShaderFunctionAsset? library) && library is not null)
                            Set(node, "function", library.exports.FirstOrDefault() ?? "");
                        transaction.Commit();
                    }
            }
            finally { UI.EndCombo(); }
        });
        Widget.DrawItemTooltip(path);
        if (selected == Guid.Empty) UI.TextDisabled("Choose a library and one of its explicitly exported functions.");
        else
        {
            if (owner.assets.TryLoad(selected, out ShaderFunctionAsset? library) && library is not null && !library.isMissing)
            {
                InspectorRow("source.function", "Function", () =>
                {
                    if (!Widget.BeginBoundedCombo("##function", selectedFunction.Length == 0 ? "Choose exported function…" : selectedFunction)) return;
                    try
                    {
                        foreach (string function in library.exports)
                            if (UI.Selectable(function, function == selectedFunction)) Set(node, "function", function);
                    }
                    finally { UI.EndCombo(); }
                });
            }
            SourceSettings(selected);
        }
        RepairPorts(node);
    }

    private void Input(GraphNodeRecord node)
    {
        ShaderGraphInputSettings input = Read(node, "settings", new ShaderGraphInputSettings());
        string id = input.id;
        bool changed = false;
        InspectorRow("input.binding", "Binding ID", () => changed = UI.InputText("##binding", ref id, 256));
        Gesture();
        if (changed) { input.id = id; Set(node, "settings", input, true); }
        InspectorRow("input.source", "Source", () =>
        {
            if (!Widget.BeginBoundedCombo("##source", input.kind.ToString())) return;
            try
            {
                foreach (ShaderIrInputKind kind in Enum.GetValues<ShaderIrInputKind>())
                    if (UI.Selectable(kind.ToString(), input.kind == kind))
                    {
                        input.kind = kind;
                        if (kind == ShaderIrInputKind.Storage) input.type = new() { isStorage = true, storageElement = new() { id = "float4" }, access = RenderStorageAccess.ReadWrite };
                        else if (kind == ShaderIrInputKind.SampledTexture) input.type = new() { id = "sampled-texture2d" };
                        else if (input.type.isStorage || input.type.id.StartsWith("sampled-texture", StringComparison.Ordinal)) input.type = new() { id = "float4" };
                        if (kind == ShaderIrInputKind.Builtin)
                        {
                            string stageId = Read(node, "stage", "");
                            GraphNodeRecord? output = stageId.Length == 0 ? null : Controller.document.FindNode(new(stageId));
                            if (output is not null && Read(output, "settings", new ShaderGraphStageSettings()).stage == ShaderStage.Compute)
                            { input.semantic = "global-invocation-id"; input.type = new() { id = "uint3" }; }
                        }
                        Set(node, "settings", input);
                    }
            }
            finally { UI.EndCombo(); }
        });
        string type = input.type.id;
        if (input.kind == ShaderIrInputKind.Storage)
        {
            InspectorRow("input.storage", "Contract", () =>
            {
                if (UI.Button("Storage Contract…")) UI.OpenPopup("##storage");
            });
            ShaderGraphType resource = input.type;
            if (StoragePopup(ref resource)) { input.type = resource; Set(node, "settings", input); }
        }
        else InspectorRow("input.type", "Type", () =>
        {
            if (!Widget.BeginBoundedCombo("##type", type)) return;
            try
            {
                foreach (string candidate in new[] { "float", "float2", "float3", "float4", "int", "int2", "int3", "int4", "uint", "uint2", "uint3", "uint4", "bool", "float3x3", "float4x4", "sampled-texture2d", "sampled-texture2d-array", "sampled-texture3d", "sampled-texture-cube" })
                    if (UI.Selectable(candidate, type == candidate)) { input.type = new() { id = candidate }; Set(node, "settings", input); }
            }
            finally { UI.EndCombo(); }
        });
        string semantic = input.semantic;
        bool semanticChanged = false;
        InspectorRow("input.semantic", "Semantic", () => semanticChanged = UI.InputText("##semantic", ref semantic, 256));
        Gesture();
        if (semanticChanged) { input.semantic = semantic; Set(node, "settings", input, true); }
        Widget.DrawItemTooltip("Stage semantic, not a native expression. Builtins must be supported by the selected stage and target; compute invocation IDs use uint3.");
        int location = input.location;
        bool locationChanged = false;
        InspectorRow("input.location", "Location", () => locationChanged = UI.InputInt("##location", ref location));
        Gesture();
        if (locationChanged && location >= 0) { input.location = location; Set(node, "settings", input, true); }
        if (input.kind is ShaderIrInputKind.Uniform or ShaderIrInputKind.SampledTexture or ShaderIrInputKind.Storage)
        {
            DrawParameter(input);
        }
        if (input.kind == ShaderIrInputKind.SampledTexture)
        {
            bool expanded = draft.expandedPreviews.Contains(node.id);
            InspectorRow("input.preview", "Preview", () =>
            {
                if (UI.Checkbox("##preview", ref expanded))
                { if (expanded) draft.expandedPreviews.Add(node.id); else draft.expandedPreviews.Remove(node.id); }
            });
            if (expanded)
            {
                ShaderDefinition definition = ShaderGraphDocument.ReadDefinition(Controller.document, owner.serialization, owner.context);
                TextureAsset? texture = definition.properties.FirstOrDefault(value => value.id.value == input.id).defaultValue.texture;
                if (texture is not null && owner.previews.TryGetTexture(texture, out var preview))
                    owner.previews.Draw(preview, new(120 * Canvas.zoom, 120 * Canvas.zoom));
                else UI.TextDisabled(texture is null ? "No default texture" : "Preparing preview…");
            }
        }
    }

    private void Output(GraphNodeRecord node)
    {
        ShaderGraphStageSettings stage = Read(node, "settings", new ShaderGraphStageSettings());
        InspectorRow("output.passes", "Passes", () => UI.TextDisabled(string.Join(", ",
            ShaderGraphPrograms.Read(Controller.document, owner.serialization, owner.context)
                .Where(program => program.stages.Contains(node.id.value, StringComparer.Ordinal)).Select(static program => program.pass))));
        InspectorRow("output.settings", "Settings", () =>
        {
            if (UI.Button("Stage & Pass Settings…")) UI.OpenPopup("##stage-settings");
        });
        StageSettingsPopup(node, stage);
        if (stage.stage == ShaderStage.Compute)
        {
            int x = stage.threadsX, y = stage.threadsY, z = stage.threadsZ;
            InspectorRow("output.threads-x", "Threads X", () => { if (UI.InputInt("##threads-x", ref x) && x > 0) { stage.threadsX = x; Set(node, "settings", stage); } });
            InspectorRow("output.threads-y", "Threads Y", () => { if (UI.InputInt("##threads-y", ref y) && y > 0) { stage.threadsY = y; Set(node, "settings", stage); } });
            InspectorRow("output.threads-z", "Threads Z", () => { if (UI.InputInt("##threads-z", ref z) && z > 0) { stage.threadsZ = z; Set(node, "settings", stage); } });
        }
    }

    private void Choice(GraphNodeRecord node, string key, string defaultValue, string[] values)
    {
        string current = Read(node, key, defaultValue);
        InspectorRow("choice." + key, Widget.NicifyName(key), () =>
        {
            if (!Widget.BeginBoundedCombo("##" + key, current)) return;
            try
            {
                foreach (string candidate in values)
                    if (UI.Selectable(candidate, current == candidate)) Set(node, key, candidate);
            }
            finally { UI.EndCombo(); }
        });
    }

    private void Gesture()
    {
        if (UI.IsItemActivated()) draft.valueGesture = Guid.NewGuid().ToString("N");
    }
}
