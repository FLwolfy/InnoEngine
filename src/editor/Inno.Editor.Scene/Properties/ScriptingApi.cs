using Inno.Core.Scripting;
using Inno.Editor.Scene;
using Inno.Editor.Scene.DragDrop;
using Inno.Editor.Scene.Inspection;
using Inno.Editor.Scene.Workspace;

[assembly: ScriptingApiNamespace(
    "InnoEditor.Scene",
    "Inno.Editor.Scene",
    ScriptingApiScope.Editor)]
[assembly: ScriptingApiNamespace(
    "InnoEditor.Scene",
    "Inno.Editor.Scene.Workspace",
    ScriptingApiScope.Editor)]
[assembly: ScriptingApiNamespace(
    "InnoEditor.Inspection",
    "Inno.Editor.Scene.Inspection",
    ScriptingApiScope.Editor)]
[assembly: ScriptingApiNamespace(
    "InnoEditor.DragDrop",
    "Inno.Editor.Scene.DragDrop",
    ScriptingApiScope.Editor)]

[assembly: ScriptingApiExport(typeof(EditorSceneWorkspace), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(SceneActionIds), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(SceneSurface), ScriptingApiScope.Editor)]

[assembly: ScriptingApiExport(typeof(IInspectorDrawer), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(IPropertyDrawer), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(InspectorDrawContext), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(InspectorDrawerAttribute), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(PropertyDrawContext), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(PropertyDrawerAttribute), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(SerializedPropertyRenderer), ScriptingApiScope.Editor)]

[assembly: ScriptingApiExport(typeof(EngineObjectReferenceDropTarget), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(HierarchyObjectDropTarget), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(HierarchySceneDropTarget), ScriptingApiScope.Editor)]
