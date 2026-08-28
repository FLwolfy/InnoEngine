using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Inno.Core.Graphs;
using Inno.Rendering.Assets;
using Inno.Rendering.Core;
using Xunit;

namespace Inno.Rendering.ShaderGraph.Tests;

public sealed class ShaderGraphCompilerTests
{
    [Fact]
    public void Compile_Surface_EmitsSharedProductionPasses()
    {
        GraphDocument document = SurfaceGraph();

        ShaderGraphCompileResult result = ShaderGraphCompiler.Compile(
            "Shaders/Pbr.ishadergraph",
            "PBR Graph",
            ShaderGraphTarget.Surface,
            document,
            Registry());

        Assert.True(result.succeeded, Format(result));
        ShaderIRModule module = Assert.IsType<ShaderIRModule>(result.module);
        Assert.Equal(
            [
                BuiltinShaderPassTags.ForwardLitClustered,
                BuiltinShaderPassTags.ForwardLit,
                BuiltinShaderPassTags.GBuffer,
                BuiltinShaderPassTags.DepthOnly,
                BuiltinShaderPassTags.ShadowCaster,
                BuiltinShaderPassTags.Picking
            ],
            module.passes.Select(static pass => pass.definition.tag));
        ShaderIRStageModule forward = module.passes[0].stages.Single(
            static stage => stage.stage == ShaderStage.Fragment);
        Assert.Contains("inno_camera_position.xyz - v_worldPosition", forward.source, StringComparison.Ordinal);
        Assert.Contains("BUFFER_RO(inno_cluster_grid, uint", forward.source, StringComparison.Ordinal);
        Assert.Contains("inno_cluster_light_indices[inno_grid_offset", forward.source, StringComparison.Ordinal);
        Assert.Contains(
            module.passes[0].bindingIds,
            static value => value.value == "inno_cluster_grid");
        Assert.DoesNotContain(
            module.passes[1].bindingIds,
            static value => value.value == "inno_cluster_grid");
        Assert.Contains("gl_FragColor = inno_object_id", module.passes[^1].stages[1].source, StringComparison.Ordinal);
        Assert.All(module.passes, static pass => Assert.NotNull(pass.generatedVaryingSource));
    }

    [Fact]
    public void Compile_Compute_EmitsStorageKernelThroughCommonIr()
    {
        var document = new GraphDocument();
        GraphNodeRecord value = Node("value", BuiltinShaderNodes.Float4);
        value.SetValue("value", Json(new[] { 1f, 2f, 3f, 4f }));
        GraphNodeRecord output = Node("output", BuiltinShaderNodes.ComputeOutput);
        document.AddNode(value);
        document.AddNode(output);
        document.AddEdge(Edge("e", value, "value", output, "value"));

        ShaderGraphCompileResult result = ShaderGraphCompiler.Compile(
            "Shaders/Compute.ishadergraph",
            "Compute Graph",
            ShaderGraphTarget.Compute,
            document,
            Registry());

        Assert.True(result.succeeded, Format(result));
        ShaderIRPass pass = Assert.Single(result.module!.passes);
        ShaderIRStageModule stage = Assert.Single(pass.stages);
        Assert.Equal(ShaderStage.Compute, stage.stage);
        Assert.Contains("inno_compute_output[gl_GlobalInvocationID.x]", stage.source, StringComparison.Ordinal);
        Assert.True((pass.definition.requiredFeatures & GraphicsFeature.Compute) != 0);
    }

