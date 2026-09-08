using System;
using System.IO;
using System.Linq;
using Inno.Core.Serialization;
using Inno.Extensibility.Modules;

namespace Inno.Runtime;

/// <summary>
/// Describes one dependency-ordered managed module in a frozen Player generation.
/// </summary>
[GenerateSerializationConverter]
public sealed class GameRuntimeModule : ISerializable
{
    /// <summary>
    /// Gets or sets the stable module name used by the managed module host.
    /// </summary>
    [SerializableProperty]
    public string name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the ownership domain declared by the deployed module.
    /// </summary>
    [SerializableProperty]
    public AssemblyDomain domain { get; set; }

    /// <summary>
    /// Gets or sets the primary assembly file name within the deployed Managed directory.
    /// </summary>
    [SerializableProperty]
    public string mainAssembly { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets additional runtime assembly file names loaded into the same module context.
    /// </summary>
    [SerializableProperty]
    public string[] preloadAssemblies { get; set; } = [];

    /// <summary>
    /// Gets or sets stable module names that must be active before this module.
    /// </summary>
    [SerializableProperty]
    public string[] dependencies { get; set; } = [];

    /// <summary>
    /// Validates module identity, ownership, file names, and dependency declarations.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// Thrown when the module cannot be activated as part of a frozen Player generation.
    /// </exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidDataException("A runtime module requires a stable name.");
        if (domain is not (AssemblyDomain.InnoPlugin or AssemblyDomain.InnoScripting))
            throw new InvalidDataException($"Runtime module '{name}' has an invalid ownership domain.");
        if (!IsAssemblyFileName(mainAssembly))
            throw new InvalidDataException($"Runtime module '{name}' has an invalid primary assembly file name.");
        if (preloadAssemblies is null || dependencies is null)
            throw new InvalidDataException($"Runtime module '{name}' contains a null collection.");
        if (preloadAssemblies.Any(static value => !IsAssemblyFileName(value))
            || preloadAssemblies.Append(mainAssembly).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            != preloadAssemblies.Length + 1)
        {
            throw new InvalidDataException($"Runtime module '{name}' contains invalid or duplicate assembly files.");
        }
        if (dependencies.Any(string.IsNullOrWhiteSpace)
            || dependencies.Contains(name, StringComparer.Ordinal)
            || dependencies.Distinct(StringComparer.Ordinal).Count() != dependencies.Length)
        {
            throw new InvalidDataException($"Runtime module '{name}' contains invalid dependencies.");
        }
    }

    private static bool IsAssemblyFileName(string value)
        => !string.IsNullOrWhiteSpace(value)
           && string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal)
           && string.Equals(Path.GetExtension(value), ".dll", StringComparison.OrdinalIgnoreCase);
}
