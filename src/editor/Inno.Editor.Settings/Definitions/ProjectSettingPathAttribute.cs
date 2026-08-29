using System;

namespace Inno.Editor.Settings;

/// <summary>
/// Places a strongly typed project setting editor under the Project root of the unified Settings window.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ProjectSettingPathAttribute : Attribute
{
    /// <summary>
    /// Creates a project setting placement.
    /// </summary>
    /// <param name="path">The slash-delimited field path beginning with <c>Project/</c>.</param>
    /// <param name="order">The stable order among fields in the same section.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="path"/> is outside the Project root or contains an empty segment.
    /// </exception>
    public ProjectSettingPathAttribute(string path, int order = 0)
    {
        string normalized = EditorSettingsPathUtility.Normalize(path);
        if (!normalized.StartsWith("Project/", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A project setting editor path must begin with 'Project/'.",
                nameof(path));
        }
        this.path = normalized;
        this.order = order;
    }

    /// <summary>Gets the normalized complete placement path.</summary>
    public string path { get; }

    /// <summary>Gets the stable order among fields in the same section.</summary>
    public int order { get; }
}
