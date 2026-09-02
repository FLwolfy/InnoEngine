using Inno.Scripting.Api;
using Inno.Editor.PlayMode;

[assembly: ScriptingApiNamespace(
    "InnoEditor.PlayMode",
    "Inno.Editor.PlayMode",
    ScriptingApiScope.Editor)]

[assembly: ScriptingApiExport(typeof(EditorPlayModeState), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(IEditorPlayMode), ScriptingApiScope.Editor)]
