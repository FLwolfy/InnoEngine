using Inno.Assets.Serialization;
using Inno.Core.Scripting;

[assembly: ScriptingApiNamespace(
    "InnoEngine.Assets",
    "Inno.Assets.Serialization",
    ScriptingApiScope.Runtime)]

[assembly: ScriptingApiExport(typeof(NativeAssetSourceSerialization), ScriptingApiScope.Runtime)]
