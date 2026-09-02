using Inno.Scripting.Api;
using Inno.Editor.Panel.Inspector;

[assembly: ScriptingApiNamespace(
    "InnoEditor.Inspection",
    "Inno.Editor.Panel.Inspector",
    ScriptingApiScope.Editor)]

[assembly: ScriptingApiExport(typeof(EngineObjectReferenceDropTarget), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(AssetReferenceDropTarget), ScriptingApiScope.Editor)]
