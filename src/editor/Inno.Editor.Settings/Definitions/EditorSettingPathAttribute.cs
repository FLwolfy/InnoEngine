using System;

namespace Inno.Editor.Settings;

/// <summary>
/// Places an <see cref="EditorSetting"/> at an arbitrary string Settings path.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EditorSettingPathAttribute : Attribute
{
    /// <summary>
    /// Creates a path placement. A definition that overrides its drawing method becomes a field;
    /// a definition that keeps the default drawing method describes the page at the complete path.
    /// </summary>
    /// <param name="path">
    /// The slash-delimited page and field path.
    /// </param>
    /// <param name="order">
    /// The stable order among fields with the same section and label.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="path"/> contains an empty segment or is outside the <c>Editor</c> root.
    /// </exception>
    public EditorSettingPathAttribute(string path, int order = 0)
    {
        string normalized = EditorSettingsPathUtility.Normalize(path);
        if (!string.Equals(normalized, "Editor", StringComparison.Ordinal) &&
            !normalized.StartsWith("Editor/", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "An Editor setting path must be 'Editor' or begin with 'Editor/'.",
                nameof(path));
        }
        this.path = normalized;
        this.order = order;
    }

    /// <summary>
    /// Gets the normalized path including the field label.
    /// </summary>
    public string path { get; }

    /// <summary>
    /// Gets the stable order among fields with the same section and label.
    /// </summary>
    public int order { get; }
}
