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
    public void ObjParser_TriangulatesAndGeneratesNormals()
    {
        const string source = """
        v -1 0 -1
        v  1 0 -1
        v  1 0  1
        v -1 0  1
        vt 0 0
        vt 1 0
        vt 1 1
        vt 0 1
        f 1/1 2/2 3/3 4/4
        """;

        MeshData data = MeshSourceParser.ParseObj("quad.obj", source);
        MeshData restored = MeshArtifactCodec.Decode(MeshArtifactCodec.Encode(data));

        Assert.Equal(4, restored.vertices.Count);
        Assert.Equal(6, restored.indices.Count);
        Assert.Single(restored.subMeshes);
        Assert.True(restored.vertices[0].normal.LengthSquared() > 0.9f);
    }

    [Fact]
    public void GltfParser_ConvertsRightHandedWindingToEngineLeftHanded()
    {
        byte[] buffer = new byte[42];
        float[] positions = [-1f, 0f, 2f, 1f, 0f, 2f, 0f, 1f, 2f];
        for (int index = 0; index < positions.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                buffer.AsSpan(index * 4, 4),
                BitConverter.SingleToInt32Bits(positions[index]));
        }
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(36, 2), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(38, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(40, 2), 2);
        string json = $$"""
        {
          "asset": { "version": "2.0" },
          "buffers": [{ "byteLength": 42, "uri": "data:application/octet-stream;base64,{{Convert.ToBase64String(buffer)}}" }],
          "bufferViews": [
            { "buffer": 0, "byteOffset": 0, "byteLength": 36 },
            { "buffer": 0, "byteOffset": 36, "byteLength": 6 }
          ],
          "accessors": [
            { "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3" },
            { "bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR" }
          ],
          "meshes": [{ "primitives": [{ "attributes": { "POSITION": 0 }, "indices": 1 }] }]
        }
        """;

        MeshData data = MeshSourceParser.ParseGltf(
            "triangle.gltf",
            Encoding.UTF8.GetBytes(json),
            isBinary: false,
            static _ => throw new InvalidOperationException(),
            static _ => { });

        Assert.Equal(-2f, data.vertices[0].position.z);
        Assert.Equal<uint>(0, data.indices[0]);
        Assert.Equal<uint>(2, data.indices[1]);
        Assert.Equal<uint>(1, data.indices[2]);
    }

    [Fact]
    public void TextureHeaders_ReportDimensionsAndColorSourceInputs()
    {
        byte[] png = new byte[24];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(png, 0);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16, 4), 64);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20, 4), 32);
        byte[] tga = new byte[18];
        BinaryPrimitives.WriteUInt16LittleEndian(tga.AsSpan(12, 2), 20);
        BinaryPrimitives.WriteUInt16LittleEndian(tga.AsSpan(14, 2), 10);

        Assert.Equal((64, 32), TextureAssetImporter.ReadPngSize(png));
        Assert.Equal((20, 10), TextureAssetImporter.ReadTgaSize(tga));
        Assert.Equal((1024, 512), TextureAssetImporter.ReadHdrSize("#?RADIANCE\n-Y 512 +X 1024\n"));
    }

    [Fact]
    public void MaterialAndPipelineState_RoundTripsThroughAssetSerialization()
    {
        var material = new MaterialAsset { renderQueue = 2450 };
        material.Set(new ShaderPropertyId("roughness"), MaterialValue.FromFloat(0.4f));
        material.Set(new ShaderPropertyId("transform"), MaterialValue.FromMatrix(Matrix.identity));
        material.SetKeyword("Cutout", enabled: true);
        byte[] materialState = SerializationManager.Encode(writer => writer.WriteProperties(material));
        var restoredMaterial = new MaterialAsset();
        SerializationManager.Decode(materialState, reader =>
        {
            reader.RestoreProperties(restoredMaterial);
            return 0;
        });

        Assert.True(restoredMaterial.TryGet(new ShaderPropertyId("roughness"), out MaterialValue roughness));
        Assert.Equal(0.4f, roughness.vector.x);
        Assert.Contains("Cutout", restoredMaterial.keywords);

        var pipeline = new RenderPipelineAsset
        {
            pipelineTypeId = "tests.pipeline",
            defaultRenderPath = RenderPath.Deferred
        };
        pipeline.quality.exposure = 1.5f;
        pipeline.SetFeatures([new RenderFeatureConfiguration("tests.feature", "{\"radius\":2}")]);
        byte[] pipelineState = SerializationManager.Encode(writer => writer.WriteProperties(pipeline));
        var restoredPipeline = new RenderPipelineAsset();
        SerializationManager.Decode(pipelineState, reader =>
        {
            reader.RestoreProperties(restoredPipeline);
            return 0;
        });

        Assert.Equal("tests.pipeline", restoredPipeline.pipelineTypeId);
        Assert.Equal(RenderPath.Deferred, restoredPipeline.defaultRenderPath);
        Assert.Equal(1.5f, restoredPipeline.quality.exposure);
        Assert.Single(restoredPipeline.features);
    }

    [Fact]
    public void MeshRendererMaterialSlots_ArePersistentButHiddenFromInspection()
    {
        var first = new MaterialAsset();
        var second = new MaterialAsset();
        var source = new MeshRenderer();
        source.SetMaterials([first, second]);
        var assets = new Dictionary<Guid, MaterialAsset>
        {
            [first.identity.persistentId] = first,
            [second.identity.persistentId] = second
        };
        byte[] state = SerializationManager.Encode(writer => writer.WriteProperties(source));
        var restored = new MeshRenderer();
        AssetSerializationServices.SetReferenceResolver(
            (persistentId, _, _, _, _) => assets[persistentId]);
        try
        {
            SerializationManager.Decode(state, reader =>
            {
                reader.RestoreProperties(restored);
                return 0;
            });
        }
        finally
        {
            AssetSerializationServices.SetReferenceResolver(null);
        }

        Assert.DoesNotContain(
            SerializationManager.GetProperties(source),
            static value => value.name == "materialSlots");
        Assert.Equal([first, second], restored.materials);
    }
}

[CollectionDefinition("Rendering assets serialization", DisableParallelization = true)]
public sealed class RenderingAssetsSerializationCollection
{
}
