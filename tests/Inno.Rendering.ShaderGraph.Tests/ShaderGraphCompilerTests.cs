using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Inno.Build.Toolchains.Bgfx.Tools;
using Inno.Extensibility.Modules;
using Inno.Core.Graphs;
using Inno.Extensibility.Types;
using Inno.Core.Serialization;
using Inno.Rendering.Assets;
using Inno.Rendering;
using Xunit;

namespace Inno.Rendering.ShaderGraph.Tests;

[Collection(ShaderNodeRegistryExtensionCollection.name)]
public sealed class ShaderGraphCompilerTests : IDisposable
{
    private readonly string m_cacheDirectory = Path.Combine(
        Path.GetTempPath(),
        "InnoShaderGraphCompilerTests",
        Guid.NewGuid().ToString("N"));
    private readonly ModuleHost m_modules;
    private readonly TypeCatalog m_types;
    private readonly SerializationRegistry m_serialization;

    public ShaderGraphCompilerTests()
    {
        m_modules = new ModuleHost(new ModuleHostOptions { cacheDirectory = m_cacheDirectory });
        m_types = new TypeCatalog(m_modules);
        m_serialization = new SerializationRegistry(m_types);
    }

    public void Dispose()
    {
        m_serialization.Dispose();
        m_types.Dispose();
        m_modules.Dispose();
        if (Directory.Exists(m_cacheDirectory))
        {
            Directory.Delete(m_cacheDirectory, recursive: true);
        }
    }

    [Fact]
    public void Compile_PluginRasterProgram_EmitsOpenContractThroughSharedIr()
    {
        ShaderGraphCompileResult result = ShaderGraphCompiler.Compile(
            "Shaders/PluginRaster.ishadergraph",
            "Plugin Raster",
            RasterGraph(),
            Registry(),
            m_serialization);

        Assert.True(result.succeeded, Format(result));
        ShaderIRModule module = Assert.IsType<ShaderIRModule>(result.module);
        ShaderTechniqueDefinition technique = Assert.Single(module.definition.techniques);
        Assert.Equal(TestRasterProgramDefinition.contractId, technique.contract.value);
        Assert.Equal(TestRasterProgramDefinition.roleId, Assert.Single(technique.passes).role.value);
        ShaderIRPass pass = Assert.Single(module.passes);
        Assert.Equal("Draw", pass.definition.name);
        Assert.Equal(ShaderProgramKind.Raster, pass.definition.programKind);
        Assert.All(pass.stages, static stage => Assert.Equal(ShaderIRSourceKind.Generated, stage.sourceKind));
        Assert.Contains("vec4(0.8, 0.2, 0.1, 1)", pass.stages[1].source, StringComparison.Ordinal);
        Assert.Equal("output", pass.stages[1].location.nodeId);
    }

    [Fact]
    public void Compile_PluginComputeProgram_EmitsComputeKernelThroughSharedIr()
    {
        var document = new GraphDocument();
        GraphNodeRecord value = ColorNode("value", 1f, 2f, 3f, 4f);
        GraphNodeRecord output = Node("output", TestComputeProgramDefinition.ID);
        document.AddNode(value);
        document.AddNode(output);
        document.AddEdge(Edge("e", value, "value", output, "value"));

        ShaderGraphCompileResult result = ShaderGraphCompiler.Compile(
            "Shaders/PluginCompute.ishadergraph",
            "Plugin Compute",
            document,
            Registry(),
            m_serialization);

        Assert.True(result.succeeded, Format(result));
        ShaderIRPass pass = Assert.Single(result.module!.passes);
        ShaderIRStageModule stage = Assert.Single(pass.stages);
        Assert.Equal(ShaderStage.Compute, stage.stage);
        Assert.Contains("gl_GlobalInvocationID.x", stage.source, StringComparison.Ordinal);
        Assert.True((pass.definition.requiredFeatures & GraphicsFeature.Compute) != 0);
        Assert.Equal(TestComputeProgramDefinition.contractId, result.module.definition.techniques[0].contract.value);
    }

