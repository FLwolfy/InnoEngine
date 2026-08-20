using Inno.Editor.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Inspector;

internal sealed class UnsupportedPropertyDrawer : IPropertyDrawer
{
    internal static UnsupportedPropertyDrawer instance { get; } = new();

    public void Draw(PropertyDrawContext context)
    {
        object? value = context.GetValue();
        NativeImGui.TextColored(
            EditorPalette.warning,
            value is null
                ? $"Unsupported {context.propertyType.Name} (null)"
                : $"Unsupported {context.propertyType.Name}: {value}");
    }
}
