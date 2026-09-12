using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
public sealed class ShaderCompilerTests : IDisposable
{
    private readonly ModuleHost m_modules = new(new() { cacheDirectory = Path.Combine(Path.GetTempPath(), "ShaderCompilerTests", Guid.NewGuid().ToString("N")) });
    private readonly TypeCatalog m_types;
    private readonly SerializationRegistry m_serialization;
    private readonly ShaderNodeCompilerRegistry m_nodes;

    public ShaderCompilerTests()
    {
        m_types = new(m_modules);
        m_serialization = new(m_types);
        m_nodes = new(m_types);
    }

    [Fact]
    public void FailedToolResultsCannotCarrySuccessfulBinaryBytes()
        => Assert.Throws<ArgumentException>(() => new ShaderStageToolResult([1], [],
            [new("TEST_ERROR", DiagnosticSeverity.Error, "The adapter rejected its binary.", new("test", 1, 1))]));

    [Fact]
    public void ToolResultsCopyAdapterOwnedBytesBindingsAndDiagnostics()
    {
        byte[] bytes = [1, 2, 3];
        ShaderSourceDiagnostic[] diagnostics = [new("TEST", DiagnosticSeverity.Warning, "A warning", new("test", 1, 1))];
        ShaderStageBinding[] bindings = [new("color", "native_color", 0)];
        var result = new ShaderStageToolResult(bytes, bindings, diagnostics);
        bytes[0] = 9;
        diagnostics[0] = new("CHANGED", DiagnosticSeverity.Error, "Mutated input", new("test", 1, 1));
        bindings[0] = new("other", "other", 1);
        Assert.Equal(1, result.bytes.Span[0]);
        Assert.Equal("TEST", Assert.Single(result.diagnostics).code);
        Assert.Equal("color", Assert.Single(result.bindings).id);
    }

    [Fact]
    public async Task CompileAsyncUsesGraphLoweringAndTypedStages()
    {
        var toolchain = new FakeToolchain();
        ShaderCompilationResult result = await Compile(new(toolchain), Template());
        Assert.True(result.succeeded, Diagnostics(result));
        Assert.Equal(2, toolchain.requests.Count);
        Assert.Equal("color", Assert.Single(result.artifact!.shaderInterface.bindings).id.value);
    }

    [Fact]
    public async Task AdapterFailureRetainsOriginalSourceLocationAndNoPartialProgram()
    {
        ShaderCompilationResult result = await Compile(new(new FakeToolchain { reportError = true }), Template());
        Assert.False(result.succeeded);
        Assert.Null(result.artifact);
        ShaderDiagnostic error = Assert.Single(result.diagnostics.Where(static value => value.severity == DiagnosticSeverity.Error));
        Assert.Equal("Shaders/function.ishadersource", error.location!.Value.assetPath);
        Assert.Equal(2, error.location.Value.line);
        Assert.Equal(3, error.location.Value.column);
    }

