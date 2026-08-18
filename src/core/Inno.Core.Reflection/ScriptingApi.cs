using Inno.Core.Reflection;
using Inno.Core.Scripting;

[assembly: ScriptingApiNamespace(
    "InnoEngine.Reflection",
    "Inno.Core.Reflection",
    ScriptingApiScope.Runtime)]

[assembly: ScriptingApiExport(typeof(StableTypeIdAttribute), ScriptingApiScope.Runtime)]

[assembly: ScriptingGlobalUsing(
    "InnoEngine.Reflection",
    ScriptingApiScope.Runtime)]
