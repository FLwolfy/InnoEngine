namespace Inno.Native.Dll;

/// <summary>
/// Shared constants for native dll layout.
/// </summary>
public static class NativeDllConstants
{
    /// <summary>Marker file used to locate repo root.</summary>
    public const string REPO_ROOT_MARKER_FILE = "InnoEngine.sln";
    /// <summary>Output folder name for native binaries.</summary>
    public const string NATIVE_DIR_NAME = "native";
    /// <summary>Repo folder name containing built native binaries.</summary>
    public const string LIB_DIR_NAME = "lib";
}
