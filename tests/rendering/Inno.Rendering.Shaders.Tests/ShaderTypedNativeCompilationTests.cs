using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inno.Build.Toolchains.Bgfx.Tools;
using Inno.Core.Diagnostics;
using Inno.Rendering;
using Inno.Rendering.Assets;
using Xunit;

namespace Inno.Rendering.Shaders.Tests;

public sealed class MetalShaderFactAttribute : FactAttribute
{
    public MetalShaderFactAttribute()
    {
        if (!OperatingSystem.IsMacOS()) Skip = "Requires the macOS BGFX Metal compiler; Windows validation is explicitly deferred.";
    }
}

public sealed class ShaderTypedNativeCompilationTests
{
    [MetalShaderFact]
    public async Task EveryRectangularMatrixShapeSupportsColumnConstructionAndExtraction()
    {
        for (int columns = 2; columns <= 4; columns++)
        for (int rows = 2; rows <= 4; rows++)
        {
            var builder = new ShaderIrBuilder();
            ShaderIrValue matrix = builder.Construct(ShaderSourceType.Atomic($"float{columns}x{rows}"),
                Enumerable.Range(1, columns * rows).Select(value => builder.Constant((float)value)).ToArray());
            ShaderIrValue product = builder.Binary(ShaderIrOperation.Multiply, matrix, matrix);
            ShaderIrValue column = builder.Extract(product, columns - 1);
            ShaderIrValue first = builder.Extract(column, 0);
            ShaderIrValue last = builder.Extract(column, rows - 1);
            AssertSuccess(await Compile(Fragment(builder, builder.Construct(ShaderSourceType.Atomic("float4"), first, last, first, builder.Constant(1f)))));
        }
    }

    [MetalShaderFact]
    public async Task OrdinaryArithmeticAndSelectionCompileWithoutSourceModules()
    {
        var builder = new ShaderIrBuilder();
        ShaderIrValue a = Color(builder, 0.25f);
        ShaderIrValue value = builder.Binary(ShaderIrOperation.Multiply, a, a);
        ShaderIrValue selected = builder.Select(builder.Constant(true), value, a);
        AssertSuccess(await Compile(Fragment(builder, selected)));
    }

    [MetalShaderFact]
    public async Task SameNamedPrivateHelpersAndPublicFunctionsAreIsolatedPerModule()
    {
        ShaderSourceModuleAnalysis first = Module("First.ishadersource", "float Adjust(float value) { return value * 1e-3; } float Shade(float value) { return Adjust(value) + .25; }");
        ShaderSourceModuleAnalysis second = Module("Second.ishadersource", "float Adjust(float value) { return value * 2.0; } float Shade(float value) { uint bits = 0xFE+1u; bits <<= 1u; return Adjust(value) + float(bits) * 0.0; }");
        var builder = new ShaderIrBuilder();
        ShaderIrValue a = builder.Call(first, "metal", new Dictionary<string, ShaderIrValue> { ["value"] = builder.Constant(0.5f) })["return"];
        ShaderIrValue b = builder.Call(second, "metal", new Dictionary<string, ShaderIrValue> { ["value"] = a })["return"];
        AssertSuccess(await Compile(Fragment(builder, builder.Construct(ShaderSourceType.Atomic("float4"), a, b, a, builder.Constant(1f)))));
    }

    [MetalShaderFact]
    public async Task StructuresArraysAndInOutUseTheSameTypedCallChain()
    {
        ShaderSourceModuleAnalysis module = Module("Surface.ishadersource", """
            struct Surface { vec2 uv; float gain; };
            float Private(float value) { return value * 2.0; }
            Surface Shade(Surface value, float weights[2], out float alpha, inout vec2 uv) {
                alpha = Private(weights[0]); uv *= weights[1]; value.uv = uv; value.gain += alpha; return value;
            }
            """);
        var builder = new ShaderIrBuilder();
        ShaderSourceType surfaceType = module.function!.returnType;
        ShaderIrValue uv = builder.Construct(ShaderSourceType.Atomic("float2"), builder.Constant(0.2f), builder.Constant(0.4f));
        ShaderIrValue surface = builder.Construct(surfaceType, uv, builder.Constant(0.5f));
        ShaderIrValue weights = builder.Construct(ShaderSourceType.ArrayOf(ShaderSourceType.Atomic("float"), 2), builder.Constant(0.5f), builder.Constant(1f));
        IReadOnlyDictionary<string, ShaderIrValue> values = builder.Call(module, "metal", new Dictionary<string, ShaderIrValue>
        { ["value"] = surface, ["weights"] = weights, ["uv"] = uv });
        ShaderIrValue result = builder.Extract(values["return"], 1);
        ShaderIrValue color = builder.Construct(ShaderSourceType.Atomic("float4"), result, builder.Extract(values["output.uv"], 0), result, values["output.alpha"]);
        AssertSuccess(await Compile(Fragment(builder, color)));
    }

