using Inno.Scripting.Api;
using Inno.Storage;

[assembly: ScriptingApiNamespace("InnoEngine.Storage", "Inno.Storage", ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(StorageKey), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(IApplicationStorage), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(Storage), ScriptingApiScope.Runtime)]
