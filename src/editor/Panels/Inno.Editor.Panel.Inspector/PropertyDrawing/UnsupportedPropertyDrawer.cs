using Inno.Editor.ImGui;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;

namespace Inno.Editor.Panel.Inspector;

internal sealed class UnsupportedPropertyDrawer : IPropertyDrawer
{
    internal static UnsupportedPropertyDrawer instance { get; } = new();

    public void Draw(PropertyDrawContext context)
    {
        object? value = context.GetValue();
        EditorWidget.ColoredText(
            EditorPalette.warning,
            value is null
                ? $"Unsupported {context.propertyType.Name} (null)"
                : $"Unsupported {context.propertyType.Name}: {value}");
    }
}
