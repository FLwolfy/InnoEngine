using System;

using Inno.Core.Settings;

namespace Inno.Scene.Layers;

/// <summary>
/// Identifies one project-scoped game layer independently from its compact runtime slot.
/// </summary>
public readonly record struct GameLayerId
{
    /// <summary>
    /// Creates a project-scoped layer identity.
    /// </summary>
    /// <param name="value">
    /// The canonical qualified identity.
    /// </param>
    public GameLayerId(ProjectScopedId value)
    {
        if (string.IsNullOrEmpty(value.name.value))
            throw new ArgumentException("A GameLayer ID requires a valid project-local name.", nameof(value));
        this.value = value.value;
    }

    /// <summary>
    /// Creates a project-scoped layer identity from its project and local parts.
    /// </summary>
    /// <param name="projectId">
    /// The current project identity.
    /// </param>
    /// <param name="name">
    /// The stable project-local layer identity.
    /// </param>
    public GameLayerId(ProjectId projectId, ProjectLocalId name)
        : this(projectId.Qualify(name))
    {
    }

    internal GameLayerId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized = value.Trim();
        if (!normalized.Contains('.', StringComparison.Ordinal))
            throw new ArgumentException("A GameLayer ID must contain project and local name segments.", nameof(value));
        for (int index = 0; index < normalized.Length; index++)
        {
            char character = normalized[index];
            bool valid = character is >= 'a' and <= 'z'
                         || character is >= '0' and <= '9'
                         || character is '.' or '-' or '_';
            if (!valid)
                throw new ArgumentException("A GameLayer ID is not portable.", nameof(value));
        }
        this.value = normalized;
    }

    /// <summary>
    /// Gets the canonical <c>projectId.name</c> identity string.
    /// </summary>
    public string value { get; }

    /// <summary>
    /// Gets whether this value contains a usable identity.
    /// </summary>
    public bool isValid => !string.IsNullOrEmpty(value);

    /// <summary>
    /// Formats this value as a human-readable representation.
    /// </summary>
    /// <returns>
    /// The qualified identity.
    /// </returns>
    public override string ToString() => value ?? string.Empty;
}
