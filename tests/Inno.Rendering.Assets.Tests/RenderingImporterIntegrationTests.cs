using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Core.Diagnostics;
using Inno.Extensibility.Modules;
using Inno.Core.Identity;
using Inno.Core.Logging;
using Inno.Extensibility.Types;
using Inno.Core.Serialization;
using Xunit;

namespace Inno.Rendering.Assets.Tests;

[Collection("Rendering assets serialization")]
public sealed class RenderingImporterIntegrationTests : IDisposable
{
    private readonly IdentityAllocator m_identities = new();
    private readonly IDisposable m_identityScope;
    private readonly string m_root;
    private readonly string m_assets;
    private readonly string m_library;
    private readonly ModuleHost m_modules;
    private readonly TypeCatalog m_types;
    private readonly SerializationRegistry m_serialization;
    private readonly DiagnosticHub m_diagnostics = new();
    private readonly LogRouter m_logs = new();

    public RenderingImporterIntegrationTests()
    {
        m_root = Path.Combine(Path.GetTempPath(), "InnoRenderingImporterTests", Guid.NewGuid().ToString("N"));
        m_assets = Path.Combine(m_root, "Assets");
        m_library = Path.Combine(m_root, "Library");
        Directory.CreateDirectory(m_assets);
        m_identityScope = m_identities.EnterScope();
        m_modules = new ModuleHost(new ModuleHostOptions
        {
            cacheDirectory = Path.Combine(m_root, "Assemblies")
        });
        _ = typeof(AssetSerializationServices);
        m_types = new TypeCatalog(m_modules);
        m_serialization = new SerializationRegistry(m_types);
        _ = typeof(ShaderCompiler);
        m_types.Rebuild();
    }

