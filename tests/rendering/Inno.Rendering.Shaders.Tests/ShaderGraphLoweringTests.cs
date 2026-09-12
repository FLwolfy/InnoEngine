using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inno.Build.Toolchains.Bgfx.Tools;
using Inno.Core.Execution;
using Inno.Core.Graphs;
using Inno.Core.Serialization;
using Inno.Extensibility.Modules;
using Inno.Extensibility.Types;
using Inno.Rendering;
using Inno.Rendering.Assets;
using Xunit;

namespace Inno.Rendering.Shaders.Tests;

[Collection("Shader registry fault injection")]
public sealed partial class ShaderGraphLoweringTests : IDisposable
{
    private readonly string m_root = Path.Combine(Path.GetTempPath(), "InnoShaderGraphTests", Guid.NewGuid().ToString("N"));
    private readonly ModuleHost m_modules;
    private readonly TypeCatalog m_types;
    private readonly SerializationRegistry m_serialization;

    public ShaderGraphLoweringTests()
    {
        m_modules = new(new ModuleHostOptions { cacheDirectory = m_root });
        m_types = new(m_modules);
        m_serialization = new(m_types);
    }

    [MetalShaderFact]
    public async Task RealGraphMixesAnIndependentNodeExtensionAndSourceFunctionInOneNativeCompilation()
    {
        GraphDocument graph = Graph(out GraphNodeRecord constant, out GraphNodeRecord extension, out GraphNodeRecord source, out GraphNodeRecord color);
        Set(constant, "value", 0.25f);
        ShaderSourceModuleAnalysis module = Source("float Shade(float value) { return value * 0.75; }");
        var request = new ShaderGraphLoweringRequest(graph, new Dictionary<string, GraphEndpoint> { ["color"] = Endpoint(color, "value") }, "metal",
            new Dictionary<GraphNodeId, ShaderSourceModuleAnalysis> { [source.id] = module });
        Set(constant, "value", 99f);
        extension.position = new(900, 1200);
        ShaderGraphLoweringResult lowered = Catalog(new DoubleCompiler()).Lower(request, m_serialization, SerializationContext.empty);
        Assert.True(lowered.succeeded, Errors(lowered));
        Assert.Contains(lowered.block!.instructions, instruction => instruction.operation == ShaderIrOperation.SourceCall);
        Assert.Contains(lowered.block.instructions, instruction => instruction.operation == ShaderIrOperation.Multiply);
        Assert.Equal(BitConverter.SingleToUInt32Bits(0.25f), lowered.block.instructions[0].constantBits);
        var stage = new ShaderIrStage(ShaderStage.Fragment, lowered.block, [], [new("color", ShaderIrOutputKind.Color)]);
        var compiler = new ShaderCompiler(new BgfxShadercToolchain(BgfxShaderTargetPlatform.MacOSArm64));
        var caps = new GraphicsCapabilities(GraphicsApi.Metal, GraphicsCapability.None, new(256, 8, 8192, 16),
            Enum.GetValues<RenderTextureFormat>(), Enum.GetValues<RenderTextureFormat>(), Enum.GetValues<RenderTextureFormat>(),
            Enum.GetValues<RenderTextureFormat>(), false, false);
        ShaderStageToolResult compiled = await compiler.CompileAsync(stage, compiler.CreateTarget(caps));
        Assert.True(compiled.succeeded, string.Join("\n", compiled.diagnostics.Select(static diagnostic => diagnostic.message)));
    }

    [Fact]
    public void LayoutOnlyEditsKeepTheSameTypedStageSemanticHash()
    {
        GraphDocument graph = Graph(out GraphNodeRecord constant, out GraphNodeRecord extension, out GraphNodeRecord source, out GraphNodeRecord color);
        ShaderSourceModuleAnalysis module = Source("float Shade(float value) { return value; }");
        string Hash()
        {
            var request = new ShaderGraphLoweringRequest(graph, new Dictionary<string, GraphEndpoint> { ["color"] = Endpoint(color, "value") }, "metal",
                new Dictionary<GraphNodeId, ShaderSourceModuleAnalysis> { [source.id] = module });
            ShaderGraphLoweringResult result = Catalog(new DoubleCompiler()).Lower(request, m_serialization, SerializationContext.empty);
            Assert.True(result.succeeded, Errors(result));
            return new ShaderIrStage(ShaderStage.Fragment, result.block!, [], [new("color", ShaderIrOutputKind.Color)]).contentHash;
        }
        string first = Hash();
        constant.position = new(912, -8);
        extension.position = new(190, 505);
        source.position = new(-55, -800);
        Assert.Equal(first, Hash());
        Set(constant, "value", 7f);
        Assert.NotEqual(first, Hash());
        string second = Hash();
        module = Source("float Shade(float value) { return value * 2.0; }");
        Assert.NotEqual(second, Hash());
    }

