using Inno.Core.Scripting;
using Inno.Rendering.Pipelines;

[assembly: ScriptingApiNamespace(
    "InnoEngine.Rendering.Pipelines",
    "Inno.Rendering.Pipelines",
    ScriptingApiScope.Runtime)]

[assembly: ScriptingApiExport(typeof(BuiltinPipelineOperations), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(UniversalRenderPipeline), ScriptingApiScope.Runtime)]
