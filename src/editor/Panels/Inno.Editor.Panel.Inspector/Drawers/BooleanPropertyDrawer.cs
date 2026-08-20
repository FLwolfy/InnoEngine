using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Inspector.Drawers;

[PropertyDrawer(typeof(bool))]
internal sealed class BooleanPropertyDrawer : IPropertyDrawer
{
    /// <inheritdoc />
    public void Draw(PropertyDrawContext context)
    {
        bool value = context.GetValue() is bool current && current;
        if (NativeImGui.Checkbox($"##{context.path}", ref value))
        {
            context.SetValue(value);
        }
    }
}
