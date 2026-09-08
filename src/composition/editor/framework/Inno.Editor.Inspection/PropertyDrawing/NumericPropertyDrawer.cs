using System;
using System.Globalization;

using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Inspection;

[PropertyDrawer(typeof(byte))]
[PropertyDrawer(typeof(sbyte))]
[PropertyDrawer(typeof(short))]
[PropertyDrawer(typeof(ushort))]
[PropertyDrawer(typeof(int))]
[PropertyDrawer(typeof(uint))]
[PropertyDrawer(typeof(long))]
[PropertyDrawer(typeof(ulong))]
[PropertyDrawer(typeof(float))]
[PropertyDrawer(typeof(double))]
[PropertyDrawer(typeof(decimal))]
internal sealed class NumericPropertyDrawer : IPropertyDrawer
{
    private const nuint C_BUFFER_SIZE = 128;
    private const string C_TEXT_STATE = "numeric";

    /// <summary>
    /// Renders the value presentation for the current editor frame.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    public void Draw(PropertyDrawContext context)
    {
        object? rawValue = context.GetValue();
        Type type = context.propertyType;
        if (type == typeof(int))
        {
            int value = rawValue is int current ? current : 0;
            if (NativeImGui.DragInt($"##{context.path}", ref value, 1f))
            {
                context.SetValue(value);
            }

            return;
        }

        if (type == typeof(float))
        {
            float value = rawValue is float current ? current : 0f;
            if (NativeImGui.DragFloat($"##{context.path}", ref value, 0.1f))
            {
                context.SetValue(value);
            }

            return;
        }

        if (!context.TryGetTextState(C_TEXT_STATE, out string? text))
        {
            text = Convert.ToString(rawValue, CultureInfo.InvariantCulture) ?? "0";
        }

        if (NativeImGui.InputText(
                $"##{context.path}",
                ref text,
                C_BUFFER_SIZE,
                ImGuiInputTextFlags.EnterReturnsTrue))
        {
            if (TryConvert(text, type, out object? converted))
            {
                context.SetValue(converted);
                context.ClearTextState(C_TEXT_STATE);
                return;
            }
        }

        context.SetTextState(C_TEXT_STATE, text);
    }

    private static bool TryConvert(string text, Type type, out object? value)
    {
        try
        {
            value = Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or InvalidCastException)
        {
            value = null;
            return false;
        }
    }
}
