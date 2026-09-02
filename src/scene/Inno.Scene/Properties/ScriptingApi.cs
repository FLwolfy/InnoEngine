using Inno.Scripting.Api;
using Inno.Scene;
using Inno.Scene.Components;
using Inno.Scene.Layers;

[assembly: ScriptingApiNamespace(
    "InnoEngine.Scene",
    "Inno.Scene",
    ScriptingApiScope.Runtime)]
[assembly: ScriptingApiNamespace(
    "InnoEngine.Scene",
    "Inno.Scene.Components",
    ScriptingApiScope.Runtime)]
[assembly: ScriptingApiNamespace(
    "InnoEngine.Scene",
    "Inno.Scene.Layers",
    ScriptingApiScope.Runtime)]

[assembly: ScriptingApiExport(typeof(AllowMultipleComponentAttribute), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AllowMultipleSystemAttribute), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(EngineObject), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(GameBehavior), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(GameComponent), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(GameObject), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(GameScene), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(GameSystem), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(PrefabInstanceInfo), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(PrefabAsset), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(SceneAsset), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(SceneManager), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(Transform), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(GameLayer), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(GameLayerDefinition), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(GameLayerId), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(GameLayerMask), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(GameLayerStack), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(GameTagCatalog), ScriptingApiScope.Runtime)]
