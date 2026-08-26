namespace Inno.Core.Assemblies;

/// <summary>
/// Identifies whether an assembly can participate in runtime or editor-only dependency graphs.
/// </summary>
public enum AssemblyScope
{
    /// <summary>The assembly is available to runtime and editor consumers.</summary>
    Runtime,

    /// <summary>The assembly is available only to editor consumers.</summary>
    Editor
}
