using System.IO;

namespace Inno.Scripting.Compiler;

/// <summary>
/// Identifies the project and derived-cache locations used by one script compiler.
/// </summary>
public sealed class ScriptCompilerOptions
{
    /// <summary>
    /// Gets the project root containing Assets and Library.
    /// </summary>
    public required string projectRootDirectory { get; init; }

    internal string ideDirectory => Path.Combine(projectRootDirectory, "Library", "IDE");

    internal string outputDirectory
        => Path.Combine(projectRootDirectory, "Library", "Artifacts", "ScriptAssemblies");

    internal string scriptApiDirectory => Path.Combine(projectRootDirectory, "Library", "ScriptApi");
}
