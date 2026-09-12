using System;
using System.Linq;
using Inno.Assets.Pipeline;
using Inno.Core.Graphs;
using Inno.Native.ImGui;
using Inno.Rendering;
using Inno.Rendering.Shaders;
using Widget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using UI = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.ShaderEditor;

internal sealed partial class ShaderEditorCanvas
{
    private void Controls(GraphNodeRecord node)
    {
        if (owner.drawers?.TryDraw(node.definitionId, new(node, owner.serialization, owner.context,
            (key, value, continuous) => SetEncoded(node, key, value, continuous), owner.previews)) == true) return;
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
                UI.TextDisabled("Forwards the complete port type");
                UI.TextWrapped(Read(node, "valueType", new ShaderGraphType { id = "float" }).CreateType().id);
                break;
            case "inno.shader.extract":
                Choice(node, "type", "float4", ["float2", "float3", "float4", "float3x3", "float4x4"]);
                int index = Read(node, "index", 0);
                if (UI.InputInt("##index", ref index) && index >= 0) Set(node, "index", index);
                break;
            case "inno.shader.sample":
                Choice(node, "type", "sampled-texture2d", ["sampled-texture2d", "sampled-texture2d-array", "sampled-texture3d", "sampled-texture-cube"]);
                bool level = Read(node, "explicitLevel", false);
                if (UI.Checkbox("Explicit LOD", ref level)) Set(node, "explicitLevel", level);
                break;
            case "inno.shader.stage-input":
                Input(node);
                break;
            case "inno.shader.source":
                Source(node);
                break;
            case ShaderGraphDocument.outputDefinitionId:
                Output(node);
                break;
            case "inno.shader.storage-load":
            case "inno.shader.storage-store":
            case "inno.shader.storage-atomic-add":
                ShaderGraphType resource = Read(node, "resource", new ShaderGraphType { isStorage = true,
                    storageElement = new() { id = "float4" }, access = RenderStorageAccess.ReadWrite });
                if (UI.Button("Storage Contract…")) UI.OpenPopup("##storage");
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

    private void Scalar(GraphNodeRecord node)
    {
        string type = Read(node, "type", "float");
        if (UI.BeginCombo("##type", type))
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
            UI.EndCombo();
        }
        switch (type)
        {
            case "float":
                float number = Read(node, "value", 0f);
                bool changed = Widget.CompactDragFloat("##value", ref number, 0.01f);
                Gesture();
                if (changed) Set(node, "value", number, true);
                break;
            case "bool":
                bool boolean = Read(node, "value", false);
                if (UI.Checkbox("Value", ref boolean)) Set(node, "value", boolean);
                break;
            case "int":
                int integer = Read(node, "value", 0);
                bool integerChanged = UI.InputInt("##value", ref integer);
                Gesture();
                if (integerChanged) Set(node, "value", integer, true);
                break;
            case "uint":
                string unsigned = Read(node, "value", 0u).ToString(System.Globalization.CultureInfo.InvariantCulture);
                bool unsignedChanged = UI.InputText("##value", ref unsigned, 32, ImGuiInputTextFlags.CharsDecimal);
                Gesture();
                if (unsignedChanged && uint.TryParse(unsigned, out uint value)) Set(node, "value", value, true);
                break;
        }
    }

    private void Source(GraphNodeRecord node)
    {
        Guid selected = Read(node, "sourceId", Guid.Empty);
        string path = Read(node, "sourcePath", "");
        if (UI.BeginCombo("##source", selected == Guid.Empty ? "Choose source function…" : System.IO.Path.GetFileName(path)))
        {
            foreach (AssetFileEntry entry in owner.assets.GetFileSystemEntries(includeDirectories: false)
                .Where(static entry => entry.assetPath.localPath.EndsWith(".ishadersource", StringComparison.OrdinalIgnoreCase)))
                if (UI.Selectable(entry.assetPath.ToString(), owner.AssetId(entry) == selected))
                {
                    using var transaction = owner.interactions.history.BeginTransaction("Assign Shader Function");
                    Set(node, "sourceId", owner.AssetId(entry));
                    Set(node, "sourcePath", entry.assetPath.ToString());
                    transaction.Commit();
                }
            UI.EndCombo();
        }
        Widget.DrawItemTooltip(path);
        if (selected == Guid.Empty) UI.TextDisabled("Ports follow the exported function.");
        else SourceSettings(selected);
        RepairPorts(node);
    }

