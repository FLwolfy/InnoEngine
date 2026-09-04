using Inno.Build.Toolchains;

namespace Inno.Build.Toolchains.ImGuizmo.Platforms;

internal abstract class CImguizmoBuilder
{
    /// <summary>
    /// Gets the native platform identifier produced by this builder.
    /// </summary>
    public abstract string outputPlatform { get; }
    
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
    /// <param name="cimguizmoDir">
    /// The cimguizmo dir text validated by the build operation.
    /// </param>
    /// <param name="cimguiDir">
    /// The cimgui dir text validated by the build operation.
    /// </param>
    /// <param name="cimguiBuildDir">
    /// The cimgui build dir text validated by the build operation.
    /// </param>
    /// <param name="cimguiOutputDir">
    /// The cimgui output dir text validated by the build operation.
    /// </param>
    /// <param name="config">
    /// The validated configuration that controls this operation.
    /// </param>
    public abstract void Build(string cimguizmoDir, string cimguiDir, string cimguiBuildDir, string cimguiOutputDir, string config);

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
