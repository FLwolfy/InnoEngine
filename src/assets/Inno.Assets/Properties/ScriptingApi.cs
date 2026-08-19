using Inno.Assets;
using Inno.Core.Scripting;

[assembly: ScriptingApiNamespace(
    "InnoEngine.Assets",
    "Inno.Assets",
    ScriptingApiScope.Runtime)]

[assembly: ScriptingApiExport(typeof(AssetManager), ScriptingApiScope.Runtime)]
