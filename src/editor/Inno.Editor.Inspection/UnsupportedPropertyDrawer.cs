using System.Numerics;

using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Inspection;

internal sealed class UnsupportedPropertyDrawer : IPropertyDrawer
{
    internal static UnsupportedPropertyDrawer instance { get; } = new();

    public void Draw(PropertyDrawContext context)
    {
        object? value = context.GetValue();
        NativeImGui.TextColored(
            new Vector4(0.9f, 0.65f, 0.25f, 1f),
            value is null
                ? $"Unsupported {context.propertyType.Name} (null)"
                : $"Unsupported {context.propertyType.Name}: {value}");
    }
}
