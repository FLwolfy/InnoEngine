using Inno.Core.Scripting;
using Inno.Rendering.ShaderGraph;

[assembly: ScriptingApiNamespace(
    "InnoEngine.Rendering.ShaderGraph",
    "Inno.Rendering.ShaderGraph",
    ScriptingApiScope.Runtime)]

[assembly: ScriptingApiExport(typeof(ShaderGraphTarget), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(ShaderValueType), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(ShaderGraphValueTypes), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(ShaderGraphTypeConversion), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(ShaderNodeExtensionAttribute), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(ShaderGraphOutputKind), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(ShaderValue), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(ShaderNodeDefinition), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(ShaderNodeEmitContext), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(ShaderNodeRegistry), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(ShaderGraphAsset), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(ShaderGraphDocumentData), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(ShaderGraphDocumentCodec), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(BuiltinShaderNodes), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(ShaderGraphCompileResult), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(ShaderGraphCompiler), ScriptingApiScope.Runtime)]
