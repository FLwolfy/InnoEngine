using Inno.Editor.Interactions;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Logging;

[EditorAction("console/copy-message", "panel/diagnostics.console/entry")]
[EditorMenu("panel/diagnostics.console/entry", "Copy Message", order: 100)]
internal sealed class CopyConsoleMessageCommand : EditorAction<ConsoleEntryCopyTarget>
{
    protected override void Execute(EditorActionContext<ConsoleEntryCopyTarget> context)
        => NativeImGui.SetClipboardText(context.target.message);
}
