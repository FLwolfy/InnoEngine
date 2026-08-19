using Inno.Assets.Core;
using Inno.Core.Scripting;

[assembly: ScriptingApiNamespace(
    "InnoEngine.Assets",
    "Inno.Assets.Core",
    ScriptingApiScope.Runtime)]

[assembly: ScriptingApiExport(typeof(AssetDependency), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AssetArtifactInfo), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AssetArtifactKey), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AssetChange), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AssetChangeKind), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AssetChangeSet), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AssetImportStatus), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AssetInfo), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AssetObject), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AssetReferenceInfo), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AssetReferenceKind), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AssetReferenceLocation), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AssetSourceKind), ScriptingApiScope.Runtime)]