    [Fact]
    public async Task Compile_Surface_RealShadercAcceptsClusteredAndFallbackPasses()
    {
        ShaderTargetPlatform platform;
        GraphicsBackend backend;
        if (OperatingSystem.IsMacOS())
        {
            platform = ShaderTargetPlatform.MacOSArm64;
            backend = GraphicsBackend.Metal;
        }
        else if (OperatingSystem.IsWindows())
        {
            platform = ShaderTargetPlatform.WindowsX64;
            backend = GraphicsBackend.Direct3D11;
        }
        else
        {
            return;
        }

        ShaderGraphCompileResult graphResult = ShaderGraphCompiler.Compile(
            "Shaders/Pbr.ishadergraph",
            "PBR Graph",
            ShaderGraphTarget.Surface,
            SurfaceGraph(),
            Registry());
        Assert.True(graphResult.succeeded, Format(graphResult));
        GraphicsCapabilities capabilities = new(
            backend,
            GraphicsFeature.Compute | GraphicsFeature.StorageBuffer,
            new GraphicsLimits(256, 8, 16384, 16),
            Enum.GetValues<RenderTextureFormat>(),
            Enum.GetValues<RenderTextureFormat>(),
            originBottomLeft: false,
            homogeneousDepth: false);
        var target = new ShaderCompileTarget(
            RendererProfileCatalog.Resolve(platform, capabilities),
            capabilities,
            optimize: false,
            debugInformation: true);

        ShaderCompilationResult result = await new ShaderCompiler().CompileAsync(
            graphResult.module!,
            target,
            ShaderVariantKey.empty,
            Path.GetTempPath());

        Assert.True(result.succeeded, string.Join(
            Environment.NewLine,
            result.diagnostics.Select(static value => $"{value.code}: {value.message}")));
        Assert.Equal(6, result.artifact!.passes.Count);
        Assert.Contains(result.artifact.passes, static pass =>
            pass.definition.tag == BuiltinShaderPassTags.ForwardLitClustered
            && pass.shaderInterface.bindings.Any(binding => binding.id.value == "inno_cluster_grid"));
        Assert.Contains(result.artifact.passes, static pass =>
            pass.definition.tag == BuiltinShaderPassTags.ForwardLit
            && pass.shaderInterface.bindings.All(binding => binding.id.value != "inno_cluster_grid"));
    }

    [Fact]
    public void Compile_VertexFragment_EmitsConnectedVertexStack()
    {
        var document = new GraphDocument();
        GraphNodeRecord position = Node("position", BuiltinShaderNodes.Float3);
        position.SetValue("value", Json(new[] { 0f, 0.25f, 0f }));
        GraphNodeRecord vertex = Node("vertex", BuiltinShaderNodes.VertexOutput);
        GraphNodeRecord color = Node("color", BuiltinShaderNodes.Color);
        color.SetValue("value", Json(new[] { 0.2f, 0.4f, 1f, 1f }));
        GraphNodeRecord fragment = Node("fragment", BuiltinShaderNodes.FragmentOutput);
        document.AddNode(position);
        document.AddNode(vertex);
        document.AddNode(color);
        document.AddNode(fragment);
        document.AddEdge(Edge("position-edge", position, "value", vertex, "position"));
        document.AddEdge(Edge("color-edge", color, "value", fragment, "color"));

        ShaderGraphCompileResult result = ShaderGraphCompiler.Compile(
            "Shaders/VertexFragment.ishadergraph",
            "Vertex Fragment",
            ShaderGraphTarget.VertexFragment,
            document,
            Registry());

        Assert.True(result.succeeded, Format(result));
        ShaderIRStageModule vertexStage = result.module!.passes[0].stages.Single(
            static stage => stage.stage == ShaderStage.Vertex);
        Assert.Contains("vec4(vec3(0, 0.25, 0), 1.0)", vertexStage.source, StringComparison.Ordinal);
        Assert.Equal("vertex", vertexStage.location.nodeId);
    }

    [Fact]
    public void Compile_MissingNode_PreservesNeutralDocumentAndReturnsMappedError()
    {
        var document = new GraphDocument();
        document.AddNode(Node("missing", "project.shader.missing"));
        string before = ShaderGraphDocumentCodec.Encode(ShaderGraphTarget.Surface, document);

        ShaderGraphCompileResult result = ShaderGraphCompiler.Compile(
            "Shaders/Missing.ishadergraph",
            "Missing",
            ShaderGraphTarget.Surface,
            document,
            Registry());

        Assert.False(result.succeeded);
        Assert.Contains(result.diagnostics, static value => value.code == "SHADER_GRAPH_MISSING_NODE");
        Assert.Equal(before, ShaderGraphDocumentCodec.Encode(ShaderGraphTarget.Surface, document));
    }

