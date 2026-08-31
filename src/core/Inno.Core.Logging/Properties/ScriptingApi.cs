using Inno.Core.Logging;
using Inno.Core.Scripting;

[assembly: ScriptingApiNamespace(
    "InnoEngine.Logging",
    "Inno.Core.Logging",
    ScriptingApiScope.Runtime)]

[assembly: ScriptingApiExport(typeof(Log), ScriptingApiScope.Runtime)]
