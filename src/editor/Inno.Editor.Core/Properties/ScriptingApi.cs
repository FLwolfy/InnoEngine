using Inno.Core.Scripting;
using Inno.Editor.Core;

[assembly: ScriptingApiNamespace(
    "InnoEditor.Core",
    "Inno.Editor.Core",
    ScriptingApiScope.Editor)]

[assembly: ScriptingApiExport(typeof(AssetSelectionTarget), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorContext), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorPanel), ScriptingApiScope.Editor)]
