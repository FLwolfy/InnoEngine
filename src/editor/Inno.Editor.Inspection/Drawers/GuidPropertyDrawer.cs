using System;
using System.Collections.Generic;

using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Inspection.Drawers;

[PropertyDrawer(typeof(Guid))]
internal sealed class GuidPropertyDrawer : IPropertyDrawer
{
    private const nuint C_BUFFER_SIZE = 64;
    private static readonly Dictionary<string, string> s_editBuffers = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void Draw(PropertyDrawContext context)
    {
        Guid value = context.GetValue() is Guid current ? current : Guid.Empty;
        string text = s_editBuffers.TryGetValue(context.path, out string? editing)
            ? editing
            : value.ToString("D");
        if (NativeImGui.InputText(
                $"##{context.path}",
                ref text,
                C_BUFFER_SIZE,
                ImGuiInputTextFlags.EnterReturnsTrue) &&
            Guid.TryParse(text, out Guid parsed))
        {
            context.SetValue(parsed);
            s_editBuffers.Remove(context.path);
            return;
        }

        s_editBuffers[context.path] = text;
    }
}
