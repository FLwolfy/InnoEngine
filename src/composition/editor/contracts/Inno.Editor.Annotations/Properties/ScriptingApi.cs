using Inno.Scripting.Api;
using Inno.Editor.Annotations;

[assembly: ScriptingApiNamespace("InnoEditor.Annotations", "Inno.Editor.Annotations", ScriptingApiScope.Authoring)]
[assembly: ScriptingApiExport(typeof(InspectorPresentationAttribute), ScriptingApiScope.Authoring)]
[assembly: ScriptingApiExport(typeof(HeaderAttribute), ScriptingApiScope.Authoring)]
[assembly: ScriptingApiExport(typeof(TextAttribute), ScriptingApiScope.Authoring)]
[assembly: ScriptingApiExport(typeof(SpaceAttribute), ScriptingApiScope.Authoring)]
[assembly: ScriptingApiExport(typeof(TooltipAttribute), ScriptingApiScope.Authoring)]
[assembly: ScriptingApiExport(typeof(InspectorNameAttribute), ScriptingApiScope.Authoring)]
[assembly: ScriptingApiExport(typeof(RangeAttribute), ScriptingApiScope.Authoring)]
[assembly: ScriptingApiExport(typeof(InspectorReadOnlyAttribute), ScriptingApiScope.Authoring)]
[assembly: ScriptingApiExport(typeof(InspectorCondition), ScriptingApiScope.Authoring)]
[assembly: ScriptingApiExport(typeof(ShowIfAttribute), ScriptingApiScope.Authoring)]
[assembly: ScriptingApiExport(typeof(HideIfAttribute), ScriptingApiScope.Authoring)]
[assembly: ScriptingApiExport(typeof(InspectorMessageType), ScriptingApiScope.Authoring)]
[assembly: ScriptingApiExport(typeof(HelpBoxAttribute), ScriptingApiScope.Authoring)]
