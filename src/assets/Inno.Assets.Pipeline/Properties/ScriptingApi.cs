using Inno.Assets.Pipeline;
using Inno.Scripting.Api;

[assembly: ScriptingApiNamespace(
    "InnoEditor.Assets",
    "Inno.Assets.Pipeline",
    ScriptingApiScope.Editor)]

[assembly: ScriptingApiExport(typeof(AssetImportContext), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(AssetExportContext), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(AssetSerializationServices), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(NativeAssetSourceSerialization), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(AssetImporter), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(AssetImporter<>), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(AssetImporterExtensionAttribute), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(AssetDeploymentScope), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(AssetImportWriter<>), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(AssetArtifactWriter), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(AssetBuildContext<>), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(AssetBuildProcessor), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(AssetBuildProcessor<>), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(AssetBuildProcessorExtensionAttribute), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorAssets), ScriptingApiScope.Editor)]
