using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Inno.Core.Serialization;
using Inno.Core.Settings;

namespace Inno.Runtime;

/// <summary>
/// Stores the immutable startup contract consumed by a deployed game Player.
/// </summary>
public sealed class GameRuntimeManifest : ISerializable
{
    /// <summary>
    /// Gets or sets the stable lowercase application identifier.
    /// </summary>
    [SerializableProperty]
    public string applicationId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the player-facing product name.
    /// </summary>
    [SerializableProperty]
    public string productName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the mount-qualified startup scene path.
    /// </summary>
    [SerializableProperty]
    public string startupScene { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the initial logical window width.
    /// </summary>
    [SerializableProperty]
    public int windowWidth { get; set; } = 1280;

    /// <summary>
    /// Gets or sets the initial logical window height.
    /// </summary>
    [SerializableProperty]
    public int windowHeight { get; set; } = 720;

    /// <summary>
    /// Gets or sets dependency-ordered runtime Plugin setting contributions.
    /// </summary>
    [SerializableProperty]
    public GameRuntimePlugin[] plugins { get; set; } = [];

    /// <summary>
    /// Validates startup identity, dimensions, scene, and Plugin ordering.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// Thrown when manifest data cannot be executed safely.
    /// </exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(applicationId)
            || applicationId.Any(static character =>
                !(character is >= 'a' and <= 'z'
                  || character is >= '0' and <= '9'
                  || character is '.' or '_' or '-')))
        {
            throw new InvalidDataException("A game application ID must use lowercase portable identifier characters.");
        }
        if (string.IsNullOrWhiteSpace(productName))
            throw new InvalidDataException("A game product name is required.");
        if (string.IsNullOrWhiteSpace(startupScene))
            throw new InvalidDataException("A game startup scene is required.");
        if (windowWidth <= 0 || windowHeight <= 0)
            throw new InvalidDataException("Game window dimensions must be positive.");
        if (plugins is null)
            throw new InvalidDataException("Game Plugin contributions cannot be null.");

        var accepted = new HashSet<string>(StringComparer.Ordinal);
        foreach (GameRuntimePlugin plugin in plugins)
        {
            plugin.Validate();
            if (!accepted.Add(plugin.id))
                throw new InvalidDataException($"Game Plugin '{plugin.id}' is declared more than once.");
            string? missing = plugin.dependencies.FirstOrDefault(dependency => !accepted.Contains(dependency));
            if (missing is not null)
            {
                throw new InvalidDataException(
                    $"Game Plugin '{plugin.id}' appears before dependency '{missing}'.");
            }
        }
    }

    /// <summary>
    /// Creates current-generation setting contributors from neutral manifest data.
    /// </summary>
    /// <returns>
    /// Dependency-ordered contributors ready for <see cref="ProjectSettingsStore"/>.
    /// </returns>
    public IReadOnlyList<ProjectSettingsContributor> CreateSettingContributors()
    {
        Validate();
        return plugins.Select(static plugin => new ProjectSettingsContributor(
            plugin.id,
            plugin.dependencies,
            plugin.overrides,
            plugin.settings)).ToArray();
    }
}
