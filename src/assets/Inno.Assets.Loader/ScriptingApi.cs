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
[assembly: ScriptingApiExport(typeof(AssetImportResult<>), ScriptingApiScope.Runtime)]

[assembly: ScriptingGlobalUsing(
    "InnoEngine.Assets",
    ScriptingApiScope.Runtime)]
