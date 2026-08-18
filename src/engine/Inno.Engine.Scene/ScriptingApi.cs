using Inno.Core.Scripting;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Components;

[assembly: ScriptingApiNamespace(
    "InnoEngine.Scene",
    "Inno.Engine.Scene",
    ScriptingApiScope.Runtime)]
[assembly: ScriptingApiNamespace(
    "InnoEngine.Scene",
    "Inno.Engine.Scene.Components",
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

[assembly: ScriptingGlobalUsing(
    "InnoEngine.Scene",
    ScriptingApiScope.Runtime)]
