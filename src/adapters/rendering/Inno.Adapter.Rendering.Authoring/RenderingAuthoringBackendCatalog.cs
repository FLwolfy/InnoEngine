using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Inno.Rendering.Assets;

namespace Inno.Adapter.Rendering;

/// <summary>Pairs one immutable authoring provider set with a runtime rendering catalog.</summary>
public sealed class RenderingAuthoringBackendCatalog : IRenderingAuthoringBackendFactory
{
    private readonly Dictionary<RenderingBackendId, RenderingAuthoringBackendProvider> m_providers = [];

    /// <summary>Validates that every runtime backend has exactly one matching authoring provider.</summary>
    /// <param name="runtime">Runtime provider snapshot paired with these authoring tools.</param>
    /// <param name="providers">Complete authoring provider set owned by this composition generation.</param>
    /// <exception cref="ArgumentException">Provider IDs are missing, duplicated, or do not match the runtime set.</exception>
    public RenderingAuthoringBackendCatalog(
        IRenderingBackendFactory runtime,
        IEnumerable<RenderingAuthoringBackendProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(providers);
        foreach (RenderingAuthoringBackendProvider provider in providers)
            if (provider is null || !provider.id.isValid || !m_providers.TryAdd(provider.id, provider))
                throw new ArgumentException("Authoring providers require unique, assigned backend IDs.", nameof(providers));
        var runtimeIds = new HashSet<RenderingBackendId>(runtime.supportedBackends);
        if (!runtimeIds.SetEquals(m_providers.Keys))
            throw new ArgumentException("Runtime and authoring rendering providers must register the same backend IDs.", nameof(providers));
        supportedBackends = new ReadOnlyCollection<RenderingBackendId>(new List<RenderingBackendId>(m_providers.Keys));
    }

    /// <inheritdoc />
    public IReadOnlyList<RenderingBackendId> supportedBackends { get; }

    /// <inheritdoc />
    public IShaderCompilerToolchain CreateShaderCompilerToolchain(RenderingBackendId backend)
        => GetProvider(backend).CreateShaderCompilerToolchain()
           ?? throw new InvalidOperationException($"Rendering provider '{backend}' returned no shader compiler.");

    /// <inheritdoc />
    public ITextureTargetCompiler CreateTextureTargetCompiler(RenderingBackendId backend)
        => GetProvider(backend).CreateTextureTargetCompiler()
           ?? throw new InvalidOperationException($"Rendering provider '{backend}' returned no texture compiler.");

    private RenderingAuthoringBackendProvider GetProvider(RenderingBackendId backend)
        => m_providers.TryGetValue(backend, out RenderingAuthoringBackendProvider? provider)
            ? provider
            : throw new NotSupportedException($"Rendering authoring backend '{backend}' is not registered.");
}
