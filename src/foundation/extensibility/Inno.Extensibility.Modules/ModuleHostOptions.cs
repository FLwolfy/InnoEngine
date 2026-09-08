using System;
using System.IO;

namespace Inno.Extensibility.Modules;

/// <summary>
/// Configures one isolated module host and its shadow-copy storage.
/// </summary>
public sealed class ModuleHostOptions
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