    [Fact]
    public void Compile_StageIllegalExtension_ReturnsNodeMappedError()
    {
        ShaderNodeDefinition[] definitions = [.. BuiltinShaderNodes.CreateDefinitions(), new VertexOnlyDefinition()];
        var registry = new ShaderNodeRegistry();
        registry.Replace(definitions);
        var document = new GraphDocument();
        GraphNodeRecord value = Node("value", VertexOnlyDefinition.ID);
        GraphNodeRecord output = Node("output", BuiltinShaderNodes.FragmentOutput);
        document.AddNode(value);
        document.AddNode(output);
        document.AddEdge(Edge("e", value, "value", output, "color"));

        ShaderGraphCompileResult result = ShaderGraphCompiler.Compile(
            "Shaders/Illegal.ishadergraph",
            "Illegal",
            ShaderGraphTarget.VertexFragment,
            document,
            registry);

        ShaderDiagnostic diagnostic = Assert.Single(
            result.diagnostics.Where(static value => value.code == "SHADER_GRAPH_STAGE_ILLEGAL"));
        Assert.Equal("value", diagnostic.location?.nodeId);
    }

    [Fact]
    public void Registry_DuplicateCandidate_DoesNotReplaceActiveGeneration()
    {
        ShaderNodeRegistry registry = Registry();
        int before = registry.definitions.Count;
        ShaderNodeDefinition duplicate = BuiltinShaderNodes.CreateDefinitions()[0];

        Assert.Throws<ArgumentException>(() => registry.Replace([duplicate, duplicate]));

        Assert.Equal(before, registry.definitions.Count);
        Assert.True(registry.TryResolveShader(BuiltinShaderNodes.SurfaceOutput, out _));
    }

    [Fact]
    public void Codec_RoundTripsCommentsAndRejectsUnknownFields()
    {
        string json = ShaderGraphDocumentCodec.Encode(ShaderGraphTarget.Surface, SurfaceGraph());
        ShaderGraphDocumentData decoded = ShaderGraphDocumentCodec.Decode(
            json.Replace("{", "{/* accepted comment */", StringComparison.Ordinal).Replace(
                "\n}",
                "\n,}",
                StringComparison.Ordinal));
        Assert.Equal(ShaderGraphTarget.Surface, decoded.target);
        Assert.Equal(2, decoded.document.nodes.Count);
        Assert.Throws<JsonException>(() => ShaderGraphDocumentCodec.Decode(
            "{\"target\":\"Surface\",\"nodes\":[],\"edges\":[],\"metadata\":{},\"schemaVersion\":1}"));
    }

    private static GraphDocument SurfaceGraph()
    {
        var document = new GraphDocument();
        GraphNodeRecord color = Node("color", BuiltinShaderNodes.Color);
        color.SetValue("value", Json(new[] { 0.8f, 0.2f, 0.1f, 1f }));
        GraphNodeRecord output = Node("surface", BuiltinShaderNodes.SurfaceOutput);
        document.AddNode(color);
        document.AddNode(output);
        document.AddEdge(Edge("base-color", color, "value", output, "baseColor"));
        return document;
    }

    private static ShaderNodeRegistry Registry()
    {
        var registry = new ShaderNodeRegistry();
        registry.Replace(BuiltinShaderNodes.CreateDefinitions());
        return registry;
    }

    private static GraphNodeRecord Node(string id, string definition)
        => new(new GraphNodeId(id), definition);

    private static GraphEdgeRecord Edge(
        string id,
        GraphNodeRecord outputNode,
        string outputPort,
        GraphNodeRecord inputNode,
        string inputPort)
        => new(
            new GraphEdgeId(id),
            new GraphEndpoint(outputNode.id, new GraphPortId(outputPort)),
            new GraphEndpoint(inputNode.id, new GraphPortId(inputPort)));

    private static GraphSerializedValue Json<T>(T value)
        => new(JsonSerializer.Serialize(value));

    private static string Format(ShaderGraphCompileResult result)
        => string.Join(Environment.NewLine, result.diagnostics.Select(
            static value => $"{value.code}: {value.message}"));

    private sealed class VertexOnlyDefinition : ShaderNodeDefinition
    {
        public const string ID = "test.shader.vertex-only";

        public VertexOnlyDefinition()
            : base(ID, "Vertex Only", "Tests", ShaderStage.Vertex)
        {
        }

        public override IReadOnlyList<GraphPortDefinition> GetPorts(GraphNodeRecord node)
            =>
            [
                new GraphPortDefinition(
                    new GraphPortId("value"),
                    "Value",
                    ShaderGraphValueTypes.GetId(ShaderValueType.Color),
                    GraphPortDirection.Output)
            ];

        public override void Emit(ShaderNodeEmitContext context)
            => context.SetOutput(
                new GraphPortId("value"),
                new ShaderValue(ShaderValueType.Color, "vec4(1.0)", context.node.id));
    }
}
