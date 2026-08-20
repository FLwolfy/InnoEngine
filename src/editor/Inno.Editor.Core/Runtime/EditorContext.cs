using System;
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
    /// <exception cref="ArgumentException">Thrown when <paramref name="projectDirectory"/> is empty.</exception>
    public EditorContext(string projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(projectDirectory))
            throw new ArgumentException("A project directory is required.", nameof(projectDirectory));
        this.projectDirectory = Path.GetFullPath(projectDirectory);
    }

    /// <summary>Gets the normalized project root directory.</summary>
    public string projectDirectory { get; }

    /// <summary>Gets the latest immutable editor frame snapshot.</summary>
    public EditorFrame frame { get; internal set; }

    /// <summary>Gets whether any editor viewport currently owns application focus.</summary>
    public bool isFocused => frame.isFocused;
}