    public void Dispose()
    {
        m_serialization.Dispose();
        m_types.Dispose();
        m_modules.Dispose();
        m_logs.Dispose();
        m_identityScope.Dispose();
        if (Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }

    [Fact]
    public void LoaderUsesNativeShaderMaterialAndPipelineAssetsWithOneSharedIr()
    {
        WriteText("Shaders/v.sc", "void main() {}");
        WriteText("Shaders/f.sc", "void main() {}");
        WriteText("Shaders/varying.def.sc", "vec3 a_position : POSITION;");
        WriteText("Meshes/triangle.obj", "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
        byte[] png = new byte[24];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(png, 0);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16, 4), 8);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20, 4), 4);
        WriteBytes("Textures/color.png", png);

        using (var writer = CreateLoader(m_assets, m_library))
        {
            ShaderSourceAsset vertex = Assert.IsType<ShaderSourceAsset>(writer.Load(
                AssetPath.Project("Shaders/v.sc"),
                typeof(ShaderSourceAsset)));
            ShaderSourceAsset fragment = Assert.IsType<ShaderSourceAsset>(writer.Load(
                AssetPath.Project("Shaders/f.sc"),
                typeof(ShaderSourceAsset)));
            ShaderSourceAsset varying = Assert.IsType<ShaderSourceAsset>(writer.Load(
                AssetPath.Project("Shaders/varying.def.sc"),
                typeof(ShaderSourceAsset)));
            var pass = new ShaderPassDefinition(
                "Draw",
                ShaderProgramKind.Raster,
                vertex,
                fragment,
                varyingSource: varying);
            var shader = new ShaderAsset();
            shader.SetDefinition(new ShaderDefinition(
                "Tests/Basic",
                [new ShaderPropertyDefinition(
                    new ShaderPropertyId("roughness"),
                    "Roughness",
                    ShaderPropertyType.Float,
                    ShaderStage.Fragment,
                    MaterialValue.FromFloat(0.5f))],
                [],
                [pass],
                [new ShaderTechniqueDefinition(
                    new ShaderTechniqueId("default"),
                    new ShaderContractId("tests.surface"),
                    [new ShaderTechniquePass(new ShaderPassRoleId("draw"), pass.name)])]),
                m_serialization);
            Assert.True(writer.Save(AssetPath.Project("Shaders/basic.ishader"), shader));

            var material = new MaterialAsset { shader = shader };
            material.Set(new ShaderPropertyId("roughness"), MaterialValue.FromFloat(0.25f));
            material.SetMetadata("tests.queue", "opaque");
            Assert.True(writer.Save(AssetPath.Project("Materials/basic.imaterial"), material));

            var pipeline = new RenderPipelineAsset
            {
                pipelineTypeId = "tests.pipeline",
                pipelineState = new SerializedRenderExtensionState(Guid.Empty, [1, 2, 3])
            };
            pipeline.SetFeatures([new RenderFeatureConfiguration("tests.outline")]);
            Assert.True(writer.Save(AssetPath.Project("Pipelines/default.irenderpipeline"), pipeline));
        }

        using var loader = CreateLoader(m_assets, m_library);
        ShaderAsset loadedShader = Assert.IsType<ShaderAsset>(loader.Load(
            AssetPath.Project("Shaders/basic.ishader"),
            typeof(ShaderAsset)));
        MaterialAsset loadedMaterial = Assert.IsType<MaterialAsset>(loader.Load(
            AssetPath.Project("Materials/basic.imaterial"),
            typeof(MaterialAsset)));
        RenderPipelineAsset loadedPipeline = Assert.IsType<RenderPipelineAsset>(loader.Load(
            AssetPath.Project("Pipelines/default.irenderpipeline"),
            typeof(RenderPipelineAsset)));
        GeometryAsset geometry = Assert.IsType<GeometryAsset>(loader.Load(
            AssetPath.Project("Meshes/triangle.obj"),
            typeof(GeometryAsset)));
        TextureAsset texture = Assert.IsType<TextureAsset>(loader.Load(
            AssetPath.Project("Textures/color.png"),
            typeof(TextureAsset)));

        Assert.Equal("Tests/Basic", loadedShader.definition!.name);
        Assert.Single(ShaderAssetRuntime.GetModule(loadedShader, m_serialization).passes);
        Assert.Same(loadedShader, loadedMaterial.shader);
        Assert.True(loadedMaterial.TryGet(new ShaderPropertyId("roughness"), out MaterialValue roughness));
        Assert.Equal(0.25f, roughness.vector.x);
        Assert.True(loadedMaterial.TryGetMetadata("tests.queue", out string? queue));
        Assert.Equal("opaque", queue);
        Assert.Equal("tests.pipeline", loadedPipeline.pipelineTypeId);
        Assert.Equal([1, 2, 3], loadedPipeline.pipelineState.propertyData);
        Assert.Single(loadedPipeline.features);
        Assert.Equal(3, geometry.vertexCount);
        Assert.Equal(8, texture.width);
        Assert.Equal(TextureColorSpace.Srgb, texture.colorSpace);
    }

    [Fact]
    public void ShaderIncludesUseTheCandidateMountSnapshotAndDeclaredPluginDependencies()
    {
        AssetSourceId providerId = new("tests.provider");
        AssetSourceId consumerId = new("tests.consumer");
        string providerRoot = Path.Combine(m_root, "ProviderPlugin");
        string consumerRoot = Path.Combine(m_root, "ConsumerPlugin");
        Directory.CreateDirectory(providerRoot);
        Directory.CreateDirectory(consumerRoot);
        WriteReadOnlyShaderSource(
            providerRoot,
            "common.sc",
            "vec4 ProviderColor() { return vec4(0.2, 0.4, 0.6, 1.0); }");
        WriteReadOnlyShaderSource(
            consumerRoot,
            "local.sc",
            "float LocalValue() { return 0.5; }");
        WriteReadOnlyShaderSource(
            consumerRoot,
            "main.sc",
            """
            #include <bgfx_shader.sh>
            #include "local.sc"
            #include "tests.provider::common.sc"
            void main() { gl_FragColor = ProviderColor() * LocalValue(); }
            """);
        AssetSourceMount project = new(AssetSourceId.project, m_assets, isReadOnly: false);
        AssetSourceMount provider = new(providerId, providerRoot, isReadOnly: true);
        AssetSourceMount undeclaredConsumer = new(consumerId, consumerRoot, isReadOnly: true);

        using (var undeclared = CreateLoader(
                   [project, provider, undeclaredConsumer],
                   Path.Combine(m_root, "UndeclaredLibrary")))
        {
            InvalidDataException error = Assert.Throws<InvalidDataException>(undeclared.Rescan);
            Assert.Contains("did not declare dependency", error.Message, StringComparison.OrdinalIgnoreCase);
        }

        AssetSourceMount declaredConsumer = new(
            consumerId,
            consumerRoot,
            isReadOnly: true,
            dependencies: [providerId]);
        using var loader = CreateLoader(
            [project, provider, declaredConsumer],
            Path.Combine(m_root, "DeclaredLibrary"));
        loader.Rescan();
        ShaderSourceAsset shader = Assert.IsType<ShaderSourceAsset>(loader.Load(
            new AssetPath(consumerId, "main.sc"),
            typeof(ShaderSourceAsset)));

        Assert.Contains("#include <bgfx_shader.sh>", shader.content, StringComparison.Ordinal);
        Assert.Contains("float LocalValue()", shader.content, StringComparison.Ordinal);
        Assert.Contains("vec4 ProviderColor()", shader.content, StringComparison.Ordinal);
        Assert.DoesNotContain("#include \"local.sc\"", shader.content, StringComparison.Ordinal);
        Assert.DoesNotContain("#include \"tests.provider::common.sc\"", shader.content, StringComparison.Ordinal);
        Assert.Equal(
        [
            new AssetPath(consumerId, "local.sc"),
            new AssetPath(providerId, "common.sc")
        ], loader.GetImportDependencies(shader));
    }

    private AssetLoader CreateLoader(string assetRoot, string libraryRoot)
        => new(
            m_types,
            m_serialization,
            m_identities,
            m_diagnostics,
            m_logs,
            assetRoot,
            libraryRoot);

    private AssetLoader CreateLoader(
        IReadOnlyList<AssetSourceMount> mounts,
        string libraryRoot)
        => new(
            m_types,
            m_serialization,
            m_identities,
            m_diagnostics,
            m_logs,
            mounts,
            libraryRoot);

    private void WriteText(string relativePath, string value)
        => WriteBytes(relativePath, Encoding.UTF8.GetBytes(value));

    private void WriteBytes(string relativePath, byte[] bytes)
    {
        string path = Path.Combine(m_assets, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllBytes(path, bytes);
    }

    private void WriteReadOnlyShaderSource(string root, string localPath, string content)
    {
        string path = Path.Combine(root, localPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, content, Encoding.UTF8);
        System.IO.File.WriteAllBytes(path + ".imeta", m_serialization.Serialize(
            new RenderingAssetSourceMeta
            {
                persistentId = Guid.NewGuid(),
                sourceKind = (int)AssetSourceKind.File,
                importerId = "inno.rendering.shader-source"
            }));
    }
}

internal sealed class RenderingAssetSourceMeta : ISerializable
{
    [SerializableProperty]
    public Guid persistentId { get; set; }

    [SerializableProperty]
    public int sourceKind { get; set; }

    [SerializableProperty]
    public string importerId { get; set; } = string.Empty;

    [SerializableProperty]
    public byte[] importerSettingsBytes { get; set; } = [];
}
