using Inno.Assets.Plugins;
using Inno.Core.Scripting;

[assembly: ScriptingApiNamespace(
    "InnoEngine.Assets.Plugins",
    "Inno.Assets.Plugins",
    ScriptingApiScope.Runtime)]

[assembly: ScriptingApiExport(typeof(PluginDefinitionAsset), ScriptingApiScope.Runtime)]
