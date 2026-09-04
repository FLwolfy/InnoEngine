using System;
using System.IO;

using Inno.Core.Serialization;
using Inno.Core.Settings;

namespace Inno.Build;

/// <summary>
/// Defines project-owned defaults copied into each temporary game or Plugin export request.
/// </summary>
[GenerateSerializationConverter]
public sealed class BuildSettings : ISerializable
{
    /// <summary>
    /// Gets or sets the default user-facing Plugin name.
    /// </summary>
    [SerializableProperty]
    public string pluginDisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the default Plugin package destination, relative to the project root or absolute.
    /// </summary>
    [SerializableProperty]
    public string pluginOutputPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether Plugin dependency packages are embedded by default.
    /// </summary>
    [SerializableProperty]
    public bool includePluginDependencies { get; set; }

    /// <summary>
    /// Gets or sets the default player-facing product name.
    /// </summary>
    [SerializableProperty]
    public string gameProductName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the default mount-qualified startup Scene path.
    /// </summary>
    [SerializableProperty]
    public string gameStartupScene { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the default game output directory, relative to the project root or absolute.
    /// </summary>
    [SerializableProperty]
    public string gameOutputDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the default initial logical window width.
    /// </summary>
    [SerializableProperty]
    public int gameWindowWidth { get; set; } = 1280;

    /// <summary>
    /// Gets or sets the default initial logical window height.
    /// </summary>
    [SerializableProperty]
    public int gameWindowHeight { get; set; } = 720;

    /// <summary>
    /// Gets or sets the default game build target.
    /// </summary>
    public BuildTargetId gameTarget
    {
        get => new(m_gameTargetId);
        set => m_gameTargetId = value.value;
    }

    [SerializableProperty]
    internal string m_gameTargetId = BuildTargetId.macOSArm64.value;

    /// <summary>
    /// Creates canonical defaults for a new project.
    /// </summary>
    /// <param name="projectName">
    /// The project directory name used for initial labels and output paths.
    /// </param>
    /// <param name="startupScene">
    /// The initial mount-qualified startup Scene, or an empty value when unavailable.
    /// </param>
    /// <param name="target">
    /// The host-appropriate initial build target.
    /// </param>
    /// <returns>
    /// A newly owned build settings object.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="projectName"/> is empty or <paramref name="target"/> is invalid.
    /// </exception>
    public static BuildSettings CreateDefault(
        string projectName,
        string startupScene,
        BuildTargetId target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        ArgumentNullException.ThrowIfNull(startupScene);
        if (string.IsNullOrWhiteSpace(target.value))
            throw new ArgumentException("A default build target is required.", nameof(target));
        string portableId = ProjectId.FromName(projectName).value;
        return new BuildSettings
        {
            pluginDisplayName = projectName,
            pluginOutputPath = $"Builds/{portableId}.iplugin",
            includePluginDependencies = false,
            gameProductName = projectName,
            gameStartupScene = startupScene,
            gameOutputDirectory = "Builds",
            gameWindowWidth = 1280,
            gameWindowHeight = 720,
            gameTarget = target
        };
    }

    /// <summary>
    /// Creates an isolated game profile from the current defaults.
    /// </summary>
    /// <param name="projectId">
    /// The authoritative project identity copied into the game profile.
    /// </param>
    /// <returns>
    /// A newly owned profile that may be changed for one build without mutating these settings.
    /// </returns>
    public BuildProfile CreateGameProfile(ProjectId projectId)
        => new()
        {
            applicationId = projectId.value,
            productName = gameProductName,
            startupScene = gameStartupScene,
            target = gameTarget,
            windowWidth = gameWindowWidth,
            windowHeight = gameWindowHeight
        };

    /// <summary>
    /// Creates an isolated copy suitable for a temporary export draft.
    /// </summary>
    /// <returns>
    /// A newly owned settings object.
    /// </returns>
    public BuildSettings Copy()
        => new()
        {
            pluginDisplayName = pluginDisplayName,
            pluginOutputPath = pluginOutputPath,
            includePluginDependencies = includePluginDependencies,
            gameProductName = gameProductName,
            gameStartupScene = gameStartupScene,
            gameOutputDirectory = gameOutputDirectory,
            gameWindowWidth = gameWindowWidth,
            gameWindowHeight = gameWindowHeight,
            m_gameTargetId = m_gameTargetId
        };

    internal void ValidateDocument()
    {
        if (pluginDisplayName is null
            || pluginOutputPath is null
            || gameProductName is null
            || gameStartupScene is null
            || gameOutputDirectory is null
            || m_gameTargetId is null)
        {
            throw new InvalidDataException("Build Settings contains a null string.");
        }
        if (gameWindowWidth <= 0 || gameWindowHeight <= 0)
            throw new InvalidDataException("Build Settings window dimensions must be positive.");
        BuildTargetId target;
        try
        {
            target = gameTarget;
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("Build Settings contains an invalid target.", exception);
        }
        if (target != BuildTargetId.macOSArm64 && target != BuildTargetId.windowsX64)
            throw new InvalidDataException($"Build target '{target}' is not supported.");
    }
}
