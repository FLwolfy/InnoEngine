using Inno.Core.Scripting;
using Inno.Editor.ImGui;

[assembly: ScriptingApiNamespace(
    "InnoEditor.ImGui",
    "Inno.Editor.ImGui",
    ScriptingApiScope.Editor)]

[assembly: ScriptingApiExport(typeof(EditorPalette), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorStyleMetrics), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(ImGuiWidget), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(InlineRenameResult), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(TreeNodeOptions), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(TreeNodeResult), ScriptingApiScope.Editor)]
