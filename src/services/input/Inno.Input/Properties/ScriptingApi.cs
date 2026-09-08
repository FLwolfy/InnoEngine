using Inno.Input;
using Inno.Scripting.Api;

[assembly: ScriptingApiNamespace("InnoEngine.Input", "Inno.Input", ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(InputSnapshot), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(IInputService), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(Input), ScriptingApiScope.Runtime)]
