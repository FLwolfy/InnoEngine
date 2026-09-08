using Inno.Core.Logging;
using Inno.Scripting.Api;

[assembly: ScriptingApiNamespace(
    "InnoEngine.Logging",
    "Inno.Core.Logging",
    ScriptingApiScope.Runtime)]

[assembly: ScriptingApiExport(typeof(Log), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(LogSessionId), ScriptingApiScope.Runtime)]
