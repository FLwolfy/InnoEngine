using Inno.Core.Scripting;
using Inno.Editor.Scene;

[assembly: ScriptingApiNamespace(
    "InnoEditor.Scene",
    "Inno.Editor.Scene",
    ScriptingApiScope.Editor)]

[assembly: ScriptingApiExport(typeof(IEditorSceneWorkspace), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(SceneEdits), ScriptingApiScope.Editor)]
