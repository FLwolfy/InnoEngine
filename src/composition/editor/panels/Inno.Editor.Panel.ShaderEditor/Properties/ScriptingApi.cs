using Inno.Editor.Panel.ShaderEditor;
using Inno.Scripting.Api;

[assembly: ScriptingApiNamespace("InnoEditor.Shaders", "Inno.Editor.Panel.ShaderEditor", ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(ShaderNodeDrawerAttribute), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(ShaderNodeDrawer), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(ShaderNodeDrawContext), ScriptingApiScope.Editor)]
