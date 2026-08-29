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
    /// Gets stable module names whose exported assemblies may satisfy this module's managed dependencies.
    /// </summary>
    /// <remarks>
    /// Dependencies are explicit for Plugin modules. Scripting modules may also declare them to make
    /// reload closure and load ordering independent from process-wide discovery.
    /// </remarks>
    public IReadOnlyList<string> upstreamModuleNames { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets whether the module load context supports cooperative unloading.
    /// </summary>
    public bool collectible { get; init; } = true;

    /// <summary>
    /// Gets or sets the ownership domain for every assembly in this module.
    /// </summary>
    public AssemblyDomain domain { get; init; } = AssemblyDomain.InnoPlugin;

    /// <summary>
    /// Gets or sets the dependency scope for assemblies without a more specific internal descriptor.
    /// </summary>
    public AssemblyScope scope { get; init; } = AssemblyScope.Runtime;

    /// <summary>
    /// Gets per-assembly scope overrides keyed by managed assembly simple name.
    /// </summary>
    /// <remarks>
    /// This is used when one plugin generation contains both runtime and editor-only assemblies.
    /// Names not present in the map use <see cref="scope"/>.
    /// </remarks>
    public IReadOnlyDictionary<string, AssemblyScope> assemblyScopes { get; init; } =
        new Dictionary<string, AssemblyScope>(StringComparer.OrdinalIgnoreCase);
}
