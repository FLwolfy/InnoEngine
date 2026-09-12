using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Inno.Core.Diagnostics;
using Inno.Core.Graphs;
using Inno.Core.Serialization;
using Inno.Extensibility.Modules;
using Inno.Extensibility.Types;
using Inno.Rendering;
using Inno.Rendering.Shaders;
using Xunit;

namespace Inno.Rendering.Assets.Tests;

[Collection("Rendering assets serialization")]
public sealed class ShaderGraphArtifactTests : IDisposable
{
    private readonly ModuleHost m_modules = new(new() { cacheDirectory = Path.Combine(Path.GetTempPath(), "ShaderGraphArtifactTests", Guid.NewGuid().ToString("N")) });
    private readonly TypeCatalog m_types;
    private readonly SerializationRegistry m_serialization;
    public ShaderGraphArtifactTests() { m_types = new(m_modules); m_serialization = new(m_types); }

    [Fact]
    public void SemanticFingerprintIgnoresLayoutButIncludesPropertiesAndFrozenDependencies()
    {
        GraphDocument graph = ShaderGraphTemplates.CreateRaster(m_serialization, SerializationContext.empty);
        string Hash(Dictionary<GraphNodeId, byte[]> sources) => ShaderGraphArtifact.GetSemanticHash(ShaderGraphArtifact.Encode(graph, sources, m_serialization), m_serialization);
        string initial = Hash([]);
        graph.nodes[0].position = new(1000, -500);
        graph.SetMetadata("inno.editor.groups", ShaderGraphDocument.Encode("view-only", m_serialization, SerializationContext.empty));
        Assert.Equal(initial, Hash([]));
        Assert.NotEqual(initial, Hash(new() { [new("module")] = [1, 2, 3] }));
        graph.nodes[0].SetValue("semantic-property", ShaderGraphDocument.Encode(7f, m_serialization, SerializationContext.empty));
        Assert.NotEqual(initial, Hash([]));
    }

    [Fact]
    public void TemporarilyEmptyInputNameRetainsItsMaterialDefaultWhileTyping()
    {
        GraphDocument graph = ShaderGraphTemplates.CreateRaster(m_serialization, SerializationContext.empty);
        GraphNodeRecord input = graph.FindNode(new("color"))!;
        var settings = ShaderGraphDocument.Read(input, "settings", new ShaderGraphInputSettings(), m_serialization, SerializationContext.empty);
        settings.id = "";
        graph = ShaderGraphBindings.ChangeInput(graph, input.id, settings, m_serialization, SerializationContext.empty);
        settings.id = "renamed-color";
        graph = ShaderGraphBindings.ChangeInput(graph, input.id, settings, m_serialization, SerializationContext.empty);
        ShaderDefinition definition = ShaderGraphDocument.ReadDefinition(graph, m_serialization, SerializationContext.empty);
        ShaderPropertyDefinition property = Assert.Single(definition.properties);
        Assert.Equal("renamed-color", property.id.value);
        Assert.Equal(1f, property.defaultValue.vector.x);
    }

    [Fact]
    public void GraphArtifactPreservesProviderOwnedContractAndRoleIdentifiers()
    {
        GraphDocument graph = ShaderGraphTemplates.CreateRaster(m_serialization, SerializationContext.empty);
        ShaderDefinition definition = ShaderGraphDocument.ReadDefinition(graph, m_serialization, SerializationContext.empty);
        definition.techniques = [new(new("default"), new("tests.surface"), [new(new("tests.draw"), "Main")])];
        SetDefinition(graph, definition);
        byte[] bytes = ShaderGraphArtifact.Encode(graph, new Dictionary<GraphNodeId, byte[]>(), m_serialization);
        GraphDocument restored = ShaderGraphArtifact.ReadDocument(bytes, m_serialization);
        ShaderDefinition decoded = ShaderGraphDocument.ReadDefinition(restored, m_serialization, SerializationContext.empty);
        Assert.DoesNotContain(ShaderDefinitionValidator.Validate(decoded), static value => value.severity == DiagnosticSeverity.Error);
        Assert.Equal("tests.surface", Assert.Single(decoded.techniques).contract.value);
        Assert.Equal("tests.draw", Assert.Single(decoded.techniques[0].passes).role.value);
        Assert.Equal(GraphDocumentCodec.Encode(graph, m_serialization), GraphDocumentCodec.Encode(restored, m_serialization));
    }

    [Fact]
    public void GraphArtifactDetachesAuthoredDefinitionNodesAndConnections()
    {
        GraphDocument graph = ShaderGraphTemplates.CreateRaster(m_serialization, SerializationContext.empty);
        byte[] committed = ShaderGraphArtifact.Encode(graph, new Dictionary<GraphNodeId, byte[]>(), m_serialization);
        graph.RemoveNode(graph.nodes[0].id);
        GraphDocument restored = ShaderGraphArtifact.ReadDocument(committed, m_serialization);
        byte[] original = GraphDocumentCodec.Encode(restored, m_serialization);
        restored.nodes[0].position = new(123, 456);
        restored.nodes[0].SetValue("unavailable.extension", new([1, 2, 3]));
        Assert.Equal(original, GraphDocumentCodec.Encode(ShaderGraphArtifact.ReadDocument(committed, m_serialization), m_serialization));
    }

    [Fact]
    public void GraphDefinitionPreservesPipelineOwnedStorageTextureContract()
    {
        var property = new ShaderPropertyDefinition(new("outputImage"), "Output Image", ShaderPropertyType.Texture2D,
            ShaderStage.Compute, default, ShaderPropertyBindingKind.StorageTexture, RenderStorageAccess.ReadWrite, ShaderPropertyBindingOwner.RenderPass);
        var pass = new ShaderPassDefinition("Compute", ShaderProgramKind.Compute,
            requiredFeatures: GraphicsCapability.Compute | GraphicsCapability.StorageTexture);
        var definition = new ShaderDefinition("Tests/Storage", [property], [], [pass]);
        GraphDocument graph = ShaderGraphDocument.Create(definition, m_serialization, SerializationContext.empty);
        ShaderDefinition restored = ShaderGraphDocument.ReadDefinition(GraphDocumentCodec.Decode(GraphDocumentCodec.Encode(graph, m_serialization), m_serialization),
            m_serialization, SerializationContext.empty);
        Assert.DoesNotContain(ShaderDefinitionValidator.Validate(restored), static value => value.severity == DiagnosticSeverity.Error);
        ShaderPropertyDefinition binding = Assert.Single(restored.properties);
        Assert.Equal(ShaderPropertyBindingKind.StorageTexture, binding.bindingKind);
        Assert.Equal(RenderStorageAccess.ReadWrite, binding.storageAccess);
        Assert.Equal(ShaderPropertyBindingOwner.RenderPass, binding.bindingOwner);
        Assert.Equal(ShaderProgramKind.Compute, Assert.Single(restored.passes).programKind);
    }

    private void SetDefinition(GraphDocument graph, ShaderDefinition definition)
        => graph.SetMetadata(ShaderGraphDocument.definitionKey, ShaderGraphDocument.Encode(m_serialization.Serialize(definition), m_serialization, SerializationContext.empty));
    public void Dispose() { m_serialization.Dispose(); m_types.Dispose(); m_modules.Dispose(); }
}
