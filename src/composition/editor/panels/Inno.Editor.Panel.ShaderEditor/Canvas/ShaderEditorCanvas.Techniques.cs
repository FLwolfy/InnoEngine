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
            if (UI.InputText("ID", ref id, 128)) { technique.id = new() { value = id }; changed = true; }
            Gesture();
            if (UI.InputText("Contract", ref contract, 256)) { technique.contract = new() { value = contract }; changed = true; }
            Gesture();
            GraphicsCapability features = technique.requiredFeatures;
            if (FeatureControls(ref features)) { technique.requiredFeatures = features; changed = true; }
            for (int j = 0; j < technique.passes.Length; j++)
            {
                UI.PushID(j);
                ShaderTechniquePass mapping = technique.passes[j];
                string role = mapping.role.value ?? "";
                if (UI.InputText("Role", ref role, 128)) { mapping.role = new() { value = role }; changed = true; }
                Gesture();
                if (UI.BeginCombo("Pass", mapping.passName))
                {
                    foreach (ShaderPassDefinition pass in definition.passes)
                        if (UI.Selectable(pass.name, pass.name == mapping.passName)) { mapping.passName = pass.name; changed = true; }
                    UI.EndCombo();
                }
                technique.passes[j] = mapping;
                if (UI.SmallButton("Remove Role"))
                { technique.passes = technique.passes.Where((_, index) => index != j).ToArray(); changed = true; UI.PopID(); break; }
                UI.PopID();
            }
            if (UI.SmallButton("Add Role"))
            {
                technique.passes = [.. technique.passes, new() { role = new("role-" + technique.passes.Length), passName = definition.passes.FirstOrDefault().name ?? "" }];
                changed = true;
            }
            definition.techniques[i] = technique;
            UI.SameLine();
            if (UI.SmallButton("Remove Technique"))
            { definition.techniques = definition.techniques.Where((_, index) => index != i).ToArray(); changed = true; UI.PopID(); break; }
            UI.Separator();
            UI.PopID();
        }
        if (UI.Button("Add Technique"))
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
        foreach (GraphicsCapability feature in Enum.GetValues<GraphicsCapability>())
        {
            ulong bits = Convert.ToUInt64(feature);
            if (bits == 0 || (bits & (bits - 1)) != 0) continue;
            bool enabled = (features & feature) != 0;
            if (UI.Checkbox(feature.ToString(), ref enabled))
            { features = enabled ? features | feature : features & ~feature; changed = true; }
        }
        UI.TreePop();
        return changed;
    }
}
