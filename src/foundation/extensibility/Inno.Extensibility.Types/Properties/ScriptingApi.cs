using Inno.Extensibility.Types;
using Inno.Scripting.Api;

[assembly: ScriptingApiNamespace(
    "InnoEngine.Reflection",
    "Inno.Extensibility.Types",
    ScriptingApiScope.Runtime)]

[assembly: ScriptingApiExport(typeof(StableTypeIdAttribute), ScriptingApiScope.Runtime)]
