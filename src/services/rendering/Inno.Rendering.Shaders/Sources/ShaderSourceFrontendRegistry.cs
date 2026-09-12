using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Extensibility.Types;

namespace Inno.Rendering.Shaders;

/// <summary>
/// Discovers source language providers through the shared type-generation transaction and owns their retirement.
/// </summary>
public sealed class ShaderSourceFrontendRegistry : TypeRegistry<ShaderSourceFrontendCatalog>
{
    private readonly TypeCatalog m_types;

    /// <summary>Registers this authoring owner with the shared candidate and rollback coordinator.</summary>
    /// <param name="types">The owner type catalog; it must outlive this registry.</param>
    public ShaderSourceFrontendRegistry(TypeCatalog types) : base(types) => m_types = types;

    /// <summary>Gets immutable language identities available in the current generation.</summary>
    public IReadOnlyList<string> languageIds
    {
        get
        {
            using IDisposable operation = m_types.AcquireOperation("Query shader source languages");
            return current.languageIds;
        }
    }

    /// <summary>Analyzes source while preventing provider retirement for the complete synchronous operation.</summary>
    /// <param name="languageId">The exact registered language identity.</param>
    /// <param name="request">Candidate source inputs and include resolver.</param>
    /// <returns>A neutral interface/diagnostic result without a provider reference.</returns>
    /// <exception cref="NotSupportedException">The language is not available in the current generation.</exception>
    public ShaderSourceAnalysis Analyze(string languageId, ShaderSourceRequest request)
    {
        using IDisposable operation = m_types.AcquireOperation("Analyze shader source interface");
        return current.Analyze(languageId, request);
    }

    /// <summary>Freezes and validates a complete module without mixing frontend generations between implementations.</summary>
    /// <param name="implementations">Explicit implementation and variant candidates from the current asset transaction.</param>
    /// <returns>A neutral module result containing no frontend or source resolver references.</returns>
    /// <exception cref="ArgumentException">The candidate list is empty or has duplicate implementation keys.</exception>
    public ShaderSourceModuleAnalysis AnalyzeModule(IEnumerable<ShaderSourceImplementationRequest> implementations)
    {
        using IDisposable operation = m_types.AcquireOperation("Analyze shader source module implementations");
        return current.AnalyzeModule(implementations);
    }

    /// <inheritdoc />
    protected override ShaderSourceFrontendCatalog Build(TypeCacheSnapshot types)
    {
        var providers = new List<IShaderSourceFrontend>();
        foreach (Type type in types.GetTypesWithAttribute<ShaderSourceFrontendExtensionAttribute>()
                     .Select(reference => reference.Resolve(types))
                     .OrderBy(static type => type.FullName, StringComparer.Ordinal))
            providers.Add(CreateExtension<IShaderSourceFrontend>(type));
        return new ShaderSourceFrontendCatalog(providers);
    }

    /// <inheritdoc />
    protected override void DisposeSnapshot(ShaderSourceFrontendCatalog snapshot)
        => DisposeExtensions(snapshot.providers);
}
