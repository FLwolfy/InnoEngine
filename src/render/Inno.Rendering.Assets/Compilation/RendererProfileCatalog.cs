using System;
using Inno.Rendering.Core;

namespace Inno.Rendering.Assets;

/// <summary>
/// Identifies a supported offline shader build platform.
/// </summary>
public enum ShaderTargetPlatform
{
    /// <summary>64-bit Windows player or editor.</summary>
    WindowsX64,
    /// <summary>Apple Silicon macOS player or editor.</summary>
    MacOSArm64
}

/// <summary>
/// Describes target-specific shaderc platform and stage profiles.
/// </summary>
public sealed class ShaderCompilerProfile
{
    /// <summary>
    /// Creates a target compiler profile.
    /// </summary>
    /// <param name="targetPlatform">Build platform.</param>
    /// <param name="backend">BGFX renderer backend.</param>
    /// <param name="shadercPlatform">Shaderc platform argument.</param>
    /// <param name="vertexProfile">Vertex profile.</param>
    /// <param name="fragmentProfile">Fragment profile.</param>
    /// <param name="computeProfile">Compute profile, or an empty string when unsupported.</param>
    public ShaderCompilerProfile(
        ShaderTargetPlatform targetPlatform,
        GraphicsBackend backend,
        string shadercPlatform,
        string vertexProfile,
        string fragmentProfile,
        string computeProfile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shadercPlatform);
        ArgumentException.ThrowIfNullOrWhiteSpace(vertexProfile);
        ArgumentException.ThrowIfNullOrWhiteSpace(fragmentProfile);
        this.targetPlatform = targetPlatform;
        this.backend = backend;
        this.shadercPlatform = shadercPlatform;
        this.vertexProfile = vertexProfile;
        this.fragmentProfile = fragmentProfile;
        this.computeProfile = computeProfile ?? string.Empty;
    }

    /// <summary>Gets the build platform.</summary>
    public ShaderTargetPlatform targetPlatform { get; }

    /// <summary>Gets the BGFX renderer backend.</summary>
    public GraphicsBackend backend { get; }

    /// <summary>Gets the shaderc platform argument.</summary>
    public string shadercPlatform { get; }

    /// <summary>Gets the vertex profile.</summary>
    public string vertexProfile { get; }

    /// <summary>Gets the fragment profile.</summary>
    public string fragmentProfile { get; }

    /// <summary>Gets the compute profile, or an empty string when unsupported.</summary>
    public string computeProfile { get; }

    /// <summary>Gets a stable cache-key fragment for this profile.</summary>
    public string key => $"{targetPlatform}:{backend}:{vertexProfile}:{fragmentProfile}:{computeProfile}";

    /// <summary>Returns the shaderc profile for one stage.</summary>
    /// <param name="stage">Single shader stage.</param>
    /// <returns>The shaderc profile.</returns>
    /// <exception cref="NotSupportedException">Thrown when the stage is unavailable for this target.</exception>
    public string GetStageProfile(ShaderStage stage)
        => stage switch
        {
            ShaderStage.Vertex => vertexProfile,
            ShaderStage.Fragment => fragmentProfile,
            ShaderStage.Compute when !string.IsNullOrWhiteSpace(computeProfile) => computeProfile,
            ShaderStage.Compute => throw new NotSupportedException(
                $"Profile '{key}' does not support compute shaders."),
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "A single shader stage is required.")
        };
}

/// <summary>
/// Selects shaderc profiles exclusively from target platform, renderer and capabilities.
/// </summary>
public static class RendererProfileCatalog
{
    /// <summary>
    /// Resolves a supported shader compiler profile.
    /// </summary>
    /// <param name="targetPlatform">Build platform.</param>
    /// <param name="capabilities">Renderer capability snapshot.</param>
    /// <returns>The matching immutable shaderc profile.</returns>
    /// <exception cref="NotSupportedException">Thrown when the platform/backend pair is unsupported.</exception>
    public static ShaderCompilerProfile Resolve(
        ShaderTargetPlatform targetPlatform,
        GraphicsCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        bool compute = capabilities.Supports(GraphicsFeature.Compute);
        return (targetPlatform, capabilities.backend) switch
        {
            (ShaderTargetPlatform.WindowsX64, GraphicsBackend.Direct3D11) => new(
                targetPlatform,
                capabilities.backend,
                "windows",
                "vs_5_0",
                "ps_5_0",
                compute ? "cs_5_0" : string.Empty),
            (ShaderTargetPlatform.WindowsX64, GraphicsBackend.Direct3D12) => new(
                targetPlatform,
                capabilities.backend,
                "windows",
                "vs_5_0",
                "ps_5_0",
                compute ? "cs_5_0" : string.Empty),
            (ShaderTargetPlatform.MacOSArm64, GraphicsBackend.Metal) => new(
                targetPlatform,
                capabilities.backend,
                "osx",
                "metal",
                "metal",
                compute ? "metal" : string.Empty),
            (ShaderTargetPlatform.WindowsX64, GraphicsBackend.Vulkan) => new(
                targetPlatform,
                capabilities.backend,
                "windows",
                "spirv",
                "spirv",
                compute ? "spirv" : string.Empty),
            (ShaderTargetPlatform.MacOSArm64, GraphicsBackend.Vulkan) => new(
                targetPlatform,
                capabilities.backend,
                "osx",
                "spirv",
                "spirv",
                compute ? "spirv" : string.Empty),
            (ShaderTargetPlatform.WindowsX64, GraphicsBackend.OpenGL) => new(
                targetPlatform,
                capabilities.backend,
                "windows",
                "430",
                "430",
                compute ? "430" : string.Empty),
            (ShaderTargetPlatform.MacOSArm64, GraphicsBackend.OpenGL) => new(
                targetPlatform,
                capabilities.backend,
                "osx",
                "430",
                "430",
                compute ? "430" : string.Empty),
            _ => throw new NotSupportedException(
                $"Shader target '{targetPlatform}/{capabilities.backend}' is not supported.")
        };
    }
}
