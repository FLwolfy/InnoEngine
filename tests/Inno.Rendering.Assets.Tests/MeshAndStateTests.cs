using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Inno.Assets.Serialization;
using Inno.Core.Assemblies;
using Inno.Core.Mathematics;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Rendering.Core;
using Xunit;

namespace Inno.Rendering.Assets.Tests;

[Collection("Rendering assets serialization")]
public sealed class MeshAndStateTests : IDisposable
{
    public MeshAndStateTests()
    {
        AssemblyManager.Initialize(new AssemblyManagerOptions
        {
            cacheDirectory = Path.Combine(Path.GetTempPath(), "InnoRenderingAssetsTests", "Assemblies")
        });
        TypeCacheManager.Initialize();
        SerializationManager.Initialize();
    }

    public void Dispose()
    {
        SerializationManager.Shutdown();
        TypeCacheManager.Shutdown();
        AssemblyManager.Shutdown();
    }

    [Fact]
    public void MaterialAndPipelineState_RoundTripsThroughAssetSerialization()
    {
        var material = new MaterialAsset { techniqueId = new ShaderTechniqueId("high-quality") };
        material.Set(new ShaderPropertyId("roughness"), MaterialValue.FromFloat(0.4f));
        material.Set(new ShaderPropertyId("transform"), MaterialValue.FromMatrix(Matrix.identity));
        material.Set(
            new ShaderPropertyId("albedo"),
            new MaterialValue
            {
                kind = MaterialValueKind.Texture,
                sampler = new RenderSamplerState(
                    RenderSamplerFilter.Point,
                    RenderSamplerAddressMode.Repeat,
                    RenderSamplerAddressMode.Mirror,
                    RenderSamplerAddressMode.Clamp)
            });
        material.SetKeyword("Cutout", enabled: true);
        material.SetMetadata("tests.sort", "2450");
        byte[] materialState = SerializationManager.Encode(writer => writer.WriteProperties(material));
        var restoredMaterial = new MaterialAsset();
        SerializationManager.Decode(materialState, reader =>
        {
            reader.RestoreProperties(restoredMaterial);
            return 0;
        });

        Assert.True(restoredMaterial.TryGet(new ShaderPropertyId("roughness"), out MaterialValue roughness));
        Assert.Equal(0.4f, roughness.vector.x);
        Assert.True(restoredMaterial.TryGet(new ShaderPropertyId("albedo"), out MaterialValue albedo));
        Assert.Equal(RenderSamplerFilter.Point, albedo.sampler.filter);
        Assert.Equal(RenderSamplerAddressMode.Mirror, albedo.sampler.addressV);
        Assert.Contains("Cutout", restoredMaterial.keywords);
        Assert.Equal("high-quality", restoredMaterial.techniqueId.value);
        Assert.True(restoredMaterial.TryGetMetadata("tests.sort", out string? sort));
        Assert.Equal("2450", sort);

        var pipeline = new RenderPipelineAsset
        {
            pipelineTypeId = "tests.pipeline",
            pipelineState = new SerializedRenderExtensionState(Guid.Empty, [1, 2, 3])
        };
        pipeline.SetFeatures([
            new RenderFeatureConfiguration(
                "tests.feature",
                new SerializedRenderExtensionState(Guid.Empty, [4, 5]),
                enabled: true)
        ]);
        byte[] pipelineState = SerializationManager.Encode(writer => writer.WriteProperties(pipeline));
        var restoredPipeline = new RenderPipelineAsset();
        SerializationManager.Decode(pipelineState, reader =>
        {
            reader.RestoreProperties(restoredPipeline);
            return 0;
        });

        Assert.Equal("tests.pipeline", restoredPipeline.pipelineTypeId);
        Assert.Equal([1, 2, 3], restoredPipeline.pipelineState.propertyData);
        RenderFeatureConfiguration feature = Assert.Single(restoredPipeline.features);
        Assert.Equal("tests.feature", feature.featureTypeId);
        Assert.Equal([4, 5], feature.state.propertyData);
    }

