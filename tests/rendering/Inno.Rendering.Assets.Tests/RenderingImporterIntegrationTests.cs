using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using Inno.Build.Toolchains.Bgfx.Tools;
using Inno.Core.Graphs;
using Inno.Rendering.Shaders;
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
        _ = new BgfxShadercToolchain(BgfxShaderTargetPlatform.MacOSArm64);
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
    public void LoaderUsesGraphShaderMaterialAndPipelineAssetsWithOneCompilationModel()
    {
        GraphDocument graph = ShaderGraphTemplates.CreateRaster(m_serialization, SerializationContext.empty);
        ShaderDefinition definition = ShaderGraphDocument.ReadDefinition(graph, m_serialization, SerializationContext.empty);
        definition.name = "Tests/Basic";
        definition.properties = [.. definition.properties, new(new("roughness"), "Roughness", ShaderPropertyType.Float,
            ShaderStage.Fragment, MaterialValue.FromFloat(0.5f))];
        definition.techniques = [new(new("default"), new("tests.surface"), [new(new("draw"), "Main")])];
        graph.SetMetadata(ShaderGraphDocument.definitionKey, ShaderGraphDocument.Encode(m_serialization.Serialize(definition), m_serialization, SerializationContext.empty));
        WriteBytes("Shaders/basic.ishader", GraphDocumentCodec.Encode(graph, m_serialization));
        WriteText("Meshes/triangle.obj", "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
        byte[] png = new byte[24];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(png, 0);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16, 4), 8);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20, 4), 4);
        WriteBytes("Textures/color.png", png);

        using (var writer = CreateLoader(m_assets, m_library))
        {
            ShaderAsset shader = Assert.IsType<ShaderAsset>(writer.Load(AssetPath.Project("Shaders/basic.ishader"), typeof(ShaderAsset)));

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
        Assert.True(loadedShader.runtimePayload.IsEmpty);
        Assert.Equal(graph.nodes.Count, ShaderGraphArtifact.ReadDocument(ShaderGraphArtifact.Read(loadedShader, loader), m_serialization).nodes.Count);
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
            "common.ishadersource",
            "vec4 ProviderColor() { return vec4(0.2, 0.4, 0.6, 1.0); }", "ProviderColor");
        WriteReadOnlyShaderSource(
            consumerRoot,
            "local.ishadersource",
            "float LocalValue() { return 0.5; }", "LocalValue");
        WriteReadOnlyShaderSource(
            consumerRoot,
            "main.ishadersource",
            """
            #include "local.ishadersource"
            #include "tests.provider::common.ishadersource"
            vec4 Evaluate() { return ProviderColor() * LocalValue(); }
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
        ShaderFunctionAsset shader = Assert.IsType<ShaderFunctionAsset>(loader.Load(
            new AssetPath(consumerId, "main.ishadersource"), typeof(ShaderFunctionAsset)));
        ShaderSourceImplementationRequest request = Assert.Single(ShaderSourceBundle.Decode(ShaderSourceBundle.Read(shader, loader), m_serialization));
        using var frontends = new ShaderSourceFrontendRegistry(m_types);
        ShaderSourceModuleAnalysis analysis = frontends.AnalyzeModule([request]);
        Assert.True(analysis.succeeded, string.Join("\n", analysis.diagnostics.Select(static value => value.message)));
        Assert.Equal("Evaluate", shader.entryPoint);
        Assert.Equal(3, Assert.Single(analysis.implementations).sources.Count);
        Assert.Equal(
        [
            new AssetPath(consumerId, "local.ishadersource"),
            new AssetPath(providerId, "common.ishadersource")
        ], loader.GetImportDependencies(shader));
    }

    [Fact]
    public void RuntimeExportStripsGraphAndFunctionArtifactsAndPreservesShaderIdentityAndDefinition()
    {
        WriteBytes("Surface.ishader", GraphDocumentCodec.Encode(ShaderGraphTemplates.CreateRaster(m_serialization, SerializationContext.empty), m_serialization));
        WriteReadOnlyShaderSource(m_assets, "Function.ishadersource", "vec4 Evaluate(vec4 color) { return color; }");
        using var loader = CreateLoader(m_assets, m_library);
        ShaderAsset shader = Assert.IsType<ShaderAsset>(loader.Load(AssetPath.Project("Surface.ishader"), typeof(ShaderAsset)));
        ShaderFunctionAsset function = Assert.IsType<ShaderFunctionAsset>(loader.Load(AssetPath.Project("Function.ishadersource"), typeof(ShaderFunctionAsset)));
        Assert.NotEmpty(ShaderGraphArtifact.Read(shader, loader));
        Assert.NotEmpty(ShaderSourceBundle.Read(function, loader));
        string destination = Path.Combine(m_root, "PlayerContent");
        AssetRuntimeContentInfo content = loader.ExportRuntimeArtifacts(destination);
        Assert.Equal(1, content.assetCount);
        using SerializationGeneration generation = m_serialization.CaptureGeneration();
        using var runtime = new AssetDatabase(destination, generation, m_types.current, new IdentityAllocator());
        ShaderAsset deployed = runtime.Load<ShaderAsset>(shader.identity.persistentId);
        Assert.Equal(shader.identity.persistentId, deployed.identity.persistentId);
        Assert.Equal(shader.definition!.name, deployed.definition!.name);
        Assert.True(deployed.runtimePayload.IsEmpty);
        Assert.False(runtime.TryGetArtifact(shader.identity.persistentId, ShaderGraphArtifact.outputName, out _));
        Assert.False(runtime.TryGetArtifact(function.identity.persistentId, ShaderSourceBundle.outputName, out _));
        foreach (string file in Directory.EnumerateFiles(Path.Combine(destination, "Artifacts"), "*", SearchOption.AllDirectories))
        {
            string bytes = Encoding.UTF8.GetString(File.ReadAllBytes(file));
            Assert.DoesNotContain("inno.shader.stage-input", bytes);
            Assert.DoesNotContain("vec4 Evaluate", bytes);
        }
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

    [Fact]
    public void InvalidGraphAutosaveSurvivesImportFailureAndRestartWithoutLosingMissingRecords()
    {
        AssetPath path = AssetPath.Project("unfinished.ishader");
        GraphDocument graph = ShaderGraphTemplates.CreateRaster(m_serialization, SerializationContext.empty);
        graph.AddNode(new(new("missing"), "unavailable.shader-node"));
        graph.AddEdge(new(new("unresolved"), new(new("missing"), new("old-output")), new(new("fragment"), new("old-input"))));
        byte[] expected = GraphDocumentCodec.Encode(graph, m_serialization);
        using (var pipeline = CreatePipeline())
        {
            var store = new ShaderGraphSourceStore(pipeline, m_serialization);
            _ = store.Save(path, graph, null);
            Assert.Equal(expected, GraphDocumentCodec.Encode(store.Read(path).document, m_serialization));
        }
        using var restarted = CreatePipeline();
        var reopened = new ShaderGraphSourceStore(restarted, m_serialization);
        Assert.Equal(expected, GraphDocumentCodec.Encode(reopened.Read(path).document, m_serialization));
    }

    [Fact]
    public void ShaderSaveRejectsExternalEditsDeletionAndNewFileCollisions()
    {
        using var pipeline = CreatePipeline();
        var store = new ShaderGraphSourceStore(pipeline, m_serialization);
        AssetPath path = AssetPath.Project("conflict.ishader");
        GraphDocument graph = ShaderGraphTemplates.CreateRaster(m_serialization, SerializationContext.empty);
        string hash = store.Save(path, graph, null);
        Assert.Throws<IOException>(() => store.Save(path, graph, null));
        GraphDocument external = graph.Clone();
        external.nodes[0].position = new(111, 222);
        byte[] bytes = GraphDocumentCodec.Encode(external, m_serialization);
        WriteBytes(path.localPath, bytes);
        Assert.Throws<IOException>(() => store.Save(path, graph, hash));
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(m_assets, path.localPath)));
        File.Delete(Path.Combine(m_assets, path.localPath));
        Assert.Throws<IOException>(() => store.Save(path, graph, hash));
    }

    [Fact]
    public void ReadOnlyInstalledGraphCanBeReadButNeverAutosaved()
    {
        string installed = Path.Combine(m_root, "Installed");
        Directory.CreateDirectory(installed);
        GraphDocument graph = ShaderGraphTemplates.CreateRaster(m_serialization, SerializationContext.empty);
        byte[] bytes = GraphDocumentCodec.Encode(graph, m_serialization);
        File.WriteAllBytes(Path.Combine(installed, "shader.ishader"), bytes);
        File.WriteAllBytes(Path.Combine(installed, "shader.ishader.imeta"), m_serialization.Serialize(new RenderingAssetSourceMeta
        { persistentId = Guid.NewGuid(), sourceKind = (int)AssetSourceKind.File, importerId = "inno.rendering.shader" }));
        var id = new AssetSourceId("test.installed");
        using var pipeline = CreatePipeline([new(AssetSourceId.project, m_assets, false), new(id, installed, true)]);
        var store = new ShaderGraphSourceStore(pipeline, m_serialization);
        var path = new AssetPath(id, "shader.ishader");
        ShaderGraphSourceSnapshot snapshot = store.Read(path);
        Assert.True(snapshot.isReadOnly);
        Assert.Throws<InvalidOperationException>(() => store.Save(path, graph, snapshot.contentHash));
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(installed, "shader.ishader")));
    }

    private AssetPipeline CreatePipeline(IReadOnlyList<AssetSourceMount>? mounts = null)
        => new(m_modules, m_types, m_serialization, m_identities, m_diagnostics, m_logs,
            new AssetPipelineOptions { assetRoot = m_assets, libraryRoot = m_library, enableFileSystemWatcher = false, sourceMounts = mounts });

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

    private void WriteReadOnlyShaderSource(string root, string localPath, string content, string entry = "Evaluate")
    {
        string path = Path.Combine(root, localPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, content, Encoding.UTF8);
        System.IO.File.WriteAllBytes(path + ".imeta", m_serialization.Serialize(
            new RenderingAssetSourceMeta
            {
                persistentId = Guid.NewGuid(),
                sourceKind = (int)AssetSourceKind.File,
                importerId = "inno.rendering.shader-function",
                importerSettingsBytes = m_serialization.Serialize(new RenderingImportSettingsEnvelope
                {
                    stableTypeId = Guid.Parse("59c92a59-ef51-4587-b917-df5d187d2717"),
                    properties = m_serialization.Encode(writer => writer.WriteProperties(new ShaderSourceImportSettings
                    { languageId = "inno.shader-language.bgfx-sc", implementationId = "bgfx", entryPoint = entry }))
                })
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

internal sealed class RenderingImportSettingsEnvelope : ISerializable
{
    [SerializableProperty] public Guid stableTypeId { get; set; }
    [SerializableProperty] public byte[] properties { get; set; } = [];
    [SerializableProperty] public Guid[] dependencies { get; set; } = [];
}
