using Inno.Assets.File;
using Inno.Core.Scripting;

[assembly: ScriptingApiNamespace(
    "InnoEngine.Assets",
    "Inno.Assets.File",
    ScriptingApiScope.Runtime)]

[assembly: ScriptingApiExport(typeof(AssetFileEntry), ScriptingApiScope.Runtime)]
