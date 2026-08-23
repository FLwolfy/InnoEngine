using System;

using Inno.Core.Mathematics;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using NativeImGui = Inno.Native.ImGui.ImGui;
using NumericsVector4 = System.Numerics.Vector4;

namespace Inno.Editor.Inspection;

internal static class ComponentFieldDrawer
{
    internal static float GetWidth(int count)
    {
        float spacing = NativeImGui.GetStyle().ItemSpacing.X;
        return MathF.Max(
            EditorWidget.style.vectorFieldMinimumWidth,
            (NativeImGui.GetContentRegionAvail().X - spacing * (count - 1)) / count);
    }

    internal static bool Float(string path, string label, ref float value, float width)
    {
        return EditorWidget.AxisDragFloat(path, label, ref value, width);
    }

    internal static bool Int(string path, string label, ref int value, float width)
    {
        return EditorWidget.AxisDragInt(path, label, ref value, width);
    }

    internal static void Next() => NativeImGui.SameLine();
}

[PropertyDrawer(typeof(Vector2))]
internal sealed class Vector2PropertyDrawer : IPropertyDrawer
{
    /// <inheritdoc />
    public void Draw(PropertyDrawContext context)
    {
        Vector2 value = context.GetValue() is Vector2 current ? current : default;
        float width = ComponentFieldDrawer.GetWidth(2);
        bool changed = ComponentFieldDrawer.Float(context.path, "X", ref value.x, width);
        ComponentFieldDrawer.Next();
        changed |= ComponentFieldDrawer.Float(context.path, "Y", ref value.y, width);
        if (changed)
        {
            context.SetValue(value);
        }
    }
}

[PropertyDrawer(typeof(Vector3))]
internal sealed class Vector3PropertyDrawer : IPropertyDrawer
{
    /// <inheritdoc />
    public void Draw(PropertyDrawContext context)
    {
        Vector3 value = context.GetValue() is Vector3 current ? current : default;
        float width = ComponentFieldDrawer.GetWidth(3);
        bool changed = ComponentFieldDrawer.Float(context.path, "X", ref value.x, width);
        ComponentFieldDrawer.Next();
        changed |= ComponentFieldDrawer.Float(context.path, "Y", ref value.y, width);
        ComponentFieldDrawer.Next();
        changed |= ComponentFieldDrawer.Float(context.path, "Z", ref value.z, width);
        if (changed)
        {
            context.SetValue(value);
        }
    }
}

[PropertyDrawer(typeof(Vector4))]
internal sealed class Vector4PropertyDrawer : IPropertyDrawer
{
    /// <inheritdoc />
    public void Draw(PropertyDrawContext context)
    {
        Vector4 value = context.GetValue() is Vector4 current ? current : default;
        float width = ComponentFieldDrawer.GetWidth(4);
        bool changed = ComponentFieldDrawer.Float(context.path, "X", ref value.x, width);
        ComponentFieldDrawer.Next();
        changed |= ComponentFieldDrawer.Float(context.path, "Y", ref value.y, width);
        ComponentFieldDrawer.Next();
        changed |= ComponentFieldDrawer.Float(context.path, "Z", ref value.z, width);
        ComponentFieldDrawer.Next();
        changed |= ComponentFieldDrawer.Float(context.path, "W", ref value.w, width);
        if (changed)
        {
            context.SetValue(value);
        }
    }
}

[PropertyDrawer(typeof(Vector2Int))]
internal sealed class Vector2IntPropertyDrawer : IPropertyDrawer
{
    /// <inheritdoc />
    public void Draw(PropertyDrawContext context)
    {
        Vector2Int value = context.GetValue() is Vector2Int current ? current : default;
        float width = ComponentFieldDrawer.GetWidth(2);
        bool changed = ComponentFieldDrawer.Int(context.path, "X", ref value.x, width);
        ComponentFieldDrawer.Next();
        changed |= ComponentFieldDrawer.Int(context.path, "Y", ref value.y, width);
        if (changed)
        {
            context.SetValue(value);
        }
    }
}

[PropertyDrawer(typeof(Vector3Int))]
internal sealed class Vector3IntPropertyDrawer : IPropertyDrawer
{
    /// <inheritdoc />
    public void Draw(PropertyDrawContext context)
    {
        Vector3Int value = context.GetValue() is Vector3Int current ? current : default;
        float width = ComponentFieldDrawer.GetWidth(3);
        bool changed = ComponentFieldDrawer.Int(context.path, "X", ref value.x, width);
        ComponentFieldDrawer.Next();
        changed |= ComponentFieldDrawer.Int(context.path, "Y", ref value.y, width);
        ComponentFieldDrawer.Next();
        changed |= ComponentFieldDrawer.Int(context.path, "Z", ref value.z, width);
        if (changed)
        {
            context.SetValue(value);
        }
    }
}

