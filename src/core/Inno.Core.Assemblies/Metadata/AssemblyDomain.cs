namespace Inno.Core.Assemblies;

/// <summary>
/// Identifies the ownership and reload boundary of a managed assembly.
/// </summary>
public enum AssemblyDomain
{
    /// <summary>The assembly is engine-owned and remains in the default load context.</summary>
    InnoInternal,

    /// <summary>The assembly contains project scripting code in a collectible load context.</summary>
    InnoScripting,

    /// <summary>The assembly belongs to the project's unified collectible plugin generation.</summary>
    InnoPlugin
}
