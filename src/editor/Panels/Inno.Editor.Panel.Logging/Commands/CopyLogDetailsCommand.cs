using Inno.Editor.Interactions;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Logging;

[EditorAction(LogPanelActions.CopyDetails, LogPanelAreas.Entry)]
[EditorMenu(LogPanelAreas.Entry, "Copy Full Entry", order: 110)]
internal sealed class CopyLogDetailsCommand : EditorAction<LogEntryCopyTarget>
{
    protected override void Execute(EditorActionContext<LogEntryCopyTarget> context)
        => NativeImGui.SetClipboardText(context.target.fullText);
}
