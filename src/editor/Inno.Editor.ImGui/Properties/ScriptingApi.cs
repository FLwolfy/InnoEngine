using Inno.Scripting.Api;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using Inno.Native.ImGui;
using Inno.Platform.Sdl3.ImGui;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;

[assembly: ScriptingApiNamespace(
    "InnoEditor.ImGui",
    "Inno.Editor.ImGui",
    ScriptingApiScope.Editor)]
[assembly: ScriptingApiNamespace(
    "InnoEditor.ImGui",
    "Inno.Editor.ImGui.ImGuiWidget",
    ScriptingApiScope.Editor)]
[assembly: ScriptingApiNamespace(
    "InnoEditor.ImGui",
    "Inno.Native.ImGui",
    ScriptingApiScope.Editor)]
[assembly: ScriptingApiNamespace(
    "InnoEditor.ImGui",
    "Inno.Platform.Sdl3.ImGui",
    ScriptingApiScope.Editor)]

[assembly: ScriptingApiExport(typeof(EditorPalette), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorStyleMetrics), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(Inno.Editor.ImGui.ImGui), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorWidget), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(InlineRenameResult), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(TreeNodeDrawContext), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(TreeNodeOptions), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(TreeNodeResult), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(ImGuiIcon), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(ImGuiChildFlags), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(ImGuiCol), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(ImGuiColorEditFlags), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(ImGuiComboFlags), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(ImGuiHoveredFlags), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(ImGuiInputTextFlags), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(ImGuiMouseButton), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(ImGuiPopupFlags), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(ImGuiSelectableFlags), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(ImGuiSliderFlags), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(ImGuiStyleVar), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(ImGuiTabBarFlags), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(ImGuiTabItemFlags), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(ImGuiTableColumnFlags), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(ImGuiTableFlags), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(ImGuiTreeNodeFlags), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(ImGuiWindowFlags), ScriptingApiScope.Editor)]
