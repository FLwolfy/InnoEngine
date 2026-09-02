using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Serialization;

namespace Inno.Core.Settings;

/// <summary>
/// Identifies one host-neutral project setting protocol.
/// </summary>
public record struct ProjectSettingId
{
    /// <summary>
    /// Creates a project setting identifier.
    /// </summary>
    /// <param name="value">
    /// Globally stable setting value.
    /// </param>
    public ProjectSettingId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        this.value = value.Trim();
    }

    /// <summary>
    /// Gets or sets the globally stable setting value.
    /// </summary>
    public string value { get; set; }

    /// <summary>
    /// Gets whether the identifier has a usable value.
    /// </summary>
    public readonly bool isValid => !string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// Formats this value as a human-readable representation.
    /// </summary>
    /// <returns>
    /// The human-readable representation of this value.
    /// </returns>
    public readonly override string ToString() => value ?? string.Empty;
}

/// <summary>
/// Declares a reloadable settings type under one stable protocol identity.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ProjectSettingDefinitionAttribute : Attribute
{
    /// <summary>
    /// Creates a project setting declaration.
    /// </summary>
    /// <param name="id">
    /// Globally stable setting identifier.
    /// </param>
    public ProjectSettingDefinitionAttribute(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        this.id = id;
    }

    /// <summary>
    /// Gets the globally stable setting identifier.
    /// </summary>
    public string id { get; }
}

/// <summary>
/// Stores one protocol-owned neutral setting contribution for persistence and composition.
/// </summary>
public struct ProjectSettingRecord
{
    /// <summary>
    /// Creates a setting record.
    /// </summary>
    /// <param name="id">
    /// Stable setting identity.
    /// </param>
    /// <param name="stableTypeId">
    /// Stable settings type identity.
    /// </param>
    /// <param name="propertyData">
    /// Composer-owned contribution bytes, or complete property bytes for replacement settings.
    /// </param>
    public ProjectSettingRecord(ProjectSettingId id, Guid stableTypeId, ReadOnlySpan<byte> propertyData)
    {
        if (!id.isValid)
            throw new ArgumentException("A project setting ID must be valid.", nameof(id));
        if (stableTypeId == Guid.Empty)
            throw new ArgumentException("A project setting requires a stable type ID.", nameof(stableTypeId));
        this.id = id;
        this.stableTypeId = stableTypeId;
        this.propertyData = propertyData.ToArray();
    }

    /// <summary>
    /// Gets or sets the stable setting identity.
    /// </summary>
    public ProjectSettingId id { get; set; }

    /// <summary>
    /// Gets or sets the stable settings type identity.
    /// </summary>
    public Guid stableTypeId { get; set; }

    /// <summary>
    /// Gets or sets the composer-owned payload or default complete-property payload.
    /// </summary>
    public byte[] propertyData { get; set; }
}

/// <summary>
/// Stores project-authored setting contributions in one native document.
/// </summary>
public sealed class ProjectSettingsDocument : ISerializable
{
    /// <summary>
    /// Gets or sets project-authored protocol contributions.
    /// </summary>
    [SerializableProperty]
    public ProjectSettingRecord[] overrides { get; set; } = [];
}

/// <summary>
/// Describes one dependency-ordered provider of default setting values.
/// </summary>
public sealed class ProjectSettingsContributor
{
    /// <summary>
    /// Creates one settings contributor.
    /// </summary>
    /// <param name="id">
    /// Stable contributor identity.
    /// </param>
    /// <param name="dependencies">
    /// Contributors that must precede this contributor.
    /// </param>
    /// <param name="overrides">
    /// Dependencies whose defaults may be explicitly replaced.
    /// </param>
    /// <param name="settings">
    /// Default setting contributions.
    /// </param>
    public ProjectSettingsContributor(
        string id,
        IEnumerable<string> dependencies,
        IEnumerable<string> overrides,
        IEnumerable<ProjectSettingRecord> settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(overrides);
        ArgumentNullException.ThrowIfNull(settings);
        this.id = id;
        this.dependencies = dependencies.ToArray();
        this.overrides = overrides.ToArray();
        this.settings = settings.ToArray();
    }

    /// <summary>
    /// Gets the stable contributor identity.
    /// </summary>
    public string id { get; }

    /// <summary>
    /// Gets contributors that must precede this contributor.
    /// </summary>
    public IReadOnlyList<string> dependencies { get; }

    /// <summary>
    /// Gets dependencies whose defaults may be replaced.
    /// </summary>
    public IReadOnlyList<string> overrides { get; }

    /// <summary>
    /// Gets default setting contributions.
    /// </summary>
    public IReadOnlyList<ProjectSettingRecord> settings { get; }
}