    [Fact]
    public async Task Compile_PluginRasterProgram_RealShadercAcceptsGeneratedPass()
    {
        BgfxShaderTargetPlatform platform;
        GraphicsBackend backend;
        if (OperatingSystem.IsMacOS())
        {
            platform = BgfxShaderTargetPlatform.MacOSArm64;
            backend = GraphicsBackend.Metal;
        }
        else if (OperatingSystem.IsWindows())
        {
            platform = BgfxShaderTargetPlatform.WindowsX64;
            backend = GraphicsBackend.Direct3D11;
        }
        else
        {
            return;
        }

        ShaderGraphCompileResult graphResult = ShaderGraphCompiler.Compile(
            "Shaders/PluginRaster.ishadergraph",
            "Plugin Raster",
            RasterGraph(),
            Registry(),
            m_serialization);
        Assert.True(graphResult.succeeded, Format(graphResult));
        GraphicsCapabilities capabilities = new(
            backend,
            GraphicsFeature.Compute | GraphicsFeature.StorageBuffer,
            new GraphicsLimits(256, 8, 16384, 16),
            Enum.GetValues<RenderTextureFormat>(),
            Enum.GetValues<RenderTextureFormat>(),
            Enum.GetValues<RenderTextureFormat>(),
            Enum.GetValues<RenderTextureFormat>(),
            originBottomLeft: false,
            homogeneousDepth: false);
        var shaderCompiler = new ShaderCompiler(new BgfxShadercToolchain(platform));
        ShaderCompileTarget target = shaderCompiler.CreateTarget(
            capabilities,
            optimize: false,
            debugInformation: true);

        ShaderCompilationResult result = await shaderCompiler.CompileAsync(
            graphResult.module!,
            target,
            RenderShaderVariant.empty,
            Path.GetTempPath());

        Assert.True(result.succeeded, string.Join(
            Environment.NewLine,
            result.diagnostics.Select(static value => $"{value.code}: {value.message}")));
        Assert.Single(result.artifact!.passes);
        Assert.Equal("Draw", result.artifact.passes[0].definition.name);
    }

    [Fact]
    public void Compile_MissingNode_PreservesNeutralDocumentAndReturnsMappedError()
    {
        var document = new GraphDocument();
        document.AddNode(Node("missing", "project.shader.missing"));
        byte[] before = ShaderGraphDocumentCodec.Encode(document, m_serialization);

        ShaderGraphCompileResult result = ShaderGraphCompiler.Compile(
            "Shaders/Missing.ishadergraph",
            "Missing",
            document,
            Registry(),
            m_serialization);

        Assert.False(result.succeeded);
        Assert.Contains(result.diagnostics, static value => value.code == "SHADER_GRAPH_MISSING_NODE");
        Assert.Equal(before, ShaderGraphDocumentCodec.Encode(document, m_serialization));
    }

    [Fact]
    public void Compile_StageIllegalExtension_ReturnsNodeMappedError()
    {
        ShaderNodeDefinition[] definitions =
        [
            new ConstantColorDefinition(),
            new TestRasterProgramDefinition(),
            new VertexOnlyDefinition()
        ];
        using var registry = new ShaderNodeRegistry();
        registry.Replace(definitions);
        var document = new GraphDocument();
        GraphNodeRecord value = Node("value", VertexOnlyDefinition.ID);
        GraphNodeRecord output = Node("output", TestRasterProgramDefinition.ID);
        document.AddNode(value);
        document.AddNode(output);
        document.AddEdge(Edge("e", value, "value", output, "color"));

        ShaderGraphCompileResult result = ShaderGraphCompiler.Compile(
            "Shaders/Illegal.ishadergraph",
            "Illegal",
            document,
            registry,
            m_serialization);

        ShaderDiagnostic diagnostic = Assert.Single(
            result.diagnostics.Where(static value => value.code == "SHADER_GRAPH_STAGE_ILLEGAL"));
        Assert.Equal("value", diagnostic.location?.nodeId);
    }

    [Fact]
    public void Compile_WithoutPluginProgramOutput_ReturnsExplicitError()
    {
        var document = new GraphDocument();
        document.AddNode(ColorNode("color", 1f, 1f, 1f, 1f));

        ShaderGraphCompileResult result = ShaderGraphCompiler.Compile(
            "Shaders/NoProgram.ishadergraph",
            "No Program",
            document,
            Registry(),
            m_serialization);

        Assert.False(result.succeeded);
        Assert.Contains(result.diagnostics, static value => value.code == "SHADER_GRAPH_PROGRAM_OUTPUT_COUNT");
    }

    [Fact]
    public void Registry_DefaultGenerationIsEmptyAndDuplicateCandidateIsAtomic()
    {
        using var empty = new ShaderNodeRegistry();
        Assert.Empty(empty.definitions);

        using ShaderNodeRegistry registry = Registry();
        int before = registry.definitions.Count;
        var duplicate = new ConstantColorDefinition();

        Assert.Throws<ArgumentException>(() => registry.Replace([duplicate, duplicate]));

        Assert.Equal(before, registry.definitions.Count);
        Assert.True(registry.TryResolveShader(TestRasterProgramDefinition.ID, out _));
    }

