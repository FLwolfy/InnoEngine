using Inno.Build.Toolchains;

namespace Inno.Build.Toolchains.MiniAudio.Platforms;

internal abstract class MiniAudioBuilder
{
    /// <summary>
    /// Gets the runtime identifier produced by this builder.
    /// </summary>
    public abstract string OutputPlatform { get; }

    /// <summary>
    /// Determines whether the current host can execute this builder.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the host operating system and architecture match the builder.
    /// </returns>
    public abstract bool IsSupported();

    /// <summary>
    /// Builds the pinned miniaudio source as a shared library.
    /// </summary>
    /// <param name="miniAudioDirectory">
    /// The absolute path of the validated miniaudio source checkout.
    /// </param>
    /// <param name="config">
    /// The normalized debug or release configuration token.
    /// </param>
    public abstract void Build(string miniAudioDirectory, string config);

    /// <summary>
    /// Converts an engine configuration token into the corresponding CMake configuration name.
    /// </summary>
    /// <param name="config">
    /// The normalized debug or release configuration token.
    /// </param>
    /// <returns>
    /// The CMake configuration name accepted by single- and multi-configuration generators.
    /// </returns>
    protected static string GetBuildType(string config)
    {
        return config == ToolchainLayout.C_DEBUG_CONFIGURATION ? "Debug" : "Release";
    }

    /// <summary>
    /// Builds the common CMake option list that preserves miniaudio's complete standard feature surface.
    /// </summary>
    /// <param name="exportDefine">
    /// The compiler-specific definition that exports the miniaudio C ABI from a shared library.
    /// </param>
    /// <returns>
    /// A command-line fragment containing the shared native build policy.
    /// </returns>
    protected static string GetCommonCMakeOptions(string exportDefine)
    {
        return string.Join(
            ' ',
            "-DBUILD_SHARED_LIBS=ON",
            $"-DCMAKE_C_FLAGS={exportDefine}",
            "-DMINIAUDIO_BUILD_EXAMPLES=OFF",
            "-DMINIAUDIO_BUILD_TESTS=OFF",
            "-DMINIAUDIO_BUILD_TOOLS=OFF",
            "-DMINIAUDIO_NO_EXTRA_NODES=ON",
            "-DMINIAUDIO_INSTALL=OFF");
    }
}
