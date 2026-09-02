using System;
using System.Collections.Generic;
using System.IO;
using Inno.Extensibility.Modules;
using Inno.Core.Mathematics;
using Inno.Extensibility.Types;
using Inno.Core.Serialization;
using Inno.Rendering;
using Xunit;

namespace Inno.Rendering.Assets.Tests;

[Collection("Rendering assets serialization")]
public sealed class HandwrittenShaderParserTests : IDisposable
{
    private readonly ModuleHost m_modules;
    private readonly TypeCatalog m_types;
    private readonly SerializationRegistry m_serialization;

    public HandwrittenShaderParserTests()
    {
        m_modules = new ModuleHost(new ModuleHostOptions
        {
            cacheDirectory = Path.Combine(Path.GetTempPath(), "InnoShaderIrTests", Guid.NewGuid().ToString("N"))
        });
        m_types = new TypeCatalog(m_modules);
        m_serialization = new SerializationRegistry(m_types);
    }

    public void Dispose()
    {
        m_serialization.Dispose();
        m_types.Dispose();
        m_modules.Dispose();
    }

    [Fact]
    public void SharedIrUsesOnlyProviderOwnedContractAndRoleIdentifiers()
    {
        ShaderIRModule module = CreateModule("void main() {}", ShaderIRSourceKind.Handwritten);

        ShaderIRValidationResult validation = ShaderIRValidator.Validate(module);

        Assert.DoesNotContain(validation.diagnostics, diagnostic =>
            diagnostic.severity == ShaderDiagnosticSeverity.Error);
        ShaderTechniqueDefinition technique = Assert.Single(module.definition.techniques);
        Assert.Equal("tests.surface", technique.contract.value);
        Assert.Equal("tests.draw", Assert.Single(technique.passes).role.value);
        Assert.Equal("Main", Assert.Single(module.definition.passes).name);
    }

    [Fact]
    public void ArtifactSerializationPreservesSharedHandwrittenAndGeneratedIrContract()
    {
        ShaderIRModule module = CreateModule("void main() {}", ShaderIRSourceKind.Generated);

        ShaderIRModule restored = ShaderIRArtifactSerialization.Decode(
            ShaderIRArtifactSerialization.Encode(module, m_serialization),
            m_serialization);

        Assert.Equal(module.definition.name, restored.definition.name);
        Assert.Equal(module.definition.techniques[0].contract, restored.definition.techniques[0].contract);
        Assert.Equal(module.passes[0].stages[0].sourceKind, restored.passes[0].stages[0].sourceKind);
        Assert.Equal("node-v", restored.passes[0].stages[0].lineNodeIds[2]);
    }

    [Fact]
    public void SharedIrPreservesPipelineOwnedStorageTextureInterface()
    {
        ShaderPropertyId outputId = new("outputImage");
        var output = new ShaderPropertyDefinition(
            outputId,
            "Output Image",
            ShaderPropertyType.Texture2D,
            ShaderStage.Compute,
            default,
            ShaderPropertyBindingKind.StorageTexture,
            RenderStorageAccess.ReadWrite);
        var pass = new ShaderPassDefinition(
            "Compute",
            ShaderProgramKind.Compute,
            requiredFeatures: GraphicsFeature.Compute | GraphicsFeature.StorageTexture);
        var definition = new ShaderDefinition("Tests/Storage", [output], [], [pass]);
        var module = new ShaderIRModule(
            definition,
            [new ShaderIRPass(
                pass,
                [new ShaderIRStageModule(
                    ShaderStage.Compute,
                    "main",
                    "void main() {}",
                    ShaderIRSourceKind.Handwritten,
                    new ShaderSourceLocation("Shaders/storage.cs.sc", "Compute", ShaderStage.Compute))],
                bindingIds: [outputId])]);

        ShaderIRValidationResult validation = ShaderIRValidator.Validate(module);
        ShaderInterface shaderInterface = ShaderInterface.FromPass(module, module.passes[0]);
        ShaderIRModule restored = ShaderIRArtifactSerialization.Decode(
            ShaderIRArtifactSerialization.Encode(module, m_serialization),
            m_serialization);

        Assert.True(validation.succeeded);
        ShaderInterfaceBinding binding = Assert.Single(shaderInterface.bindings);
        Assert.Equal(ShaderPropertyBindingKind.StorageTexture, binding.bindingKind);
        Assert.Equal(RenderStorageAccess.ReadWrite, binding.storageAccess);
        Assert.Equal(ShaderPropertyBindingKind.StorageTexture, restored.definition.properties[0].bindingKind);
        Assert.Equal(RenderStorageAccess.ReadWrite, restored.definition.properties[0].storageAccess);
    }

    internal static ShaderIRModule CreateModule(string source, ShaderIRSourceKind sourceKind)
    {
        var pass = new ShaderPassDefinition("Main", ShaderProgramKind.Raster);
        var definition = new ShaderDefinition(
            "Tests/Compiler",
            [new ShaderPropertyDefinition(
                new ShaderPropertyId("baseColor"),
                "Base Color",
                ShaderPropertyType.Color,
                ShaderStage.Fragment,
                MaterialValue.FromColor(Color.WHITE))],
            [new ShaderKeywordDefinition("SURFACE", ["Opaque", "Transparent"])],
            [pass],
            [new ShaderTechniqueDefinition(
                new ShaderTechniqueId("default"),
                new ShaderContractId("tests.surface"),
                [new ShaderTechniquePass(new ShaderPassRoleId("tests.draw"), pass.name)])]);
        return new ShaderIRModule(
            definition,
            [new ShaderIRPass(
                pass,
                [
                    new ShaderIRStageModule(
                        ShaderStage.Vertex,
                        "main",
                        source,
                        sourceKind,
                        new ShaderSourceLocation("Shaders/v.sc", "Main", ShaderStage.Vertex),
                        new Dictionary<int, string> { [2] = "node-v" }),
                    new ShaderIRStageModule(
                        ShaderStage.Fragment,
                        "main",
                        "void main() {}",
                        sourceKind,
                        new ShaderSourceLocation("Shaders/f.sc", "Main", ShaderStage.Fragment))
                ])]);
    }
}
