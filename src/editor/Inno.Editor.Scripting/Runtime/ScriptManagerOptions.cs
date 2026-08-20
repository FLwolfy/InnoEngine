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
    /// Gets or sets whether startup and subsequent source changes request automatic compilation.
    /// </summary>
    /// <remarks>
    /// The initial request starts immediately. Later change requests are consumed only at a focused
    /// Editor safe point after the configured debounce duration.
    /// </remarks>
    public bool autoCompile { get; init; } = true;

    /// <summary>
    /// Gets or sets the file-watcher debounce duration in milliseconds.
    /// </summary>
    public int debounceMilliseconds { get; init; } = 250;

    internal string assetDirectory => Path.Combine(projectRootDirectory, "Assets");
    internal string ideDirectory => Path.Combine(projectRootDirectory, "Library", "IDE");
    internal string outputDirectory => Path.Combine(projectRootDirectory, "Library", "Artifacts", "ScriptAssemblies");
    internal string scriptApiDirectory => Path.Combine(projectRootDirectory, "Library", "ScriptApi");
}
