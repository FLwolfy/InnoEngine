using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Inno.Build.Toolchains.Bgfx.Tools;
using Xunit;

namespace Inno.Rendering.Shaders.Tests;

public sealed class ShaderIrBuilderTests
{
    [Fact]
    public void NumericGraphLowersToTypedOrderedValuesInsteadOfSourceFragments()
    {
        var builder = new ShaderIrBuilder();
        ShaderIrValue input = builder.Input("brightness", ShaderSourceType.Atomic("float"));
        ShaderIrValue scaled = builder.Binary(ShaderIrOperation.Multiply, input, builder.Constant(2f));
        ShaderIrValue clamped = builder.Binary(ShaderIrOperation.Minimum, scaled, builder.Constant(1f));
        ShaderIrBlock block = builder.Build(new Dictionary<string, ShaderIrValue> { ["color"] = clamped });
        Assert.Equal([ShaderIrOperation.Input, ShaderIrOperation.Constant, ShaderIrOperation.Multiply,
            ShaderIrOperation.Constant, ShaderIrOperation.Minimum], block.instructions.Select(static value => value.operation));
        Assert.Equal("float", block.outputs["color"].type.id);
        Assert.Equal(0x40000000UL, block.instructions[1].constantBits);
        Assert.Same(input, builder.Input("brightness", ShaderSourceType.Atomic("float")));
        Assert.Throws<ArgumentException>(() => builder.Input("brightness", ShaderSourceType.Atomic("int")));
    }

    [Fact]
    public void ScalarConstantsKeepSignedZeroAndFullIntegerPrecision()
    {
        var builder = new ShaderIrBuilder();
        _ = builder.Constant(-0f);
        _ = builder.Constant(uint.MaxValue);
        _ = builder.Constant(int.MinValue);
        _ = builder.Constant(true);
        ShaderIrBlock block = builder.Build(new Dictionary<string, ShaderIrValue>());
        Assert.Equal([0x80000000UL, 0xffffffffUL, 0x80000000UL, 1UL], block.instructions.Select(static value => value.constantBits));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Constant(float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Constant(float.PositiveInfinity));
    }

    [Fact]
    public void CrossRegionValuesAndImplicitNumericCoercionsAreRejected()
    {
        var first = new ShaderIrBuilder();
        var second = new ShaderIrBuilder();
        ShaderIrValue a = first.Constant(1f);
        ShaderIrValue b = second.Constant(1f);
        Assert.Throws<ArgumentException>(() => first.Binary(ShaderIrOperation.Add, a, b));
        Assert.Throws<ArgumentException>(() => first.Binary(ShaderIrOperation.Add, a, first.Constant(1)));
        Assert.Throws<ArgumentException>(() => first.Binary(ShaderIrOperation.SourceCall, a, a));
        Assert.Throws<ArgumentException>(() => first.Build(new Dictionary<string, ShaderIrValue> { ["wrong"] = b }));
        Assert.Throws<ArgumentException>(() => first.Input("empty", ShaderSourceType.Atomic("void")));
    }

    [Fact]
    public void AggregateConstructionAndExtractionKeepFullMemberTypes()
    {
        var builder = new ShaderIrBuilder();
        ShaderIrValue x = builder.Constant(1f);
        ShaderIrValue y = builder.Constant(2f);
        ShaderIrValue vector = builder.Construct(ShaderSourceType.Atomic("float2"), x, y);
        ShaderSourceType structure = ShaderSourceType.Structure("Surface", [new("uv", vector.type), new("intensity", x.type)]);
        ShaderIrValue surface = builder.Construct(structure, vector, x);
        ShaderIrValue array = builder.Construct(ShaderSourceType.ArrayOf(structure, 2), surface, surface);
        Assert.True(structure.IsEquivalentTo(builder.Extract(array, 1).type));
        Assert.Equal("float2", builder.Extract(surface, 0).type.id);
        Assert.Equal("float", builder.Extract(vector, 1).type.id);
        Assert.Throws<ArgumentException>(() => builder.Construct(structure, x, vector));
        Assert.Throws<ArgumentException>(() => builder.Construct(vector.type, x));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Extract(vector, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Extract(array, -1));
    }