    [Fact]
    public void MaterialResolver_UsesProviderOwnedContractsRolesAndCapabilities()
    {
        var advancedPass = new ShaderPassDefinition(
            "advanced-draw",
            ShaderProgramKind.Raster,
            requiredFeatures: GraphicsFeature.Compute);
        var basicPass = new ShaderPassDefinition("basic-draw", ShaderProgramKind.Raster);
        var shader = new ShaderAsset();
        shader.SetDefinition(new ShaderDefinition(
            "Provider-owned shader",
            [],
            [],
            [advancedPass, basicPass],
            [
                new ShaderTechniqueDefinition(
                    new ShaderTechniqueId("advanced"),
                    new ShaderContractId("example.2d.sprite"),
                    [new ShaderTechniquePass(new ShaderPassRoleId("draw"), advancedPass.name)],
                    GraphicsFeature.Compute),
                new ShaderTechniqueDefinition(
                    new ShaderTechniqueId("basic"),
                    new ShaderContractId("example.2d.sprite"),
                    [new ShaderTechniquePass(new ShaderPassRoleId("draw"), basicPass.name)])
            ]));
        var material = new MaterialAsset { shader = shader };
        GraphicsCapabilities basicCapabilities = CreateCapabilities(GraphicsFeature.None);
        GraphicsCapabilities computeCapabilities = CreateCapabilities(GraphicsFeature.Compute);

        MaterialPassResolution basic = Assert.IsType<MaterialPassResolution>(MaterialPassResolver.Resolve(
            material,
            new ShaderContractId("example.2d.sprite"),
            new ShaderPassRoleId("draw"),
            basicCapabilities));
        MaterialPassResolution advanced = Assert.IsType<MaterialPassResolution>(MaterialPassResolver.Resolve(
            material,
            new ShaderContractId("example.2d.sprite"),
            new ShaderPassRoleId("draw"),
            computeCapabilities));

        Assert.Equal("basic", basic.technique.id.value);
        Assert.Equal("basic-draw", basic.pass.name);
        Assert.Equal("advanced", advanced.technique.id.value);
        Assert.Equal("advanced-draw", advanced.pass.name);
        Assert.Null(MaterialPassResolver.Resolve(
            material,
            new ShaderContractId("unrelated.contract"),
            new ShaderPassRoleId("draw"),
            computeCapabilities));
    }

    [Fact]
    public void ShaderRasterState_RoundTripsWithoutLosingProviderChoices()
    {
        ShaderRenderState sourceState = ShaderRenderState.opaque;
        sourceState.topology = RenderPrimitiveTopology.LineStrip;
        sourceState.cull = ShaderCullMode.Front;
        sourceState.frontFace = RenderFrontFace.Clockwise;
        sourceState.multisampling = false;
        var source = new ShaderAsset();
        source.SetDefinition(new ShaderDefinition(
            "Tests/RasterState",
            [],
            [],
            [
                new ShaderPassDefinition(
                    "wire",
                    ShaderProgramKind.Raster,
                    renderState: sourceState)
            ]));

        byte[] state = SerializationManager.Encode(writer => writer.WriteProperties(source));
        var restored = new ShaderAsset();
        SerializationManager.Decode(state, reader =>
        {
            reader.RestoreProperties(restored);
            return 0;
        });

        ShaderRenderState restoredState = Assert.Single(restored.definition!.passes).renderState;
        Assert.Equal(RenderPrimitiveTopology.LineStrip, restoredState.topology);
        Assert.Equal(ShaderCullMode.Front, restoredState.cull);
        Assert.Equal(RenderFrontFace.Clockwise, restoredState.frontFace);
        Assert.False(restoredState.multisampling);
    }

    [Fact]
    public void MaterialMetadataRemainsAnOpenProviderProtocol()
    {
        var source = new MaterialAsset();
        source.SetMetadata("sprite.order", "17");
        source.SetMetadata("raytracing.mask", "opaque");
        byte[] state = SerializationManager.Encode(writer => writer.WriteProperties(source));
        var restored = new MaterialAsset();
        SerializationManager.Decode(state, reader =>
        {
            reader.RestoreProperties(restored);
            return 0;
        });

        Assert.True(restored.TryGetMetadata("sprite.order", out string? spriteOrder));
        Assert.Equal("17", spriteOrder);
        Assert.True(restored.TryGetMetadata("raytracing.mask", out string? rayMask));
        Assert.Equal("opaque", rayMask);
    }

    private static GraphicsCapabilities CreateCapabilities(GraphicsFeature features)
        => new(
            GraphicsBackend.Noop,
            features,
            new GraphicsLimits(64, 4, 4096, 8),
            Enum.GetValues<RenderTextureFormat>(),
            Enum.GetValues<RenderTextureFormat>(),
            [],
            [],
            originBottomLeft: false,
            homogeneousDepth: false);
}

[CollectionDefinition("Rendering assets serialization", DisableParallelization = true)]
public sealed class RenderingAssetsSerializationCollection
{
}
