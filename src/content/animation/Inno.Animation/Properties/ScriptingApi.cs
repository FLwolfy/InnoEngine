using Inno.Animation;
using Inno.Scripting.Api;

[assembly: ScriptingApiNamespace(
    "InnoEngine.Animation",
    "Inno.Animation",
    ScriptingApiScope.Runtime)]

[assembly: ScriptingApiExport(typeof(Animation), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AnimationBindingId), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AnimationTarget), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AnimationBindingProvider), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AnimationBindingProviderAttribute), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AnimationValueKind), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AnimationValue), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AnimationInterpolation), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AnimationClock), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AnimationPlaybackState), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AnimationPlayOptions), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AnimationPlaybackHandle), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AnimationSample), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(IAnimationBindingSink), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AnimationKeyframe), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AnimationTrack), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AnimationEventMarker), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AnimationClipAsset), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(AnimationMarkerEvent), ScriptingApiScope.Runtime)]
