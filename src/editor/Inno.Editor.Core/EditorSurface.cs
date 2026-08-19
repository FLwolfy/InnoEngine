namespace Inno.Editor.Core;

/// <summary>
/// Identifies built-in editor interaction surfaces.
/// Feature assemblies may use their panel or a dedicated marker type as an additional surface.
/// </summary>
public static class EditorSurface
{
    /// <summary>Identifies interactions that are not scoped to a particular view.</summary>
    public sealed class Global;

    /// <summary>Identifies the application main menu.</summary>
    public sealed class MainMenu;
}
