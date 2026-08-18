using Inno.Assets;
using Inno.Core.Scripting;

[assembly: ScriptingApiNamespace(
    "InnoEngine.Assets",
    "Inno.Assets",
    ScriptingApiScope.Runtime)]

[assembly: ScriptingApiExport(typeof(AssetManager), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AssetManagerOptions), ScriptingApiScope.Runtime)]

[assembly: ScriptingGlobalUsing(
    "InnoEngine.Assets",
    ScriptingApiScope.Runtime)]
