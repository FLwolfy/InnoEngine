using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Inspection;

[PropertyDrawer(typeof(bool))]
internal sealed class BooleanPropertyDrawer : IPropertyDrawer
{
    /// <summary>
    /// Renders the value presentation for the current editor frame.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    public void Draw(PropertyDrawContext context)
    {
        bool value = context.GetValue() is bool current && current;
        if (NativeImGui.Checkbox($"##{context.path}", ref value))
        {
            context.SetValue(value);
        }
    }
}
