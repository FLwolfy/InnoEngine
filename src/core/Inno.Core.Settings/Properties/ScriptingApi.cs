using Inno.Core.Scripting;
using Inno.Core.Settings;

[assembly: ScriptingApiNamespace(
    "InnoEngine.Settings",
    "Inno.Core.Settings",
    ScriptingApiScope.Runtime)]

[assembly: ScriptingApiExport(typeof(ProjectSettingId), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(ProjectSettingDefinitionAttribute), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(ProjectSettingComposerAttribute), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(ProjectSettingContributionSource), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(ProjectSettingContributionContext), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(ProjectSettingContribution<>), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(ProjectSettingComposer), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(ProjectSettingComposer<,>), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(ProjectSettingsManager), ScriptingApiScope.Runtime)]
