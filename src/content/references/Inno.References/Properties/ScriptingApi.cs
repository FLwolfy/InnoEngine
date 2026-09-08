using Inno.References;
using Inno.Scripting.Api;

[assembly: ScriptingApiNamespace("InnoEngine.References", "Inno.References", ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(ContentReadScope), ScriptingApiScope.Runtime)]