    [Fact]
    public void MatrixConstructionSpecifiesColumnMajorOrderAndExtractionReturnsAColumn()
    {
        var builder = new ShaderIrBuilder();
        ShaderIrValue matrix = builder.Construct(ShaderSourceType.Atomic("float2x3"),
            Enumerable.Range(1, 6).Select(value => builder.Constant((float)value)).ToArray());
        Assert.Equal("float3", builder.Extract(matrix, 1).type.id);
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Extract(matrix, 2));
    }

    [Fact]
    public void ComparisonAndSelectionCannotHideTypeOrControlFlowChanges()
    {
        var builder = new ShaderIrBuilder();
        ShaderIrValue a = builder.Constant(1f);
        ShaderIrValue b = builder.Constant(2f);
        ShaderIrValue comparison = builder.Binary(ShaderIrOperation.LessThan, a, b);
        Assert.Equal("bool", comparison.type.id);
        Assert.Equal("float", builder.Select(comparison, a, b).type.id);
        Assert.Throws<ArgumentException>(() => builder.Select(a, a, b));
        Assert.Throws<ArgumentException>(() => builder.Select(comparison, a, builder.Constant(1)));
        ShaderIrValue vector = builder.Construct(ShaderSourceType.Atomic("float2"), a, b);
        Assert.Throws<ArgumentException>(() => builder.Binary(ShaderIrOperation.Equal, vector, vector));
        Assert.Throws<ArgumentException>(() => builder.Binary(ShaderIrOperation.Add, comparison, comparison));
    }

    [Fact]
    public void MixedArithmeticAndSourceCallDeriveOutAndInOutByName()
    {
        ShaderSourceModuleAnalysis module = Module("float Shade(float gain, out float alpha, inout vec2 uv) { alpha = gain; uv *= gain; return alpha; }");
        Assert.True(module.succeeded);
        var builder = new ShaderIrBuilder();
        ShaderIrValue gain = builder.Binary(ShaderIrOperation.Multiply, builder.Constant(0.5f), builder.Constant(2f));
        ShaderIrValue uv = builder.Construct(ShaderSourceType.Atomic("float2"), builder.Constant(0f), builder.Constant(1f));
        // Dictionary insertion order must not become native function argument order.
        IReadOnlyDictionary<string, ShaderIrValue> outputs = builder.Call(module, "test", new Dictionary<string, ShaderIrValue>
        { ["uv"] = uv, ["gain"] = gain });
        Assert.Equal(["return", "output.alpha", "output.uv"], outputs.Keys);
        ShaderIrBlock block = builder.Build(outputs);
        ShaderIrInstruction call = block.instructions[^1];
        Assert.Same(gain, call.inputs[0]);
        Assert.Same(uv, call.inputs[1]);
        Assert.Equal(["float", "float", "float2"], call.outputs.Select(static value => value.type.id));
        Assert.Equal("Shade", call.source!.entryPoint);
        Assert.True(call.hasSideEffects);
    }

    [Fact]
    public void CallsWithUnusedOutputsAndVoidCallsRemainInEvaluationOrder()
    {
        ShaderSourceModuleAnalysis module = Module("void Shade(inout float value) { value *= value; }");
        var builder = new ShaderIrBuilder();
        ShaderIrValue input = builder.Constant(2f);
        IReadOnlyDictionary<string, ShaderIrValue> first = builder.Call(module, "test", new Dictionary<string, ShaderIrValue> { ["value"] = input });
        _ = builder.Call(module, "test", new Dictionary<string, ShaderIrValue> { ["value"] = first["output.value"] });
        ShaderSourceModuleAnalysis voidModule = Module("void Shade() { }");
        Assert.Empty(builder.Call(voidModule, "test", new Dictionary<string, ShaderIrValue>()));
        ShaderIrBlock block = builder.Build(new Dictionary<string, ShaderIrValue>());
        Assert.Equal(3, block.instructions.Count(static value => value.hasSideEffects));
        Assert.Same(first["output.value"], block.instructions[2].inputs[0]);
    }

    [Fact]
    public void InvalidOrMismatchedSourceModulesNeverEnterAnIntermediateBlock()
    {
        var builder = new ShaderIrBuilder();
        ShaderSourceModuleAnalysis module = Module("float Shade(float gain) { return gain; }");
        ShaderSourceModuleAnalysis invalid = Module("float Other(float gain) { return gain; }");
        Assert.Throws<ArgumentException>(() => builder.Call(invalid, "test", new Dictionary<string, ShaderIrValue>()));
        Assert.Throws<ArgumentException>(() => builder.Call(module, "not-registered", new Dictionary<string, ShaderIrValue>()));
        Assert.Throws<ArgumentException>(() => builder.Call(module, "test", new Dictionary<string, ShaderIrValue> { ["renamed"] = builder.Constant(1f) }));
        Assert.Throws<ArgumentException>(() => builder.Call(module, "test", new Dictionary<string, ShaderIrValue> { ["gain"] = builder.Constant(1) }));
        Assert.DoesNotContain(builder.Build(new Dictionary<string, ShaderIrValue>()).instructions, value => value.operation == ShaderIrOperation.SourceCall);
    }

    [Fact]
    public void CompletedBlocksAreDetachedFromLaterBuilderAndOutputDictionaryEdits()
    {
        var builder = new ShaderIrBuilder();
        ShaderIrValue value = builder.Constant(1f);
        var outputs = new Dictionary<string, ShaderIrValue> { ["value"] = value };
        ShaderIrBlock block = builder.Build(outputs);
        _ = builder.Constant(2f);
        outputs.Clear();
        Assert.Single(block.instructions);
        Assert.Same(value, block.outputs["value"]);
    }

    private static ShaderSourceModuleAnalysis Module(string text)
        => new ShaderSourceFrontendCatalog([new BgfxShaderSourceFrontend()]).AnalyzeModule([
            new("test", "inno.shader-language.bgfx-sc", new(new("Module.ishadersource", text), "Shade", new NoIncludes()))]);

    private sealed class NoIncludes : IShaderSourceResolver
    {
        public ShaderSourceFile ReadInclude(string includingFile, string include) => throw new FileNotFoundException(include);
    }
}
