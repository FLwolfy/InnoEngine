using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Serialization;
using Inno.Rendering;
using Xunit;

namespace Inno.Rendering.Runtime.Tests;

public sealed partial class RenderRuntimeGenerationTests
{
    [Fact]
    public void ExplicitArtifactScopesDoNotPublishIntoCanonicalMaterialCachesAndReleaseIndependently()
    {
        var material = new MaterialAsset { shader = PublicationShader("saved") };
        var provider = new PublicationProvider(m_serialization) { artifact = Publication("saved") };
        var device = new RecordingRenderDevice();
        var projectDiagnostics = new TestDiagnosticSink();
        var previewDiagnostics = new TestDiagnosticSink();
        using var runtime = new RenderRuntime(m_types, device, projectDiagnostics, targetArtifacts: provider);
        RenderShaderArtifact draft = Publication("draft");
        GraphicsPipelineHandle canonical = default, preview = default;
        ResourceProbePipeline.action = resources =>
        {
            Assert.True(resources.TryResolveGraphicsMaterial(material, new("test.publication"), new("draw"), null, null, out var saved));
            Assert.True(resources.TryResolveMaterialArtifact(new("preview/document"), draft, material, new("test.publication"),
                new("draw"), ShaderProgramKind.Raster, null, null, previewDiagnostics, out var edited));
            Assert.Equal("saved", saved!.definition.name);
            Assert.Equal("draft", edited!.definition.name);
            canonical = saved.graphicsPipeline; preview = edited.graphicsPipeline;
            Assert.NotEqual(canonical, preview);
            Assert.True(resources.TryResolveMaterialArtifact(new("preview/document"), Publication("next-frame"), material, new("test.publication"),
                new("draw"), ShaderProgramKind.Raster, null, null, previewDiagnostics, out var sameFrame));
            Assert.Equal(preview, sameFrame!.graphicsPipeline);
        };
        RunResourceFrame(runtime);
        Assert.Equal(1, provider.requests);
        runtime.resources.Release(new("preview/document"));
        Assert.DoesNotContain(preview, device.destroyedPrograms);
        ResourceProbePipeline.action = resources =>
        {
            Assert.True(resources.TryResolveGraphicsMaterial(material, new("test.publication"), new("draw"), null, null, out var saved));
            Assert.Equal(canonical, saved!.graphicsPipeline);
        };
        RunResourceFrame(runtime);
        Assert.Contains(preview, device.destroyedPrograms);
        Assert.DoesNotContain(canonical, device.destroyedPrograms);
        Assert.Empty(projectDiagnostics.items);
    }

    [Fact]
    public void InvalidDraftProgramKeepsOnlyItsOwnLastGoodAndReportsOnlyToItsConsumer()
    {
        var material = new MaterialAsset { shader = PublicationShader("saved") };
        var provider = new PublicationProvider(m_serialization) { artifact = Publication("saved") };
        var device = new RecordingRenderDevice();
        var project = new TestDiagnosticSink();
        var preview = new TestDiagnosticSink();
        using var runtime = new RenderRuntime(m_types, device, project, targetArtifacts: provider);
        RenderShaderArtifact draft = Publication("draft");
        GraphicsPipelineHandle lastGood = default;
        ResourceProbePipeline.action = resources =>
        {
            Assert.True(resources.TryResolveMaterialArtifact(new("preview/a"), draft, material, new("test.publication"),
                new("draw"), ShaderProgramKind.Raster, null, null, preview, out var pass));
            if (lastGood.isValid) Assert.Equal(lastGood, pass!.graphicsPipeline);
            lastGood = pass!.graphicsPipeline;
        };
        RunResourceFrame(runtime);
        draft = Publication("broken");
        device.failProgramName = "Publication/broken";
        RunResourceFrame(runtime);
        Assert.Empty(project.items);
        Assert.Contains(preview.items, issue => issue.code == "RENDER_PROGRAM_CREATE_FAILED");
        ResourceProbePipeline.action = resources =>
        {
            Assert.True(resources.TryResolveGraphicsMaterial(material, new("test.publication"), new("draw"), null, null, out var saved));
            Assert.Equal("saved", saved!.definition.name);
            Assert.NotEqual(lastGood, saved.graphicsPipeline);
        };
        RunResourceFrame(runtime);
        Assert.Empty(project.items);
    }

