using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Inno.Build.Toolchains.Bgfx.Tools;
using Inno.Rendering;
using Inno.Rendering.Assets;
using Xunit;

namespace Inno.Rendering.Shaders.Tests;

public sealed class ShaderControlFlowIrTests
{
    [Fact]
    public void BranchCapturesOuterValuesButOnlyExposesMergedValues()
    {
        var builder = new ShaderIrBuilder();
        ShaderIrValue input = builder.Constant(2f);
        ShaderIrValue? leaked = null;
        ShaderIrBuilder? retained = null;
        IReadOnlyDictionary<string, ShaderIrValue> outputs = builder.Branch(builder.Constant(true),
            region => { retained = region; leaked = region.Binary(ShaderIrOperation.Add, input, input); return Values(("result", leaked)); },
            region => Values(("result", region.Constant(3f))));
        ShaderIrBlock block = builder.Build(outputs);
        ShaderIrInstruction branch = block.instructions[^1];
        Assert.Equal(ShaderIrOperation.Branch, branch.operation);
        Assert.Equal(2, branch.regions.Count);
        Assert.Single(branch.regions[0].instructions);
        Assert.True(branch.hasSideEffects);
        Assert.Throws<ArgumentException>(() => builder.Binary(ShaderIrOperation.Add, leaked!, input));
        Assert.Throws<InvalidOperationException>(() => retained!.Constant(1f));
        Assert.NotSame(leaked, outputs["result"]);
    }

    [Fact]
    public void FailedRegionConstructionRollsBackWithoutLeavingInstructionsOrIdentities()
    {
        var builder = new ShaderIrBuilder();
        ShaderIrValue condition = builder.Constant(true);
        ShaderIrValue input = builder.Constant(2f);
        Assert.Throws<ArgumentException>(() => builder.Branch(condition,
            region => Values(("first", region.Constant(1f))), region => Values(("renamed", region.Constant(1f)))));
        Assert.Equal(2, builder.Build(Values()).instructions.Count);
        Assert.Throws<InvalidOperationException>(() => builder.Branch(condition,
            region => Values(("result", builder.Constant(1f))), region => Values(("result", input))));
        Assert.Throws<InvalidOperationException>(() => builder.Branch(condition,
            region => Values(("result", region.Input("hidden-stage-input", input.type))), region => Values(("result", input))));
        Assert.Equal(2, builder.Constant(4f).index);
    }

    [Fact]
    public void SiblingScopesCannotShareUnmergedValues()
    {
        var builder = new ShaderIrBuilder();
        ShaderIrValue? first = null;
        Assert.Throws<ArgumentException>(() => builder.Branch(builder.Constant(true),
            region => { first = region.Constant(1f); return Values(("value", first)); },
            region => Values(("value", first!))));
    }

    [Fact]
    public void LoopsHaveExplicitIndicesStateAndSimultaneousNamedOutputs()
    {
        var builder = new ShaderIrBuilder();
        ShaderIrValue first = builder.Constant(1f);
        ShaderIrValue second = builder.Constant(2f);
        IReadOnlyDictionary<string, ShaderIrValue> outputs = builder.Loop(builder.Constant(3u), Values(("b", second), ("a", first)),
            (region, index, state) =>
            {
                Assert.Equal("uint", index.type.id);
                Assert.Equal(["a", "b"], state.Keys);
                return Values(("a", state["b"]), ("b", state["a"]));
            });
        ShaderIrInstruction loop = builder.Build(outputs).instructions[^1];
        Assert.Equal(["iteration", "state.a", "state.b"], loop.regions[0].instructions.Select(static instruction => instruction.inputName));
        Assert.Same(first, loop.inputs[1]);
        Assert.Same(second, loop.inputs[2]);
        Assert.Equal(["a", "b"], outputs.Keys);
        Assert.Throws<ArgumentException>(() => builder.Loop(builder.Constant(1u), Values(("a", first)), (region, index, state) => Values(("a", index))));
        Assert.Throws<ArgumentException>(() => builder.Loop(builder.Constant(1), Values(), (region, index, state) => Values()));
    }

    [Fact]
    public void NestedFragmentOnlyEffectsAreStillRejectedByComputeStageValidation()
    {
        var builder = new ShaderIrBuilder();
        ShaderIrValue condition = builder.Constant(true);
        _ = builder.Loop(builder.Constant(1u), Values(), (region, index, state) => region.Branch(condition,
            yes => { yes.Discard(condition); return Values(); }, no => Values()));
        Assert.Throws<ArgumentException>(() => new ShaderIrStage(ShaderStage.Compute, builder.Build(Values()), [], []));
    }

