using System;
using System.IO;
using System.Linq;
using Inno.Core.Diagnostics;
using Inno.Core.Graphs;
using Inno.Core.Identity;
using Inno.Core.Serialization;
using Inno.Extensibility.Modules;
using Inno.Extensibility.Types;
using Inno.Rendering;
using Xunit;

namespace Inno.Rendering.MaterialGraph.Tests;

public sealed class MaterialGraphTests : IDisposable
{
    private readonly string m_cacheDirectory = Path.Combine(
        Path.GetTempPath(),
        "InnoMaterialGraphTests",
        Guid.NewGuid().ToString("N"));
    private readonly ModuleHost m_modules;
    private readonly TypeCatalog m_types;
    private readonly SerializationRegistry m_serialization;
    private readonly IdentityAllocator m_identities;
    private readonly IDisposable m_identityScope;

    public MaterialGraphTests()
    {
        m_identities = new IdentityAllocator();
        m_identityScope = m_identities.EnterScope();
        m_modules = new ModuleHost(new ModuleHostOptions { cacheDirectory = m_cacheDirectory });
        m_types = new TypeCatalog(m_modules);
        m_serialization = new SerializationRegistry(m_types);
    }

    public void Dispose()
    {
        m_serialization.Dispose();
        m_types.Dispose();
        m_modules.Dispose();
        m_identityScope.Dispose();
        if (Directory.Exists(m_cacheDirectory))
            Directory.Delete(m_cacheDirectory, recursive: true);
    }

    [Fact]
    public void FactoryAndEvaluator_MapReflectedMaterialValuesWithoutGeneratingShaderState()
    {
        ShaderAsset shader = CreateShader();
        GraphDocument document = MaterialGraphDocumentFactory.Create(shader, m_serialization);
        GraphNodeRecord roughness = document.nodes.Single(node =>
            ReadString(node, "propertyId") == "roughness");
        roughness.SetValue(
            "value",
            GraphSerializedValue.From(MaterialValue.FromFloat(0.25f), m_serialization));
        var asset = new MaterialAsset { shader = shader };

        MaterialGraphEvaluationResult result = MaterialGraphEvaluator.Commit(
            asset,
            document,
            m_serialization);

        Assert.True(result.succeeded, Format(result));
        Assert.Equal(2, result.properties.Count);
        Assert.True(asset.TryGet(new ShaderPropertyId("roughness"), out MaterialValue value));
        Assert.Equal(0.25f, value.vector.x);
        Assert.Same(shader, asset.shader);
        Assert.Equal(
            shader.identity.persistentId,
            MaterialGraphDocumentModel.ReadShaderId(document, m_serialization));
    }

