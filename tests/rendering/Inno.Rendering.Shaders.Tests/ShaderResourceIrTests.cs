using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Inno.Build.Toolchains.Bgfx.Tools;
using Inno.Rendering;
using Inno.Rendering.Assets;
using Xunit;

namespace Inno.Rendering.Shaders.Tests;

public sealed class ShaderResourceIrTests
{
    [Fact]
    public void AggregateNamesCannotImpersonateGpuScalarVectorOrResourceTypes()
    {
        var builder = new ShaderIrBuilder();
        ShaderSourceType impostor = ShaderSourceType.Structure("uint", [new("field", ShaderSourceType.Atomic("uint"))]);
        ShaderIrValue value = builder.Construct(impostor, builder.Constant(0u));
        ShaderIrValue storage = builder.Input("storage", ShaderSourceType.Storage(ShaderStorageType.Buffer(impostor, RenderStorageAccess.ReadWrite)));
        Assert.Throws<ArgumentException>(() => builder.AtomicAddStorage(storage, builder.Constant(0u), value));
        ShaderSourceType textureType = ShaderSourceType.Structure("sampled-texture2d", [new("field", ShaderSourceType.Atomic("float"))]);
        ShaderIrValue texture = builder.Input("texture", textureType);
        ShaderIrValue uv = builder.Construct(ShaderSourceType.Atomic("float2"), builder.Constant(0f), builder.Constant(0f));
        Assert.Throws<ArgumentException>(() => builder.Sample(texture, uv));
        var color = new ShaderIrBuilder();
        ShaderIrValue fakeColor = color.Construct(ShaderSourceType.Structure("float4", [new("field", ShaderSourceType.Atomic("float"))]), color.Constant(1f));
        Assert.Throws<ArgumentException>(() => new ShaderIrStage(ShaderStage.Fragment,
            color.Build(new Dictionary<string, ShaderIrValue> { ["color"] = fakeColor }), [], [new("color", ShaderIrOutputKind.Color)]));
    }

    [Fact]
    public void StorageTypesKeepAccessShapeFormatAndCompleteElementLayout()
    {
        ShaderSourceType read = Buffer("uint", RenderStorageAccess.Read);
        Assert.True(read.IsEquivalentTo(Buffer("uint", RenderStorageAccess.Read)));
        Assert.False(read.IsEquivalentTo(Buffer("uint", RenderStorageAccess.ReadWrite)));
        Assert.False(read.IsEquivalentTo(Buffer("int", RenderStorageAccess.Read)));
        Assert.False(read.IsEquivalentTo(ShaderSourceType.Atomic("storage")));
        Assert.False(Image().IsEquivalentTo(ShaderSourceType.Storage(ShaderStorageType.Image(RenderTextureFormat.RGBA8, RenderStorageAccess.ReadWrite, array: true))));
        Assert.Throws<ArgumentException>(() => ShaderStorageType.Buffer(read, RenderStorageAccess.Read));
        Assert.Throws<ArgumentException>(() => ShaderStorageType.Image(RenderTextureFormat.RGBA8Srgb, RenderStorageAccess.Read));
        Assert.Throws<ArgumentException>(() => ShaderStorageType.Image(RenderTextureFormat.RGBA8, RenderStorageAccess.Read, RenderTextureDimension.Cube));
    }

    [Fact]
    public void StorageReadsWritesAndAtomicsKeepTheirExactEvaluationOrder()
    {
        var builder = new ShaderIrBuilder();
        ShaderIrValue resource = builder.Input("buffer", Buffer("uint", RenderStorageAccess.ReadWrite));
        ShaderIrValue index = builder.Constant(0u);
        ShaderIrValue first = builder.LoadStorage(resource, index);
        builder.StoreStorage(resource, index, builder.Constant(5u));
        ShaderIrValue second = builder.LoadStorage(resource, index);
        ShaderIrValue previous = builder.AtomicAddStorage(resource, index, builder.Constant(1u));
        ShaderIrBlock block = builder.Build(new Dictionary<string, ShaderIrValue>());
        Assert.Equal([ShaderIrOperation.StorageLoad, ShaderIrOperation.StorageStore, ShaderIrOperation.StorageLoad, ShaderIrOperation.StorageAtomicAdd],
            block.instructions.Where(static value => value.hasSideEffects).Select(static value => value.operation));
        Assert.NotEqual(first.index, second.index);
        Assert.Equal("uint", previous.type.id);
        Assert.Single(block.instructions.Where(static value => value.operation == ShaderIrOperation.StorageStore));
    }