    private void Input(GraphNodeRecord node)
    {
        ShaderGraphInputSettings input = Read(node, "settings", new ShaderGraphInputSettings());
        string id = input.id;
        bool changed = UI.InputText("##binding", ref id, 256);
        Gesture();
        if (changed) { input.id = id; Set(node, "settings", input, true); }
        if (UI.BeginCombo("##kind", input.kind.ToString()))
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
            UI.EndCombo();
        }
        string type = input.type.id;
        if (input.kind == ShaderIrInputKind.Storage)
        {
            if (UI.Button("Storage Contract…")) UI.OpenPopup("##storage");
            ShaderGraphType resource = input.type;
            if (StoragePopup(ref resource)) { input.type = resource; Set(node, "settings", input); }
        }
        else if (UI.BeginCombo("##type", type))
        {
            foreach (string candidate in new[] { "float", "float2", "float3", "float4", "int", "int2", "int3", "int4", "uint", "uint2", "uint3", "uint4", "bool", "float3x3", "float4x4", "sampled-texture2d", "sampled-texture2d-array", "sampled-texture3d", "sampled-texture-cube" })
                if (UI.Selectable(candidate, type == candidate)) { input.type = new() { id = candidate }; Set(node, "settings", input); }
            UI.EndCombo();
        }
        string semantic = input.semantic;
        bool semanticChanged = UI.InputText("##semantic", ref semantic, 256);
        Gesture();
        if (semanticChanged) { input.semantic = semantic; Set(node, "settings", input, true); }
        Widget.DrawItemTooltip("Stage semantic, not a native expression. Builtins must be supported by the selected stage and target; compute invocation IDs use uint3.");
        int location = input.location;
        bool locationChanged = UI.InputInt("##location", ref location);
        Gesture();
        if (locationChanged && location >= 0) { input.location = location; Set(node, "settings", input, true); }
        if (input.kind is ShaderIrInputKind.Uniform or ShaderIrInputKind.SampledTexture or ShaderIrInputKind.Storage)
        {
            if (UI.Button("Parameter Settings…")) UI.OpenPopup("##parameter");
            ParameterPopup(input);
        }
        if (input.kind == ShaderIrInputKind.SampledTexture)
        {
            bool expanded = draft.expandedPreviews.Contains(node.id);
            if (UI.Checkbox("Preview", ref expanded))
            { if (expanded) draft.expandedPreviews.Add(node.id); else draft.expandedPreviews.Remove(node.id); }
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
        UI.TextDisabled(stage.pass);
        if (UI.Button("Stage & Pass Settings…")) UI.OpenPopup("##stage-settings");
        StageSettingsPopup(node, stage);
        if (stage.stage == ShaderStage.Compute)
        {
            int x = stage.threadsX, y = stage.threadsY, z = stage.threadsZ;
            if (UI.InputInt("X", ref x) && x > 0) { stage.threadsX = x; Set(node, "settings", stage); }
            if (UI.InputInt("Y", ref y) && y > 0) { stage.threadsY = y; Set(node, "settings", stage); }
            if (UI.InputInt("Z", ref z) && z > 0) { stage.threadsZ = z; Set(node, "settings", stage); }
        }
    }

    private void Choice(GraphNodeRecord node, string key, string defaultValue, string[] values)
    {
        string current = Read(node, key, defaultValue);
        if (!UI.BeginCombo("##" + key, current)) return;
        foreach (string candidate in values)
            if (UI.Selectable(candidate, current == candidate)) Set(node, key, candidate);
        UI.EndCombo();
    }

    private void Gesture()
    {
        if (UI.IsItemActivated()) draft.valueGesture = Guid.NewGuid().ToString("N");
    }
}
