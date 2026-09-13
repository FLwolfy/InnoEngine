using Inno.Editor.Shaders;
using Inno.Scripting.Api;

[assembly: ScriptingApiNamespace("InnoEditor.Shaders", "Inno.Editor.Shaders", ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(ShaderNodeDrawerAttribute), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(ShaderNodeDrawer), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(ShaderNodeDrawContext), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(MaterialDocuments), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(ShaderPropertyInspector), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(ShaderParameterPresentation), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(ShaderPreviewProviderAttribute), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(ShaderPreviewProvider), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(ShaderPreviewContext), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(ShaderPreviews), ScriptingApiScope.Editor)]
