using System;
using System.Collections.Generic;

using Inno.Build;
using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Settings;

internal enum BuildSettingsKey
{
    GameProductName,
    GameStartupScene,
    GameWindowWidth,
    GameWindowHeight,
    GameTarget,
    GameOutputDirectory,
    PluginDisplayName,
    PluginOutputPath,
    IncludePluginDependencies
}

internal sealed class BuildSettingsField
{
    private const nuint C_TEXT_CAPACITY = 4096;

    internal BuildSettingsField(
        BuildSettingsKey key,
        string path,
        string section,
        string description)
    {
        this.key = key;
        this.path = path;
        pagePath = path[..path.LastIndexOf('/')];
        label = path[(path.LastIndexOf('/') + 1)..];
        this.section = section;
        this.description = description;
    }

    internal BuildSettingsKey key { get; }
    internal string path { get; }
    internal string pagePath { get; }
    internal string label { get; }
    internal string section { get; }
    internal string description { get; }

    internal bool Draw(BuildSettings settings)
        => key switch
        {
            BuildSettingsKey.GameProductName => DrawTextValue(
                settings.gameProductName,
                value => settings.gameProductName = value),
            BuildSettingsKey.GameStartupScene => DrawTextValue(
                settings.gameStartupScene,
                value => settings.gameStartupScene = value),
            BuildSettingsKey.GameWindowWidth => DrawPositiveInt(
                settings.gameWindowWidth,
                value => settings.gameWindowWidth = value),
            BuildSettingsKey.GameWindowHeight => DrawPositiveInt(
                settings.gameWindowHeight,
                value => settings.gameWindowHeight = value),
            BuildSettingsKey.GameTarget => DrawTarget(settings),
            BuildSettingsKey.GameOutputDirectory => DrawTextValue(
                settings.gameOutputDirectory,
                value => settings.gameOutputDirectory = value),
            BuildSettingsKey.PluginDisplayName => DrawTextValue(
                settings.pluginDisplayName,
                value => settings.pluginDisplayName = value),
            BuildSettingsKey.PluginOutputPath => DrawTextValue(
                settings.pluginOutputPath,
                value => settings.pluginOutputPath = value),
            BuildSettingsKey.IncludePluginDependencies => DrawIncludeDependencies(settings),
            _ => throw new InvalidOperationException($"Unknown Build Settings field '{key}'.")
        };

    internal bool IsDefault(BuildSettings settings, BuildSettings defaults)
        => key switch
        {
            BuildSettingsKey.GameProductName => settings.gameProductName == defaults.gameProductName,
            BuildSettingsKey.GameStartupScene => settings.gameStartupScene == defaults.gameStartupScene,
            BuildSettingsKey.GameWindowWidth => settings.gameWindowWidth == defaults.gameWindowWidth,
            BuildSettingsKey.GameWindowHeight => settings.gameWindowHeight == defaults.gameWindowHeight,
            BuildSettingsKey.GameTarget => settings.gameTarget == defaults.gameTarget,
            BuildSettingsKey.GameOutputDirectory => settings.gameOutputDirectory == defaults.gameOutputDirectory,
            BuildSettingsKey.PluginDisplayName => settings.pluginDisplayName == defaults.pluginDisplayName,
            BuildSettingsKey.PluginOutputPath => settings.pluginOutputPath == defaults.pluginOutputPath,
            BuildSettingsKey.IncludePluginDependencies =>
                settings.includePluginDependencies == defaults.includePluginDependencies,
            _ => throw new InvalidOperationException($"Unknown Build Settings field '{key}'.")
        };

    internal void Reset(BuildSettings settings, BuildSettings defaults)
    {
        switch (key)
        {
            case BuildSettingsKey.GameProductName:
                settings.gameProductName = defaults.gameProductName;
                break;
            case BuildSettingsKey.GameStartupScene:
                settings.gameStartupScene = defaults.gameStartupScene;
                break;
            case BuildSettingsKey.GameWindowWidth:
                settings.gameWindowWidth = defaults.gameWindowWidth;
                break;
            case BuildSettingsKey.GameWindowHeight:
                settings.gameWindowHeight = defaults.gameWindowHeight;
                break;
            case BuildSettingsKey.GameTarget:
                settings.gameTarget = defaults.gameTarget;
                break;
            case BuildSettingsKey.GameOutputDirectory:
                settings.gameOutputDirectory = defaults.gameOutputDirectory;
                break;
            case BuildSettingsKey.PluginDisplayName:
                settings.pluginDisplayName = defaults.pluginDisplayName;
                break;
            case BuildSettingsKey.PluginOutputPath:
                settings.pluginOutputPath = defaults.pluginOutputPath;
                break;
            case BuildSettingsKey.IncludePluginDependencies:
                settings.includePluginDependencies = defaults.includePluginDependencies;
                break;
            default:
                throw new InvalidOperationException($"Unknown Build Settings field '{key}'.");
        }
    }

