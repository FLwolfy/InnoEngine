using Inno.Scripting.Api;
using Inno.Editor.Panel.Hierarchy;

[assembly: ScriptingApiNamespace(
    "InnoEditor.Hierarchy",
    "Inno.Editor.Panel.Hierarchy",
    ScriptingApiScope.Editor)]

[assembly: ScriptingApiExport(typeof(HierarchyObjectDropTarget), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(HierarchySceneDropTarget), ScriptingApiScope.Editor)]
