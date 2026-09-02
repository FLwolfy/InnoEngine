namespace Inno.Build.Toolchains;

/// <summary>
/// Defines stable repository-relative names shared by native dependency toolchains.
/// </summary>
public static class ToolchainLayout
{
    /// <summary>
    /// Identifies the repository marker used while resolving a toolchain workspace.
    /// </summary>
    public const string C_REPOSITORY_MARKER_FILE = "InnoEngine.sln";

    /// <summary>
    /// Identifies the directory containing checked-out native dependency sources.
    /// </summary>
    public const string C_EXTERNAL_DIRECTORY_NAME = "extern";

    /// <summary>
    /// Identifies the rebuildable native output directory.
    /// </summary>
    public const string C_OUTPUT_DIRECTORY_NAME = ".lib";

    /// <summary>
    /// Identifies the normalized debug configuration token.
    /// </summary>
    public const string C_DEBUG_CONFIGURATION = "debug";

    /// <summary>
    /// Identifies the normalized release configuration token.
    /// </summary>
    public const string C_RELEASE_CONFIGURATION = "release";
}
