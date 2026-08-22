using Inno.Editor.Interactions;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Logging;

[EditorAction(ConsolePanelActions.CopyDetails, ConsolePanelAreas.Entry)]
[EditorMenu(ConsolePanelAreas.Entry, "Copy Full Entry", order: 110)]
internal sealed class CopyConsoleDetailsCommand : EditorAction<ConsoleEntryCopyTarget>
{
    protected override void Execute(EditorActionContext<ConsoleEntryCopyTarget> context)
        => NativeImGui.SetClipboardText(context.target.fullText);
}
