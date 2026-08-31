using Inno.Core.Identity;
using Inno.Core.Scripting;

[assembly: ScriptingApiNamespace(
    "InnoEngine.Core",
    "Inno.Core.Identity",
    ScriptingApiScope.Runtime)]

[assembly: ScriptingApiExport(typeof(IIdentityObject), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(Identity), ScriptingApiScope.Runtime)]
