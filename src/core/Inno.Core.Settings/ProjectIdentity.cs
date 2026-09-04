using System;
using System.Text;

using Inno.Core.Serialization;
using Inno.Extensibility.Types;

namespace Inno.Core.Settings;

/// <summary>
/// Identifies one project namespace used to qualify project-authored logical names.
/// </summary>
public readonly record struct ProjectId
{
    /// <summary>
    /// Creates a portable project identifier.
    /// </summary>
    /// <param name="value">
    /// Lowercase ASCII namespace segments.
    /// </param>
    public ProjectId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized = value.Trim();
        ValidatePortable(normalized, "Project ID", nameof(value));
        this.value = normalized;
    }

    /// <summary>
    /// Creates a portable initial project identifier from a user-facing project name.
    /// </summary>
    /// <param name="name">
    /// The project name used to derive the initial namespace.
    /// </param>
    /// <returns>
    /// A portable initial project identifier.
    /// </returns>
    public static ProjectId FromName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var builder = new StringBuilder(name.Length);
        bool pendingSeparator = false;
        foreach (char character in name.Trim())
        {
            if (character is >= 'A' and <= 'Z')
                Append((char)(character + ('a' - 'A')));
            else if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
                Append(character);
            else
                pendingSeparator = builder.Length > 0;
        }
        string value = builder.Length == 0 ? "inno.project" : builder.ToString();
        return new ProjectId(string.Equals(value, "project", StringComparison.Ordinal)
            ? "inno.project"
            : value);

        void Append(char character)
        {
            if (pendingSeparator)
                builder.Append('-');
            pendingSeparator = false;
            builder.Append(character);
        }
    }

    /// <summary>
    /// Gets the canonical identifier text.
    /// </summary>
    public string value { get; }

    /// <summary>
    /// Gets whether the value contains a usable identifier.
    /// </summary>
    public bool isValid => !string.IsNullOrEmpty(value);

    /// <summary>
    /// Qualifies a local project name beneath this project namespace.
    /// </summary>
    /// <param name="name">
    /// The portable local name.
    /// </param>
    /// <returns>
    /// The complete project-scoped identity.
    /// </returns>
    public ProjectScopedId Qualify(ProjectLocalId name)
        => new(this, name);

    /// <summary>
    /// Formats the canonical identifier.
    /// </summary>
    /// <returns>
    /// The canonical identifier text.
    /// </returns>
    public override string ToString() => value ?? string.Empty;

    internal static void ValidatePortable(string value, string label, string parameterName)
    {
        if (value.Length > 128)
            throw new ArgumentException($"{label} cannot exceed 128 characters.", parameterName);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            bool valid = character is >= 'a' and <= 'z'
                         || character is >= '0' and <= '9'
                         || character is '.' or '-' or '_';
            if (!valid)
            {
                throw new ArgumentException(
                    $"{label} must use lowercase ASCII letters, digits, dots, hyphens, or underscores.",
                    parameterName);
            }
        }
        if (value[0] is '.' or '-' or '_' || value[^1] is '.' or '-' or '_')
            throw new ArgumentException($"{label} must begin and end with a letter or digit.", parameterName);
        if (value.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException($"{label} cannot contain empty namespace segments.", parameterName);
    }
}

