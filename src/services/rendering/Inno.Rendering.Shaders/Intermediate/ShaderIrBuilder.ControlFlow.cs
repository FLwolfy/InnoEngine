using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Inno.Rendering.Shaders;

public sealed partial class ShaderIrBuilder
{
    /// <summary>Builds a real conditional: only the selected region executes, including its memory and source-call effects.</summary>
    /// <param name="condition">Scalar Boolean visible in this region.</param>
    /// <param name="whenTrue">Transient callback building the true region and its named outputs.</param>
    /// <param name="whenFalse">Transient callback building the false region with exactly matching output names/types.</param>
    /// <returns>Values merged into the enclosing scope; empty output maps represent effect-only branches.</returns>
    /// <exception cref="ArgumentException">The condition or branch output contracts are incompatible.</exception>
    /// <exception cref="InvalidOperationException">A callback mutates the enclosing scope or declares a nested stage input.</exception>
    public IReadOnlyDictionary<string, ShaderIrValue> Branch(ShaderIrValue condition,
        Func<ShaderIrBuilder, IReadOnlyDictionary<string, ShaderIrValue>> whenTrue,
        Func<ShaderIrBuilder, IReadOnlyDictionary<string, ShaderIrValue>> whenFalse)
    {
        RequireOwned(condition);
        ArgumentNullException.ThrowIfNull(whenTrue);
        ArgumentNullException.ThrowIfNull(whenFalse);
        if (!condition.type.IsEquivalentTo(ShaderSourceType.Atomic("bool"))) throw new ArgumentException("A branch condition must be bool.", nameof(condition));
        int nextValue = m_root.m_nextValue;
        try
        {
            ShaderIrBlock first = BuildRegion(whenTrue);
            ShaderIrBlock second = BuildRegion(whenFalse);
            ValidateOutputs(first.outputs, second.outputs);
            IReadOnlyDictionary<string, ShaderIrValue> outputs = MergeValues(first.outputs);
            m_instructions.Add(new(ShaderIrOperation.Branch, [condition], outputs.Values, regions: [first, second]));
            return outputs;
        }
        catch { m_root.m_nextValue = nextValue; throw; }
    }

    /// <summary>Builds a counted loop; each iteration receives the preceding iteration's complete carried state.</summary>
    /// <param name="iterations">Unsigned iteration count, evaluated once before entering the loop.</param>
    /// <param name="initialState">Named values used before the first iteration and returned unchanged for zero iterations.</param>
    /// <param name="body">Transient callback receiving its builder, uint iteration index and typed carried values.</param>
    /// <returns>Final carried values in the enclosing scope; an empty state map permits effect-only loops.</returns>
    /// <exception cref="ArgumentException">The count is not uint or the body's output state changes names or types.</exception>
    /// <exception cref="InvalidOperationException">The body mutates the enclosing builder.</exception>
    public IReadOnlyDictionary<string, ShaderIrValue> Loop(ShaderIrValue iterations,
        IReadOnlyDictionary<string, ShaderIrValue> initialState,
        Func<ShaderIrBuilder, ShaderIrValue, IReadOnlyDictionary<string, ShaderIrValue>, IReadOnlyDictionary<string, ShaderIrValue>> body)
    {
        RequireOwned(iterations);
        ArgumentNullException.ThrowIfNull(initialState);
        ArgumentNullException.ThrowIfNull(body);
        if (!iterations.type.IsEquivalentTo(ShaderSourceType.Atomic("uint"))) throw new ArgumentException("A loop count must be uint.", nameof(iterations));
        foreach ((string name, ShaderIrValue value) in initialState)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            RequireOwned(value);
        }
        var initial = new SortedDictionary<string, ShaderIrValue>(initialState.ToDictionary(static pair => pair.Key, static pair => pair.Value), StringComparer.Ordinal);
        int nextValue = m_root.m_nextValue;
        try
        {
            ShaderIrBlock region = BuildRegion(builder =>
            {
                ShaderIrValue index = builder.RegionInput("iteration", ShaderSourceType.Atomic("uint"));
                var carried = new Dictionary<string, ShaderIrValue>(StringComparer.Ordinal);
                foreach ((string name, ShaderIrValue value) in initial) carried.Add(name, builder.RegionInput("state." + name, value.type));
                return body(builder, index, new ReadOnlyDictionary<string, ShaderIrValue>(carried));
            });
            ValidateOutputs(initial, region.outputs);
            IReadOnlyDictionary<string, ShaderIrValue> outputs = MergeValues(initial);
            m_instructions.Add(new(ShaderIrOperation.Loop, new[] { iterations }.Concat(initial.Values), outputs.Values, regions: [region]));
            return outputs;
        }
        catch { m_root.m_nextValue = nextValue; throw; }
    }

    private ShaderIrBlock BuildRegion(Func<ShaderIrBuilder, IReadOnlyDictionary<string, ShaderIrValue>> body)
    {
        var child = new ShaderIrBuilder(this);
        m_buildingChild = true;
        try { return child.Build(body(child)); }
        finally { m_buildingChild = false; child.m_closed = true; }
    }

    private ShaderIrValue RegionInput(string name, ShaderSourceType type)
    {
        ShaderIrValue output = NewValue(type);
        m_instructions.Add(new(ShaderIrOperation.RegionInput, [], [output], inputName: name));
        return output;
    }

    private IReadOnlyDictionary<string, ShaderIrValue> MergeValues(IReadOnlyDictionary<string, ShaderIrValue> contract)
    {
        var values = new Dictionary<string, ShaderIrValue>(StringComparer.Ordinal);
        foreach ((string name, ShaderIrValue value) in contract.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (value.type.storage is not null || value.type.id.StartsWith("sampled-texture", StringComparison.Ordinal))
                throw new ArgumentException("Resource bindings cannot be merged or loop-carried as ordinary values.");
            values.Add(name, NewValue(value.type));
        }
        return new ReadOnlyDictionary<string, ShaderIrValue>(values);
    }

    private static void ValidateOutputs(IReadOnlyDictionary<string, ShaderIrValue> expected, IReadOnlyDictionary<string, ShaderIrValue> actual)
    {
        if (expected.Count != actual.Count || expected.Any(pair => !actual.TryGetValue(pair.Key, out ShaderIrValue? value) || !pair.Value.type.IsEquivalentTo(value.type)))
            throw new ArgumentException("Control-flow regions require identical named output types.");
    }
}
