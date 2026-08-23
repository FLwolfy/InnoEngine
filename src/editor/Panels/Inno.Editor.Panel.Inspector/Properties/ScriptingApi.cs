using Inno.Core.Scripting;
using Inno.Editor.Panel.Inspector;

[assembly: ScriptingApiNamespace(
    "InnoEditor.Inspection",
    "Inno.Editor.Panel.Inspector",
    ScriptingApiScope.Editor)]

[assembly: ScriptingApiExport(typeof(InspectorAreas), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(InspectorActions), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EngineObjectReferenceDropTarget), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(AssetReferenceDropTarget), ScriptingApiScope.Editor)]
