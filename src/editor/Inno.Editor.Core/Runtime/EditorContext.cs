using System;
using System.Collections.Generic;
using System.IO;

namespace Inno.Editor.Core;

/// <summary>
/// Provides passive project and frame state shared by editor extensions.
/// Interaction routing is supplied separately by Inno.Editor.Interactions.
/// </summary>
public sealed class EditorContext
{
    /// <summary>
    /// Creates a passive editor context for one project.
    /// </summary>
    /// <param name="projectDirectory">The project root containing Assets and Library.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="projectDirectory"/> is empty.
    /// </exception>
    internal EditorContext(string projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(projectDirectory))
            throw new ArgumentException("A project directory is required.", nameof(projectDirectory));
        this.projectDirectory = Path.GetFullPath(projectDirectory);
        layout = new EditorLayoutSettings(this.projectDirectory);
    }

    /// <summary>
    /// Gets the normalized project root directory.
    /// </summary>
    public string projectDirectory { get; }

    /// <summary>
    /// Gets the absolute path of the project editor layout document.
    /// </summary>
    internal string layoutPath => layout.path;

    /// <summary>
    /// Gets the Dear ImGui layout text without editor module or panel state sections.
    /// </summary>
    internal string imguiLayout => layout.imguiLayout;

    internal EditorLayoutSettings layout { get; }

    /// <summary>
    /// Gets layout section names matching an ordinal prefix.
    /// </summary>
    /// <param name="prefix">
    /// The prefix to match, or an empty string for every section.
    /// </param>
    /// <returns>
    /// A stable sorted snapshot of matching section names.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="prefix"/> is <see langword="null"/>.
    /// </exception>
    internal IReadOnlyList<string> GetLayoutSectionNames(string prefix = "")
        => layout.GetSectionNames(prefix);

    /// <summary>
    /// Tries to read one independent editor layout section snapshot.
    /// </summary>
    /// <param name="sectionName">
    /// The section name without its INI header syntax.
    /// </param>
    /// <param name="values">
    /// The copied values when the section exists.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested section exists.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="sectionName"/> is empty or cannot be represented in the layout.
    /// </exception>
    internal bool TryGetLayoutSection(
        string sectionName,
        out IReadOnlyDictionary<string, string> values)
        => layout.TryGetSection(sectionName, out values);

    /// <summary>
    /// Adds or replaces one human-readable editor layout section in memory.
    /// </summary>
    /// <param name="sectionName">
    /// The section name without its INI header syntax.
    /// </param>
    /// <param name="values">
    /// The complete scalar or JSON-formatted values.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="values"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when a section name, key, or value cannot be represented in the layout.
    /// </exception>
    internal void SetLayoutSection(
        string sectionName,
        IEnumerable<KeyValuePair<string, string>> values)
        => layout.SetSection(sectionName, values);

    /// <summary>
    /// Removes one editor layout section from the in-memory document.
    /// </summary>
    /// <param name="sectionName">
    /// The section name without its INI header syntax.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when an existing section was removed.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="sectionName"/> is empty or cannot be represented in the layout.
    /// </exception>
    internal bool RemoveLayoutSection(string sectionName)
        => layout.RemoveSection(sectionName);

    /// <summary>
    /// Replaces the Dear ImGui layout while retaining editor module and panel state sections.
    /// </summary>
    /// <param name="value">
    /// The complete layout text returned by Dear ImGui.
    /// </param>
    internal void SetImGuiLayout(string? value)
        => layout.SetImGuiLayout(value);

    /// <summary>
    /// Atomically saves the project editor layout when it changed.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when a changed document was written.
    /// </returns>
    /// <exception cref="IOException">
    /// Thrown when the layout document cannot be written atomically.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">
    /// Thrown when the layout document is inaccessible.
    /// </exception>
    internal bool SaveLayoutIfChanged()
        => layout.SaveIfChanged();

    /// <summary>
    /// Atomically rewrites the complete project editor layout document.
    /// </summary>
    /// <exception cref="IOException">
    /// Thrown when the layout document cannot be written atomically.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">
    /// Thrown when the layout document is inaccessible.
    /// </exception>
    internal void SaveLayout()
        => layout.Save();

    /// <summary>
    /// Gets the latest immutable editor frame snapshot.
    /// </summary>
    public EditorFrame frame { get; internal set; }

    /// <summary>
    /// Gets whether any editor viewport currently owns application focus.
    /// </summary>
    public bool isFocused => frame.isFocused;

}
