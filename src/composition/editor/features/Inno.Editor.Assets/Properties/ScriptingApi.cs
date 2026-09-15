using Inno.Editor.Assets;
using Inno.Scripting.Api;

[assembly: ScriptingApiNamespace(
    "InnoEditor.Assets",
    "Inno.Editor.Assets",
    ScriptingApiScope.Editor)]

[assembly: ScriptingApiExport(typeof(AssetCreationMenuAttribute), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(AssetCreationContext), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(AssetCreationTemplate), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(AssetCreationTemplate<>), ScriptingApiScope.Editor)]