    [Fact]
    public void Codec_RoundTripsNativeNeutralDocumentAndRejectsCorruptPayload()
    {
        GraphDocument document = RasterGraph();
        document.SetMetadata("tests.preview", GraphSerializedValue.From("sphere", m_serialization));

        byte[] bytes = ShaderGraphDocumentCodec.Encode(document, m_serialization);
        ShaderGraphDocumentData decoded = ShaderGraphDocumentCodec.Decode(bytes, m_serialization);

        Assert.Equal(2, decoded.document.nodes.Count);
        Assert.Equal(
            "sphere",
            decoded.document.metadata["tests.preview"].Deserialize<string>(m_serialization));
        Assert.ThrowsAny<Exception>(() => ShaderGraphDocumentCodec.Decode([1, 2, 3], m_serialization));
    }

    private GraphDocument RasterGraph()
    {
        var document = new GraphDocument();
        GraphNodeRecord color = ColorNode("color", 0.8f, 0.2f, 0.1f, 1f);
        GraphNodeRecord output = Node("output", TestRasterProgramDefinition.ID);
        document.AddNode(color);
        document.AddNode(output);
        document.AddEdge(Edge("color-edge", color, "value", output, "color"));
        return document;
    }

    private static ShaderNodeRegistry Registry()
    {
        var registry = new ShaderNodeRegistry();
        registry.Replace(
        [
            new ConstantColorDefinition(),
            new TestRasterProgramDefinition(),
            new TestComputeProgramDefinition()
        ]);
        return registry;
    }

