using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Inno.Rendering.Core;
using Xunit;

namespace Inno.Rendering.Assets.Tests;

public sealed class ShaderCompilerTests
{
    [Fact]
    public async Task CompileAsync_UsesSameChainForHandwrittenAndGeneratedSources()
    {
        var toolchain = new FakeToolchain();
        var compiler = new ShaderCompiler(toolchain);
        ShaderCompileTarget target = CreateTarget(GraphicsFeature.Compute);

        ShaderCompilationResult handwritten = await compiler.CompileAsync(
            HandwrittenShaderParserTests.CreateModule("void main() {}", ShaderIRSourceKind.Handwritten),
            target,
            ShaderVariantKey.empty,
            "/project/Assets");
        ShaderCompilationResult generated = await compiler.CompileAsync(
            HandwrittenShaderParserTests.CreateModule("void main() {}", ShaderIRSourceKind.Generated),
            target,
            ShaderVariantKey.empty,
            "/project/Assets");

        Assert.True(handwritten.succeeded);
        Assert.True(generated.succeeded);
        Assert.Equal(4, toolchain.requests.Count);
        Assert.Equal(handwritten.artifact!.shaderInterface.bindings.Count,
            generated.artifact!.shaderInterface.bindings.Count);
    }

    [Fact]
    public async Task CompileAsync_MapsGeneratedLineBackToNode()
    {
        var compiler = new ShaderCompiler(new FakeToolchain());
        ShaderIRModule module = HandwrittenShaderParserTests.CreateModule(
            "line one\nFAIL",
            ShaderIRSourceKind.Generated);

        ShaderCompilationResult result = await compiler.CompileAsync(
            module,
            CreateTarget(GraphicsFeature.None),
            ShaderVariantKey.empty,
            "/project/Assets");

        Assert.False(result.succeeded);
        ShaderDiagnostic diagnostic = Assert.Single(result.diagnostics.Where(value =>
            value.severity == ShaderDiagnosticSeverity.Error));
        Assert.Equal("node-v", diagnostic.location!.Value.nodeId);
        Assert.Equal(2, diagnostic.location.Value.line);
    }

    [Fact]
    public async Task CompileAsync_SkipsUnsupportedAlternativePassAndKeepsFallback()
    {
        var toolchain = new FakeToolchain();
        var compiler = new ShaderCompiler(toolchain);
        var clustered = new ShaderPassDefinition(
            "Clustered",
            BuiltinShaderPassTags.ForwardLitClustered,
            null,
            null,
            null,
            null,
            GraphicsFeature.Compute | GraphicsFeature.StorageBuffer);
        var fallback = new ShaderPassDefinition(
            "Fallback",
            BuiltinShaderPassTags.ForwardLit,
            null,
            null,
            null,
            null);
        var definition = new ShaderDefinition("Tests/Alternatives", [], [], [clustered, fallback]);
        ShaderIRPass CreatePass(ShaderPassDefinition pass) => new(
            pass,
            [
                new ShaderIRStageModule(
                    ShaderStage.Vertex,
                    "main",
                    "void main() {}",
                    ShaderIRSourceKind.Generated,
                    new ShaderSourceLocation("Shaders/alternative.vs.sc", pass.name, ShaderStage.Vertex)),
                new ShaderIRStageModule(
                    ShaderStage.Fragment,
                    "main",
                    "void main() {}",
                    ShaderIRSourceKind.Generated,
                    new ShaderSourceLocation("Shaders/alternative.fs.sc", pass.name, ShaderStage.Fragment))
            ]);
        var module = new ShaderIRModule(definition, [CreatePass(clustered), CreatePass(fallback)]);

        ShaderCompilationResult result = await compiler.CompileAsync(
            module,
            CreateTarget(GraphicsFeature.None),
            ShaderVariantKey.empty,
            "/project/Assets");

        Assert.True(result.succeeded);
        Assert.Equal(BuiltinShaderPassTags.ForwardLit, Assert.Single(result.artifact!.passes).definition.tag);
        Assert.Equal(2, toolchain.requests.Count);
        Assert.Contains(result.diagnostics, static value =>
            value.code == "SHADER_IR_CAPABILITY_UNAVAILABLE"
            && value.severity == ShaderDiagnosticSeverity.Warning);
    }

