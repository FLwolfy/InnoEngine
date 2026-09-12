using System;
using System.Linq;
using Inno.Assets.Pipeline;
using Inno.Core.Graphs;
using Inno.Core.Mathematics;
using Inno.Native.ImGui;
using Inno.Rendering;
using Inno.Rendering.Shaders;
using Widget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using UI = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.ShaderEditor;

internal sealed partial class ShaderEditorCanvas
{
    private bool StoragePopup(ref ShaderGraphType type)
    {
        UI.SetNextWindowSize(new(490, 390), ImGuiCond.Appearing);
        if (!UI.BeginPopup("##storage")) return false;
        bool changed = false;
        try
        {
            bool image = type.isImage;
            if (UI.Checkbox("Storage Image", ref image))
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
                if (UI.Checkbox("Array Layers", ref array)) { type.isArray = array; changed = true; }
            }
            else
            {
                string current = type.storageElement?.id ?? "float4";
                if (UI.BeginCombo("Element", current))
                {
                    foreach (string candidate in new[] { "float", "float2", "float3", "float4", "int", "int2", "int3", "int4", "uint", "uint2", "uint3", "uint4" })
                        if (UI.Selectable(candidate, current == candidate)) { type.storageElement = new() { id = candidate }; changed = true; }
                    UI.EndCombo();
                }
            }
            UI.TextWrapped("Storage resources are bound by the render pass. The graph declares types and access; the Render Graph owns resource lifetime and synchronization.");
        }
        finally { UI.EndPopup(); }
        return changed;
    }

    private void ParameterPopup(ShaderGraphInputSettings input)
    {
        UI.SetNextWindowSize(new(490, 380), ImGuiCond.Appearing);
        if (!UI.BeginPopup("##parameter")) return;
        try
        {
            ShaderDefinition definition = ShaderGraphDocument.ReadDefinition(Controller.document, owner.serialization, owner.context);
            int index = Array.FindIndex(definition.properties, value => value.id.value == input.id);
            if (index < 0) { UI.TextWrapped("Enter a valid binding name and supported material type to declare this parameter."); return; }
            ShaderPropertyDefinition property = definition.properties[index];
            bool changed = false;
            string displayName = property.displayName;
            if (UI.InputText("Display Name", ref displayName, 256)) { property.displayName = displayName; changed = true; }
            Gesture();
            ShaderPropertyBindingOwner bindingOwner = property.bindingOwner;
            if (EnumControl("Bound By", ref bindingOwner)) { property.bindingOwner = bindingOwner; changed = true; }
            UI.SeparatorText("Default Value");
            MaterialValue value = property.defaultValue;
            if (property.bindingKind == ShaderPropertyBindingKind.SampledTexture)
            {
                if (UI.BeginCombo("Texture", value.texture?.assetPath.ToString() ?? "None"))
                {
                    if (UI.Selectable("None", value.texture is null)) { value.kind = MaterialValueKind.Texture; value.texture = null; changed = true; }
                    foreach (AssetFileEntry entry in owner.assets.GetFileSystemEntries(includeDirectories: false))
                        if (owner.assets.TryGetAssetType(entry.assetPath, out Type? type) && type is not null && typeof(TextureAsset).IsAssignableFrom(type)
                            && UI.Selectable(entry.assetPath.ToString(), value.texture?.assetPath == entry.assetPath)
                            && owner.assets.TryLoad(entry.assetPath, out TextureAsset? texture) && texture is not null)
                        { value = MaterialValue.FromTexture(texture); changed = true; }
                    UI.EndCombo();
                }
            }
            else if (property.bindingKind == ShaderPropertyBindingKind.Uniform)
            {
                if (property.type == ShaderPropertyType.Matrix4x4)
                {
                    Matrix matrix = value.kind == MaterialValueKind.Matrix ? value.matrix : Matrix.identity;
                    float[] elements = [matrix.m11, matrix.m12, matrix.m13, matrix.m14, matrix.m21, matrix.m22, matrix.m23, matrix.m24,
                        matrix.m31, matrix.m32, matrix.m33, matrix.m34, matrix.m41, matrix.m42, matrix.m43, matrix.m44];
                    for (int i = 0; i < elements.Length; i++)
                    {
                        UI.SetNextItemWidth(90);
                        if (Widget.CompactDragFloat("##matrix" + i, ref elements[i], 0.01f)) changed = true;
                        Gesture();
                        if (i % 4 != 3) UI.SameLine();
                    }
                    value = MaterialValue.FromMatrix(new(elements[0], elements[1], elements[2], elements[3], elements[4], elements[5], elements[6], elements[7],
                        elements[8], elements[9], elements[10], elements[11], elements[12], elements[13], elements[14], elements[15]));
                }
                else
                {
                    Vector4 vector = value.vector;
                    float[] components = [vector.x, vector.y, vector.z, vector.w];
                    int count = property.type switch { ShaderPropertyType.Float => 1, ShaderPropertyType.Vector2 => 2, ShaderPropertyType.Vector3 => 3, _ => 4 };
                    for (int i = 0; i < count; i++)
                    {
                        UI.SetNextItemWidth(90);
                        if (Widget.CompactDragFloat("##component" + i, ref components[i], 0.01f)) changed = true;
                        Gesture();
                        if (i + 1 < count) UI.SameLine();
                    }
                    value = count == 1 ? MaterialValue.FromFloat(components[0]) : MaterialValue.FromVector(new(components[0], components[1], components[2], components[3]));
                }
            }
            else UI.TextDisabled("Supplied by the render pass.");
            if (changed)
            {
                property.defaultValue = value;
                definition.properties[index] = property;
                CommitDefinition(definition, "Edit Shader Parameter", UI.IsAnyItemActive());
            }
        }
        finally { UI.EndPopup(); }
    }

    private void CommitDefinition(ShaderDefinition definition, string label, bool continuous = false)
    {
        GraphDocument candidate = Controller.document.Clone();
        candidate.SetMetadata(ShaderGraphDocument.definitionKey, ShaderGraphDocument.Encode(owner.serialization.Serialize(definition, owner.context), owner.serialization, owner.context));
        Controller.ReplaceDocument(candidate, label, continuous ? draft.valueGesture : null);
        owner.Changed(draft);
    }
}
