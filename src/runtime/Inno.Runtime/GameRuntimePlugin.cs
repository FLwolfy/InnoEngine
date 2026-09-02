using System;
using System.IO;
using System.Linq;
using Inno.Core.Serialization;
using Inno.Core.Settings;

namespace Inno.Runtime;

/// <summary>
/// Stores one Plugin's runtime-only project setting contribution.
/// </summary>
[GenerateSerializationConverter]
public sealed class GameRuntimePlugin : ISerializable
{
    /// <summary>
    /// Gets or sets the stable Plugin identifier.
    /// </summary>
    [SerializableProperty]
    public string id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets Plugin IDs that must precede this Plugin.
    /// </summary>
    [SerializableProperty]
    public string[] dependencies { get; set; } = [];

    /// <summary>
    /// Gets or sets dependencies whose setting defaults may be replaced.
    /// </summary>
    [SerializableProperty]
    public string[] overrides { get; set; } = [];

    /// <summary>
    /// Gets or sets neutral setting contribution records.
    /// </summary>
    [SerializableProperty]
    public ProjectSettingRecord[] settings { get; set; } = [];

    /// <summary>
    /// Validates identity and ownership declarations.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// Thrown when the contribution is malformed.
    /// </exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidDataException("A runtime Plugin contribution requires an ID.");
        if (dependencies is null || overrides is null || settings is null)
            throw new InvalidDataException($"Runtime Plugin '{id}' contains a null collection.");
        if (dependencies.Any(string.IsNullOrWhiteSpace)
            || dependencies.Distinct(StringComparer.Ordinal).Count() != dependencies.Length)
        {
            throw new InvalidDataException($"Runtime Plugin '{id}' has invalid dependencies.");
        }
        if (overrides.Any(dependency => !dependencies.Contains(dependency, StringComparer.Ordinal)))
            throw new InvalidDataException($"Runtime Plugin '{id}' overrides an undeclared dependency.");
        if (settings.Select(static setting => setting.id).Distinct().Count() != settings.Length)
            throw new InvalidDataException($"Runtime Plugin '{id}' has duplicate setting contributions.");
        if (settings.Any(static setting =>
                !setting.id.isValid
                || setting.stableTypeId == Guid.Empty
                || setting.propertyData is null
                || setting.propertyData.Length == 0))
        {
            throw new InvalidDataException($"Runtime Plugin '{id}' has an invalid setting contribution.");
        }
    }
}
