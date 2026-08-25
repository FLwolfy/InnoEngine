using Inno.Editor.Interactions;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Logging;

[EditorAction(LoggingInteractionIds.C_COPY_DETAILS, LoggingInteractionIds.C_ENTRY_AREA)]
[EditorMenu(LoggingInteractionIds.C_ENTRY_AREA, "Copy Full Entry", order: 110)]
internal sealed class CopyConsoleDetailsCommand : EditorAction<ConsoleEntryCopyTarget>
{
    protected override void Execute(EditorActionContext<ConsoleEntryCopyTarget> context)
        => NativeImGui.SetClipboardText(context.target.fullText);
}
