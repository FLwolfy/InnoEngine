using System;
using EditorImGui = Inno.Editor.ImGui.ImGui;
using Inno.Native.ImGui;

namespace Inno.Editor.Inspection;

[PropertyDrawer(typeof(Guid))]
internal sealed class GuidPropertyDrawer : IPropertyDrawer
{
    private const int C_BUFFER_SIZE = 64;
    private const string C_TEXT_STATE = "guid";

    /// <summary>
    /// Renders the value presentation for the current editor frame.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    public void Draw(PropertyDrawContext context)
    {
        Guid value = context.GetValue() is Guid current ? current : Guid.Empty;
        string text = context.TryGetTextState(C_TEXT_STATE, out string? editing)
            ? editing!
            : value.ToString("D");
        if (EditorImGui.InputText(
                $"##{context.path}",
                ref text,
                C_BUFFER_SIZE,
                ImGuiInputTextFlags.EnterReturnsTrue) &&
            Guid.TryParse(text, out Guid parsed))
        {
            context.SetValue(parsed);
            context.ClearTextState(C_TEXT_STATE);
            return;
        }

        context.SetTextState(C_TEXT_STATE, text);
    }
}
