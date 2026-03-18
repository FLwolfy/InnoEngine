namespace Inno.Core.Reflection;

/// <summary>
/// Logical group classification for Inno assemblies.
/// </summary>
public enum AssemblyGroup
{
    /// <summary>
    /// No explicit group metadata was found.
    /// </summary>
    None,

    /// <summary>
    /// Game-level assembly group.
    /// </summary>
    Game,

    /// <summary>
    /// Core runtime assembly group.
    /// </summary>
    Core,

    /// <summary>
    /// Plugin assembly group.
    /// </summary>
    Plugin
}
