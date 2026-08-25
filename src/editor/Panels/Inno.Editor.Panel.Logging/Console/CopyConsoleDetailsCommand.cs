using Inno.Editor.Interactions;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Logging;

[EditorAction("console/copy-details", "panel/diagnostics.console/entry")]
[EditorMenu("panel/diagnostics.console/entry", "Copy Full Entry", order: 110)]
internal sealed class CopyConsoleDetailsCommand : EditorAction<ConsoleEntryCopyTarget>
{
    protected override void Execute(EditorActionContext<ConsoleEntryCopyTarget> context)
        => NativeImGui.SetClipboardText(context.target.fullText);
}