    [Fact]
    public void Evaluator_RejectsAValueKindThatCannotBindToTheReflectedProperty()
    {
        ShaderAsset shader = CreateShader();
        GraphDocument document = MaterialGraphDocumentFactory.Create(shader, m_serialization);
        GraphNodeRecord roughness = document.nodes.Single(node =>
            ReadString(node, "propertyId") == "roughness");
        roughness.SetValue(
            "value",
            GraphSerializedValue.From(
                MaterialValue.FromColor(Inno.Core.Mathematics.Color.WHITE),
                m_serialization));
        var asset = new MaterialAsset { shader = shader };

        MaterialGraphEvaluationResult result = MaterialGraphEvaluator.Evaluate(
            asset,
            document,
            m_serialization);

        Assert.False(result.succeeded);
        Assert.Contains(result.diagnostics, static diagnostic =>
            diagnostic.code == "MATERIAL_GRAPH_VALUE_INCOMPATIBLE"
            && diagnostic.severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void DocumentCodec_RoundTripsStableNodesEdgesAndValues()
    {
        GraphDocument source = MaterialGraphDocumentFactory.Create(CreateShader(), m_serialization);

        byte[] bytes = MaterialGraphDocumentCodec.Encode(source, m_serialization);
        GraphDocument restored = MaterialGraphDocumentCodec.Decode(bytes, m_serialization).document;

        Assert.Equal(source.nodes.Select(static node => node.id), restored.nodes.Select(static node => node.id));
        Assert.Equal(source.edges.Select(static edge => edge.id), restored.edges.Select(static edge => edge.id));
        Assert.Equal(bytes, MaterialGraphDocumentCodec.Encode(restored, m_serialization));
    }

    [Fact]
    public void OrdinaryMaterial_EmbedsOneToOneGraphAndRestoresItsCurrentValues()
    {
        ShaderAsset shader = CreateShader();
        var material = new MaterialAsset { shader = shader };
        material.Set(new ShaderPropertyId("roughness"), MaterialValue.FromFloat(0.28f));

        GraphDocument document = MaterialGraphDocumentStore.ReadOrCreate(material, m_serialization);
        GraphNodeRecord roughness = document.nodes.Single(node =>
            ReadString(node, "propertyId") == "roughness");
        Assert.Equal(
            0.28f,
            MaterialGraphNodeResolver.ReadValue(roughness, m_serialization).vector.x);

        roughness.SetValue(
            "value",
            GraphSerializedValue.From(MaterialValue.FromFloat(0.64f), m_serialization));
        _ = MaterialGraphEvaluator.Commit(material, document, m_serialization);
        MaterialGraphDocumentStore.Write(material, document, m_serialization);

        Assert.True(material.TryGet(new ShaderPropertyId("roughness"), out MaterialValue committed));
        Assert.Equal(0.64f, committed.vector.x);
        GraphDocument restored = MaterialGraphDocumentStore.ReadOrCreate(material, m_serialization);
        Assert.Equal(
            GraphDocumentCodec.Encode(document, m_serialization),
            GraphDocumentCodec.Encode(restored, m_serialization));
    }

    [Fact]
    public void OutputSelection_IsAuthoritativeAndRejectsMismatchedRuntimeMaterialState()
    {
        ShaderAsset selected = CreateShader();
        ShaderAsset stale = CreateShader();
        GraphDocument document = MaterialGraphDocumentFactory.Create(selected, m_serialization);
        var asset = new MaterialAsset { shader = stale };

        MaterialGraphEvaluationResult result = MaterialGraphEvaluator.Evaluate(
            asset,
            document,
            m_serialization);

        Assert.False(result.succeeded);
        Assert.Contains(result.diagnostics, static diagnostic =>
            diagnostic.code == "MATERIAL_GRAPH_SHADER_MISSING"
            && diagnostic.severity == DiagnosticSeverity.Error);
    }

    private string? ReadString(GraphNodeRecord node, string key)
        => node.TryGetValue(key, out GraphSerializedValue? value)
            ? value!.Deserialize<string>(m_serialization)
            : null;

    private void SetDefinition(ShaderAsset shader, ShaderDefinition definition)
        => shader.SetDefinition(definition, m_serialization);

    private ShaderAsset CreateShader()
    {
        var shader = new ShaderAsset();
        _ = m_identities.Register(shader);
        SetDefinition(shader, new ShaderDefinition(
            "Tests/MaterialGraph",
            [
                new ShaderPropertyDefinition(
                    new ShaderPropertyId("roughness"),
                    "Roughness",
                    ShaderPropertyType.Float,
                    ShaderStage.Fragment,
                    MaterialValue.FromFloat(0.5f)),
                new ShaderPropertyDefinition(
                    new ShaderPropertyId("tint"),
                    "Tint",
                    ShaderPropertyType.Color,
                    ShaderStage.Fragment,
                    MaterialValue.FromColor(Inno.Core.Mathematics.Color.WHITE)),
                new ShaderPropertyDefinition(
                    new ShaderPropertyId("frame"),
                    "Frame",
                    ShaderPropertyType.Float,
                    ShaderStage.Vertex,
                    MaterialValue.FromFloat(0f),
                    bindingOwner: ShaderPropertyBindingOwner.RenderPass)
            ],
            [],
            []));
        return shader;
    }

    private static string Format(MaterialGraphEvaluationResult result)
        => string.Join(Environment.NewLine, result.diagnostics.Select(static value =>
            $"{value.code}: {value.message}"));
}
