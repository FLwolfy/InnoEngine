using System;
using System.IO;

namespace Inno.Core.Assemblies;

/// <summary>
/// Configures the global assembly catalog and its shadow-copy storage.
/// </summary>
public sealed class AssemblyManagerOptions
{
    /// <summary>
    /// Gets or sets the directory used for isolated assembly generations.
    /// </summary>
    public string cacheDirectory { get; set; } = Path.Combine(
        AppContext.BaseDirectory,
        "AssemblyCache");

    /// <summary>
    /// Gets or sets whether referenced Inno host assemblies are loaded during initialization.
    /// </summary>
    public bool preloadEntryAssemblyDependencies { get; set; } = true;
}
