using System;
using System.IO;

namespace Inno.Editor.Scripting;

/// <summary>
/// Configures project script discovery, output, and automatic compilation.
/// </summary>
public sealed class ScriptManagerOptions
{
    /// <summary>
    /// Gets or sets the project root containing the Assets directory.
    /// </summary>
    public required string projectRootDirectory { get; init; }

    /// <summary>
    /// Gets or sets whether source and plugin changes trigger compilation automatically.
    /// </summary>
    public bool autoCompile { get; init; } = true;

    /// <summary>
    /// Gets or sets the file-watcher debounce duration in milliseconds.
    /// </summary>
    public int debounceMilliseconds { get; init; } = 250;

    internal string assetDirectory => Path.Combine(projectRootDirectory, "Assets");
    internal string outputDirectory => Path.Combine(projectRootDirectory, "Library", "ScriptAssemblies");
}
