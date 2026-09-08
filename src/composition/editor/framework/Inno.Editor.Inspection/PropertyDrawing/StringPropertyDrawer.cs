using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Inspection;

[PropertyDrawer(typeof(string))]
internal sealed class StringPropertyDrawer : IPropertyDrawer
{
    private const nuint C_BUFFER_SIZE = 1024;

    /// <summary>
    /// Renders the value presentation for the current editor frame.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    public void Draw(PropertyDrawContext context)
    {
        string value = context.GetValue() as string ?? string.Empty;
        if (NativeImGui.InputText($"##{context.path}", ref value, C_BUFFER_SIZE, ImGuiInputTextFlags.None))
        {
            context.SetValue(value);
        }
    }
}
