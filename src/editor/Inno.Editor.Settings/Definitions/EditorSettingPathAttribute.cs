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
    /// <param name="path">The slash-delimited page and field path.</param>
    /// <param name="order">The stable order among fields with the same section and label.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="path"/> contains an empty segment.
    /// </exception>
    public EditorSettingPathAttribute(string path, int order = 0)
    {
        this.path = EditorSettingsPathUtility.Normalize(path);
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
