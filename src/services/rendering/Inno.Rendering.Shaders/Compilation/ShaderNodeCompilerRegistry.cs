using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Inno.Core.Serialization;
using Inno.Core.Graphs;
using Inno.Extensibility.Types;

namespace Inno.Rendering.Shaders;

/// <summary>Owns reloadable shader node compilers through the shared candidate, rollback and retirement protocol.</summary>
public sealed class ShaderNodeCompilerRegistry : TypeRegistry<ShaderNodeCompilerCatalog>
{
    private readonly TypeCatalog m_types;

    /// <summary>Registers the compiler generation owner with the shared type catalog.</summary>
    /// <param name="types">Owner catalog, which must outlive this registry.</param>
    public ShaderNodeCompilerRegistry(TypeCatalog types) : base(types) => m_types = types;

    /// <summary>Gets stable compiler identities without exposing provider instances.</summary>
    public IReadOnlyList<string> definitionIds
    {
        get
        {
            using IDisposable operation = m_types.AcquireOperation("Query shader node compilers");
            return current.definitionIds;
        }
    }

    /// <summary>Lowers a whole region under one shared generation operation.</summary>
    /// <param name="request">Frozen graph region and resolved immutable inputs.</param>
    /// <param name="serialization">The current owner converter registry.</param>
    /// <param name="context">The complete owner reference/asset serialization context.</param>
    /// <param name="cancellationToken">Cancellation between node invocations.</param>
    /// <returns>A detached region or neutral diagnostics, never compiler instances.</returns>
    public ShaderGraphLoweringResult Lower(ShaderGraphLoweringRequest request, SerializationRegistry serialization,
        SerializationContext context, CancellationToken cancellationToken = default)
    {
        using IDisposable operation = m_types.AcquireOperation("Lower shader graph region");
        return current.Lower(request, serialization, context, cancellationToken);
    }

    /// <summary>Captures a node's current typed ports under the shared generation lease.</summary>
    /// <param name="node">Neutral node properties.</param>
    /// <param name="serialization">Owner converter registry.</param>
    /// <param name="context">Complete owner reference context.</param>
    /// <param name="source">Resolved frozen source function, or null.</param>
    /// <param name="implementationId">Selected source implementation identity.</param>
    /// <param name="input">Resolved stage input interface, or null.</param>
    /// <returns>An immutable provider-free port snapshot.</returns>
    public IReadOnlyList<ShaderNodePort> DescribePorts(GraphNodeRecord node, SerializationRegistry serialization,
        SerializationContext context, ShaderSourceModuleAnalysis? source = null, string implementationId = "",
        ShaderIrStageInput? input = null)
    {
        using IDisposable operation = m_types.AcquireOperation("Describe shader node ports");
        return current.DescribePorts(node, serialization, context, source, implementationId, input);
    }

    /// <inheritdoc />
    protected override ShaderNodeCompilerCatalog Build(TypeCacheSnapshot types)
    {
        var providers = new List<IShaderNodeCompiler>();
        foreach (Type type in types.GetTypesWithAttribute<ShaderNodeCompilerExtensionAttribute>()
                     .Select(reference => reference.Resolve(types)).OrderBy(static type => type.FullName, StringComparer.Ordinal))
            providers.Add(CreateExtension<IShaderNodeCompiler>(type));
        return new(providers);
    }

    /// <inheritdoc />
    protected override void DisposeSnapshot(ShaderNodeCompilerCatalog snapshot) => DisposeExtensions(snapshot.providers);
}
