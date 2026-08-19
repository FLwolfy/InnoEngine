using System;

using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Scene.Inspection.Drawers;

[PropertyDrawer(typeof(Enum), useForChildren: true, priority: 100)]
internal sealed class EnumPropertyDrawer : IPropertyDrawer
{
    /// <inheritdoc />
    public void Draw(PropertyDrawContext context)
    {
        Type enumType = context.propertyType;
        object value = context.GetValue() ?? Activator.CreateInstance(enumType)!;
        if (enumType.IsDefined(typeof(FlagsAttribute), inherit: false))
        {
            DrawFlags(context, enumType, value);
            return;
        }

        string preview = Enum.GetName(enumType, value) ?? value.ToString() ?? "Unknown";
        if (!NativeImGui.BeginCombo($"##{context.path}", preview))
        {
            return;
        }

        Array values = Enum.GetValues(enumType);
        for (int i = 0; i < values.Length; i++)
        {
            object candidate = values.GetValue(i)!;
            string name = Enum.GetName(enumType, candidate) ?? candidate.ToString()!;
            if (NativeImGui.Selectable(name, Equals(candidate, value)))
            {
                context.SetValue(candidate);
            }
        }

        NativeImGui.EndCombo();
    }

    private static void DrawFlags(PropertyDrawContext context, Type enumType, object value)
    {
        ulong currentBits = ToBits(enumType, value);
        string preview = value.ToString() ?? currentBits.ToString();
        if (!NativeImGui.BeginCombo($"##{context.path}", preview))
        {
            return;
        }

        Array values = Enum.GetValues(enumType);
        for (int i = 0; i < values.Length; i++)
        {
            object candidate = values.GetValue(i)!;
            ulong candidateBits = ToBits(enumType, candidate);
            bool selected = candidateBits == 0
                ? currentBits == 0
                : (currentBits & candidateBits) == candidateBits;
            string name = Enum.GetName(enumType, candidate) ?? candidate.ToString()!;
            if (!NativeImGui.Selectable(name, selected, ImGuiSelectableFlags.NoAutoClosePopups))
            {
                continue;
            }

            currentBits = candidateBits == 0
                ? 0
                : selected
                    ? currentBits & ~candidateBits
                    : currentBits | candidateBits;
            context.SetValue(Enum.ToObject(enumType, currentBits));
        }

        NativeImGui.EndCombo();
    }

    private static ulong ToBits(Type enumType, object value)
    {
        Type underlyingType = Enum.GetUnderlyingType(enumType);
        return Type.GetTypeCode(underlyingType) switch
        {
            TypeCode.SByte => unchecked((ulong)Convert.ToSByte(value)),
            TypeCode.Int16 => unchecked((ulong)Convert.ToInt16(value)),
            TypeCode.Int32 => unchecked((ulong)Convert.ToInt32(value)),
            TypeCode.Int64 => unchecked((ulong)Convert.ToInt64(value)),
            TypeCode.Byte => Convert.ToByte(value),
            TypeCode.UInt16 => Convert.ToUInt16(value),
            TypeCode.UInt32 => Convert.ToUInt32(value),
            TypeCode.UInt64 => Convert.ToUInt64(value),
            _ => throw new InvalidOperationException($"Unsupported enum underlying type '{underlyingType.FullName}'.")
        };
    }
}
