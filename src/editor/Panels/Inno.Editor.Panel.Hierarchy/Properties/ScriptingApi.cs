using Inno.Core.Scripting;
using Inno.Editor.Panel.Hierarchy;
using Inno.Editor.Panel.Hierarchy.DragDrop;
using Inno.Editor.Panel.Hierarchy.Workspace;

[assembly: ScriptingApiNamespace(
    "InnoEditor.Hierarchy",
    "Inno.Editor.Panel.Hierarchy",
    ScriptingApiScope.Editor)]
[assembly: ScriptingApiNamespace(
    "InnoEditor.Hierarchy",
    "Inno.Editor.Panel.Hierarchy.DragDrop",
    ScriptingApiScope.Editor)]
[assembly: ScriptingApiNamespace(
    "InnoEditor.Hierarchy",
    "Inno.Editor.Panel.Hierarchy.Workspace",
    ScriptingApiScope.Editor)]

[assembly: ScriptingApiExport(typeof(HierarchyAreas), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(HierarchyActions), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorSceneWorkspace), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(HierarchyObjectDropTarget), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(HierarchySceneDropTarget), ScriptingApiScope.Editor)]
