using Inno.Scripting.Api;
using Inno.Editor.Rendering;

[assembly: ScriptingApiNamespace(
    "InnoEditor.Rendering",
    "Inno.Editor.Rendering",
    ScriptingApiScope.Editor)]

[assembly: ScriptingApiExport(typeof(EditorViewportKindId), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorViewportProviderExtensionAttribute), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorViewportContext), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorViewportProjection), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorViewportNavigationMode), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorViewportNavigationCapabilities), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorViewportNavigationProfileId), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorViewportFocusBounds), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorViewportNavigationProfile), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorViewportNavigationState), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorViewportPresentation), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorViewportManipulationSpace), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorViewportPointerContext), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorViewportSubmission), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorViewportProvider), ScriptingApiScope.Editor)]
