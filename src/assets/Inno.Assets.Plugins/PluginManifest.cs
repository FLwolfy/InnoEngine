using System;
using System.IO;
using System.Linq;
using Inno.Core.Settings;
using Inno.Core.Serialization;

namespace Inno.Assets.Plugins;

/// <summary>Describes one local ZIP Plugin without listing discovered extension types.</summary>
public sealed class PluginManifest : ISerializable
{
    /// <summary>Gets or sets the globally stable lowercase Plugin ID.</summary>
    [SerializableProperty]
    public string pluginId { get; set; } = string.Empty;

    /// <summary>Gets or sets the artist-facing display name.</summary>
    [SerializableProperty]
    public string displayName { get; set; } = string.Empty;

    /// <summary>Gets or sets Plugin IDs that must activate before this Plugin.</summary>
    [SerializableProperty]
    public string[] dependencies { get; set; } = [];

    /// <summary>Gets or sets dependency Plugin IDs whose contributions may be explicitly replaced.</summary>
    [SerializableProperty]
    public string[] overrides { get; set; } = [];

    /// <summary>Gets or sets source-local content roots relative to the archive.</summary>
    [SerializableProperty]
    public string[] contentRoots { get; set; } = ["Assets"];

    /// <summary>Gets or sets source-local assembly definition entries.</summary>
    [SerializableProperty]
    public string[] assemblyDefinitions { get; set; } = [];

    /// <summary>Gets or sets default project setting contributions.</summary>
    [SerializableProperty]
    public ProjectSettingRecord[] settingContributions { get; set; } = [];

    /// <summary>Validates stable identity and dependency declarations.</summary>
    /// <exception cref="InvalidDataException">Thrown when manifest data is invalid.</exception>
    public void Validate()
    {
        try
        {
            _ = new Inno.Assets.Core.AssetSourceId(pluginId);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("Plugin.inno contains an invalid Plugin ID.", exception);
        }
        if (string.Equals(pluginId, Inno.Assets.Core.AssetSourceId.project.value, StringComparison.Ordinal))
            throw new InvalidDataException("Plugin ID 'project' is reserved by the writable project source.");
        if (string.IsNullOrWhiteSpace(displayName))
            throw new InvalidDataException("Plugin.inno requires a display name.");
        EnsureUniquePluginIds(dependencies, nameof(dependencies));
        EnsureUniquePluginIds(overrides, nameof(overrides));
        EnsureUniquePaths(contentRoots, nameof(contentRoots));
        EnsureUniquePaths(assemblyDefinitions, nameof(assemblyDefinitions));
        if (dependencies.Contains(pluginId, StringComparer.Ordinal))
            throw new InvalidDataException("A Plugin cannot depend on itself.");
        if (overrides.Any(value => !dependencies.Contains(value, StringComparer.Ordinal)))
            throw new InvalidDataException("Every explicit override must identify a declared dependency.");
        if (settingContributions is null)
            throw new InvalidDataException("Plugin setting contributions cannot be null.");
        if (settingContributions.Select(static value => value.id).Distinct().Count() != settingContributions.Length)
            throw new InvalidDataException("Plugin setting contributions must use unique setting IDs.");
        foreach (ProjectSettingRecord contribution in settingContributions)
        {
            if (!contribution.id.isValid
                || contribution.stableTypeId == Guid.Empty
                || contribution.propertyData is null
                || contribution.propertyData.Length == 0)
            {
                throw new InvalidDataException("Plugin setting contribution contains invalid identity or payload data.");
            }
        }
        if (contentRoots.Length != 1 || !string.Equals(contentRoots[0], "Assets", StringComparison.Ordinal))
            throw new InvalidDataException("The current local Plugin container requires the single content root 'Assets'.");
    }

    private static void EnsureUniquePluginIds(string[] values, string name)
    {
        if (values is null || values.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException($"Plugin manifest field '{name}' contains an empty value.");
        if (values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new InvalidDataException($"Plugin manifest field '{name}' contains duplicate values.");
        foreach (string value in values)
            _ = new Inno.Assets.Core.AssetSourceId(value);
    }

    private static void EnsureUniquePaths(string[] values, string name)
    {
        if (values is null || values.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException($"Plugin manifest field '{name}' contains an empty path.");
        string[] normalized = values.Select(static value =>
        {
            if (value.Contains('\\') || value.StartsWith("/", StringComparison.Ordinal))
                throw new InvalidDataException("Plugin manifest paths must be portable archive-relative paths.");
            string path = value.Trim('/');
            if (path.Split('/').Any(static segment => segment is "" or "." or ".."))
                throw new InvalidDataException("Plugin manifest paths cannot contain traversal segments.");
            return path;
        }).ToArray();
        if (normalized.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalized.Length)
            throw new InvalidDataException($"Plugin manifest field '{name}' contains duplicate paths.");
    }
}
