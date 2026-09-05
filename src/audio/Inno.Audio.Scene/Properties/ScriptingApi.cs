using Inno.Audio.Scene;
using Inno.Scripting.Api;

[assembly: ScriptingApiNamespace(
    "InnoEngine.Audio",
    "Inno.Audio.Scene",
    ScriptingApiScope.Runtime)]

[assembly: ScriptingApiExport(typeof(AudioSource), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AudioListener), ScriptingApiScope.Runtime)]
