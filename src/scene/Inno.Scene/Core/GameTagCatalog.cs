using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Extensibility.Types;
using Inno.Core.Serialization;
using Inno.Core.Settings;

namespace Inno.Scene;

/// <summary>
/// Stores the project-wide tag definitions used to author and validate scene object assignments.
/// </summary>
[StableTypeId("5bd9122b-8ce7-4ddd-8c47-32e4235f819e")]
[ProjectSettingDefinition("inno.scene.tags")]
public sealed class GameTagCatalog : ISerializable
{
    [SerializableProperty]
    private string[] m_tags = [GameObject.defaultTag];

    /// <summary>
    /// Gets the stable project setting protocol for the project-wide tag catalog.
    /// </summary>
    public static ProjectSettingId settingId => new("inno.scene.tags");

    /// <summary>
    /// Gets the defined tags with the immutable default tag first.
    /// </summary>
    /// <returns>
    /// An independently owned deterministic tag snapshot.
    /// </returns>
    public IReadOnlyList<string> GetTags()
    {
        ValidateState();
        return (string[])m_tags.Clone();
    }

    /// <summary>
    /// Determines whether an ordinal tag is defined by the project.
    /// </summary>
    /// <param name="tag">
    /// The tag to resolve.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the normalized tag is defined.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="tag"/> is empty or invalid.
    /// </exception>
    public bool IsDefined(string tag)
    {
        string normalized = Normalize(tag);
        ValidateState();
        return m_tags.Contains(normalized, StringComparer.Ordinal);
    }

    /// <summary>
    /// Adds one normalized project tag definition.
    /// </summary>
    /// <param name="tag">
    /// The unique tag to define.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a definition was added.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="tag"/> is empty or invalid.
    /// </exception>
    public bool Add(string tag)
    {
        string normalized = Normalize(tag);
        ValidateState();
        if (m_tags.Contains(normalized, StringComparer.Ordinal))
            return false;
        m_tags = m_tags
            .Append(normalized)
            .OrderBy(static value => string.Equals(value, GameObject.defaultTag, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        return true;
    }

    /// <summary>
    /// Removes one custom project tag definition without rewriting scene assignments.
    /// </summary>
    /// <param name="tag">
    /// The custom tag to remove.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when an existing definition was removed.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="tag"/> is empty or invalid.
    /// </exception>
    public bool Remove(string tag)
    {
        string normalized = Normalize(tag);
        ValidateState();
        if (string.Equals(normalized, GameObject.defaultTag, StringComparison.Ordinal))
            return false;
        int index = Array.FindIndex(
            m_tags,
            candidate => string.Equals(candidate, normalized, StringComparison.Ordinal));
        if (index < 0)
            return false;
        m_tags = m_tags.Where(candidate => !string.Equals(candidate, normalized, StringComparison.Ordinal)).ToArray();
        return true;
    }

    /// <summary>
    /// Creates a detached mutable copy of this catalog.
    /// </summary>
    /// <returns>
    /// An independently owned catalog with the same definitions.
    /// </returns>
    public GameTagCatalog Clone()
    {
        ValidateState();
        return new GameTagCatalog { m_tags = (string[])m_tags.Clone() };
    }

    [OnSerializableRestored]
    private void OnSerializableRestored()
        => ValidateState();

    internal static string Normalize(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        string normalized = tag.Trim();
        if (normalized.Contains('\r') || normalized.Contains('\n'))
            throw new ArgumentException("Tags cannot contain line breaks.", nameof(tag));
        return normalized;
    }

    private void ValidateState()
    {
        if (m_tags is null || m_tags.Length == 0)
            throw new InvalidOperationException("A tag catalog must contain the built-in default tag.");
        string[] normalized = m_tags
            .Select(Normalize)
            .OrderBy(static value => string.Equals(value, GameObject.defaultTag, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        if (!string.Equals(normalized[0], GameObject.defaultTag, StringComparison.Ordinal))
            throw new InvalidOperationException("The built-in default tag must be the first project tag.");
        if (normalized.Distinct(StringComparer.Ordinal).Count() != normalized.Length)
            throw new InvalidOperationException("Project tag definitions must be unique.");
        if (!normalized.SequenceEqual(m_tags, StringComparer.Ordinal))
            throw new InvalidOperationException("Project tag definitions are not normalized and deterministically ordered.");
    }
}
