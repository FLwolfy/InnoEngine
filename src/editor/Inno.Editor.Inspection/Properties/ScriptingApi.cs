using Inno.Core.Scripting;
using Inno.Editor.Inspection;

[assembly: ScriptingApiNamespace(
    "InnoEditor.Inspection",
    "Inno.Editor.Inspection",
    ScriptingApiScope.Editor)]

[assembly: ScriptingApiExport(typeof(InspectionDrawer<>), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(IPropertyDrawer), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(InspectionDrawContext), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(InspectionDrawerAttribute), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(PropertyDrawContext), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(PropertyDrawerAttribute), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(IInspectionPropertyEditService), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(SerializedPropertyRenderer), ScriptingApiScope.Editor)]
