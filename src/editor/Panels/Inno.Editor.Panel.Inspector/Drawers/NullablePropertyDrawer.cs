using System;

using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Inspector.Drawers;

[PropertyDrawer(typeof(Nullable<>))]
internal sealed class NullablePropertyDrawer : IPropertyDrawer
{
    /// <inheritdoc />
    public void Draw(PropertyDrawContext context)
    {
        Type underlyingType = Nullable.GetUnderlyingType(context.propertyType)
            ?? throw new InvalidOperationException("Nullable drawer requires a nullable property type.");
        object? value = context.GetValue();
        bool hasValue = value is not null;
        bool previousHasValue = hasValue;
        _ = NativeImGui.Checkbox($"##{context.path}_has_value", ref hasValue);
        if (hasValue != previousHasValue)
        {
            value = hasValue ? Activator.CreateInstance(underlyingType) : null;
            context.SetValue(value);
        }

        if (!hasValue)
        {
            NativeImGui.SameLine();
            NativeImGui.TextUnformatted("Null");
            return;
        }

        NativeImGui.SameLine();
        context.DrawInlineChild(
            "Value",
            underlyingType,
            context.GetValue,
            context.SetValue);
    }
}
