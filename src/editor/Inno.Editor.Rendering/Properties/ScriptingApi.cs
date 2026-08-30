using Inno.Core.Scripting;
using Inno.Editor.Rendering;

[assembly: ScriptingApiNamespace(
    "InnoEditor.Rendering",
    "Inno.Editor.Rendering",
    ScriptingApiScope.Editor)]

[assembly: ScriptingApiExport(typeof(EditorViewportKindId), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorViewportProviderExtensionAttribute), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorViewportContext), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorViewportManipulationSpace), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorViewportPointerContext), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorViewportSubmission), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorViewportProvider), ScriptingApiScope.Editor)]
