using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inno.Native.Bgfx.Tools;
using Inno.Rendering.Assets;
using Inno.Rendering.Core;

namespace Inno.Rendering.Bgfx;

/// <summary>Identifies a host or offline target supported by the bundled BGFX tools.</summary>
public enum BgfxShaderTargetPlatform
{
    /// <summary>64-bit Windows player or editor.</summary>
    WindowsX64,
    /// <summary>Apple Silicon macOS player or editor.</summary>
    MacOSArm64
}

/// <summary>Compiles common Shader IR stages with the BGFX shaderc toolchain.</summary>
public sealed class BgfxShadercToolchain : IShaderCompilerToolchain
{
    private readonly BgfxShaderTargetPlatform m_targetPlatform;

    /// <summary>Creates a compiler targeting the current supported host platform.</summary>
    /// <exception cref="PlatformNotSupportedException">Thrown outside Windows x64 and macOS arm64.</exception>
    public BgfxShadercToolchain()
        : this(CurrentPlatform())
    {
    }

    /// <summary>Creates a compiler for one explicit offline target platform.</summary>
    /// <param name="targetPlatform">Target platform whose shaderc profiles are required.</param>
    public BgfxShadercToolchain(BgfxShaderTargetPlatform targetPlatform)
    {
        m_targetPlatform = targetPlatform;
    }

    /// <inheritdoc />
    public ShaderCompileTarget CreateTarget(
        GraphicsCapabilities capabilities,
        bool optimize = true,
        bool debugInformation = false)
    {
        BgfxShaderCompilerProfile profile = BgfxRendererProfileCatalog.Resolve(
            m_targetPlatform,
            capabilities);
        return new ShaderCompileTarget(profile.key, capabilities, optimize, debugInformation);
    }

    /// <inheritdoc />
    public async ValueTask<ShaderToolResult> CompileAsync(
        ShaderToolRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        BgfxShaderCompilerProfile profile = BgfxRendererProfileCatalog.Resolve(
            m_targetPlatform,
            request.target.capabilities);
        if (!string.Equals(profile.key, request.target.profileKey, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Shader target '{request.target.profileKey}' does not belong to this BGFX toolchain.",
                nameof(request));
        }

        string temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "InnoEngine",
            "Shaderc",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            string sourcePath = Path.Combine(temporaryDirectory, "stage.sc");
            string outputPath = Path.Combine(temporaryDirectory, "stage.bin");
            string bgfxShaderInclude = ResolveBgfxShaderInclude();
            File.Copy(
                bgfxShaderInclude,
                Path.Combine(temporaryDirectory, "bgfx_shader.sh"),
                overwrite: true);
            File.Copy(
                Path.Combine(
                    Path.GetDirectoryName(bgfxShaderInclude)
                        ?? throw new InvalidOperationException("BGFX shader include has no parent directory."),
                    "bgfx_compute.sh"),
                Path.Combine(temporaryDirectory, "bgfx_compute.sh"),
                overwrite: true);
            await File.WriteAllTextAsync(
                sourcePath,
                request.stage.source,
                cancellationToken).ConfigureAwait(false);

            List<string> arguments =
            [
                "-f",
                sourcePath,
                "-o",
                outputPath,
                "--type",
                ToShadercStage(request.stage.stage),
                "--platform",
                profile.shadercPlatform,
                "--profile",
                profile.GetStageProfile(request.stage.stage),
                "-i",
                request.sourceRoot
            ];
            if (request.stage.stage != ShaderStage.Compute
                && request.stagePass.generatedVaryingSource is not null)
            {
                string varyingPath = Path.Combine(temporaryDirectory, "varying.def.sc");
                await File.WriteAllTextAsync(
                    varyingPath,
                    request.stagePass.generatedVaryingSource,
                    cancellationToken).ConfigureAwait(false);
                arguments.Add("--varyingdef");
                arguments.Add(varyingPath);
            }

            if (request.variant.options.Count != 0)
            {
                arguments.Add("--define");
                arguments.Add(string.Join(
                    ";",
                    request.variant.options.Select(static value => $"{value.Key}_{value.Value}=1")));
            }

            if (request.target.optimize)
            {
                arguments.Add("-O");
                arguments.Add("3");
            }

            if (request.target.debugInformation)
                arguments.Add("--debug");

            ToolRunResult result = await ToolRunner.RunAsync(
                BgfxTool.Shaderc,
                arguments,
                temporaryDirectory,
                cancellationToken).ConfigureAwait(false);
            byte[]? bytes = result.succeeded && File.Exists(outputPath)
                ? await File.ReadAllBytesAsync(outputPath, cancellationToken).ConfigureAwait(false)
                : null;
            return new ShaderToolResult(
                bytes,
                result.exitCode,
                result.standardOutput,
                result.standardError);
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
                Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static BgfxShaderTargetPlatform CurrentPlatform()
        => OperatingSystem.IsWindows()
            ? BgfxShaderTargetPlatform.WindowsX64
            : OperatingSystem.IsMacOS()
                ? BgfxShaderTargetPlatform.MacOSArm64
                : throw new PlatformNotSupportedException(
                    "The bundled BGFX shader compiler supports Windows x64 and macOS arm64 hosts.");

    private static string ToShadercStage(ShaderStage stage)
        => stage switch
        {
            ShaderStage.Vertex => "vertex",
            ShaderStage.Fragment => "fragment",
            ShaderStage.Compute => "compute",
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "A single shader stage is required.")
        };