    [MetalShaderFact]
    public async Task SampledTextureAndUniformBindingsAreReturnedWithTheBinary()
    {
        ShaderSourceModuleAnalysis module = Module("Sample.ishadersource", "vec4 Shade(sampler2D image, vec2 uv, vec4 tint) { return texture2D(image, uv) * tint; }");
        var builder = new ShaderIrBuilder();
        ShaderIrValue image = builder.Input("texture-id", ShaderSourceType.Atomic("sampled-texture2d"));
        ShaderIrValue tint = builder.Input("tint-id", ShaderSourceType.Atomic("float4"));
        ShaderIrValue uv = builder.Input("uv-id", ShaderSourceType.Atomic("float2"));
        ShaderIrValue color = builder.Call(module, "metal", new Dictionary<string, ShaderIrValue> { ["image"] = image, ["uv"] = uv, ["tint"] = tint })["return"];
        ShaderIrStage stage = Fragment(builder, color,
            [new("texture-id", image.type, ShaderIrInputKind.SampledTexture, location: 3),
                new("tint-id", tint.type, ShaderIrInputKind.Uniform), new("uv-id", uv.type, ShaderIrInputKind.Varying, "texcoord")]);
        ShaderStageToolResult result = await Compile(stage);
        AssertSuccess(result);
        Assert.Equal(2, result.bindings.Count);
        Assert.Equal(3, result.bindings.Single(binding => binding.id == "texture-id").location);
        Assert.StartsWith("s_inno_", result.bindings.Single(binding => binding.id == "texture-id").nativeName);
        Assert.StartsWith("u_inno_", result.bindings.Single(binding => binding.id == "tint-id").nativeName);
        Assert.All(result.bindings, binding => Assert.InRange(binding.nativeName.Length, 1, 63));
        Assert.Equal(result.bindings.Count, result.bindings.Select(binding => binding.nativeName).Distinct(StringComparer.Ordinal).Count());
    }

    [MetalShaderFact]
    public async Task VertexInstanceInputAndMrtFragmentCompileAsTypedStages()
    {
        var vertex = new ShaderIrBuilder();
        ShaderIrValue position = vertex.Input("position", ShaderSourceType.Atomic("float4"));
        ShaderIrValue instance = vertex.Input("instance", ShaderSourceType.Atomic("float4"));
        ShaderIrValue clip = vertex.Binary(ShaderIrOperation.Add, position, instance);
        ShaderIrBlock vertexBody = vertex.Build(new Dictionary<string, ShaderIrValue> { ["clip"] = clip, ["color"] = instance });
        AssertSuccess(await Compile(new(ShaderStage.Vertex, vertexBody,
            [new("position", position.type, ShaderIrInputKind.VertexAttribute, "position"),
                new("instance", instance.type, ShaderIrInputKind.VertexAttribute, "instance-data")],
            [new("clip", ShaderIrOutputKind.ClipPosition), new("color", ShaderIrOutputKind.Varying, "color")])));
        var fragment = new ShaderIrBuilder();
        ShaderIrValue color = fragment.Input("color", ShaderSourceType.Atomic("float4"));
        ShaderIrBlock fragmentBody = fragment.Build(new Dictionary<string, ShaderIrValue> { ["base"] = color, ["emission"] = color });
        AssertSuccess(await Compile(new(ShaderStage.Fragment, fragmentBody,
            [new("color", color.type, ShaderIrInputKind.Varying, "color")],
            [new("base", ShaderIrOutputKind.Color), new("emission", ShaderIrOutputKind.Color, location: 1)])));
    }

    [MetalShaderFact]
    public async Task ComputeEntryAndWorkgroupAreGeneratedNotSuppliedByTheSourceModule()
    {
        var builder = new ShaderIrBuilder();
        ShaderIrValue id = builder.Input("invocation", ShaderSourceType.Atomic("uint3"));
        ShaderSourceModuleAnalysis module = Module("Compute.ishadersource", "void Shade(uvec3 gid) { if (gid.x > 0u) return; }");
        _ = builder.Call(module, "metal", new Dictionary<string, ShaderIrValue> { ["gid"] = id });
        ShaderIrStage stage = new(ShaderStage.Compute, builder.Build(new Dictionary<string, ShaderIrValue>()),
            [new("invocation", id.type, ShaderIrInputKind.Builtin, "global-invocation-id")], [], 8, 4, 1);
        AssertSuccess(await Compile(stage));
        ShaderStageToolResult unsupported = await Compile(stage, GraphicsCapability.None);
        Assert.False(unsupported.succeeded);
        Assert.Contains(unsupported.diagnostics, diagnostic => diagnostic.code == "BGFX_IR_GENERATION" && diagnostic.message.Contains("Compute", StringComparison.Ordinal));
    }