    [Fact]
    public async Task UnsupportedAlternativePassKeepsSupportedPassAndExactBindingLayout()
    {
        GraphDocument graph = Template();
        ShaderDefinition definition = Definition(graph);
        GraphDocument alternate = graph.Clone();
        foreach (GraphNodeRecord original in alternate.nodes)
        {
            var node = new GraphNodeRecord(new("alternate." + original.id.value), original.definitionId);
            foreach ((string key, GraphSerializedValue value) in original.values) node.SetValue(key, value.Clone());
            if (node.definitionId == ShaderGraphDocument.outputDefinitionId)
            {
                ShaderGraphStageSettings settings = ShaderGraphDocument.Read<ShaderGraphStageSettings>(node, "settings", null!, m_serialization, SerializationContext.empty);
                settings.pass = "Clustered";
                node.SetValue("settings", ShaderGraphDocument.Encode(settings, m_serialization, SerializationContext.empty));
            }
            else node.SetValue("stage", ShaderGraphDocument.Encode("alternate." + ShaderGraphDocument.Read(node, "stage", "", m_serialization, SerializationContext.empty), m_serialization, SerializationContext.empty));
            graph.AddNode(node);
        }
        foreach (GraphEdgeRecord edge in alternate.edges)
            graph.AddEdge(new(new("alternate." + edge.id.value), new(new("alternate." + edge.output.nodeId.value), edge.output.portId), new(new("alternate." + edge.input.nodeId.value), edge.input.portId)));
        definition.passes = [new("Clustered", ShaderProgramKind.Raster, requiredFeatures: GraphicsCapability.Compute | GraphicsCapability.StorageBuffer), definition.passes[0]];
        graph.SetMetadata(ShaderGraphDocument.definitionKey, ShaderGraphDocument.Encode(m_serialization.Serialize(definition), m_serialization, SerializationContext.empty));
        var toolchain = new FakeToolchain();
        ShaderCompilationResult result = await Compile(new(toolchain), graph);
        Assert.True(result.succeeded, Diagnostics(result));
        Assert.Equal("Main", Assert.Single(result.artifact!.passes).definition.name);
        Assert.Equal(2, toolchain.requests.Count);
        Assert.Contains(result.diagnostics, static value => value.code == "SHADER_CAPABILITY_UNAVAILABLE" && value.severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public async Task LastGoodStorePreservesArtifactAfterFailedCandidate()
    {
        Guid shaderId = Guid.NewGuid();
        var store = new ShaderLastGoodStore();
        ShaderCompilationResult success = await Compile(new(new FakeToolchain()), Template());
        ShaderCompilationResult failure = await Compile(new(new FakeToolchain { reportError = true }), Template());
        ShaderArtifactSelection first = store.Select(shaderId, success.artifact!.targetKey, RenderShaderVariant.empty, success);
        ShaderArtifactSelection second = store.Select(shaderId, success.artifact.targetKey, RenderShaderVariant.empty, failure);
        Assert.True(first.candidateSucceeded);
        Assert.False(second.candidateSucceeded);
        Assert.True(second.usingLastGood);
        Assert.Same(success.artifact, second.artifact);
    }

    private GraphDocument Template() => ShaderGraphTemplates.CreateRaster(m_serialization, SerializationContext.empty);
    private ShaderDefinition Definition(GraphDocument graph) => ShaderGraphDocument.ReadDefinition(graph, m_serialization, SerializationContext.empty);
    private ValueTask<ShaderCompilationResult> Compile(ShaderCompiler compiler, GraphDocument graph)
    {
        ShaderGraphProgramResult program = new ShaderGraphProgramCompiler(m_nodes).Lower(graph, "tests.fake", new Dictionary<GraphNodeId, ShaderSourceModuleAnalysis>(), m_serialization, SerializationContext.empty);
        RenderTextureFormat[] formats = Enum.GetValues<RenderTextureFormat>();
        var capabilities = new GraphicsCapabilities(GraphicsApi.Metal, GraphicsCapability.None, new(256, 8, 8192, 16), formats, formats, formats, formats, false, false);
        return compiler.CompileAsync(Definition(graph), program, compiler.CreateTarget(capabilities), RenderShaderVariant.empty,
            m_serialization, SerializationContext.empty);
    }
    private static string Diagnostics(ShaderCompilationResult result) => string.Join("\n", result.diagnostics.Select(static value => value.message));

    public void Dispose() { m_nodes.Dispose(); m_serialization.Dispose(); m_types.Dispose(); m_modules.Dispose(); }

    private sealed class FakeToolchain : IShaderCompilerToolchain
    {
        public string implementationId => "tests.fake";
        internal bool reportError { get; init; }
        public IReadOnlyList<string> supportedSourceLanguages => [];
        internal List<ShaderStageToolRequest> requests { get; } = [];
        public ShaderCompileTarget CreateTarget(GraphicsCapabilities capabilities, bool optimize = true, bool debugInformation = false)
            => new("tests:fake", capabilities, optimize, debugInformation);
        public ValueTask<ShaderStageToolResult> CompileAsync(ShaderStageToolRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            requests.Add(request);
            return ValueTask.FromResult(reportError
                ? new ShaderStageToolResult([], [], [new("TEST_ERROR", DiagnosticSeverity.Error, "Opaque compiler failure", new("Shaders/function.ishadersource", 2, 3))])
                : new ShaderStageToolResult([1, 2, 3], request.stage.inputs.Where(static input => input.kind == ShaderIrInputKind.Uniform)
                    .Select(static input => new ShaderStageBinding(input.id, "native_" + input.id, 0)), []));
        }

    }
}
