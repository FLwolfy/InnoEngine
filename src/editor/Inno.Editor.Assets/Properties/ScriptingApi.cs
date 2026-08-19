using Inno.Core.Scripting;
using Inno.Editor.Assets;
using Inno.Editor.Assets.AssetEditors;
using Inno.Editor.Assets.DragDrop;
using Inno.Editor.Assets.Selection;

[assembly: ScriptingApiNamespace(
    "InnoEditor.Assets",
    "Inno.Editor.Assets",
    ScriptingApiScope.Editor)]
[assembly: ScriptingApiNamespace(
    "InnoEditor.Assets",
    "Inno.Editor.Assets.AssetEditors",
    ScriptingApiScope.Editor)]
[assembly: ScriptingApiNamespace(
    "InnoEditor.Assets",
    "Inno.Editor.Assets.Selection",
    ScriptingApiScope.Editor)]
[assembly: ScriptingApiNamespace(
    "InnoEditor.DragDrop",
    "Inno.Editor.Assets.DragDrop",
    ScriptingApiScope.Editor)]

[assembly: ScriptingApiExport(typeof(AssetEditor), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(AssetEditorAttribute), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(AssetEditorContext), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(AssetOperationValidation), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(AssetBrowserState), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(AssetSelectionTarget), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(AssetSurface), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(AssetActionIds), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(AssetDragSource), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(AssetDirectoryDropTarget), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(AssetReferenceDropTarget), ScriptingApiScope.Editor)]
