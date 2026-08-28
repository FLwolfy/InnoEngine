using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using Inno.Assets.Core;
using Inno.Assets.Loader;
using Inno.Assets.Serialization;
using Inno.Core.Assemblies;
using Inno.Core.Identity;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Rendering.ShaderGraph;
using Xunit;

namespace Inno.Rendering.Assets.Tests;

[Collection("Rendering assets serialization")]
public sealed class RenderingImporterIntegrationTests : IDisposable
{
    private readonly string m_root;
    private readonly string m_assets;
    private readonly string m_library;

    public RenderingImporterIntegrationTests()
    {
        m_root = Path.Combine(Path.GetTempPath(), "InnoRenderingImporterTests", Guid.NewGuid().ToString("N"));
        m_assets = Path.Combine(m_root, "Assets");
        m_library = Path.Combine(m_root, "Library");
        Directory.CreateDirectory(m_assets);
        IdentityManager.Initialize();
        AssemblyManager.Initialize(new AssemblyManagerOptions
        {
            cacheDirectory = Path.Combine(m_root, "Assemblies")
        });
        _ = typeof(AssetSerializationServices);
        TypeCacheManager.Initialize();
        SerializationManager.Initialize();
        _ = typeof(ShaderCompiler);
    }

    public void Dispose()
    {
        AssetSerializationServices.SetReferenceResolver(null);
        SerializationManager.Shutdown();
        TypeCacheManager.Shutdown();
        AssemblyManager.Shutdown();
        IdentityManager.Shutdown();
        if (Directory.Exists(m_root))
        {
            Directory.Delete(m_root, recursive: true);
        }
    }

    [Fact]
    public void Loader_DiscoversRenderingImportersAndResolvesMaterialShaderDependency()
    {
        WriteText("Shaders/v.sc", "void main() {}");
        WriteText("Shaders/f.sc", "void main() {}");
        WriteText("Shaders/varying.def.sc", "vec3 a_position : POSITION;");
        WriteText("Shaders/basic.ishader", """
        {
          "name": "Tests/Basic",
          "properties": [
            { "id": "roughness", "type": "Float", "stages": ["Fragment"], "default": 0.5 }
          ],
          "passes": [
            {
              "name": "Forward",
              "tag": "ForwardLit",
              "vertex": "Shaders/v.sc",
              "fragment": "Shaders/f.sc",
              "varying": "Shaders/varying.def.sc"
            }
          ]
        }
        """);
        WriteText("Materials/basic.imaterial", """
        {
          "shader": "Shaders/basic.ishader",
          "properties": { "roughness": 0.25 },
          "keywords": []
        }
        """);
        WriteText("Pipelines/default.irenderpipeline", """
        {
          "pipeline": "inno.pipeline.universal",
          "renderPath": "Deferred",
          "quality": { "hdr": true, "bloom": true, "exposure": 1.0 },
          "features": [{ "type": "tests.outline", "settings": { "width": 2 } }]
        }
        """);
        WriteText("Shaders/basic.ishadergraph", """
        {
          "target": "Surface",
          "nodes": [
            {
              "id": "surface",
              "definition": "inno.shader.output.surface",
              "position": [320, 120],
              "values": {}
            }
          ],
          "edges": [],
          "metadata": {}
        }
        """);
        WriteText("Materials/graph.imaterial", """
        {
          "shader": "Shaders/basic.ishadergraph",
          "properties": {},
          "keywords": []
        }
        """);
        WriteText("Meshes/triangle.obj", """
        v 0 0 0
        v 1 0 0
        v 0 1 0
        f 1 2 3
        """);
        byte[] png = new byte[24];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(png, 0);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16, 4), 8);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20, 4), 4);
        WriteBytes("Textures/color.png", png);

        using var loader = new AssetLoader(m_assets, m_library);
        AssetSerializationServices.SetReferenceResolver((
            persistentId,
            stableTypeId,
            lastKnownPath,
            expectedType,
            _) => loader.ResolveReference(
                persistentId,
                stableTypeId,
                lastKnownPath,
                expectedType));
        ShaderAsset shader = Assert.IsType<ShaderAsset>(loader.Load(
            "Shaders/basic.ishader",
            typeof(ShaderAsset)));
        bool materialImported = loader.Import("Materials/basic.imaterial");
        Assert.True(
            materialImported,
            GetImportDiagnostics(loader, "Materials/basic.imaterial"));
        MaterialAsset material = Assert.IsType<MaterialAsset>(loader.Load(
            "Materials/basic.imaterial",
            typeof(MaterialAsset)));
        RenderPipelineAsset pipeline = Assert.IsType<RenderPipelineAsset>(loader.Load(
            "Pipelines/default.irenderpipeline",
            typeof(RenderPipelineAsset)));
        MeshAsset mesh = Assert.IsType<MeshAsset>(loader.Load("Meshes/triangle.obj", typeof(MeshAsset)));
        TextureAsset texture = Assert.IsType<TextureAsset>(loader.Load("Textures/color.png", typeof(TextureAsset)));
        ShaderGraphAsset graph = Assert.IsType<ShaderGraphAsset>(loader.Load(
            "Shaders/basic.ishadergraph",
            typeof(ShaderGraphAsset)));
        bool graphMaterialImported = loader.Import("Materials/graph.imaterial");
        Assert.True(graphMaterialImported, GetImportDiagnostics(loader, "Materials/graph.imaterial"));
        MaterialAsset graphMaterial = Assert.IsType<MaterialAsset>(loader.Load(
            "Materials/graph.imaterial",
            typeof(MaterialAsset)));

        Assert.Equal("Tests/Basic", shader.definition!.name);
        Assert.Same(shader, material.shader);
        Assert.True(material.TryGet(new ShaderPropertyId("roughness"), out MaterialValue roughness));
        Assert.Equal(0.25f, roughness.vector.x);
        Assert.Equal(RenderPath.Deferred, pipeline.defaultRenderPath);
        Assert.Single(pipeline.features);
        Assert.Equal(3, mesh.vertexCount);
        Assert.Equal(8, texture.width);
        Assert.Equal(TextureColorSpace.Srgb, texture.colorSpace);
        Assert.Equal(ShaderGraphTarget.Surface, graph.target);
        Assert.Single(graph.document!.nodes);
        Assert.Same(graph, graphMaterial.shader);
        Assert.NotNull(graph.definition);
        Assert.Equal(6, ShaderAssetRuntime.GetModule(graph).passes.Count);
    }

    private void WriteText(string relativePath, string value)
        => WriteBytes(relativePath, Encoding.UTF8.GetBytes(value));

    private void WriteBytes(string relativePath, byte[] bytes)
    {
        string path = Path.Combine(m_assets, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private static string GetImportDiagnostics(AssetLoader loader, string relativePath)
    {
        if (!loader.TryGetPersistentId(relativePath, out Guid persistentId)
            || !loader.TryGetInfo(persistentId, out AssetInfo? info)
            || info is null)
        {
            return "No catalog diagnostics were produced.";
        }

        return string.Join(Environment.NewLine, info.diagnostics);
    }
}
