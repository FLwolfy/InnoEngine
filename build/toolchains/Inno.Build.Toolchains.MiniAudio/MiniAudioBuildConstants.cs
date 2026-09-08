namespace Inno.Build.Toolchains.MiniAudio;

internal static class MiniAudioBuildConstants
{
    /// <summary>
    /// Identifies the rebuildable native output product directory.
    /// </summary>
    public const string OUTPUT_PRODUCT_DIR_NAME = "miniaudio";

    /// <summary>
    /// Identifies the checked-out miniaudio source directory.
    /// </summary>
    public const string MINIAUDIO_DIR_NAME = "miniaudio";

    /// <summary>
    /// Identifies the dependency-local native build directory.
    /// </summary>
    public const string BUILD_DIR_NAME = "build";

    /// <summary>
    /// Identifies the upstream CMake project marker.
    /// </summary>
    public const string CMAKE_LISTS_FILE = "CMakeLists.txt";

    /// <summary>
    /// Identifies the public header used to generate the managed ABI surface.
    /// </summary>
    public const string HEADER_FILE = "miniaudio.h";

    /// <summary>
    /// Identifies the translation unit that implements the shared library.
    /// </summary>
    public const string SOURCE_FILE = "miniaudio.c";
}
