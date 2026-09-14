using System;
using System.Linq;
using Inno.Core.Graphs;
using Inno.Native.ImGui;
using Inno.Rendering;
using Inno.Rendering.Shaders;
using UI = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.ShaderEditor;

internal sealed partial class ShaderEditorCanvas
{
    private void StageSettingsPopup(GraphNodeRecord node, ShaderGraphStageSettings stage)
    {
        UI.SetNextWindowSize(new(540, 600), ImGuiCond.Appearing);
        if (!UI.BeginPopup("##stage-settings")) return;
        try
        {
            ShaderDefinition definition = ShaderGraphDocument.ReadDefinition(Controller.document, owner.serialization, owner.context);
            bool definitionChanged = false;
            UI.SeparatorText("Shader");
            string name = definition.name;
            bool nameChanged = false;
            InspectorRow("settings.name", "Name", () => nameChanged = UI.InputText("##name", ref name, 256));
            if (nameChanged) { definition.name = name; definitionChanged = true; }
            Gesture();
            UI.SeparatorText(stage.stage + " Interface");
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
                        if (UI.Selectable(available.name, activePass == available.name)) { activePass = available.name; draft.inspectedPass = activePass; }
                }
                finally { UI.EndCombo(); }
            });
            ShaderGraphPassProgram assignment = programs.FirstOrDefault(program => program.pass == activePass);
            bool assigned = assignment.stages?.Contains(node.id.value, StringComparer.Ordinal) == true;
            bool assignmentChanged = false;
            if (activePass.Length != 0)
                InspectorRow("settings.shared-stage", "Shared Stage", () => assignmentChanged = UI.Checkbox("##shared-stage", ref assigned));
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
                UI.PushID(i);
                ShaderGraphOutput output = stage.outputs[i];
                bool changed = false;
                string id = output.id;
                bool idChanged = false;
                InspectorRow("output.id", "Port ID", () => idChanged = UI.InputText("##port-id", ref id, 128));
                if (idChanged) { output.id = id; changed = true; }
                Gesture();
                ShaderIrOutputKind kind = output.kind;
                if (EnumControl("Destination", ref kind)) { output.kind = kind; changed = true; }
                string semantic = output.semantic ?? "";
                bool semanticChanged = false;
                InspectorRow("output.semantic", "Semantic", () => semanticChanged = UI.InputText("##semantic", ref semantic, 128));
                if (semanticChanged) { output.semantic = semantic; changed = true; }
                Gesture();
                int location = output.location;
                bool locationChanged = false;
                InspectorRow("output.location", "Location", () => locationChanged = UI.InputInt("##location", ref location));
                if (locationChanged) { output.location = location; changed = true; }
                Gesture();
                if (changed) { stage.outputs[i] = output; Set(node, "settings", stage, true); }
                if (UI.SmallButton("Remove Output"))
                {
                    stage.outputs = stage.outputs.Where((_, index) => index != i).ToArray();
                    Set(node, "settings", stage);
                    UI.PopID();
                    break;
                }
                UI.Separator();
                UI.PopID();
            }
            if (UI.Button("Add Output"))
            {
                stage.outputs = [.. stage.outputs, new() { id = "output-" + Guid.NewGuid().ToString("N"),
                    kind = stage.stage == ShaderStage.Vertex ? ShaderIrOutputKind.Varying : ShaderIrOutputKind.Color,
                    semantic = "texcoord", location = stage.outputs.Length }];
                Set(node, "settings", stage);
            }
            int passIndex = Array.FindIndex(definition.passes, pass => pass.name == activePass);
            if (passIndex >= 0 && UI.CollapsingHeader("Pass State"))
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
                InspectorRow("state.depth-write", "Depth Write", () => depthWriteChanged = UI.Checkbox("##depth-write", ref depthWrite));
                InspectorRow("state.multisampling", "Multisampling", () => multisamplingChanged = UI.Checkbox("##multisampling", ref multisampling));
                if (depthWriteChanged) { state.depthWrite = depthWrite; changed = true; }
                if (multisamplingChanged) { state.multisampling = multisampling; changed = true; }
                InspectorRow("state.blend-preset", "Blend Preset", () =>
                {
                    if (!Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget.BeginBoundedCombo("##blend-preset", state.blend.enabled ? "Blended" : "Opaque")) return;
                    try
                    {
                        if (UI.Selectable("Opaque")) { state.blend = RenderBlendState.opaque; changed = true; }
                        if (UI.Selectable("Alpha")) { state.blend = RenderBlendState.alpha; changed = true; }
                        if (UI.Selectable("Premultiplied")) { state.blend = RenderBlendState.premultiplied; changed = true; }
                        if (UI.Selectable("Additive")) { state.blend = RenderBlendState.additive; changed = true; }
                    }
                    finally { UI.EndCombo(); }
                });
                int mask = state.colorWriteMask;
                bool maskChanged = false;
                InspectorRow("state.write-mask", "RGBA Write Mask", () => maskChanged = UI.InputInt("##write-mask", ref mask));
                if (maskChanged) { state.colorWriteMask = (byte)Math.Clamp(mask, 0, 15); changed = true; }
                RenderBlendState blend = state.blend;
                if (UI.TreeNode("Custom Blending"))
                {
                    bool enabled = blend.enabled;
                    bool enabledChanged = false;
                    InspectorRow("blend.enabled", "Enabled", () => enabledChanged = UI.Checkbox("##enabled", ref enabled));
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
                    UI.TreePop();
                }
                state.blend = blend;
                GraphicsCapability features = pass.requiredFeatures;
                if (FeatureControls(ref features)) { pass.requiredFeatures = features; changed = true; }
                if (changed) { pass.renderState = state; definition.passes[passIndex] = pass; definitionChanged = true; }
            }
            definitionChanged |= TechniqueControls(definition);
            if (UI.CollapsingHeader("Variants"))
            {
                for (int i = 0; i < definition.keywords.Length; i++)
                {
                    UI.PushID("keyword-" + i);
                    ShaderKeywordDefinition keyword = definition.keywords[i];
                    string id = keyword.id;
                    bool keywordChanged = false;
                    InspectorRow("variant.keyword", "Keyword", () => keywordChanged = UI.InputText("##keyword", ref id, 128));
                    if (keywordChanged) { keyword.id = id; definitionChanged = true; }
                    Gesture();
                    for (int j = 0; j < keyword.options.Length; j++)
                    {
                        string option = keyword.options[j];
                        bool optionChanged = false, remove = false;
                        InspectorRow("variant.option", "Option " + (j + 1), () =>
                        {
                            optionChanged = UI.InputText("##option", ref option, 128);
                            UI.SameLine();
                            remove = UI.SmallButton("Remove");
                        });
                        if (optionChanged) { keyword.options[j] = option; definitionChanged = true; }
                        Gesture();
                        if (remove) { keyword.options = keyword.options.Where((_, index) => index != j).ToArray(); definitionChanged = true; break; }
                    }
                    if (UI.SmallButton("Add Option")) { keyword.options = [.. keyword.options, "Option" + keyword.options.Length]; definitionChanged = true; }
                    definition.keywords[i] = keyword;
                    if (UI.SmallButton("Remove Keyword")) { definition.keywords = definition.keywords.Where((_, index) => index != i).ToArray(); definitionChanged = true; UI.PopID(); break; }
                    UI.PopID();
                }
                if (UI.Button("Add Keyword")) { definition.keywords = [.. definition.keywords, new("Keyword" + definition.keywords.Length, ["Off", "On"])]; definitionChanged = true; }
            }
            if (definitionChanged)
            {
                GraphDocument candidate = Controller.document.Clone();
                candidate.SetMetadata(ShaderGraphDocument.definitionKey, ShaderGraphDocument.Encode(owner.serialization.Serialize(definition, owner.context), owner.serialization, owner.context));
                Controller.ReplaceDocument(candidate, "Edit Shader Settings", UI.IsAnyItemActive() ? draft.valueGesture : null);
                owner.Changed(draft);
            }
        }
        finally { UI.EndPopup(); }
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
                    if (UI.Selectable(candidate.ToString(), candidate.Equals(current))) { current = candidate; changed = true; }
            }
            finally { UI.EndCombo(); }
        });
        value = current;
        return changed;
    }
}
