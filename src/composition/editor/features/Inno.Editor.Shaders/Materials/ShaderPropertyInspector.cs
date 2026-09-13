using System;
using Inno.Core.Mathematics;
using Inno.Editor.Inspection;
using Inno.Rendering;

namespace Inno.Editor.Shaders;

/// <summary>Uses the shared Inspector drawers for both Shader defaults and Material overrides.</summary>
public static class ShaderPropertyInspector
{
    /// <summary>Draws a typed value without choosing its persistence or history policy.</summary>
    /// <param name="context">Current Inspector invocation.</param>
    /// <param name="owner">Detached draft owner used for widget identity.</param>
    /// <param name="path">Stable draft property path.</param>
    /// <param name="property">Current Shader declaration.</param>
    /// <param name="value">Current default or override, never mutated.</param>
    /// <param name="setter">Writes only the host's detached draft.</param>
    /// <param name="edits">Host-owned gesture and history service.</param>
    /// <param name="readOnly">Whether this source is editable.</param>
    /// <param name="presentation">Optional Editor-only description and scalar bounds; it never changes inherited or stored values.</param>
    public static void Draw(InspectionDrawContext context, object owner, string path, ShaderPropertyDefinition property,
        MaterialValue value, Action<MaterialValue> setter, IInspectionPropertyEditService edits, bool readOnly,
        ShaderParameterPresentation? presentation = null)
    {
        context.properties.DrawValue(context.editorContext, owner, path, property.displayName, ValueType(property.type),
            () => Unbox(property.type, value), next => setter(Box(property.type, next, value)), edits, readOnly,
            hdrColor: property.type == ShaderPropertyType.Color, tooltip: presentation?.description,
            minimum: property.type == ShaderPropertyType.Float && presentation?.hasRange == true ? presentation.minimum : null,
            maximum: property.type == ShaderPropertyType.Float && presentation?.hasRange == true ? presentation.maximum : null);
        if (property.type == ShaderPropertyType.Color)
            Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget.Hint("Linear RGBA · HDR values are preserved");
        if (property.bindingKind == ShaderPropertyBindingKind.SampledTexture)
            context.properties.DrawValue(context.editorContext, owner, path + ".sampler", "Sampler", typeof(RenderSamplerState),
                () => value.sampler, next => { var candidate = value; candidate.kind = MaterialValueKind.Texture;
                    candidate.sampler = (RenderSamplerState)next!; setter(candidate); }, edits, readOnly);
    }

    /// <summary>Tests whether a stored override has the exact representation expected by a declaration.</summary>
    /// <param name="type">Declared material type.</param>
    /// <param name="kind">Stored representation.</param>
    /// <returns>True only for supported matching kinds; no lossy conversion is performed.</returns>
    public static bool Compatible(ShaderPropertyType type, MaterialValueKind kind) => type switch
    {
        ShaderPropertyType.Float => kind == MaterialValueKind.Float,
        ShaderPropertyType.Vector2 or ShaderPropertyType.Vector3 or ShaderPropertyType.Vector4 => kind == MaterialValueKind.Vector,
        ShaderPropertyType.Color => kind == MaterialValueKind.Color,
        ShaderPropertyType.Matrix4x4 => kind == MaterialValueKind.Matrix,
        ShaderPropertyType.Texture2D or ShaderPropertyType.Texture2DArray or ShaderPropertyType.Texture3D or ShaderPropertyType.TextureCube => kind == MaterialValueKind.Texture,
        _ => false
    };

