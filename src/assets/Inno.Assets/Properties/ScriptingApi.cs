using Inno.Assets;
using Inno.Scripting.Api;

[assembly: ScriptingApiNamespace(
    "InnoEngine.Assets",
    "Inno.Assets",
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
[assembly: ScriptingApiExport(typeof(Assets), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AssetSourceId), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AssetPath), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AssetReferenceInfo), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AssetReferenceKind), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AssetReferenceLocation), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AssetSourceKind), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(BinaryAsset), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(TextAsset), ScriptingApiScope.Runtime)]
