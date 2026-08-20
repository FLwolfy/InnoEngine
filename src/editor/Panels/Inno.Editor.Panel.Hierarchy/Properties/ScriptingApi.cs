using Inno.Core.Scripting;
using Inno.Editor.Panel.Hierarchy;

[assembly: ScriptingApiNamespace(
    "InnoEditor.Hierarchy",
    "Inno.Editor.Panel.Hierarchy",
    ScriptingApiScope.Editor)]

[assembly: ScriptingApiExport(typeof(HierarchyAreas), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(HierarchyActions), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorSceneWorkspace), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(HierarchyObjectDropTarget), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(HierarchySceneDropTarget), ScriptingApiScope.Editor)]