    [MetalShaderFact]
    public async Task NestedBranchLoopAndStorageEffectsCompileThroughTheSameMetalChain()
    {
        var builder = new ShaderIrBuilder();
        ShaderSourceType bufferType = ShaderSourceType.Storage(ShaderStorageType.Buffer(ShaderSourceType.Atomic("uint"), RenderStorageAccess.ReadWrite));
        ShaderIrValue buffer = builder.Input("output", bufferType);
        ShaderIrValue gid = builder.Input("gid", ShaderSourceType.Atomic("uint3"));
        ShaderIrValue count = builder.Binary(ShaderIrOperation.Add, builder.Extract(gid, 0), builder.Constant(3u));
        IReadOnlyDictionary<string, ShaderIrValue> result = builder.Loop(count, Values(("a", builder.Constant(1u)), ("b", builder.Constant(2u))),
            (region, index, state) =>
            {
                ShaderIrValue condition = region.Binary(ShaderIrOperation.LessThan, index, region.Constant(2u));
                return region.Branch(condition,
                    yes =>
                    {
                        _ = yes.AtomicAddStorage(buffer, index, state["a"]);
                        return Values(("a", state["b"]), ("b", state["a"]));
                    },
                    no =>
                    {
                        ShaderIrValue loaded = no.LoadStorage(buffer, index);
                        return Values(("a", loaded), ("b", no.Binary(ShaderIrOperation.Add, state["b"], loaded)));
                    });
            });
        builder.StoreStorage(buffer, builder.Constant(0u), result["a"]);
        builder.StoreStorage(buffer, builder.Constant(1u), result["b"]);
        await Compile(new(ShaderStage.Compute, builder.Build(Values()),
            [new("output", bufferType, ShaderIrInputKind.Storage), new("gid", gid.type, ShaderIrInputKind.Builtin, "global-invocation-id")], [], 8));
    }

    [MetalShaderFact]
    public async Task AggregateCarriedStateAndZeroIterationLoopsCompileWithoutFlatteningTheirTypes()
    {
        var builder = new ShaderIrBuilder();
        ShaderSourceType pair = ShaderSourceType.Structure("Pair", [new("a", ShaderSourceType.Atomic("float")), new("b", ShaderSourceType.Atomic("float"))]);
        ShaderIrValue initial = builder.Construct(pair, builder.Constant(1f), builder.Constant(2f));
        IReadOnlyDictionary<string, ShaderIrValue> result = builder.Loop(builder.Constant(0u), Values(("pair", initial)),
            (region, index, state) => Values(("pair", region.Construct(pair, region.Extract(state["pair"], 1), region.Extract(state["pair"], 0)))));
        ShaderIrValue a = builder.Extract(result["pair"], 0);
        ShaderIrValue b = builder.Extract(result["pair"], 1);
        ShaderIrValue color = builder.Construct(ShaderSourceType.Atomic("float4"), a, b, a, builder.Constant(1f));
        await Compile(new(ShaderStage.Fragment, builder.Build(Values(("color", color))), [], [new("color", ShaderIrOutputKind.Color)]));
    }

    private static Dictionary<string, ShaderIrValue> Values(params (string name, ShaderIrValue value)[] entries)
        => entries.ToDictionary(static pair => pair.name, static pair => pair.value, StringComparer.Ordinal);

    private static async Task Compile(ShaderIrStage stage)
    {
        var toolchain = new BgfxShadercToolchain(BgfxShaderTargetPlatform.MacOSArm64);
        var capabilities = new GraphicsCapabilities(GraphicsApi.Metal, GraphicsCapability.Compute | GraphicsCapability.StorageBuffer,
            new GraphicsLimits(256, 8, 8192, 16), Enum.GetValues<RenderTextureFormat>(), Enum.GetValues<RenderTextureFormat>(),
            Enum.GetValues<RenderTextureFormat>(), Enum.GetValues<RenderTextureFormat>(), false, false);
        ShaderStageToolResult result = await new ShaderCompiler(toolchain).CompileAsync(stage, toolchain.CreateTarget(capabilities));
        Assert.True(result.succeeded, string.Join("\n", result.diagnostics.Select(static value => value.message)));
    }
}
