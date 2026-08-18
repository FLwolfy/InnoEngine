using Inno.Assets.Types;
using Inno.Core.Scripting;

[assembly: ScriptingApiNamespace(
    "InnoEngine.Assets",
    "Inno.Assets.Types",
    ScriptingApiScope.Runtime)]

[assembly: ScriptingApiExport(typeof(BinaryAsset), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(TextAsset), ScriptingApiScope.Runtime)]