    [Fact]
    public void ResourceAccessAndCoordinateErrorsFailBeforeNativeCodeGeneration()
    {
        var builder = new ShaderIrBuilder();
        ShaderIrValue read = builder.Input("read", Buffer("uint", RenderStorageAccess.Read));
        ShaderIrValue write = builder.Input("write", Buffer("uint", RenderStorageAccess.Write));
        ShaderIrValue index = builder.Constant(0u);
        Assert.Throws<ArgumentException>(() => builder.LoadStorage(write, index));
        Assert.Throws<ArgumentException>(() => builder.StoreStorage(read, index, builder.Constant(1u)));
        Assert.Throws<ArgumentException>(() => builder.AtomicAddStorage(read, index, builder.Constant(1u)));
        Assert.Throws<ArgumentException>(() => builder.LoadStorage(read, builder.Constant(0)));
        Assert.Throws<ArgumentException>(() => builder.StoreStorage(write, index, builder.Constant(0f)));
        Assert.Throws<ArgumentException>(() => builder.Select(builder.Constant(true), read, read));
        ShaderIrValue image = builder.Input("image", Image());
        Assert.Throws<ArgumentException>(() => builder.LoadStorage(image, index));
        Assert.Throws<ArgumentException>(() => new ShaderIrStage(ShaderStage.Compute, builder.Build(new Dictionary<string, ShaderIrValue>()),
            [new("read", read.type, ShaderIrInputKind.Uniform), new("write", write.type, ShaderIrInputKind.Storage), new("image", image.type, ShaderIrInputKind.Storage, location: 1)], []));
    }

    [Fact]
    public void ImplicitDerivativesAndDiscardCannotLeakIntoVertexOrComputeStages()
    {
        var builder = new ShaderIrBuilder();
        ShaderIrValue texture = builder.Input("texture", ShaderSourceType.Atomic("sampled-texture2d"));
        ShaderIrValue uv = builder.Construct(ShaderSourceType.Atomic("float2"), builder.Constant(0f), builder.Constant(0f));
        _ = builder.Sample(texture, uv);
        Assert.Throws<ArgumentException>(() => new ShaderIrStage(ShaderStage.Compute, builder.Build(new Dictionary<string, ShaderIrValue>()),
            [new("texture", texture.type, ShaderIrInputKind.SampledTexture)], []));
        Assert.Throws<ArgumentException>(() => builder.Sample(texture, builder.Constant(0f)));
        Assert.Throws<ArgumentException>(() => builder.SampleLevel(texture, uv, builder.Constant(0)));
        var discard = new ShaderIrBuilder();
        discard.Discard(discard.Constant(false));
        Assert.Throws<ArgumentException>(() => new ShaderIrStage(ShaderStage.Compute, discard.Build(new Dictionary<string, ShaderIrValue>()), [], []));
    }

    [MetalShaderFact]
    public async Task TypedStorageBuffersCompileRealLoadsStoresAndAtomics()
    {
        var builder = new ShaderIrBuilder();
        ShaderIrValue source = builder.Input("source", Buffer("uint", RenderStorageAccess.Read));
        ShaderIrValue target = builder.Input("target", Buffer("uint", RenderStorageAccess.ReadWrite));
        ShaderIrValue index = builder.Constant(0u);
        ShaderIrValue value = builder.LoadStorage(source, index);
        ShaderIrValue previous = builder.AtomicAddStorage(target, index, value);
        builder.StoreStorage(target, builder.Constant(1u), previous);
        ShaderIrStage stage = new(ShaderStage.Compute, builder.Build(new Dictionary<string, ShaderIrValue>()),
            [new("source", source.type, ShaderIrInputKind.Storage), new("target", target.type, ShaderIrInputKind.Storage, location: 1)], [], 8);
        ShaderStageToolResult result = await Compile(stage);
        Success(result);
        Assert.Equal(2, result.bindings.Count);
        Assert.False((await Compile(stage, GraphicsCapability.Compute)).succeeded);
    }

