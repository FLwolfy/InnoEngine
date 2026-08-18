using Inno.Assets.Core;
using Inno.Core.Scripting;

[assembly: ScriptingApiNamespace(
    "InnoEngine.Assets",
    "Inno.Assets.Core",
    ScriptingApiScope.Runtime)]

[assembly: ScriptingApiExport(typeof(AssetDependency), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AssetObject), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AssetReferenceInfo), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AssetReferenceKind), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AssetReferenceLocation), ScriptingApiScope.Runtime)]

[assembly: ScriptingGlobalUsing(
    "InnoEngine.Assets",
    ScriptingApiScope.Runtime)]
