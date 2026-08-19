using Inno.Assets.Loader;
using Inno.Core.Scripting;

[assembly: ScriptingApiNamespace(
    "InnoEngine.Assets",
    "Inno.Assets.Loader",
    ScriptingApiScope.Runtime)]

[assembly: ScriptingApiExport(typeof(AssetImportContext), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AssetImporter), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AssetImporter<>), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AssetImporterExtensionAttribute), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AssetImportWriter<>), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AssetArtifactWriter), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AssetBuildContext<>), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AssetBuildProcessor), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AssetBuildProcessor<>), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AssetBuildProcessorExtensionAttribute), ScriptingApiScope.Runtime)]