    [Fact]
    public void ShaderProgramsFollowPublishedContentNotTheLatestAssetDefinitionOrItsVersion()
    {
        var first = Publication("first");
        var second = Publication("second");
        var shader = PublicationShader("first");
        var material = new MaterialAsset { shader = shader };
        var provider = new PublicationProvider(m_serialization) { artifact = first };
        var device = new RecordingRenderDevice();
        using var runtime = new RenderRuntime(m_types, device, new TestDiagnosticSink(), targetArtifacts: provider);
        RenderMaterialPass Read()
        {
            RenderMaterialPass? pass = null;
            ResourceProbePipeline.action = resources => Assert.True(resources.TryResolveGraphicsMaterial(material,
                new("test.publication"), new("draw"), null, null, out pass));
            RunResourceFrame(runtime);
            return Assert.IsType<RenderMaterialPass>(pass);
        }
        RenderMaterialPass original = Read();
        shader.SetDefinition(PublicationDefinition("uncompiled"), m_serialization, SerializationContext.empty);
        Assert.Equal(original.graphicsPipeline, Read().graphicsPipeline);
        provider.artifact = second;
        RenderMaterialPass replacement = Read();
        Assert.Equal("second", replacement.definition.name);
        Assert.NotEqual(original.graphicsPipeline, replacement.graphicsPipeline);
        Assert.Contains(original.graphicsPipeline, device.destroyedPrograms);
        provider.artifact = first;
        Assert.Equal("first", Read().definition.name);
        Assert.Equal(3, device.createdPrograms.Count);
    }

    [Fact]
    public void AllPreviouslyUsedPassesMustBeCreatedBeforeAnyReplacementIsPublished()
    {
        var shader = PublicationShader("main", "shadow");
        var material = new MaterialAsset { shader = shader };
        var provider = new PublicationProvider(m_serialization) { artifact = Publication("main", "shadow") };
        var device = new RecordingRenderDevice();
        using var runtime = new RenderRuntime(m_types, device, new TestDiagnosticSink(), targetArtifacts: provider);
        var resolved = new List<GraphicsPipelineHandle>();
        ResourceProbePipeline.action = resources =>
        {
            resolved.Clear();
            foreach (string role in new[] { "draw", "shadow" })
            {
                Assert.True(resources.TryResolveGraphicsMaterial(material, new("test.publication"), new(role), null, null, out RenderMaterialPass? pass));
                resolved.Add(pass!.graphicsPipeline);
            }
        };
        RunResourceFrame(runtime);
        GraphicsPipelineHandle[] original = resolved.ToArray();
        provider.artifact = Publication(["main", "shadow"], marker: 2);
        device.failProgramName = "Publication/shadow";
        RunResourceFrame(runtime);
        Assert.Equal(original, resolved);
        Assert.Single(device.destroyedPrograms);
        Assert.DoesNotContain(device.destroyedPrograms[0], original);
        device.failProgramName = null;
        RunResourceFrame(runtime);
        Assert.All(resolved, handle => Assert.DoesNotContain(handle, original));
        Assert.All(original, handle => Assert.Contains(handle, device.destroyedPrograms));
    }

    [Fact]
    public void ShaderArtifactChangesCannotSplitTheCurrentExtractionFrame()
    {
        var material = new MaterialAsset { shader = PublicationShader("first") };
        var first = Publication("first");
        var second = Publication("second");
        var provider = new PublicationProvider(m_serialization) { artifact = first };
        using var runtime = new RenderRuntime(m_types, new RecordingRenderDevice(), new TestDiagnosticSink(), targetArtifacts: provider);
        ResourceProbePipeline.action = resources =>
        {
            Assert.True(resources.TryResolveGraphicsMaterial(material, new("test.publication"), new("draw"), null, null, out RenderMaterialPass? a));
            provider.artifact = second;
            Assert.True(resources.TryResolveGraphicsMaterial(material, new("test.publication"), new("draw"), null, null, out RenderMaterialPass? b));
            Assert.Equal(a!.graphicsPipeline, b!.graphicsPipeline);
        };
        RunResourceFrame(runtime);
        Assert.Equal(1, provider.requests);
    }

    [Fact]
    public void CompletedRetirementFailureStillAttemptsEveryProgramExactlyOnce()
    {
        var material = new MaterialAsset { shader = PublicationShader("main", "shadow") };
        var provider = new PublicationProvider(m_serialization) { artifact = Publication("main", "shadow") };
        var device = new RecordingRenderDevice();
        using var runtime = new RenderRuntime(m_types, device, new TestDiagnosticSink(), targetArtifacts: provider);
        ResourceProbePipeline.action = resources =>
        {
            foreach (string role in new[] { "draw", "shadow" })
                Assert.True(resources.TryResolveGraphicsMaterial(material, new("test.publication"), new(role), null, null, out _));
        };
        RunResourceFrame(runtime);
        device.failedProgramRetirements = 1;
        Assert.ThrowsAny<Exception>(runtime.Dispose);
        Assert.Equal(2, device.destroyedPrograms.Count);
        runtime.Dispose();
        Assert.Equal(2, device.programRetirementAttempts);
    }