    [Fact]
    public void MissingCompilerAndRenamedSourcePortPreserveEveryOriginalConnection()
    {
        GraphDocument graph = Graph(out _, out _, out GraphNodeRecord source, out GraphNodeRecord color);
        int edgeCount = graph.edges.Count;
        ShaderGraphLoweringRequest Request(ShaderSourceModuleAnalysis module) => new(graph,
            new Dictionary<string, GraphEndpoint> { ["color"] = Endpoint(color, "value") }, "metal",
            new Dictionary<GraphNodeId, ShaderSourceModuleAnalysis> { [source.id] = module });
        ShaderGraphLoweringResult missing = Catalog().Lower(Request(Source("float Shade(float value) { return value; }")), m_serialization, SerializationContext.empty);
        Assert.False(missing.succeeded);
        Assert.Contains(missing.diagnostics, diagnostic => diagnostic.code == "SHADER_NODE_COMPILER_MISSING");
        ShaderGraphLoweringResult renamed = Catalog(new DoubleCompiler()).Lower(Request(Source("float Shade(float renamed) { return renamed; }")), m_serialization, SerializationContext.empty);
        Assert.False(renamed.succeeded);
        Assert.Contains(renamed.diagnostics, diagnostic => diagnostic.code == "SHADER_GRAPH_PORT_MISSING" && diagnostic.portId == "input.value");
        Assert.Equal(edgeCount, graph.edges.Count);
        ShaderGraphLoweringResult restored = Catalog(new DoubleCompiler()).Lower(Request(Source("float Shade(float value) { return value; }")), m_serialization, SerializationContext.empty);
        Assert.True(restored.succeeded, Errors(restored));
    }

    private GraphDocument Graph(out GraphNodeRecord constant, out GraphNodeRecord extension, out GraphNodeRecord source, out GraphNodeRecord color)
    {
        var graph = new GraphDocument();
        constant = Add(graph, "constant", "inno.shader.constant");
        extension = Add(graph, "extension", "tests.double");
        source = Add(graph, "source", "inno.shader.source");
        color = Add(graph, "color", "inno.shader.construct");
        Connect(graph, constant, "value", extension, "input");
        Connect(graph, extension, "value", source, "input.value");
        for (int index = 0; index < 4; index++) Connect(graph, source, "return", color, "component." + index);
        return graph;
    }

    private static ShaderNodeCompilerCatalog Catalog(params IShaderNodeCompiler[] extra)
        => new(new IShaderNodeCompiler[] { new ShaderConstantNodeCompiler(), new ShaderBinaryNodeCompiler(), new ShaderConstructNodeCompiler(),
            new ShaderStageInputNodeCompiler(), new ShaderSelectNodeCompiler(), new ShaderSourceNodeCompiler() }.Concat(extra));

    private void Set<T>(GraphNodeRecord node, string property, T value)
        => node.SetValue(property, new(m_serialization.Encode(writer => writer.Write("value", value), SerializationContext.empty)));

    private static GraphNodeRecord Add(GraphDocument graph, string id, string definition)
    {
        var node = new GraphNodeRecord(new(id), definition);
        graph.AddNode(node);
        return node;
    }

    private static GraphEndpoint Endpoint(GraphNodeRecord node, string port) => new(node.id, new(port));
    private static void Connect(GraphDocument graph, GraphNodeRecord source, string output, GraphNodeRecord target, string input)
        => graph.AddEdge(new(new("edge." + graph.edges.Count), Endpoint(source, output), Endpoint(target, input)));

    private static ShaderSourceModuleAnalysis Source(string text, string path = "module.ishadersource")
        => new ShaderSourceFrontendCatalog([new BgfxShaderSourceFrontend()]).AnalyzeModule(
            [new("metal", "inno.shader-language.bgfx-sc", new(new(path, text), "Shade", new NoIncludes()))]);

    private static string Errors(ShaderGraphLoweringResult result) => string.Join("\n", result.diagnostics.Select(static diagnostic => diagnostic.message));

    public void Dispose()
    {
        m_serialization.Dispose();
        m_types.Dispose();
        m_modules.Dispose();
        if (Directory.Exists(m_root)) Directory.Delete(m_root, true);
    }

    private sealed class NoIncludes : IShaderSourceResolver
    {
        public ShaderSourceFile ReadInclude(string includingFile, string include) => throw new FileNotFoundException(include);
    }

    private sealed class DoubleCompiler : IShaderNodeCompiler
    {
        public string definitionId => "tests.double";
        public IReadOnlyList<ShaderNodePort> GetPorts(ShaderNodeDescriptionContext context)
            => [new("input", ShaderSourceType.Atomic("float"), GraphPortDirection.Input), new("value", ShaderSourceType.Atomic("float"), GraphPortDirection.Output)];
        public IReadOnlyDictionary<string, ShaderIrValue> Lower(ShaderNodeLoweringContext context)
            => new Dictionary<string, ShaderIrValue> { ["value"] = context.builder.Binary(ShaderIrOperation.Multiply, context.Input("input"), context.builder.Constant(2f)) };
    }
}
