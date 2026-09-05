using Inno.Audio;
using Inno.Audio.Runtime;
using Inno.Scripting.Api;

[assembly: ScriptingApiNamespace(
    "InnoEngine.Audio",
    "Inno.Audio.Runtime",
    ScriptingApiScope.Runtime)]

[assembly: ScriptingApiExport(typeof(AudioProjectSettings), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AudioContentId), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AudioContentReference), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AudioContentScope), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AudioEmitterSnapshot), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AudioListenerSnapshot), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AudioContentProviderExtensionAttribute), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AudioContentProviderContext), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AudioContentProvider), ScriptingApiScope.Runtime)]
