using Inno.Build.Toolchains;

namespace Inno.Build.Toolchains.Sdl3.Platforms;

internal abstract class Sdl3Builder
{
    /// <summary>
    /// Gets text used for stable identity, presentation, or diagnostics by this contract.
    /// </summary>
    public abstract string OutputPlatform { get; }

    /// <summary>
    /// Determines whether the current host can execute this implementation.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    public abstract bool IsSupported();

    /// <summary>
    /// Builds a validated result from the current immutable input snapshot.
    /// </summary>
    /// <param name="sdlDir">
    /// The sdl dir text validated by the build operation.
    /// </param>
    /// <param name="config">
    /// The validated configuration that controls this operation.
    /// </param>
    public abstract void Build(string sdlDir, string config);

    /// <summary>
    /// Retrieves the requested build type value from current authoritative state.
    /// </summary>
    /// <param name="config">
    /// The validated configuration that controls this operation.
    /// </param>
    /// <returns>
    /// The validated text representation owned by the caller.
    /// </returns>
    protected static string GetBuildType(string config)
    {
        return config == ToolchainLayout.C_DEBUG_CONFIGURATION ? "Debug" : "Release";
    }
}
