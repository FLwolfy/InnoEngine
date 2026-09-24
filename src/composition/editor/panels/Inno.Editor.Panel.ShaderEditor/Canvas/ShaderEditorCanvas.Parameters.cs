using System;
using System.Linq;
using Inno.Core.Graphs;
using Inno.Native.ImGui;
using Inno.Rendering;
using Inno.Rendering.Shaders;
using Inno.Editor.Shaders;
using EditorImGui = Inno.Editor.ImGui.ImGui;
using Widget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using ImGuiApi = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.ShaderEditor;

internal sealed partial class ShaderEditorCanvas
{
    private bool StoragePopup(ref ShaderGraphType type)
    {
        ImGuiApi.SetNextWindowSize(new(490, 390), ImGuiCond.Appearing);
        if (!ImGuiApi.BeginPopup("##storage")) return false;
        bool changed = false;
        try
        {
            bool image = type.isImage;
            bool imageChanged = false;
            InspectorRow("storage.image", "Storage Image", () => imageChanged = ImGuiApi.Checkbox("##image", ref image));
            if (imageChanged)
            {
                type.isImage = image;
                type.isStorage = true;
                type.storageElement = image ? null : new() { id = "float4" };
                type.format = RenderTextureFormat.RGBA16Float;
                changed = true;
            }
            RenderStorageAccess access = type.access;
            if (EnumControl("Access", ref access)) { type.access = access; changed = true; }
            if (type.isImage)
            {
                RenderTextureFormat format = type.format;
                if (EnumControl("Format", ref format)) { type.format = format; changed = true; }
                RenderTextureDimension dimension = type.dimension;
                if (EnumControl("Dimension", ref dimension)) { type.dimension = dimension; changed = true; }
                bool array = type.isArray;
                bool arrayChanged = false;
                InspectorRow("storage.array", "Array Layers", () => arrayChanged = ImGuiApi.Checkbox("##array", ref array));
                if (arrayChanged) { type.isArray = array; changed = true; }
            }
            else
            {
                string current = type.storageElement?.id ?? "float4";
                string selected = current;
                InspectorRow("storage.element", "Element", () =>
                {
                    if (!Widget.BeginBoundedCombo("##element", current)) return;
                    try
                    {
                        foreach (string candidate in new[] { "float", "float2", "float3", "float4", "int", "int2", "int3", "int4", "uint", "uint2", "uint3", "uint4" })
                            if (ImGuiApi.Selectable(candidate, current == candidate)) selected = candidate;
                    }
                    finally { ImGuiApi.EndCombo(); }
                });
                if (selected != current) { type.storageElement = new() { id = selected }; changed = true; }
            }
            ImGuiApi.TextWrapped("Storage resources are bound by the render pass. The graph declares types and access; the Render Graph owns resource lifetime and synchronization.");
        }
        finally { ImGuiApi.EndPopup(); }
        return changed;
    }