/// <summary>
/// Stores the project-independent portion of one project-scoped identity.
/// </summary>
public readonly record struct ProjectLocalId
{
    /// <summary>
    /// Creates a portable local identity.
    /// </summary>
    /// <param name="value">
    /// Lowercase ASCII local namespace segments.
    /// </param>
    public ProjectLocalId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized = value.Trim();
        ProjectId.ValidatePortable(normalized, "Project-local ID", nameof(value));
        this.value = normalized;
    }

    /// <summary>
    /// Gets the canonical local identity text.
    /// </summary>
    public string value { get; }

    /// <summary>
    /// Creates a deterministic portable local identity from a display name.
    /// </summary>
    /// <param name="name">
    /// The user-facing name.
    /// </param>
    /// <returns>
    /// The normalized local identity.
    /// </returns>
    public static ProjectLocalId FromName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var builder = new StringBuilder(name.Length);
        bool pendingSeparator = false;
        foreach (char character in name.Trim())
        {
            if (character is >= 'A' and <= 'Z')
            {
                AppendSeparator(builder, ref pendingSeparator);
                builder.Append((char)(character + ('a' - 'A')));
            }
            else if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                AppendSeparator(builder, ref pendingSeparator);
                builder.Append(character);
            }
            else if (character is ' ' or '\t' or '.' or '-' or '_')
            {
                pendingSeparator = builder.Length > 0;
            }
            else
            {
                throw new ArgumentException(
                    "A project-scoped name must use ASCII letters, digits, spaces, dots, hyphens, or underscores.",
                    nameof(name));
            }
        }
        if (builder.Length == 0)
            throw new ArgumentException("A project-scoped name must contain a letter or digit.", nameof(name));
        return new ProjectLocalId(builder.ToString());
    }

    /// <summary>
    /// Formats the canonical local identity.
    /// </summary>
    /// <returns>
    /// The canonical local identity text.
    /// </returns>
    public override string ToString() => value ?? string.Empty;

    private static void AppendSeparator(StringBuilder builder, ref bool pendingSeparator)
    {
        if (pendingSeparator && builder.Length > 0)
            builder.Append('-');
        pendingSeparator = false;
    }
}

/// <summary>
/// Combines a mutable project namespace with a stable project-independent local identity.
/// </summary>
public readonly record struct ProjectScopedId
{
    /// <summary>
    /// Creates a qualified project identity.
    /// </summary>
    /// <param name="projectId">
    /// The current project namespace.
    /// </param>
    /// <param name="name">
    /// The stable local identity.
    /// </param>
    public ProjectScopedId(ProjectId projectId, ProjectLocalId name)
    {
        if (!projectId.isValid)
            throw new ArgumentException("A valid Project ID is required.", nameof(projectId));
        if (string.IsNullOrEmpty(name.value))
            throw new ArgumentException("A valid project-local ID is required.", nameof(name));
        this.projectId = projectId;
        this.name = name;
    }

    /// <summary>
    /// Gets the project namespace.
    /// </summary>
    public ProjectId projectId { get; }

    /// <summary>
    /// Gets the project-independent local identity.
    /// </summary>
    public ProjectLocalId name { get; }

    /// <summary>
    /// Gets the canonical <c>projectId.name</c> representation.
    /// </summary>
    public string value => $"{projectId.value}.{name.value}";

    /// <summary>
    /// Formats the canonical qualified identity.
    /// </summary>
    /// <returns>
    /// The <c>projectId.name</c> representation.
    /// </returns>
    public override string ToString() => value;
}

/// <summary>
/// Stores the editable identity namespace of the current project.
/// </summary>
[StableTypeId("d5f90462-506d-4ff7-aa9f-0f14da28b9c3")]
[ProjectSettingDefinition("inno.project.identity", allowPluginContributions: false)]
public sealed class ProjectIdentitySettings : ISerializable
{
    private string m_projectId = "inno.project";

    /// <summary>
    /// Gets the stable project setting protocol identity.
    /// </summary>
    public static ProjectSettingId settingId => new("inno.project.identity");

    /// <summary>
    /// Gets or sets the editable project namespace.
    /// </summary>
    [SerializableProperty]
    public string projectId
    {
        get => m_projectId;
        set => m_projectId = new ProjectId(value).value;
    }

    /// <summary>
    /// Gets the validated project identifier.
    /// </summary>
    public ProjectId id => new(m_projectId);

    /// <summary>
    /// Qualifies a display name under the current project identifier.
    /// </summary>
    /// <param name="name">
    /// The project-local display name.
    /// </param>
    /// <returns>
    /// The complete project-scoped identity.
    /// </returns>
    public ProjectScopedId Qualify(string name)
        => id.Qualify(ProjectLocalId.FromName(name));
}