    private GraphNodeRecord ColorNode(
        string id,
        float x,
        float y,
        float z,
        float w)
    {
        GraphNodeRecord node = Node(id, ConstantColorDefinition.ID);
        node.SetValue(
            "value",
            GraphSerializedValue.From(new[] { x, y, z, w }, m_serialization));
        return node;
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

    private static string Format(ShaderGraphCompileResult result)
        => string.Join(Environment.NewLine, result.diagnostics.Select(
            static value => $"{value.code}: {value.message}"));

    private sealed class ConstantColorDefinition : ShaderNodeDefinition
    {
        public const string ID = "tests.shader.constant-color";

        public ConstantColorDefinition()
            : base(ID, "Constant Color", "Tests", ShaderStage.Vertex | ShaderStage.Fragment | ShaderStage.Compute)
        {
        }

        public override IReadOnlyList<GraphPortDefinition> GetPorts(GraphNodeRecord node)
        {
            _ = node;
            return
            [
                new GraphPortDefinition(
                    new GraphPortId("value"),
                    "Value",
                    ShaderGraphValueTypes.GetId(ShaderValueType.Color),
                    GraphPortDirection.Output)
            ];
        }

        public override void Emit(ShaderNodeEmitContext context)
        {
            float[] value = context.ReadValue("value", new[] { 1f, 1f, 1f, 1f });
            string expression = string.Create(
                CultureInfo.InvariantCulture,
                $"vec4({value[0]:0.########}, {value[1]:0.########}, {value[2]:0.########}, {value[3]:0.########})");
            context.SetOutput(
                new GraphPortId("value"),
                new ShaderValue(ShaderValueType.Color, expression, context.node.id));
        }
    }

    private sealed class TestRasterProgramDefinition : ShaderGraphProgramNodeDefinition
    {
        public const string ID = "tests.shader.raster-program";
        public const string contractId = "tests.raster-contract";
        public const string roleId = "tests.draw";

        public TestRasterProgramDefinition()
            : base(ID, "Test Raster Program", "Tests", ShaderStage.Fragment)
        {
        }

        public override IReadOnlyList<GraphPortDefinition> GetPorts(GraphNodeRecord node)
        {
            _ = node;
            return
            [
                new GraphPortDefinition(
                    new GraphPortId("color"),
                    "Color",
                    ShaderGraphValueTypes.GetId(ShaderValueType.Color),
                    GraphPortDirection.Input,
                    required: true)
            ];
        }

        public override void Emit(ShaderNodeEmitContext context)
            => context.SetSemantic("color", context.GetInput(new GraphPortId("color")));

        public override ShaderIRModule BuildProgram(ShaderGraphProgramContext context)
        {
            ShaderGraphEmission fragment = context.Emit(ShaderStage.Fragment);
            ShaderValue color = fragment.GetSemantic("color");
            var pass = new ShaderPassDefinition("Draw", ShaderProgramKind.Raster);
            string vertexSource = """
                $input a_position
                #include <bgfx_shader.sh>

                void main()
                {
                    gl_Position = vec4(a_position, 1.0);
                }
                """;
            string fragmentSource = $$"""
                #include <bgfx_shader.sh>

                void main()
                {
                    gl_FragColor = {{color.expression}};
                }
                """;
            var definition = new ShaderDefinition(
                context.shaderName,
                fragment.properties,
                [],
                [pass],
                [new ShaderTechniqueDefinition(
                    new ShaderTechniqueId("default"),
                    new ShaderContractId(contractId),
                    [new ShaderTechniquePass(new ShaderPassRoleId(roleId), pass.name)])]);
            return new ShaderIRModule(
                definition,
                [new ShaderIRPass(
                    pass,
                    [
                        new ShaderIRStageModule(
                            ShaderStage.Vertex,
                            "main",
                            vertexSource,
                            ShaderIRSourceKind.Generated,
                            new ShaderSourceLocation(context.assetPath, pass.name, ShaderStage.Vertex, nodeId: context.outputNode.id.value)),
                        context.CreateStage(pass.name, ShaderStage.Fragment, fragmentSource, fragment)
                    ],
                    "vec3 a_position : POSITION;")]);
        }
    }

    private sealed class TestComputeProgramDefinition : ShaderGraphProgramNodeDefinition
    {
        public const string ID = "tests.shader.compute-program";
        public const string contractId = "tests.compute-contract";

        public TestComputeProgramDefinition()
            : base(ID, "Test Compute Program", "Tests", ShaderStage.Compute)
        {
        }

        public override IReadOnlyList<GraphPortDefinition> GetPorts(GraphNodeRecord node)
        {
            _ = node;
            return
            [
                new GraphPortDefinition(
                    new GraphPortId("value"),
                    "Value",
                    ShaderGraphValueTypes.GetId(ShaderValueType.Color),
                    GraphPortDirection.Input,
                    required: true)
            ];
        }

        public override void Emit(ShaderNodeEmitContext context)
            => context.SetSemantic("value", context.GetInput(new GraphPortId("value")));

        public override ShaderIRModule BuildProgram(ShaderGraphProgramContext context)
        {
            ShaderGraphEmission compute = context.Emit(ShaderStage.Compute);
            ShaderValue value = compute.GetSemantic("value");
            var pass = new ShaderPassDefinition(
                "Compute",
                ShaderProgramKind.Compute,
                requiredFeatures: GraphicsFeature.Compute | GraphicsFeature.StorageBuffer);
            string source = $$"""
                #include <bgfx_compute.sh>
                BUFFER_RW(inno_test_output, vec4, 0);
                NUM_THREADS(1, 1, 1)

                void main()
                {
                    inno_test_output[gl_GlobalInvocationID.x] = {{value.expression}};
                }
                """;
            var definition = new ShaderDefinition(
                context.shaderName,
                [],
                [],
                [pass],
                [new ShaderTechniqueDefinition(
                    new ShaderTechniqueId("default"),
                    new ShaderContractId(contractId),
                    [new ShaderTechniquePass(new ShaderPassRoleId("tests.dispatch"), pass.name)],
                    GraphicsFeature.Compute | GraphicsFeature.StorageBuffer)]);
            return new ShaderIRModule(
                definition,
                [new ShaderIRPass(pass, [context.CreateStage(pass.name, ShaderStage.Compute, source, compute)])]);
        }
    }

    private sealed class VertexOnlyDefinition : ShaderNodeDefinition
    {
        public const string ID = "tests.shader.vertex-only";

        public VertexOnlyDefinition()
            : base(ID, "Vertex Only", "Tests", ShaderStage.Vertex)
        {
        }

        public override IReadOnlyList<GraphPortDefinition> GetPorts(GraphNodeRecord node)
        {
            _ = node;
            return
            [
                new GraphPortDefinition(
                    new GraphPortId("value"),
                    "Value",
                    ShaderGraphValueTypes.GetId(ShaderValueType.Color),
                    GraphPortDirection.Output)
            ];
        }

        public override void Emit(ShaderNodeEmitContext context)
            => context.SetOutput(
                new GraphPortId("value"),
                new ShaderValue(ShaderValueType.Color, "vec4(1.0)", context.node.id));
    }
}
