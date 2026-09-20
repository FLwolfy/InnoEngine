using System;
using System.Linq;
using Inno.Rendering;
using UI = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.ShaderEditor;

internal sealed partial class ShaderEditorCanvas
{
    private bool TechniqueControls(ShaderDefinition definition)
    {
        if (!UI.CollapsingHeader("Techniques & Roles")) return false;
        UI.TextWrapped("Techniques map pipeline-defined roles to this shader's passes. Contract and role identities are supplied by the rendering pipeline.");
        bool changed = false;
        for (int i = 0; i < definition.techniques.Length; i++)
        {
            UI.PushID("technique-" + i);
            ShaderTechniqueDefinition technique = definition.techniques[i];
            string id = technique.id.value ?? "", contract = technique.contract.value ?? "";
            bool idChanged = false;
            InspectorRow("technique.id", "ID", () => idChanged = UI.InputText("##id", ref id, 128));
            if (idChanged) { technique.id = new() { value = id }; changed = true; }
            Gesture();
            bool contractChanged = false;
            InspectorRow("technique.contract", "Contract", () => contractChanged = UI.InputText("##contract", ref contract, 256));
            if (contractChanged) { technique.contract = new() { value = contract }; changed = true; }
            Gesture();
            GraphicsCapability features = technique.requiredFeatures;
            if (FeatureControls(ref features)) { technique.requiredFeatures = features; changed = true; }
            for (int j = 0; j < technique.passes.Length; j++)
            {
                UI.PushID(j);
                ShaderTechniquePass mapping = technique.passes[j];
                string role = mapping.role.value ?? "";
                bool roleChanged = false;
                InspectorRow("technique.role", "Role", () => roleChanged = UI.InputText("##role", ref role, 128));
                if (roleChanged) { mapping.role = new() { value = role }; changed = true; }
                Gesture();
                bool passChanged = false;
                InspectorRow("technique.pass", "Pass", () =>
                {
                    if (!Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget.BeginBoundedCombo("##pass", mapping.passName)) return;
                    try
                    {
                        foreach (ShaderPassDefinition pass in definition.passes)
                            if (UI.Selectable(pass.name, pass.name == mapping.passName)) { mapping.passName = pass.name; passChanged = true; }
                    }
                    finally { UI.EndCombo(); }
                });
                changed |= passChanged;
                technique.passes[j] = mapping;
                if (UI.SmallButton("Remove Role"))
                { technique.passes = technique.passes.Where((_, index) => index != j).ToArray(); changed = true; UI.PopID(); break; }
                UI.PopID();
            }
            if (CenteredAddButton("Add Role"))
            {
                technique.passes = [.. technique.passes, new() { role = new("role-" + technique.passes.Length), passName = definition.passes.FirstOrDefault().name ?? "" }];
                changed = true;
            }
            definition.techniques[i] = technique;
            if (UI.SmallButton("Remove Technique"))
            { definition.techniques = definition.techniques.Where((_, index) => index != i).ToArray(); changed = true; UI.PopID(); break; }
            UI.Separator();
            UI.PopID();
        }
        if (CenteredAddButton("Add Technique"))
        {
            definition.techniques = [.. definition.techniques, new() { id = new("technique-" + Guid.NewGuid().ToString("N")), contract = default, passes = [] }];
            changed = true;
        }
        return changed;
    }

    private static bool FeatureControls(ref GraphicsCapability features)
    {
        if (!UI.TreeNode("Required Capabilities")) return false;
        bool changed = false;
        GraphicsCapability updated = features;
        foreach (GraphicsCapability feature in Enum.GetValues<GraphicsCapability>())
        {
            ulong bits = Convert.ToUInt64(feature);
            if (bits == 0 || (bits & (bits - 1)) != 0) continue;
            bool enabled = (updated & feature) != 0;
            bool featureChanged = false;
            InspectorRow("feature." + feature, feature.ToString(), () => featureChanged = UI.Checkbox("##enabled", ref enabled));
            if (featureChanged)
            { updated = enabled ? updated | feature : updated & ~feature; changed = true; }
        }
        features = updated;
        UI.TreePop();
        return changed;
    }
}
