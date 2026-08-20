using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Inspector;

[PropertyDrawer(typeof(string))]
internal sealed class StringPropertyDrawer : IPropertyDrawer
{
    private const nuint C_BUFFER_SIZE = 1024;

    /// <inheritdoc />
    public void Draw(PropertyDrawContext context)
    {
        string value = context.GetValue() as string ?? string.Empty;
        if (NativeImGui.InputText($"##{context.path}", ref value, C_BUFFER_SIZE, ImGuiInputTextFlags.None))
        {
            context.SetValue(value);
        }
    }
}
