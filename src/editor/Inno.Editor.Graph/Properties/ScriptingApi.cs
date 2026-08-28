using Inno.Core.Scripting;
using Inno.Editor.Graph;

[assembly: ScriptingApiNamespace("InnoEditor.Graph", "Inno.Editor.Graph", ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(GraphEditorModule), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(GraphDocumentController), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(GraphCanvasState), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(GraphClipboardData), ScriptingApiScope.Editor)]
