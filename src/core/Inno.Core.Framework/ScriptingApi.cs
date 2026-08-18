using Inno.Core.Scripting;

[assembly: ScriptingApiNamespace(
    "InnoEngine.Core",
    "Inno.Core.Framework",
    ScriptingApiScope.Runtime)]

[assembly: ScriptingApiExport(
    typeof(Inno.Core.Framework.Time),
    ScriptingApiScope.Runtime)]

[assembly: ScriptingGlobalUsing(
    "InnoEngine.Core",
    ScriptingApiScope.Runtime)]
