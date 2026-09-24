using System;
using System.Linq;
using Inno.Core.Graphs;
using Inno.Native.ImGui;
using Inno.Rendering;
using Inno.Rendering.Shaders;
using EditorImGui = Inno.Editor.ImGui.ImGui;
using ImGuiApi = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.ShaderEditor;

internal sealed partial class ShaderEditorCanvas
{
    private void StageSettingsPopup(GraphNodeRecord node, ShaderGraphStageSettings stage)
    {
        ImGuiApi.SetNextWindowSize(new(540, 600), ImGuiCond.Appearing);
        if (!ImGuiApi.BeginPopup("##stage-settings")) return;
        try
        {
            ShaderDefinition definition = ShaderGraphDocument.ReadDefinition(Controller.document, owner.serialization, owner.context);
            bool definitionChanged = false;
            ImGuiApi.SeparatorText("Shader");
            string name = definition.name;
            bool nameChanged = false;
            InspectorRow("settings.name", "Name", () => nameChanged = EditorImGui.InputText("##name", ref name, 256));
            if (nameChanged) { definition.name = name; definitionChanged = true; }
            Gesture();
            ImGuiApi.SeparatorText(stage.stage + " Interface");
            ShaderGraphPassProgram[] programs = ShaderGraphPrograms.Read(Controller.document, owner.serialization, owner.context);
            string activePass = draft.inspectedPass;
            if (!definition.passes.Any(pass => pass.name == activePass))
                activePass = programs.FirstOrDefault(program => program.stages.Contains(node.id.value, StringComparer.Ordinal)).pass
                    ?? definition.passes.FirstOrDefault().name ?? "";
            InspectorRow("settings.pass", "Pass State", () =>
            {
                if (!Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget.BeginBoundedCombo("##pass-state", activePass)) return;
                try
                {
                    foreach (ShaderPassDefinition available in definition.passes)
                        if (ImGuiApi.Selectable(available.name, activePass == available.name)) { activePass = available.name; draft.inspectedPass = activePass; }
                }
                finally { ImGuiApi.EndCombo(); }
            });
            ShaderGraphPassProgram assignment = programs.FirstOrDefault(program => program.pass == activePass);
            bool assigned = assignment.stages?.Contains(node.id.value, StringComparer.Ordinal) == true;
            bool assignmentChanged = false;
            if (activePass.Length != 0)
                InspectorRow("settings.shared-stage", "Shared Stage", () => assignmentChanged = ImGuiApi.Checkbox("##shared-stage", ref assigned));
            if (assignmentChanged)
            {
                var stageIds = (assignment.stages ?? []).Where(id => id != node.id.value).Select(static id => new GraphNodeId(id)).ToList();
                if (assigned)
                {
                    stageIds.RemoveAll(id => Controller.document.FindNode(id) is GraphNodeRecord existing
                        && Read(existing, "settings", new ShaderGraphStageSettings()).stage == stage.stage);
                    stageIds.Add(node.id);
                }
                Controller.ReplaceDocument(ShaderGraphPrograms.Bind(Controller.document, activePass, stageIds, owner.serialization, owner.context), "Assign Shared Shader Stage");
                owner.Changed(draft);
            }
            for (int i = 0; i < stage.outputs.Length; i++)
            {
                ImGuiApi.PushID(i);
                ShaderGraphOutput output = stage.outputs[i];
                bool changed = false;
                string id = output.id;
                bool idChanged = false;
                InspectorRow("output.id", "Port ID", () => idChanged = EditorImGui.InputText("##port-id", ref id, 128));
                if (idChanged) { output.id = id; changed = true; }
                Gesture();
                ShaderIrOutputKind kind = output.kind;
                if (EnumControl("Destination", ref kind)) { output.kind = kind; changed = true; }
                string semantic = output.semantic ?? "";
                bool semanticChanged = false;
                InspectorRow("output.semantic", "Semantic", () => semanticChanged = EditorImGui.InputText("##semantic", ref semantic, 128));
                if (semanticChanged) { output.semantic = semantic; changed = true; }
                Gesture();
                int location = output.location;
                bool locationChanged = false;
                InspectorRow("output.location", "Location", () => locationChanged = ImGuiApi.InputInt("##location", ref location));
                if (locationChanged) { output.location = location; changed = true; }
                Gesture();
                if (changed) { stage.outputs[i] = output; Set(node, "settings", stage, true); }
                if (ImGuiApi.SmallButton("Remove Output"))
                {
                    stage.outputs = stage.outputs.Where((_, index) => index != i).ToArray();
                    Set(node, "settings", stage);
                    ImGuiApi.PopID();
                    break;
                }
                ImGuiApi.Separator();
                ImGuiApi.PopID();
            }
            if (CenteredAddButton("Add Output"))
            {
                stage.outputs = [.. stage.outputs, new() { id = "output-" + Guid.NewGuid().ToString("N"),
                    kind = stage.stage == ShaderStage.Vertex ? ShaderIrOutputKind.Varying : ShaderIrOutputKind.Color,
                    semantic = "texcoord", location = stage.outputs.Length }];
                Set(node, "settings", stage);
            }
            int passIndex = Array.FindIndex(definition.passes, pass => pass.name == activePass);
            if (passIndex >= 0 && ImGuiApi.CollapsingHeader("Pass State"))
            {
                ShaderPassDefinition pass = definition.passes[passIndex];
                ShaderRenderState state = pass.renderState;
                bool changed = false;
                RenderPrimitiveTopology topology = state.topology;
                if (EnumControl("Topology", ref topology)) { state.topology = topology; changed = true; }
                ShaderCullMode cull = state.cull;
                if (EnumControl("Cull", ref cull)) { state.cull = cull; changed = true; }
                RenderFrontFace front = state.frontFace;
                if (EnumControl("Front Face", ref front)) { state.frontFace = front; changed = true; }
                ShaderCompareFunction depth = state.depthCompare;
                if (EnumControl("Depth Test", ref depth)) { state.depthCompare = depth; changed = true; }
                bool depthWrite = state.depthWrite, multisampling = state.multisampling;
                bool depthWriteChanged = false, multisamplingChanged = false;
                InspectorRow("state.depth-write", "Depth Write", () => depthWriteChanged = ImGuiApi.Checkbox("##depth-write", ref depthWrite));
                InspectorRow("state.multisampling", "Multisampling", () => multisamplingChanged = ImGuiApi.Checkbox("##multisampling", ref multisampling));
                if (depthWriteChanged) { state.depthWrite = depthWrite; changed = true; }
                if (multisamplingChanged) { state.multisampling = multisampling; changed = true; }
                InspectorRow("state.blend-preset", "Blend Preset", () =>
                {
                    if (!Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget.BeginBoundedCombo("##blend-preset", state.blend.enabled ? "Blended" : "Opaque")) return;
                    try
                    {
                        if (ImGuiApi.Selectable("Opaque")) { state.blend = RenderBlendState.opaque; changed = true; }
                        if (ImGuiApi.Selectable("Alpha")) { state.blend = RenderBlendState.alpha; changed = true; }
                        if (ImGuiApi.Selectable("Premultiplied")) { state.blend = RenderBlendState.premultiplied; changed = true; }
                        if (ImGuiApi.Selectable("Additive")) { state.blend = RenderBlendState.additive; changed = true; }
                    }
                    finally { ImGuiApi.EndCombo(); }
                });
                int mask = state.colorWriteMask;
                bool maskChanged = false;
                InspectorRow("state.write-mask", "RGBA Write Mask", () => maskChanged = ImGuiApi.InputInt("##write-mask", ref mask));
                if (maskChanged) { state.colorWriteMask = (byte)Math.Clamp(mask, 0, 15); changed = true; }
                RenderBlendState blend = state.blend;
                if (ImGuiApi.TreeNode("Custom Blending"))
                {
                    bool enabled = blend.enabled;
                    bool enabledChanged = false;
                    InspectorRow("blend.enabled", "Enabled", () => enabledChanged = ImGuiApi.Checkbox("##enabled", ref enabled));
                    if (enabledChanged) { blend.enabled = enabled; changed = true; }
                    RenderBlendFactor colorSource = blend.colorSource, colorDestination = blend.colorDestination,
                        alphaSource = blend.alphaSource, alphaDestination = blend.alphaDestination;
                    RenderBlendEquation colorEquation = blend.colorEquation, alphaEquation = blend.alphaEquation;
                    if (EnumControl("RGB Source", ref colorSource)) { blend.colorSource = colorSource; changed = true; }
                    if (EnumControl("RGB Destination", ref colorDestination)) { blend.colorDestination = colorDestination; changed = true; }
                    if (EnumControl("RGB Equation", ref colorEquation)) { blend.colorEquation = colorEquation; changed = true; }
                    if (EnumControl("Alpha Source", ref alphaSource)) { blend.alphaSource = alphaSource; changed = true; }
                    if (EnumControl("Alpha Destination", ref alphaDestination)) { blend.alphaDestination = alphaDestination; changed = true; }
                    if (EnumControl("Alpha Equation", ref alphaEquation)) { blend.alphaEquation = alphaEquation; changed = true; }
                    ImGuiApi.TreePop();
                }
                state.blend = blend;
                GraphicsCapability features = pass.requiredFeatures;
                if (FeatureControls(ref features)) { pass.requiredFeatures = features; changed = true; }
                if (changed) { pass.renderState = state; definition.passes[passIndex] = pass; definitionChanged = true; }
            }
            definitionChanged |= TechniqueControls(definition);
            if (ImGuiApi.CollapsingHeader("Variants"))
            {
                for (int i = 0; i < definition.keywords.Length; i++)
                {
                    ImGuiApi.PushID("keyword-" + i);
                    ShaderKeywordDefinition keyword = definition.keywords[i];
                    string id = keyword.id;
                    bool keywordChanged = false;
                    InspectorRow("variant.keyword", "Keyword", () => keywordChanged = EditorImGui.InputText("##keyword", ref id, 128));
                    if (keywordChanged) { keyword.id = id; definitionChanged = true; }
                    Gesture();
                    for (int j = 0; j < keyword.options.Length; j++)
                    {
                        string option = keyword.options[j];
                        bool optionChanged = false, remove = false;
                        InspectorRow("variant.option", "Option " + (j + 1), () =>
                        {
                            optionChanged = EditorImGui.InputText("##option", ref option, 128);
                            ImGuiApi.SameLine();
                            remove = ImGuiApi.SmallButton("Remove");
                        });
                        if (optionChanged) { keyword.options[j] = option; definitionChanged = true; }
                        Gesture();
                        if (remove) { keyword.options = keyword.options.Where((_, index) => index != j).ToArray(); definitionChanged = true; break; }
                    }
                    if (CenteredAddButton("Add Option")) { keyword.options = [.. keyword.options, "Option" + keyword.options.Length]; definitionChanged = true; }
                    definition.keywords[i] = keyword;
                    if (ImGuiApi.SmallButton("Remove Keyword")) { definition.keywords = definition.keywords.Where((_, index) => index != i).ToArray(); definitionChanged = true; ImGuiApi.PopID(); break; }
                    ImGuiApi.PopID();
                }
                if (CenteredAddButton("Add Keyword")) { definition.keywords = [.. definition.keywords, new("Keyword" + definition.keywords.Length, ["Off", "On"])]; definitionChanged = true; }
            }
            if (definitionChanged)
            {
                GraphDocument candidate = Controller.document.Clone();
                candidate.SetMetadata(ShaderGraphDocument.definitionKey, ShaderGraphDocument.Encode(owner.serialization.Serialize(definition, owner.context), owner.serialization, owner.context));
                Controller.ReplaceDocument(candidate, "Edit Shader Settings", ImGuiApi.IsAnyItemActive() ? draft.valueGesture : null);
                owner.Changed(draft);
            }
        }
        finally { ImGuiApi.EndPopup(); }
    }

    private bool EnumControl<T>(string label, ref T value) where T : struct, Enum
    {
        bool changed = false;
        T current = value;
        InspectorRow("enum." + label, label, () =>
        {
            if (!Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget.BeginBoundedCombo("##enum", current.ToString())) return;
            try
            {
                foreach (T candidate in Enum.GetValues<T>())
                    if (ImGuiApi.Selectable(candidate.ToString(), candidate.Equals(current))) { current = candidate; changed = true; }
            }
            finally { ImGuiApi.EndCombo(); }
        });
        value = current;
        return changed;
    }
}
