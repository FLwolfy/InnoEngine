using System;
using System.Linq;
using Inno.Rendering;
using Xunit;

namespace Inno.Rendering.Tests;

public sealed class PipelineResourceTests
{
    [Fact]
    public void VertexLayoutAcceptsAllPortableSemanticChannels()
    {
        RenderVertexSemantic[] semantics = Enum.GetValues<RenderVertexSemantic>();
        var layout = new RenderVertexLayout(
            semantics.Select(static semantic =>
                new RenderVertexAttribute(semantic, RenderVertexFormat.Float4)).ToArray());

        Assert.Equal(semantics.Length, layout.attributes.Count);
        Assert.Equal(semantics.Length * 16, layout.stride);
    }

    [Fact]
    public void VertexLayout_RejectsDuplicateSemantic()
    {
        Assert.Throws<ArgumentException>(() => new RenderVertexLayout(
        [
            new RenderVertexAttribute(RenderVertexSemantic.Position, RenderVertexFormat.Float3),
            new RenderVertexAttribute(RenderVertexSemantic.Position, RenderVertexFormat.Float2)
        ]));
    }

    [Fact]
    public void VertexLayout_PreservesExplicitOffsetsAndTrailingPadding()
    {
        var layout = new RenderVertexLayout(
        [
            new RenderVertexAttribute(RenderVertexSemantic.Position, RenderVertexFormat.Float3, 0),
            new RenderVertexAttribute(RenderVertexSemantic.Color0, RenderVertexFormat.UInt8Normalized4, 16),
            new RenderVertexAttribute(RenderVertexSemantic.TextureCoordinate0, RenderVertexFormat.Half2)
        ], stride: 32);

        Assert.Equal([0, 16, 20], layout.attributes.Select(static attribute => attribute.byteOffset));
        Assert.Equal(32, layout.stride);
    }

    [Fact]
    public void VertexLayoutEquality_IncludesTrailingStride()
    {
        RenderVertexAttribute[] attributes =
        [
            new RenderVertexAttribute(RenderVertexSemantic.Position, RenderVertexFormat.Float3, 0)
        ];
        var tightlyPacked = new RenderVertexLayout(attributes, stride: 12);
        var padded = new RenderVertexLayout(attributes, stride: 16);

        Assert.NotEqual(tightlyPacked, padded);
        Assert.NotEqual(tightlyPacked.GetHashCode(), padded.GetHashCode());
    }

    [Fact]
    public void VertexLayout_RejectsOverlappingAttributesAndShortStride()
    {
        Assert.Throws<ArgumentException>(() => new RenderVertexLayout(
        [
            new RenderVertexAttribute(RenderVertexSemantic.Position, RenderVertexFormat.Float3, 0),
            new RenderVertexAttribute(RenderVertexSemantic.Color0, RenderVertexFormat.UInt8Normalized4, 8)
        ]));
        Assert.Throws<ArgumentException>(() => new RenderVertexLayout(
        [
            new RenderVertexAttribute(RenderVertexSemantic.Position, RenderVertexFormat.Float3)
        ], stride: 8));
    }

    [Fact]
    public void PersistentVertexBuffer_RequiresMatchingLayout()
    {
        RenderBufferDescriptor buffer = new(3, 12, RenderBufferUsage.Vertex);
        RenderVertexLayout mismatched = new(
        [
            new RenderVertexAttribute(RenderVertexSemantic.Position, RenderVertexFormat.Float4)
        ]);

        Assert.Throws<ArgumentException>(() => new PersistentBufferDescriptor(buffer, mismatched));
    }

    [Fact]
    public void TextureDescriptor_ModelsVolumeAndCubemapSubresourcesWithoutBackendTypes()
    {
        var volume = new RenderTextureDescriptor(
            16,
            8,
            RenderTextureFormat.RGBA8,
            RenderTextureUsage.Sampled,
            mipCount: 3,
            dimension: RenderTextureDimension.Texture3D,
            depth: 8);
        var cubemapArray = new RenderTextureDescriptor(
            32,
            32,
            RenderTextureFormat.RGBA16Float,
            RenderTextureUsage.Sampled,
            arrayLayers: 2,
            dimension: RenderTextureDimension.Cube);

        Assert.Equal(8, volume.GetSubresourceLayerCount(0));
        Assert.Equal(2, volume.GetSubresourceLayerCount(2));
        Assert.Equal(12, cubemapArray.GetSubresourceLayerCount(0));
        Assert.NotEqual(
            volume,
            new RenderTextureDescriptor(
                16,
                8,
                RenderTextureFormat.RGBA8,
                RenderTextureUsage.Sampled,
                mipCount: 3));
    }

    [Fact]
    public void TextureDescriptor_RejectsContradictoryDimensions()
    {
        Assert.Throws<ArgumentException>(() => new RenderTextureDescriptor(
            16,
            8,
            RenderTextureFormat.RGBA8,
            RenderTextureUsage.Sampled,
            dimension: RenderTextureDimension.Cube));
        Assert.Throws<ArgumentException>(() => new RenderTextureDescriptor(
            16,
            16,
            RenderTextureFormat.RGBA8,
            RenderTextureUsage.Sampled,
            arrayLayers: 2,
            dimension: RenderTextureDimension.Texture3D,
            depth: 4));
        Assert.Throws<ArgumentException>(() => new RenderTextureDescriptor(
            16,
            16,
            RenderTextureFormat.RGBA8,
            RenderTextureUsage.Sampled,
            sampleCount: 4,
            dimension: RenderTextureDimension.Texture3D,
            depth: 4));
    }

    [Fact]
    public void BlendStateSupportsSeparateColorAlphaEquationsAndConstantFactors()
    {
        var blend = new RenderBlendState
        {
            enabled = true,
            colorSource = RenderBlendFactor.Constant,
            colorDestination = RenderBlendFactor.InverseConstant,
            colorEquation = RenderBlendEquation.ReverseSubtract,
            alphaSource = RenderBlendFactor.One,
            alphaDestination = RenderBlendFactor.DestinationAlpha,
            alphaEquation = RenderBlendEquation.Maximum,
            constantRgba = 0x80402010,
            alphaToCoverage = true
        };
        var raster = new RenderRasterState { blend = blend };

        Assert.Equal(RenderBlendFactor.Constant, raster.blend.colorSource);
        Assert.Equal(RenderBlendEquation.Maximum, raster.blend.alphaEquation);
        Assert.Equal(0x80402010u, raster.blend.constantRgba);
        Assert.True(raster.blend.alphaToCoverage);
    }

    [Fact]
    public void ShaderBinding_RejectsDefaultIdentifier()
    {
        Assert.Throws<ArgumentException>(() => new RenderShaderBindingDescriptor(
            default,
            RenderShaderBindingKind.Uniform));
    }

    [Fact]
    public void ShaderBinding_ModelsStorageTextureAccessWithoutBackendTypes()
    {
        var binding = new RenderShaderBindingDescriptor(
            new RenderBindingId("u_output"),
            RenderShaderBindingKind.StorageTexture,
            slot: 3,
            storageAccess: RenderStorageAccess.ReadWrite);

        Assert.Equal(RenderShaderBindingKind.StorageTexture, binding.kind);
        Assert.Equal(RenderStorageAccess.ReadWrite, binding.storageAccess);
        Assert.Equal(3, binding.slot);
    }
}
