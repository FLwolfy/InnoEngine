using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Inno.Build.Toolchains.Bgfx.Tools;
using Inno.Rendering.Assets;
using Inno.Rendering;
using Xunit;

namespace Inno.Rendering.Bgfx.Tests;

public sealed class BgfxToolchainTests : IDisposable
{
    private static readonly byte[] C_KTX_IDENTIFIER =
    [
        0xAB, 0x4B, 0x54, 0x58, 0x20, 0x31,
        0x31, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A
    ];

    private readonly string m_root = Path.Combine(
        Path.GetTempPath(),
        "InnoBgfxToolchainTests",
        Guid.NewGuid().ToString("N"));

    public BgfxToolchainTests() => Directory.CreateDirectory(m_root);

    [Fact]
    public void MetalProfileIsOwnedByMacBgfxToolchain()
    {
        GraphicsCapabilities capabilities = CreateCapabilities(
            GraphicsBackend.Metal,
            GraphicsFeature.Compute);
        var toolchain = new BgfxShadercToolchain(BgfxShaderTargetPlatform.MacOSArm64);

        ShaderCompileTarget target = toolchain.CreateTarget(capabilities);

        Assert.Contains("bgfx-shaderc:MacOSArm64:Metal", target.profileKey, StringComparison.Ordinal);
        Assert.Throws<NotSupportedException>(() =>
            new BgfxShadercToolchain(BgfxShaderTargetPlatform.WindowsX64).CreateTarget(capabilities));
    }

    [Fact]
    public void Direct3DAndMetalTargetsSelectProfilesWithoutForkingShaderSource()
    {
        const string commonSource = "$input a_position\n#include <bgfx_shader.sh>\nvoid main() { gl_Position = vec4(a_position, 1.0); }";
        var stage = new ShaderIRStageModule(
            ShaderStage.Vertex,
            "main",
            commonSource,
            ShaderIRSourceKind.Handwritten,
            new ShaderSourceLocation("Shaders/common.vs.sc", "Main", ShaderStage.Vertex));
        ShaderCompileTarget metal = new BgfxShadercToolchain(BgfxShaderTargetPlatform.MacOSArm64)
            .CreateTarget(CreateCapabilities(GraphicsBackend.Metal, GraphicsFeature.Compute));
        ShaderCompileTarget direct3D = new BgfxShadercToolchain(BgfxShaderTargetPlatform.WindowsX64)
            .CreateTarget(CreateCapabilities(GraphicsBackend.Direct3D11, GraphicsFeature.Compute));

        Assert.Equal(commonSource, stage.source);
        Assert.Contains(":Metal:", metal.profileKey, StringComparison.Ordinal);
        Assert.Contains(":Direct3D11:", direct3D.profileKey, StringComparison.Ordinal);
        Assert.NotEqual(metal.profileKey, direct3D.profileKey);
    }

    [Fact]
    public async Task Direct3DProfileCompilesCommonSourceOnWindows()
    {
        if (!OperatingSystem.IsWindows())
            return;
        const string vertex = "$input a_position\n#include <bgfx_shader.sh>\nvoid main() { gl_Position = vec4(a_position, 1.0); }";
        const string fragment = "#include <bgfx_shader.sh>\nvoid main() { gl_FragColor = vec4(1.0); }";
        const string varying = "vec3 a_position : POSITION;";
        var pass = new ShaderPassDefinition("Draw", ShaderProgramKind.Raster);
        var module = new ShaderIRModule(
            new ShaderDefinition("Tests/Direct3D", [], [], [pass]),
            [new ShaderIRPass(
                pass,
                [
                    new ShaderIRStageModule(
                        ShaderStage.Vertex,
                        "main",
                        vertex,
                        ShaderIRSourceKind.Handwritten,
                        new ShaderSourceLocation("Shaders/test.vs.sc", "Draw", ShaderStage.Vertex)),
                    new ShaderIRStageModule(
                        ShaderStage.Fragment,
                        "main",
                        fragment,
                        ShaderIRSourceKind.Handwritten,
                        new ShaderSourceLocation("Shaders/test.fs.sc", "Draw", ShaderStage.Fragment))
                ],
                varying)]);
        var compiler = new ShaderCompiler(new BgfxShadercToolchain(BgfxShaderTargetPlatform.WindowsX64));

        ShaderCompilationResult result = await compiler.CompileAsync(
            module,
            compiler.CreateTarget(CreateCapabilities(GraphicsBackend.Direct3D11, GraphicsFeature.None)),
            RenderShaderVariant.empty,
            m_root);

        Assert.True(result.succeeded, string.Join(
            Environment.NewLine,
            result.diagnostics.Select(static diagnostic => $"{diagnostic.code}: {diagnostic.message}")));
        Assert.Single(result.artifact!.passes);
    }

    [Fact]
    public async System.Threading.Tasks.Task TextureCompilerProducesValidatedPortableContainer()
    {
        string sourcePath = Path.Combine(m_root, "checker.tga");
        File.WriteAllBytes(sourcePath, CreateTga());

        byte[] artifact = await new BgfxTextureTargetCompiler().CompileKtxAsync(
            sourcePath,
            TextureColorSpace.Srgb);

        Assert.True(artifact.Length > C_KTX_IDENTIFIER.Length);
        Assert.Equal(C_KTX_IDENTIFIER, artifact[..C_KTX_IDENTIFIER.Length]);
    }

    public void Dispose()
    {
        if (Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }

    private static GraphicsCapabilities CreateCapabilities(
        GraphicsBackend backend,
        GraphicsFeature features)
        => new(
            backend,
            features,
            new GraphicsLimits(256, 8, 8192, 16),
            Enum.GetValues<RenderTextureFormat>(),
            Enum.GetValues<RenderTextureFormat>(),
            Enum.GetValues<RenderTextureFormat>(),
            Enum.GetValues<RenderTextureFormat>(),
            originBottomLeft: false,
            homogeneousDepth: false);

    private static byte[] CreateTga()
    {
        byte[] bytes = new byte[18 + (2 * 2 * 4)];
        bytes[2] = 2;
        bytes[12] = 2;
        bytes[14] = 2;
        bytes[16] = 32;
        bytes[17] = 0x28;

        ReadOnlySpan<byte> pixels =
        [
            0x00, 0x00, 0xFF, 0xFF,
            0x00, 0xFF, 0x00, 0xFF,
            0xFF, 0x00, 0x00, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF
        ];
        pixels.CopyTo(bytes.AsSpan(18));
        return bytes;
    }
}
