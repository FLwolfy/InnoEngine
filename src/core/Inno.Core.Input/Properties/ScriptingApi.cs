using Inno.Core.Input;
using Inno.Core.Scripting;

[assembly: ScriptingApiNamespace(
    "InnoEngine.Input",
    "Inno.Core.Input",
    ScriptingApiScope.Runtime)]

[assembly: ScriptingApiExport(typeof(KeyCode), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(KeyModifier), ScriptingApiScope.Runtime)]
