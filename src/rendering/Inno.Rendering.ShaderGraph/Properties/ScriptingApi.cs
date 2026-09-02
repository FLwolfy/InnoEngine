using Inno.Scripting.Api;
using Inno.Rendering.ShaderGraph;

[assembly: ScriptingApiNamespace(
    "InnoEngine.Rendering.ShaderGraph",
    "Inno.Rendering.ShaderGraph",
    ScriptingApiScope.Runtime)]

[assembly: ScriptingApiExport(typeof(ShaderValueType), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(ShaderGraphValueTypes), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(ShaderGraphTypeConversion), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(ShaderNodeExtensionAttribute), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(ShaderValue), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(ShaderNodeDefinition), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(ShaderNodeEmitContext), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(ShaderGraphEmission), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(ShaderGraphProgramContext), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(ShaderGraphProgramNodeDefinition), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(ShaderGraphAsset), ScriptingApiScope.Runtime)]
