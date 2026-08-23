using Inno.Core.Scripting;
using Inno.Engine.Scene.Assets;

[assembly: ScriptingApiNamespace(
    "InnoEngine.Scene",
    "Inno.Engine.Scene.Assets",
    ScriptingApiScope.Runtime)]

[assembly: ScriptingApiExport(typeof(PrefabAsset), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(SceneAsset), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(GameLayerSettingsAsset), ScriptingApiScope.Runtime)]
