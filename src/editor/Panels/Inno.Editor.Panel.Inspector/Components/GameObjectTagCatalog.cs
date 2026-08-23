using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Editor.Core;
using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Inspector;

/// <summary>
/// Stores the human-readable project tag catalog used by GameObject Inspector controls.
/// </summary>
internal sealed class GameObjectTagCatalog
{
    private readonly SortedSet<string> m_tags = new(StringComparer.Ordinal);

    /// <summary>
    /// Creates an empty project tag catalog containing the built-in default tag.
    /// </summary>
    internal GameObjectTagCatalog()
    {
        m_tags.Add(GameObject.defaultTag);
    }

    /// <summary>
    /// Gets the project tags with the built-in default tag first and remaining entries sorted ordinally.
    /// </summary>
    /// <returns>A stable tag snapshot.</returns>
    internal IReadOnlyList<string> GetTags()
        => m_tags
            .OrderBy(static tag => string.Equals(tag, GameObject.defaultTag, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(static tag => tag, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Adds a project tag after trimming surrounding white space.
    /// </summary>
    /// <param name="tag">The tag to add.</param>
    /// <returns><see langword="true"/> when a new tag was added.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="tag"/> is empty or contains a line break.
    /// </exception>
    internal bool Add(string tag)
    {
        string normalized = Normalize(tag);
        return m_tags.Add(normalized);
    }

    /// <summary>
    /// Removes a custom project tag definition.
    /// </summary>
    /// <param name="tag">The custom tag to remove.</param>
    /// <returns>
    /// <see langword="true"/> when the tag was removed; otherwise, <see langword="false"/>.
    /// </returns>
    internal bool Remove(string tag)
    {
        string normalized = Normalize(tag);
        return !string.Equals(normalized, GameObject.defaultTag, StringComparison.Ordinal) &&
               m_tags.Remove(normalized);
    }

    /// <summary>
    /// Imports tags already present in loaded scene data into the project catalog.
    /// </summary>
    /// <param name="tags">The scene tags that must remain selectable.</param>
    internal void Synchronize(IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        foreach (string tag in tags)
            _ = m_tags.Add(Normalize(tag));
    }

    /// <summary>
    /// Captures the catalog into the owning module's readable workspace section.
    /// </summary>
    /// <param name="writer">The isolated workspace writer assigned to the Inspection module.</param>
    internal void Capture(EditorWorkspaceStateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Set("tags", GetTags());
    }

    /// <summary>
    /// Restores the catalog from the owning module's readable workspace section.
    /// </summary>
    /// <param name="reader">The isolated workspace reader assigned to the Inspection module.</param>
    internal void Restore(EditorWorkspaceStateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        m_tags.Clear();
        m_tags.Add(GameObject.defaultTag);
        string[] tags = reader.Get("tags", Array.Empty<string>());
        for (int i = 0; i < tags.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(tags[i]))
                _ = m_tags.Add(Normalize(tags[i]));
        }
    }

    private static string Normalize(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        string normalized = tag.Trim();
        if (normalized.Contains('\r') || normalized.Contains('\n'))
            throw new ArgumentException("Tags cannot contain line breaks.", nameof(tag));
        return normalized;
    }
}
