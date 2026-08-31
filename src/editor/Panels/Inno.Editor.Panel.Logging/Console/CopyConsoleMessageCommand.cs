using Inno.Editor.Interactions;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Logging;

[EditorAction(LoggingInteractionIds.C_COPY_MESSAGE, LoggingInteractionIds.C_ENTRY_AREA)]
[EditorMenu(LoggingInteractionIds.C_ENTRY_AREA, "Copy Message", order: 100)]
internal sealed class CopyConsoleMessageCommand : EditorAction<ConsoleEntryCopyTarget>
{
    protected override void Execute(EditorActionContext<ConsoleEntryCopyTarget> context)
        => NativeImGui.SetClipboardText(context.target.message);
}