    [MetalShaderFact]
    public async Task TypedStorageImagesCompileForTwoDimensionalArraysAndVolumes()
    {
        foreach ((RenderTextureDimension dimension, bool array) in new[]
            { (RenderTextureDimension.Texture2D, false), (RenderTextureDimension.Texture2D, true), (RenderTextureDimension.Texture3D, false) })
        {
            var builder = new ShaderIrBuilder();
            ShaderSourceType type = ShaderSourceType.Storage(ShaderStorageType.Image(RenderTextureFormat.RGBA16Float, RenderStorageAccess.ReadWrite, dimension, array));
            ShaderIrValue image = builder.Input("image", type);
            ShaderIrValue zero = builder.Constant(0);
            ShaderIrValue coordinate = array || dimension == RenderTextureDimension.Texture3D
                ? builder.Construct(ShaderSourceType.Atomic("int3"), zero, zero, zero)
                : builder.Construct(ShaderSourceType.Atomic("int2"), zero, zero);
            ShaderIrValue loaded = builder.LoadStorage(image, coordinate);
            builder.StoreStorage(image, coordinate, builder.Binary(ShaderIrOperation.Add, loaded, loaded));
            ShaderIrStage stage = new(ShaderStage.Compute, builder.Build(new Dictionary<string, ShaderIrValue>()), [new("image", type, ShaderIrInputKind.Storage)], []);
            Success(await Compile(stage));
            Assert.False((await Compile(stage, GraphicsCapability.Compute | GraphicsCapability.StorageBuffer)).succeeded);
        }
    }

    [MetalShaderFact]
    public async Task SamplingAndFragmentDiscardCompileWithoutAHandwrittenModule()
    {
        foreach (string kind in new[] { "sampled-texture2d", "sampled-texture2d-array", "sampled-texture3d", "sampled-texture-cube" })
        {
            var builder = new ShaderIrBuilder();
            ShaderIrValue texture = builder.Input("texture", ShaderSourceType.Atomic(kind));
            ShaderIrValue zero = builder.Constant(0f);
            ShaderIrValue uv = kind == "sampled-texture2d" ? builder.Construct(ShaderSourceType.Atomic("float2"), zero, zero)
                : builder.Construct(ShaderSourceType.Atomic("float3"), zero, zero, zero);
            ShaderIrValue sampled = builder.Sample(texture, uv);
            ShaderIrValue level = builder.SampleLevel(texture, uv, zero);
            ShaderIrValue color = builder.Binary(ShaderIrOperation.Add, sampled, level);
            builder.Discard(builder.Binary(ShaderIrOperation.LessThan, builder.Extract(sampled, 3), builder.Constant(0.01f)));
            Success(await Compile(new(ShaderStage.Fragment, builder.Build(new Dictionary<string, ShaderIrValue> { ["color"] = color }),
                [new("texture", texture.type, ShaderIrInputKind.SampledTexture)], [new("color", ShaderIrOutputKind.Color)])));
        }
    }

    private static ShaderSourceType Buffer(string element, RenderStorageAccess access)
        => ShaderSourceType.Storage(ShaderStorageType.Buffer(ShaderSourceType.Atomic(element), access));
    private static ShaderSourceType Image() => ShaderSourceType.Storage(ShaderStorageType.Image(RenderTextureFormat.RGBA8, RenderStorageAccess.ReadWrite));
    private static async Task<ShaderStageToolResult> Compile(ShaderIrStage stage, GraphicsCapability features = GraphicsCapability.Compute
        | GraphicsCapability.StorageBuffer | GraphicsCapability.StorageTexture | GraphicsCapability.Texture2DArray | GraphicsCapability.Texture3D)
    {
        var toolchain = new BgfxShadercToolchain(BgfxShaderTargetPlatform.MacOSArm64);
        var capabilities = new GraphicsCapabilities(GraphicsApi.Metal, features, new GraphicsLimits(256, 8, 8192, 16),
            Enum.GetValues<RenderTextureFormat>(), Enum.GetValues<RenderTextureFormat>(), Enum.GetValues<RenderTextureFormat>(),
            Enum.GetValues<RenderTextureFormat>(), false, false);
        return await new ShaderCompiler(toolchain).CompileAsync(stage, toolchain.CreateTarget(capabilities));
    }
    private static void Success(ShaderStageToolResult result)
        => Assert.True(result.succeeded, string.Join("\n", result.diagnostics.Select(static value => value.message)));
}
