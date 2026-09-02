using System;
using System.IO;
using System.Linq;

namespace Inno.Build;

/// <summary>
/// Requests one deterministic project-to-Plugin package without a companion authoring asset.
/// </summary>
public sealed class PluginBuildRequest
{
    /// <summary>
    /// Gets or initializes the globally stable lowercase Plugin identity.
    /// </summary>
    public required string pluginId { get; init; }

    /// <summary>
    /// Gets or initializes the user-facing Plugin name.
    /// </summary>
    public required string displayName { get; init; }

    /// <summary>
    /// Gets or initializes the destination ZIP path.
    /// </summary>
    public required string outputPath { get; init; }

    /// <summary>
    /// Gets or initializes whether the complete transitive dependency closure is embedded.
    /// </summary>
    public bool includeDependencies { get; init; }

    /// <summary>
    /// Validates package identity and destination syntax.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// Thrown when the request cannot identify a portable Plugin package.
    /// </exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(pluginId)
            || pluginId == "project"
            || pluginId.Any(static character =>
                !(character is >= 'a' and <= 'z'
                  || character is >= '0' and <= '9'
                  || character is '.' or '_' or '-')))
        {
            throw new InvalidDataException("Plugin ID must be a non-reserved lowercase portable identifier.");
        }
        if (string.IsNullOrWhiteSpace(displayName))
            throw new InvalidDataException("A Plugin display name is required.");
        if (string.IsNullOrWhiteSpace(outputPath)
            || !string.Equals(Path.GetExtension(outputPath), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Plugin destination must use the .zip extension.");
        }
    }
}
