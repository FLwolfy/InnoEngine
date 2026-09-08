using System;
using System.IO;
using Inno.Core.Serialization;
using Inno.Extensibility.Modules;
using Inno.Extensibility.Types;
using Inno.Rendering;
using Xunit;

namespace Inno.Rendering.Assets.Tests;

[Collection("Rendering assets serialization")]
public sealed class ShaderPublicationTests : IDisposable
{
    private readonly ModuleHost m_modules;
    private readonly TypeCatalog m_types;
    private readonly SerializationRegistry m_serialization;

    public ShaderPublicationTests()
    {
        m_modules = new ModuleHost(new ModuleHostOptions
        {
            cacheDirectory = Path.Combine(Path.GetTempPath(), "InnoShaderPublication", Guid.NewGuid().ToString("N"))
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
    public void ShaderCommitDetachesEveryNestedDeclarationOnInputAndOutput()
    {
        ShaderDefinition source = CreateDefinition();
        var shader = new ShaderAsset();
        shader.SetDefinition(source, m_serialization);
        byte[] committed = m_serialization.Encode(writer => writer.WriteProperties(shader));

        Mutate(source);
        AssertOriginal(shader.definition!);
        Mutate(shader.definition!);
        AssertOriginal(shader.definition!);
        Assert.Equal(committed, m_serialization.Encode(writer => writer.WriteProperties(shader)));

        var restored = new ShaderAsset();
        m_serialization.Decode(committed, reader =>
        {
            reader.RestoreProperties(restored);
            return 0;
        });
        Mutate(restored.definition!);
        AssertOriginal(restored.definition!);
    }

    [Fact]
    public void FailedShaderCommitPreservesThePreviousDefinitionAndSerializedState()
    {
        var shader = new ShaderAsset();
        shader.SetDefinition(CreateDefinition(), m_serialization);
        byte[] committed = m_serialization.Encode(writer => writer.WriteProperties(shader));
        ShaderDefinition invalid = CreateDefinition();
        invalid.properties = null!;

        Assert.Throws<ArgumentException>(() => shader.SetDefinition(invalid, m_serialization));

        AssertOriginal(shader.definition!);
        Assert.Null(shader.definition!.passes[0].vertexSource);
        Assert.Equal(committed, m_serialization.Encode(writer => writer.WriteProperties(shader)));
    }

    [Fact]
    public void ShaderIrRetainsCanonicalInputWhenTheAuthoringDefinitionIsEdited()
    {
        ShaderDefinition definition = CreateDefinition();
        var pass = new ShaderIRPass(definition.passes[0], []);
        var passes = new[] { pass };
        var module = new ShaderIRModule(definition, passes);
        byte[] committed = ShaderIRArtifactSerialization.Encode(module, m_serialization);

        Mutate(definition);
        Mutate(module.definition);
        pass.definition.metadata[0] = new ShaderMetadataEntry("sort", "changed");
        passes[0] = new ShaderIRPass(new ShaderPassDefinition("other", ShaderProgramKind.Compute), []);

        AssertOriginal(module.definition);
        Assert.Same(pass, Assert.Single(module.passes));
        Assert.Equal("opaque", pass.definition.metadata[0].value);
        Assert.Equal(committed, ShaderIRArtifactSerialization.Encode(module, m_serialization));
        ShaderIRModule restored = ShaderIRArtifactSerialization.Decode(committed, m_serialization);
        Mutate(restored.definition);
        AssertOriginal(restored.definition);
    }

    [Fact]
    public void MaterialSelectionAndResolvedPassDoNotPublishTheirMetadataArrays()
    {
        ShaderDefinition definition = CreateDefinition();
        var resolution = new MaterialPassResolution(definition.techniques[0], definition.passes[0]);
        RenderMaterialPass materialPass = MaterialProvider.Capture(definition.passes[0]);

        Mutate(definition);
        resolution.technique.passes[0] = new ShaderTechniquePass(new ShaderPassRoleId("other"), "other");
        resolution.pass.metadata[0] = new ShaderMetadataEntry("sort", "changed");
        materialPass.definition.metadata[0] = new ShaderMetadataEntry("sort", "changed");

        Assert.Equal("draw", resolution.technique.passes[0].role.value);
        Assert.Equal("main", resolution.technique.passes[0].passName);
        Assert.Equal("opaque", resolution.pass.metadata[0].value);
        Assert.Equal("opaque", materialPass.definition.metadata[0].value);
    }

    private static ShaderDefinition CreateDefinition()
        => new("Original",
            [new ShaderPropertyDefinition(new ShaderPropertyId("value"), "Value", ShaderPropertyType.Float,
                ShaderStage.Fragment, MaterialValue.FromFloat(1))],
            [new ShaderKeywordDefinition("quality", ["high"])],
            [new ShaderPassDefinition("main", ShaderProgramKind.Raster,
                metadata: [new ShaderMetadataEntry("sort", "opaque")])],
            [new ShaderTechniqueDefinition(new ShaderTechniqueId("default"), new ShaderContractId("test.draw"),
                [new ShaderTechniquePass(new ShaderPassRoleId("draw"), "main")])]);

    private static void Mutate(ShaderDefinition definition)
    {
        definition.name = "Changed";
        definition.properties[0].displayName = "Changed";
        definition.keywords[0].options[0] = "low";
        definition.passes[0].metadata[0] = new ShaderMetadataEntry("sort", "changed");
        definition.techniques[0].passes[0] = new ShaderTechniquePass(new ShaderPassRoleId("other"), "other");
    }

    private static void AssertOriginal(ShaderDefinition definition)
    {
        Assert.Equal("Original", definition.name);
        Assert.Equal("Value", definition.properties[0].displayName);
        Assert.Equal("high", definition.keywords[0].options[0]);
        Assert.Equal("opaque", definition.passes[0].metadata[0].value);
        Assert.Equal("draw", definition.techniques[0].passes[0].role.value);
        Assert.Equal("main", definition.techniques[0].passes[0].passName);
    }

    private sealed class MaterialProvider : RenderResourceProvider
    {
        internal static RenderMaterialPass Capture(ShaderPassDefinition definition)
            => CreateMaterialPass(definition, default, default, []);
    }
}