    [Fact]
    public void UnfinishedRetirementKeepsProgramOwnershipUntilOwnerDrainCompletes()
    {
        var material = new MaterialAsset { shader = PublicationShader("main", "shadow") };
        var provider = new PublicationProvider(m_serialization) { artifact = Publication("main", "shadow") };
        var device = new RecordingRenderDevice();
        using var runtime = new RenderRuntime(m_types, device, new TestDiagnosticSink(), targetArtifacts: provider);
        ResourceProbePipeline.action = resources =>
        {
            foreach (string role in new[] { "draw", "shadow" })
                Assert.True(resources.TryResolveGraphicsMaterial(material, new("test.publication"), new(role), null, null, out _));
        };
        RunResourceFrame(runtime);
        device.pendingProgramRetirements = 1;
        // RuntimeSubsystem.Dispose drains its retirement barrier on the owner thread. The first
        // attempt is unfinished; the same retained program must be retried before teardown completes.
        runtime.Dispose();
        Assert.Equal(2, device.destroyedPrograms.Count);
        Assert.All(device.createdPrograms, handle => Assert.Contains(handle, device.destroyedPrograms));
        Assert.Equal(3, device.programRetirementAttempts);
        runtime.Dispose();
        Assert.Equal(3, device.programRetirementAttempts);
    }

    [Fact]
    public void ShaderArtifactCodecRetainsTheExactDetachedRuntimeContract()
    {
        RenderShaderArtifact artifact = Publication("main");
        RenderShaderArtifact decoded = RenderShaderArtifactCodec.Decode(RenderShaderArtifactCodec.Encode(artifact), artifact.shaderName, artifact.variant);
        Assert.Equal(artifact.contentHash, decoded.contentHash);
        Assert.Equal(artifact.definitionData.ToArray(), decoded.definitionData.ToArray());
        var provider = new PublicationProvider(m_serialization);
        ShaderDefinition definition = provider.ReadShaderDefinition(decoded);
        definition.passes[0].name = "mutated";
        Assert.Equal("main", provider.ReadShaderDefinition(decoded).passes[0].name);
        Assert.NotEqual(artifact.contentHash, Publication(["main"], marker: 2).contentHash);
    }

    private ShaderAsset PublicationShader(params string[] passes)
    {
        var shader = new ShaderAsset();
        m_identities.InitializePersistentIdentity(shader, Guid.NewGuid());
        shader.SetDefinition(PublicationDefinition(passes), m_serialization, SerializationContext.empty);
        return shader;
    }

    private static ShaderDefinition PublicationDefinition(params string[] passes)
        => new("Publication", [], [], passes.Select(name => new ShaderPassDefinition(name, ShaderProgramKind.Raster)),
            [new(new("default"), new("test.publication"), passes.Select((name, index) => new ShaderTechniquePass(new(index == 0 ? "draw" : name), name)))]);

    private RenderShaderArtifact Publication(params string[] passes) => Publication(passes, 1);

    private RenderShaderArtifact Publication(string[] passes, byte marker)
        => new("Publication", "test.noop", RenderShaderVariant.empty, new([]),
            passes.Select(name => new RenderShaderPassArtifact(name, ShaderProgramKind.Raster, new(), new([]),
                [new(ShaderStage.Vertex, [marker]), new(ShaderStage.Fragment, [marker])])).ToArray(),
            m_serialization.Serialize(PublicationDefinition(passes), SerializationContext.empty));

    private sealed class PublicationProvider(SerializationRegistry serialization) : IRenderTargetArtifactProvider
    {
        internal RenderShaderArtifact? artifact;
        internal int requests;
        public ShaderDefinition ReadShaderDefinition(RenderShaderArtifact value)
            => serialization.Deserialize<ShaderDefinition>(value.definitionData.Span, SerializationContext.empty);
        public RenderTargetArtifactStatus GetShaderArtifact(ShaderAsset shader, RenderShaderVariant variant,
            GraphicsCapabilities capabilities, out RenderShaderArtifact? value)
        {
            requests++;
            value = artifact;
            return value is null ? RenderTargetArtifactStatus.Pending : RenderTargetArtifactStatus.Ready;
        }
        public RenderTargetArtifactStatus GetTextureArtifact(RenderTextureArtifactReference texture, out ReadOnlyMemory<byte> value)
        {
            value = default;
            return RenderTargetArtifactStatus.Unavailable;
        }
    }
}