    /// <summary>Applies the edited components of a representative value without replacing unrelated mixed components.</summary>
    /// <param name="type">Common exact Shader property type.</param>
    /// <param name="before">Representative value before the control gesture sample.</param>
    /// <param name="edited">Representative value after this sample.</param>
    /// <param name="target">Another selected Material's current effective value.</param>
    /// <returns>A detached value preserving the target's untouched channels, texture or sampler.</returns>
    public static MaterialValue ApplyEdit(ShaderPropertyType type, MaterialValue before, MaterialValue edited, MaterialValue target)
    {
        if (!Compatible(type, before.kind) || !Compatible(type, edited.kind) || !Compatible(type, target.kind))
            throw new ArgumentException("A multi-Material edit requires exactly matching value kinds.");
        if (type == ShaderPropertyType.Float) return edited;
        if (edited.kind == MaterialValueKind.Texture)
        {
            if (!ReferenceEquals(before.texture, edited.texture)) target.texture = edited.texture;
            target.sampler = new(
                before.sampler.filter == edited.sampler.filter ? target.sampler.filter : edited.sampler.filter,
                before.sampler.addressU == edited.sampler.addressU ? target.sampler.addressU : edited.sampler.addressU,
                before.sampler.addressV == edited.sampler.addressV ? target.sampler.addressV : edited.sampler.addressV,
                before.sampler.addressW == edited.sampler.addressW ? target.sampler.addressW : edited.sampler.addressW);
        }
        else if (edited.kind == MaterialValueKind.Matrix)
        {
            Matrix b = before.matrix, e = edited.matrix, t = target.matrix;
            target.matrix = new(
                Merge(b.m11,e.m11,t.m11), Merge(b.m12,e.m12,t.m12), Merge(b.m13,e.m13,t.m13), Merge(b.m14,e.m14,t.m14),
                Merge(b.m21,e.m21,t.m21), Merge(b.m22,e.m22,t.m22), Merge(b.m23,e.m23,t.m23), Merge(b.m24,e.m24,t.m24),
                Merge(b.m31,e.m31,t.m31), Merge(b.m32,e.m32,t.m32), Merge(b.m33,e.m33,t.m33), Merge(b.m34,e.m34,t.m34),
                Merge(b.m41,e.m41,t.m41), Merge(b.m42,e.m42,t.m42), Merge(b.m43,e.m43,t.m43), Merge(b.m44,e.m44,t.m44));
        }
        else
        {
            Vector4 b = before.vector, e = edited.vector, t = target.vector;
            target.vector = new(Merge(b.x,e.x,t.x), Merge(b.y,e.y,t.y), Merge(b.z,e.z,t.z), Merge(b.w,e.w,t.w));
        }
        return target;
        static float Merge(float previous, float next, float current)
            => BitConverter.SingleToUInt32Bits(previous) == BitConverter.SingleToUInt32Bits(next) ? current : next;
    }

    private static Type ValueType(ShaderPropertyType type) => type switch
    {
        ShaderPropertyType.Float => typeof(float), ShaderPropertyType.Vector2 => typeof(Vector2),
        ShaderPropertyType.Vector3 => typeof(Vector3), ShaderPropertyType.Vector4 => typeof(Vector4),
        ShaderPropertyType.Color => typeof(Color), ShaderPropertyType.Matrix4x4 => typeof(Matrix),
        ShaderPropertyType.Texture2D or ShaderPropertyType.Texture2DArray or ShaderPropertyType.Texture3D or ShaderPropertyType.TextureCube => typeof(TextureAsset),
        _ => throw new InvalidOperationException("No material editor exists for " + type)
    };

    private static object? Unbox(ShaderPropertyType type, MaterialValue value) => type switch
    {
        ShaderPropertyType.Float => value.vector.x, ShaderPropertyType.Vector2 => new Vector2(value.vector.x, value.vector.y),
        ShaderPropertyType.Vector3 => new Vector3(value.vector.x, value.vector.y, value.vector.z), ShaderPropertyType.Vector4 => value.vector,
        ShaderPropertyType.Color => new Color(value.vector.x, value.vector.y, value.vector.z, value.vector.w),
        ShaderPropertyType.Matrix4x4 => value.matrix, _ => value.texture
    };

    private static MaterialValue Box(ShaderPropertyType type, object? value, MaterialValue previous) => type switch
    {
        ShaderPropertyType.Float => MaterialValue.FromFloat((float)value!),
        ShaderPropertyType.Vector2 => MaterialValue.FromVector(new Vector4(((Vector2)value!).x, ((Vector2)value!).y, 0, 0)),
        ShaderPropertyType.Vector3 => MaterialValue.FromVector(new Vector4(((Vector3)value!).x, ((Vector3)value!).y, ((Vector3)value!).z, 0)),
        ShaderPropertyType.Vector4 => MaterialValue.FromVector((Vector4)value!), ShaderPropertyType.Color => MaterialValue.FromColor((Color)value!),
        ShaderPropertyType.Matrix4x4 => MaterialValue.FromMatrix((Matrix)value!),
        _ => new MaterialValue { kind = MaterialValueKind.Texture, texture = (TextureAsset?)value, sampler = previous.sampler }
    };
}
