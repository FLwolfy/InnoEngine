using System;
using System.Text;

using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.ImGui;

internal static unsafe class ImGuiUtf8Buffer
{
    internal static bool InputText(
        string label,
        string? hint,
        ref string value,
        nuint capacity,
        ImGuiInputTextFlags flags)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        if (capacity is 0 or > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        byte[] buffer = new byte[(int)capacity];
        Encoding.UTF8.GetEncoder().Convert(
            value.AsSpan(),
            buffer.AsSpan(0, buffer.Length - 1),
            flush: true,
            out _,
            out int bytesUsed,
            out _);
        buffer[bytesUsed] = 0;

        bool changed;
        fixed (byte* nativeBuffer = buffer)
        {
            changed = hint is null
                ? NativeImGui.InputText(label, nativeBuffer, capacity, flags)
                : NativeImGui.InputTextWithHint(label, hint, nativeBuffer, capacity, flags);
        }

        if (changed)
        {
            int terminator = Array.IndexOf(buffer, (byte)0);
            value = Encoding.UTF8.GetString(buffer, 0, terminator < 0 ? buffer.Length : terminator);
        }
        return changed;
    }
}