    [Fact]
    public void ProfileCatalog_UsesMetalOnlyForMacTarget()
    {
        GraphicsCapabilities capabilities = CreateCapabilities(GraphicsBackend.Metal, GraphicsFeature.Compute);

        ShaderCompilerProfile profile = RendererProfileCatalog.Resolve(
            ShaderTargetPlatform.MacOSArm64,
            capabilities);

        Assert.Equal("osx", profile.shadercPlatform);
        Assert.Equal("metal", profile.GetStageProfile(ShaderStage.Compute));
        Assert.Throws<NotSupportedException>(() => RendererProfileCatalog.Resolve(
            ShaderTargetPlatform.WindowsX64,
            capabilities));
    }

    [Fact]
    public void LastGoodStore_PreservesArtifactAfterFailedCandidate()
    {
        Guid shaderId = Guid.NewGuid();
        var store = new ShaderLastGoodStore();
        CompiledShaderArtifact artifact = CreateArtifact();
        var success = new ShaderCompilationResult(artifact, []);
        var failure = new ShaderCompilationResult(
            null,
            [new ShaderDiagnostic("TEST", ShaderDiagnosticSeverity.Error, "Failure")]);

        ShaderArtifactSelection first = store.Select(
            shaderId,
            artifact.targetKey,
            ShaderVariantKey.empty,
            success);
        ShaderArtifactSelection second = store.Select(
            shaderId,
            artifact.targetKey,
            ShaderVariantKey.empty,
            failure);

        Assert.True(first.candidateSucceeded);
        Assert.False(second.candidateSucceeded);
        Assert.True(second.usingLastGood);
        Assert.Same(artifact, second.artifact);
    }

    private static ShaderCompileTarget CreateTarget(GraphicsFeature features)
    {
        GraphicsCapabilities capabilities = CreateCapabilities(GraphicsBackend.Metal, features);
        return new ShaderCompileTarget(
            RendererProfileCatalog.Resolve(ShaderTargetPlatform.MacOSArm64, capabilities),
            capabilities);
    }

    private static GraphicsCapabilities CreateCapabilities(
        GraphicsBackend backend,
        GraphicsFeature features)
        => new(
            backend,
            features,
            new GraphicsLimits(256, 8, 8192, 16),
            Enum.GetValues<RenderTextureFormat>(),
            Enum.GetValues<RenderTextureFormat>(),
            originBottomLeft: false,
            homogeneousDepth: false);

    private static CompiledShaderArtifact CreateArtifact()
    {
        ShaderIRModule module = HandwrittenShaderParserTests.CreateModule(
            "void main() {}",
            ShaderIRSourceKind.Handwritten);
        return new CompiledShaderArtifact(
            module.definition.name,
            "target",
            ShaderVariantKey.empty,
            ShaderInterface.FromModule(module),
            []);
    }

    private sealed class FakeToolchain : IShaderCompilerToolchain
    {
        internal List<ShaderToolRequest> requests { get; } = [];

        public ValueTask<ShaderToolResult> CompileAsync(
            ShaderToolRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            requests.Add(request);
            return request.stage.source.Contains("FAIL", StringComparison.Ordinal)
                ? ValueTask.FromResult(new ShaderToolResult(
                    null,
                    1,
                    string.Empty,
                    "generated.sc(2,1): error: test failure"))
                : ValueTask.FromResult(new ShaderToolResult(
                    Encoding.UTF8.GetBytes(request.stage.source),
                    0,
                    string.Empty,
                    string.Empty));
        }
    }
}
