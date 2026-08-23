using Inno.Core.Scripting;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Components;
using Inno.Engine.Scene.Layers;

[assembly: ScriptingApiNamespace(
    "InnoEngine.Scene",
    "Inno.Engine.Scene",
    ScriptingApiScope.Runtime)]
[assembly: ScriptingApiNamespace(
    "InnoEngine.Scene",
    "Inno.Engine.Scene.Components",
    ScriptingApiScope.Runtime)]
[assembly: ScriptingApiNamespace(
    "InnoEngine.Scene",
    "Inno.Engine.Scene.Layers",
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
[assembly: ScriptingApiExport(typeof(SceneManager), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(Transform), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(Layer), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(LayerDefinition), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(LayerMask), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(LayerStack), ScriptingApiScope.Runtime)]
