using Inno.Scripting.Api;
using Inno.Rendering;

[assembly: ScriptingApiNamespace(
    "InnoEngine.Rendering",
    "Inno.Rendering",
    ScriptingApiScope.Runtime)]

[assembly: ScriptingApiExport(typeof(GraphicsSettings), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(RenderFrameStatistics), ScriptingApiScope.Runtime)]