    [MetalShaderFact]
    public async Task InvalidNativeFunctionBodyCannotPublishAStageBinary()
    {
        ShaderSourceModuleAnalysis module = Module("Broken.ishadersource", "float Shade(float value) { return ThisFunctionDoesNotExist(value); }");
        var builder = new ShaderIrBuilder();
        ShaderIrValue value = builder.Call(module, "metal", new Dictionary<string, ShaderIrValue> { ["value"] = builder.Constant(1f) })["return"];
        ShaderStageToolResult result = await Compile(Fragment(builder, builder.Construct(ShaderSourceType.Atomic("float4"), value, value, value, value)));
        Assert.False(result.succeeded);
        Assert.Empty(result.bytes.ToArray());
        Assert.Contains(result.diagnostics, diagnostic => diagnostic.severity == DiagnosticSeverity.Error);
        Assert.True(result.diagnostics.Any(diagnostic => diagnostic.location.assetPath.Contains("Broken.ishadersource", StringComparison.Ordinal)),
            string.Join("\n", result.diagnostics.Select(static diagnostic => diagnostic.message)));
    }

    [MetalShaderFact]
    public async Task IncludeAliasesCompileFromFrozenSnapshotsAfterTheOwnerResolverIsGone()
    {
        var resolver = new AliasedInclude();
        ShaderSourceModuleAnalysis module = Module("Include.ishadersource", "#include \"@library/math\"\nfloat Shade(float value) { return Scale(value); }", resolver);
        resolver.available = false;
        var builder = new ShaderIrBuilder();
        ShaderIrValue value = builder.Call(module, "metal", new Dictionary<string, ShaderIrValue> { ["value"] = builder.Constant(1f) })["return"];
        AssertSuccess(await Compile(Fragment(builder, builder.Construct(ShaderSourceType.Atomic("float4"), value, value, value, value))));
        Assert.Single(module.implementations[0].includes);
        Assert.Equal("plugin:library/scale.ishadersource", module.implementations[0].includes[0].resolvedPath);
    }

    [Fact]
    public async Task CancellationIsPreservedBeforeStartingNativeCompilation()
    {
        var builder = new ShaderIrBuilder();
        ShaderIrStage stage = Fragment(builder, Color(builder, 1f));
        var toolchain = new BgfxShadercToolchain(BgfxShaderTargetPlatform.MacOSArm64);
        var target = toolchain.CreateTarget(Capabilities(GraphicsCapability.None));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await toolchain.CompileAsync(new ShaderStageToolRequest(stage, target), new CancellationToken(true)));
    }

    private static ShaderIrValue Color(ShaderIrBuilder builder, float value)
    {
        ShaderIrValue scalar = builder.Constant(value);
        return builder.Construct(ShaderSourceType.Atomic("float4"), scalar, scalar, scalar, scalar);
    }

    private static ShaderIrStage Fragment(ShaderIrBuilder builder, ShaderIrValue color, ShaderIrStageInput[]? inputs = null)
        => new(ShaderStage.Fragment, builder.Build(new Dictionary<string, ShaderIrValue> { ["color"] = color }), inputs ?? [], [new("color", ShaderIrOutputKind.Color)]);

    private static ShaderSourceModuleAnalysis Module(string path, string source, IShaderSourceResolver? resolver = null)
    {
        ShaderSourceModuleAnalysis result = new ShaderSourceFrontendCatalog([new BgfxShaderSourceFrontend()]).AnalyzeModule(
            [new("metal", "inno.shader-language.bgfx-sc", new(new(path, source), "Shade", resolver ?? new AliasedInclude()))]);
        Assert.True(result.succeeded, string.Join("\n", result.diagnostics.Select(static diagnostic => diagnostic.message)));
        return result;
    }

    private static async Task<ShaderStageToolResult> Compile(ShaderIrStage stage,
        GraphicsCapability capabilities = GraphicsCapability.Compute | GraphicsCapability.Instancing | GraphicsCapability.Texture2DArray | GraphicsCapability.Texture3D)
    {
        var compiler = new ShaderCompiler(new BgfxShadercToolchain(BgfxShaderTargetPlatform.MacOSArm64));
        return await compiler.CompileAsync(stage, compiler.CreateTarget(Capabilities(capabilities)));
    }

    private static GraphicsCapabilities Capabilities(GraphicsCapability features)
        => new(GraphicsApi.Metal, features, new GraphicsLimits(256, 8, 8192, 16),
            Enum.GetValues<RenderTextureFormat>(), Enum.GetValues<RenderTextureFormat>(), Enum.GetValues<RenderTextureFormat>(),
            Enum.GetValues<RenderTextureFormat>(), originBottomLeft: false, homogeneousDepth: false);

    private static void AssertSuccess(ShaderStageToolResult result)
        => Assert.True(result.succeeded, string.Join("\n", result.diagnostics.Select(static diagnostic => diagnostic.message)));

    private sealed class AliasedInclude : IShaderSourceResolver
    {
        internal bool available = true;
        public ShaderSourceFile ReadInclude(string includingFile, string include)
            => available && include == "@library/math" ? new("plugin:library/scale.ishadersource", "float Scale(float value) { return value * 2.0; }")
                : throw new FileNotFoundException(include);
    }
}
