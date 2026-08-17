using System;
using System.Collections.Generic;

namespace Inno.Core.Assemblies;

/// <summary>
/// Describes one independently reloadable managed assembly module.
/// </summary>
public sealed class AssemblyLoadRequest
{
    /// <summary>
    /// Gets or sets the stable logical module name.
    /// </summary>
    public required string moduleName { get; init; }

    /// <summary>
    /// Gets or sets the path of the module's primary managed assembly.
    /// </summary>
    public required string mainAssemblyPath { get; init; }

    /// <summary>
    /// Gets or sets additional managed assemblies loaded into the same context.
    /// </summary>
    public IReadOnlyList<string> preloadAssemblyPaths { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets whether the module load context supports cooperative unloading.
    /// </summary>
    public bool collectible { get; init; } = true;
}
