using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Inno.Core.Graphs;
using Inno.Core.Serialization;
using Inno.Extensibility.Modules;
using Inno.Extensibility.Types;
using Inno.Rendering.Shaders;
using Inno.Build.Toolchains.Bgfx.Tools;
using Inno.Rendering.Assets;
using Inno.Rendering;
using Xunit;

namespace Inno.Adapter.Rendering.Bgfx.Tests;

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
            GraphicsApi.Metal,
            GraphicsCapability.Compute);
        var toolchain = new BgfxShadercToolchain(BgfxShaderTargetPlatform.MacOSArm64);

        ShaderCompileTarget target = toolchain.CreateTarget(capabilities);

        Assert.Contains("bgfx-shaderc:MacOSArm64:Metal", target.profileKey, StringComparison.Ordinal);
        Assert.Throws<NotSupportedException>(() =>
            new BgfxShadercToolchain(BgfxShaderTargetPlatform.WindowsX64).CreateTarget(capabilities));
    }

    [Fact]
    public void Direct3DAndMetalTargetsSelectProfilesWithoutForkingShaderSource()
    {
        ShaderCompileTarget metal = new BgfxShadercToolchain(BgfxShaderTargetPlatform.MacOSArm64)
            .CreateTarget(CreateCapabilities(GraphicsApi.Metal, GraphicsCapability.Compute));
        ShaderCompileTarget direct3D = new BgfxShadercToolchain(BgfxShaderTargetPlatform.WindowsX64)
            .CreateTarget(CreateCapabilities(GraphicsApi.Direct3D11, GraphicsCapability.Compute));

        Assert.Contains(":Metal:", metal.profileKey, StringComparison.Ordinal);
        Assert.Contains(":Direct3D11:", direct3D.profileKey, StringComparison.Ordinal);
        Assert.NotEqual(metal.profileKey, direct3D.profileKey);
    }

    [Fact]
    public async Task Direct3DProfileCompilesTheCommonGraphOnWindows()
    {
        if (!OperatingSystem.IsWindows())
            return;
        using var modules = new ModuleHost(new() { cacheDirectory = Path.Combine(m_root, "Modules") });
        using var types = new TypeCatalog(modules);
        using var serialization = new SerializationRegistry(types);
        using var nodes = new ShaderNodeCompilerRegistry(types);
        GraphDocument graph = ShaderGraphTemplates.CreateRaster(serialization, SerializationContext.empty);
        ShaderGraphProgramResult program = new ShaderGraphProgramCompiler(nodes).Lower(graph, "bgfx",
            new Dictionary<GraphNodeId, ShaderSourceModuleAnalysis>(), serialization, SerializationContext.empty);
        var compiler = new ShaderCompiler(new BgfxShadercToolchain(BgfxShaderTargetPlatform.WindowsX64));

        ShaderCompilationResult result = await compiler.CompileAsync(
            ShaderGraphDocument.ReadDefinition(graph, serialization, SerializationContext.empty),
            program,
            compiler.CreateTarget(CreateCapabilities(GraphicsApi.Direct3D11, GraphicsCapability.None)),
            RenderShaderVariant.empty,
            serialization, SerializationContext.empty);

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
        GraphicsApi backend,
        GraphicsCapability features)
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