    internal static bool ValuesEqual(BuildSettings left, BuildSettings right)
        => string.Equals(left.gameProductName, right.gameProductName, StringComparison.Ordinal)
           && string.Equals(left.gameStartupScene, right.gameStartupScene, StringComparison.Ordinal)
           && string.Equals(left.gameOutputDirectory, right.gameOutputDirectory, StringComparison.Ordinal)
           && left.gameWindowWidth == right.gameWindowWidth
           && left.gameWindowHeight == right.gameWindowHeight
           && left.gameTarget == right.gameTarget
           && string.Equals(left.pluginDisplayName, right.pluginDisplayName, StringComparison.Ordinal)
           && string.Equals(left.pluginOutputPath, right.pluginOutputPath, StringComparison.Ordinal)
           && left.includePluginDependencies == right.includePluginDependencies;

    private static bool DrawTextValue(
        string value,
        Action<string> apply)
    {
        NativeImGui.SetNextItemWidth(-1f);
        if (!NativeImGui.InputText("##value", ref value, C_TEXT_CAPACITY))
            return false;

        apply(value);
        return true;
    }

    private static bool DrawPositiveInt(int value, Action<int> apply)
    {
        NativeImGui.SetNextItemWidth(-1f);
        if (!NativeImGui.InputInt("##value", ref value))
            return false;

        apply(Math.Max(1, value));
        return true;
    }

    private static bool DrawTarget(BuildSettings settings)
    {
        bool changed = false;
        NativeImGui.SetNextItemWidth(-1f);
        if (!NativeImGui.BeginCombo("##value", GetTargetLabel(settings.gameTarget)))
            return false;
        try
        {
            changed |= DrawTargetChoice(settings, BuildTargetId.macOSArm64);
            changed |= DrawTargetChoice(settings, BuildTargetId.windowsX64);
        }
        finally
        {
            NativeImGui.EndCombo();
        }
        return changed;
    }

    private static bool DrawIncludeDependencies(BuildSettings settings)
    {
        bool value = settings.includePluginDependencies;
        if (!NativeImGui.Checkbox("##value", ref value))
            return false;

        settings.includePluginDependencies = value;
        return true;
    }

    private static bool DrawTargetChoice(BuildSettings settings, BuildTargetId target)
    {
        bool selected = settings.gameTarget == target;
        bool changed = NativeImGui.Selectable(GetTargetLabel(target), selected);
        if (changed)
            settings.gameTarget = target;
        if (selected)
            NativeImGui.SetItemDefaultFocus();
        return changed;
    }

    private static string GetTargetLabel(BuildTargetId target)
        => target == BuildTargetId.macOSArm64
            ? "macOS (Apple silicon)"
            : target == BuildTargetId.windowsX64
                ? "Windows (x64)"
                : target.ToString();

}

internal static class BuildSettingsPresentation
{
    internal static IReadOnlyList<BuildSettingsField> fields { get; } =
    [
        new BuildSettingsField(
            BuildSettingsKey.GameProductName,
            "Build/Game/Product Name",
            "Game Export Defaults",
            "Player-facing product name copied into each game export."),
        new BuildSettingsField(
            BuildSettingsKey.GameStartupScene,
            "Build/Game/Startup Scene",
            "Game Export Defaults",
            "Mount-qualified Scene path loaded when the exported Player starts."),
        new BuildSettingsField(
            BuildSettingsKey.GameWindowWidth,
            "Build/Game/Initial Window Width",
            "Game Export Defaults",
            "Initial logical Player window width."),
        new BuildSettingsField(
            BuildSettingsKey.GameWindowHeight,
            "Build/Game/Initial Window Height",
            "Game Export Defaults",
            "Initial logical Player window height."),
        new BuildSettingsField(
            BuildSettingsKey.GameTarget,
            "Build/Game/Target",
            "Game Export Defaults",
            "Platform target selected when the game export window opens."),
        new BuildSettingsField(
            BuildSettingsKey.GameOutputDirectory,
            "Build/Game/Output Directory",
            "Game Export Defaults",
            "Game output directory. Relative paths resolve from the project root."),
        new BuildSettingsField(
            BuildSettingsKey.PluginDisplayName,
            "Build/Plugin/Display Name",
            "Plugin Export Defaults",
            "User-facing Plugin name copied into each Plugin export."),
        new BuildSettingsField(
            BuildSettingsKey.PluginOutputPath,
            "Build/Plugin/Destination IPlugin",
            "Plugin Export Defaults",
            "Package destination ending in .iplugin. Relative paths resolve from the project root."),
        new BuildSettingsField(
            BuildSettingsKey.IncludePluginDependencies,
            "Build/Plugin/Include Dependencies",
            "Plugin Export Defaults",
            "Whether dependency Plugin packages are embedded by default. Export changes remain temporary.")
    ];
}
