using Inno.Core.Scripting;
using Inno.Editor.Inspection;

[assembly: ScriptingApiNamespace(
    "InnoEditor.Inspection",
    "Inno.Editor.Inspection",
    ScriptingApiScope.Editor)]

[assembly: ScriptingApiExport(typeof(IInspectorDrawer), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(IPropertyDrawer), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(InspectorDrawContext), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(InspectorDrawerAttribute), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(PropertyDrawContext), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(PropertyDrawerAttribute), ScriptingApiScope.Editor)]

[assembly: ScriptingGlobalUsing(
    "InnoEditor.Inspection",
    ScriptingApiScope.Editor)]