    private static string ResolveBgfxShaderInclude()
    {
        string[] starts = [AppContext.BaseDirectory, Directory.GetCurrentDirectory()];
        foreach (string start in starts)
        {
            for (DirectoryInfo? directory = new(start); directory is not null; directory = directory.Parent)
            {
                string candidate = Path.Combine(
                    directory.FullName,
                    "extern",
                    "bgfx",
                    "src",
                    "bgfx_shader.sh");
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        throw new FileNotFoundException(
            "Unable to resolve the BGFX shader include 'bgfx_shader.sh' from the application or repository root.");
    }
}

internal sealed class BgfxShaderCompilerProfile(
    BgfxShaderTargetPlatform targetPlatform,
    GraphicsBackend backend,
    string shadercPlatform,
    string vertexProfile,
    string fragmentProfile,
    string computeProfile)
{
    internal BgfxShaderTargetPlatform targetPlatform { get; } = targetPlatform;
    internal GraphicsBackend backend { get; } = backend;
    internal string shadercPlatform { get; } = shadercPlatform;
    internal string vertexProfile { get; } = vertexProfile;
    internal string fragmentProfile { get; } = fragmentProfile;
    internal string computeProfile { get; } = computeProfile;
    internal string key => $"bgfx-shaderc:{targetPlatform}:{backend}:{vertexProfile}:{fragmentProfile}:{computeProfile}";

    internal string GetStageProfile(ShaderStage stage)
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

internal static class BgfxRendererProfileCatalog
{
    internal static BgfxShaderCompilerProfile Resolve(
        BgfxShaderTargetPlatform targetPlatform,
        GraphicsCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        bool compute = capabilities.Supports(GraphicsFeature.Compute);
        return (targetPlatform, capabilities.backend) switch
        {
            (BgfxShaderTargetPlatform.WindowsX64, GraphicsBackend.Direct3D11 or GraphicsBackend.Direct3D12) => new(
                targetPlatform,
                capabilities.backend,
                "windows",
                "s_5_0",
                "s_5_0",
                compute ? "s_5_0" : string.Empty),
            (BgfxShaderTargetPlatform.MacOSArm64, GraphicsBackend.Metal) => new(
                targetPlatform,
                capabilities.backend,
                "osx",
                "metal",
                "metal",
                compute ? "metal" : string.Empty),
            (BgfxShaderTargetPlatform.WindowsX64, GraphicsBackend.Vulkan) => new(
                targetPlatform,
                capabilities.backend,
                "windows",
                "spirv",
                "spirv",
                compute ? "spirv" : string.Empty),
            (BgfxShaderTargetPlatform.MacOSArm64, GraphicsBackend.Vulkan) => new(
                targetPlatform,
                capabilities.backend,
                "osx",
                "spirv",
                "spirv",
                compute ? "spirv" : string.Empty),
            (BgfxShaderTargetPlatform.WindowsX64, GraphicsBackend.OpenGL) => new(
                targetPlatform,
                capabilities.backend,
                "windows",
                "430",
                "430",
                compute ? "430" : string.Empty),
            (BgfxShaderTargetPlatform.MacOSArm64, GraphicsBackend.OpenGL) => new(
                targetPlatform,
                capabilities.backend,
                "osx",
                "430",
                "430",
                compute ? "430" : string.Empty),
            _ => throw new NotSupportedException(
                $"BGFX shader target '{targetPlatform}/{capabilities.backend}' is not supported.")
        };
    }
}