[PropertyDrawer(typeof(Vector4Int))]
internal sealed class Vector4IntPropertyDrawer : IPropertyDrawer
{
    /// <inheritdoc />
    public void Draw(PropertyDrawContext context)
    {
        Vector4Int value = context.GetValue() is Vector4Int current ? current : default;
        float width = ComponentFieldDrawer.GetWidth(4);
        bool changed = ComponentFieldDrawer.Int(context.path, "X", ref value.x, width);
        ComponentFieldDrawer.Next();
        changed |= ComponentFieldDrawer.Int(context.path, "Y", ref value.y, width);
        ComponentFieldDrawer.Next();
        changed |= ComponentFieldDrawer.Int(context.path, "Z", ref value.z, width);
        ComponentFieldDrawer.Next();
        changed |= ComponentFieldDrawer.Int(context.path, "W", ref value.w, width);
        if (changed)
        {
            context.SetValue(value);
        }
    }
}

[PropertyDrawer(typeof(Rect))]
internal sealed class RectPropertyDrawer : IPropertyDrawer
{
    /// <inheritdoc />
    public void Draw(PropertyDrawContext context)
    {
        Rect value = context.GetValue() is Rect current ? current : default;
        float width = ComponentFieldDrawer.GetWidth(4);
        bool changed = ComponentFieldDrawer.Float(context.path, "X", ref value.x, width);
        ComponentFieldDrawer.Next();
        changed |= ComponentFieldDrawer.Float(context.path, "Y", ref value.y, width);
        ComponentFieldDrawer.Next();
        changed |= ComponentFieldDrawer.Float(context.path, "W", ref value.width, width);
        ComponentFieldDrawer.Next();
        changed |= ComponentFieldDrawer.Float(context.path, "H", ref value.height, width);
        if (changed)
        {
            context.SetValue(value);
        }
    }
}

[PropertyDrawer(typeof(RectInt))]
internal sealed class RectIntPropertyDrawer : IPropertyDrawer
{
    /// <inheritdoc />
    public void Draw(PropertyDrawContext context)
    {
        RectInt value = context.GetValue() is RectInt current ? current : default;
        float width = ComponentFieldDrawer.GetWidth(4);
        bool changed = ComponentFieldDrawer.Int(context.path, "X", ref value.x, width);
        ComponentFieldDrawer.Next();
        changed |= ComponentFieldDrawer.Int(context.path, "Y", ref value.y, width);
        ComponentFieldDrawer.Next();
        changed |= ComponentFieldDrawer.Int(context.path, "W", ref value.width, width);
        ComponentFieldDrawer.Next();
        changed |= ComponentFieldDrawer.Int(context.path, "H", ref value.height, width);
        if (changed)
        {
            context.SetValue(value);
        }
    }
}

[PropertyDrawer(typeof(Color))]
internal sealed class ColorPropertyDrawer : IPropertyDrawer
{
    /// <inheritdoc />
    public void Draw(PropertyDrawContext context)
    {
        Color value = context.GetValue() is Color current ? current : default;
        var nativeValue = new NumericsVector4(value.r, value.g, value.b, value.a);
        if (NativeImGui.ColorEdit4($"##{context.path}", ref nativeValue))
        {
            context.SetValue(new Color(nativeValue.X, nativeValue.Y, nativeValue.Z, nativeValue.W));
        }
    }
}

[PropertyDrawer(typeof(Quaternion))]
internal sealed class QuaternionPropertyDrawer : IPropertyDrawer
{
    /// <inheritdoc />
    public void Draw(PropertyDrawContext context)
    {
        Quaternion value = context.GetValue() is Quaternion current ? current : Quaternion.identity;
        Vector3 eulerDegrees = value.ToEulerAnglesXYZDegrees();
        float width = ComponentFieldDrawer.GetWidth(3);
        bool changed = ComponentFieldDrawer.Float(context.path, "X", ref eulerDegrees.x, width);
        ComponentFieldDrawer.Next();
        changed |= ComponentFieldDrawer.Float(context.path, "Y", ref eulerDegrees.y, width);
        ComponentFieldDrawer.Next();
        changed |= ComponentFieldDrawer.Float(context.path, "Z", ref eulerDegrees.z, width);
        if (changed)
        {
            context.SetValue(Quaternion.FromEulerAnglesXYZDegrees(eulerDegrees).normalized);
        }
    }
}
