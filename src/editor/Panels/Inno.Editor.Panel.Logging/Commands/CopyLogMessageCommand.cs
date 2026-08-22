using Inno.Editor.Interactions;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Logging;

[EditorAction(LogPanelActions.CopyMessage, LogPanelAreas.Entry)]
[EditorMenu(LogPanelAreas.Entry, "Copy Message", order: 100)]
internal sealed class CopyLogMessageCommand : EditorAction<LogEntryCopyTarget>
{
    protected override void Execute(EditorActionContext<LogEntryCopyTarget> context)
        => NativeImGui.SetClipboardText(context.target.message);
}
