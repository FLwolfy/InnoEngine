using Inno.Rendering.Assets;
using Inno.Scripting.Api;

[assembly: ScriptingApiNamespace("InnoEditor.Rendering.Assets", "Inno.Rendering.Assets", ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(ShaderFunctionAsset), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(ShaderSourceImportSettings), ScriptingApiScope.Editor)]
