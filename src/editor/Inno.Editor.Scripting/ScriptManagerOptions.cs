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
    /// Gets or sets whether source and plugin changes create a pending focus-gated compilation request.
    /// </summary>
    public bool autoCompile { get; init; } = true;

    /// <summary>
    /// Gets or sets the file-watcher debounce duration in milliseconds.
    /// </summary>
    public int debounceMilliseconds { get; init; } = 250;

    /// <summary>
    /// Gets or sets the number of recent script compilation output generations retained on disk.
    /// </summary>
    public int retainedCompilationGenerations { get; init; } = 3;

    internal string assetDirectory => Path.Combine(projectRootDirectory, "Assets");
    internal string ideDirectory => Path.Combine(projectRootDirectory, "Library", "IDE");
    internal string outputDirectory => Path.Combine(projectRootDirectory, "Library", "ScriptAssemblies");
    internal string scriptApiDirectory => Path.Combine(projectRootDirectory, "Library", "ScriptApi");
}
