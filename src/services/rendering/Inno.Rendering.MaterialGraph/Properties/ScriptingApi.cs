using Inno.Scripting.Api;
using Inno.Rendering.MaterialGraph;

[assembly: ScriptingApiNamespace(
    "InnoEngine.Rendering.MaterialGraph",
    "Inno.Rendering.MaterialGraph",
    ScriptingApiScope.Runtime)]

[assembly: ScriptingApiExport(typeof(MaterialGraphAsset), ScriptingApiScope.Runtime)]