    private void DrawParameter(ShaderGraphInputSettings input)
    {
        if (m_inspection is null) return;
        ShaderDefinition definition = ShaderGraphDocument.ReadDefinition(Controller.document, owner.serialization, owner.context);
        int index = Array.FindIndex(definition.properties, value => value.id.value == input.id);
        if (index < 0) { Widget.Hint("Enter a binding name and supported type to declare this parameter."); return; }
        ShaderPropertyDefinition property = definition.properties[index];
        bool parameterOpen = Widget.SectionHeader("Parameter", "The stable binding ID identifies overrides. Shader defaults and Material overrides are edited independently.");
        if (parameterOpen)
        {
            string displayName = property.displayName;
            bool changed = false;
            InspectorRow("parameter.display-name", "Display Name", () => changed = EditorImGui.InputText("##display-name", ref displayName, 256));
            Gesture();
            if (changed) { property.displayName = displayName; Save(property, true); }
            ShaderPropertyBindingOwner bindingOwner = property.bindingOwner;
            if (EnumControl("Bound By", ref bindingOwner)) { property.bindingOwner = bindingOwner; Save(property, false); }
            if (property.type is ShaderPropertyType.Vector4 or ShaderPropertyType.Color)
            {
                bool color = property.type == ShaderPropertyType.Color;
                InspectorRow("parameter.color", "Color", () =>
                {
                    if (ImGuiApi.Checkbox("##color", ref color))
                    {
                        property.type = color ? ShaderPropertyType.Color : ShaderPropertyType.Vector4;
                        MaterialValue converted = property.defaultValue;
                        converted.kind = color ? MaterialValueKind.Color : MaterialValueKind.Vector;
                        property.defaultValue = converted;
                        Save(property, false);
                    }
                });
            }
        }
        if (property.bindingOwner != ShaderPropertyBindingOwner.Material)
        {
            if (parameterOpen) Widget.Hint("Supplied by the Render Pass. This binding is read-only in Material Inspectors.");
            return;
        }
        if (property.bindingKind is not (ShaderPropertyBindingKind.Uniform or ShaderPropertyBindingKind.SampledTexture))
        {
            if (parameterOpen) Widget.Hint("Storage resources require a Render Pass owner.");
            return;
        }
        ShaderParameterPresentation presentation = ShaderParameterPresentation.Read(Controller.document, property.id, owner.serialization, owner.context);
        if (!Widget.SectionHeader("Material Inspector", "Presentation belongs only to this Shader's authoring graph. It does not modify existing Material values or enter the Player."))
            return;
        string group = presentation.group;
        bool groupChanged = false;
        InspectorRow("parameter.group", "Group", () => groupChanged = EditorImGui.InputText("##group", ref group, 256));
        if (groupChanged) { presentation.group = group; SavePresentation(true); }
        Gesture();
        string description = presentation.description;
        bool descriptionChanged = false;
        InspectorRow("parameter.description", "Description", () => descriptionChanged = EditorImGui.InputText("##description", ref description, 2048));
        if (descriptionChanged) { presentation.description = description; SavePresentation(true); }
        Gesture();
        bool visible = presentation.visible;
        InspectorRow("parameter.visible", "Visible in Material", () =>
        {
            if (ImGuiApi.Checkbox("##visible", ref visible)) { presentation.visible = visible; SavePresentation(false); }
        });
        if (property.type == ShaderPropertyType.Float)
        {
            bool range = presentation.hasRange;
            InspectorRow("parameter.range", "Range", () =>
            {
                if (ImGuiApi.Checkbox("##range", ref range)) { presentation.hasRange = range; SavePresentation(false); }
            });
            if (range)
            {
                m_inspection.properties.DrawValue(m_inspection.editorContext, draft, "shader.parameter." + property.id.value + ".minimum",
                    "Minimum", typeof(double), () => presentation.minimum, value =>
                    {
                        if (!double.IsFinite((double)value!)) return;
                        presentation.minimum = Math.Clamp((double)value!, -float.MaxValue, presentation.maximum);
                        SavePresentation(true);
                    }, new ParameterEdits(this), draft.readOnly, minimum: -float.MaxValue, maximum: presentation.maximum);
                m_inspection.properties.DrawValue(m_inspection.editorContext, draft, "shader.parameter." + property.id.value + ".maximum",
                    "Maximum", typeof(double), () => presentation.maximum, value =>
                    {
                        if (!double.IsFinite((double)value!)) return;
                        presentation.maximum = Math.Clamp((double)value!, presentation.minimum, float.MaxValue);
                        SavePresentation(true);
                    }, new ParameterEdits(this), draft.readOnly, minimum: presentation.minimum, maximum: float.MaxValue);
                Widget.Hint("Editing bounds only · existing defaults and overrides are not clamped on display");
            }
        }
        ShaderPropertyDefinition shown = property;
        shown.displayName = "Default";
        ShaderPropertyInspector.Draw(m_inspection, draft, "shader.parameter." + property.id.value, shown, property.defaultValue,
            value => { property.defaultValue = value; Save(property, true); }, new ParameterEdits(this), draft.readOnly, presentation);
        void SavePresentation(bool continuous)
        {
            Gesture();
            GraphDocument candidate = Controller.document.Clone();
            ShaderParameterPresentation.Write(candidate, property.id, presentation, owner.serialization, owner.context);
            Controller.ReplaceDocument(candidate, "Edit Parameter Presentation", continuous && ImGuiApi.IsAnyItemActive() ? draft.valueGesture : null);
            owner.Changed(draft);
        }
        void Save(ShaderPropertyDefinition value, bool continuous)
        {
            definition.properties[index] = value;
            CommitDefinition(definition, "Edit Shader Parameter", continuous && ImGuiApi.IsAnyItemActive());
        }
    }

    private sealed class ParameterEdits(ShaderEditorCanvas canvas) : Inno.Editor.Inspection.IInspectionPropertyEditService
    {
        public bool ChangeProperty(object owner, string propertyName, Action mutation, string historyName)
        {
            canvas.Gesture();
            mutation();
            return true;
        }
    }

    private void CommitDefinition(ShaderDefinition definition, string label, bool continuous = false)
    {
        GraphDocument candidate = Controller.document.Clone();
        candidate.SetMetadata(ShaderGraphDocument.definitionKey, ShaderGraphDocument.Encode(owner.serialization.Serialize(definition, owner.context), owner.serialization, owner.context));
        Controller.ReplaceDocument(candidate, label, continuous ? draft.valueGesture : null);
        owner.Changed(draft);
    }
}
