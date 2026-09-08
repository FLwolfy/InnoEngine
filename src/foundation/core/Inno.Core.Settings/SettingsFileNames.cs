namespace Inno.Core.Settings;

/// <summary>
/// Defines the canonical project-root names of the independent settings documents.
/// </summary>
public static class SettingsFileNames
{
    /// <summary>
    /// Gets the machine/editor preference document name.
    /// </summary>
    public const string editor = "Settings.Editor.inno";

    /// <summary>
    /// Gets the runtime project settings document name.
    /// </summary>
    public const string project = "Settings.Project.inno";

    /// <summary>
    /// Gets the authoring build-default document name.
    /// </summary>
    public const string build = "Settings.Build.inno";
}
