using Inno.Scripting.Api;
using Inno.Runtime;

[assembly: ScriptingApiNamespace(
    "InnoEngine.Core",
    "Inno.Runtime",
    ScriptingApiScope.Runtime)]

[assembly: ScriptingApiExport(
    typeof(Time),
    ScriptingApiScope.Runtime)]
