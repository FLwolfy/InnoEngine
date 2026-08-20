using Inno.Core.Scripting;
using Inno.Editor.Panel.FileBrowser;
using Inno.Editor.Panel.FileBrowser.AssetEditors;
using Inno.Editor.Panel.FileBrowser.Selection;

[assembly: ScriptingApiNamespace(
    "InnoEditor.Assets",
    "Inno.Editor.Panel.FileBrowser",
    ScriptingApiScope.Editor)]
[assembly: ScriptingApiNamespace(
    "InnoEditor.Assets",
    "Inno.Editor.Panel.FileBrowser.AssetEditors",
    ScriptingApiScope.Editor)]
[assembly: ScriptingApiNamespace(
    "InnoEditor.Assets",
    "Inno.Editor.Panel.FileBrowser.Selection",
    ScriptingApiScope.Editor)]

[assembly: ScriptingApiExport(typeof(AssetEditor), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(AssetEditorAttribute), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(AssetEditorContext), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(AssetOperationValidation), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(AssetBrowserState), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(FileBrowserAreas), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(FileBrowserActions), ScriptingApiScope.Editor)]
