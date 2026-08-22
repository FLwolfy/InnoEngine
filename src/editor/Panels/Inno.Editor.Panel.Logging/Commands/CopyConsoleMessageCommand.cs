using Inno.Editor.Interactions;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Logging;

[EditorAction(ConsolePanelActions.CopyMessage, ConsolePanelAreas.Entry)]
[EditorMenu(ConsolePanelAreas.Entry, "Copy Message", order: 100)]
internal sealed class CopyConsoleMessageCommand : EditorAction<ConsoleEntryCopyTarget>
{
    protected override void Execute(EditorActionContext<ConsoleEntryCopyTarget> context)
        => NativeImGui.SetClipboardText(context.target.message);
}
