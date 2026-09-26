using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Inno.Rendering.Assets;

namespace Inno.Adapter.Rendering;

/// <summary>
/// Pairs one immutable authoring provider set with a runtime rendering catalog.
/// </summary>
public sealed class RenderingAuthoringBackendCatalog : IRenderingAuthoringBackendFactory
{
    private readonly Dictionary<RenderingBackendId, RenderingAuthoringBackendProvider> m_providers = [];

    /// <summary>
    /// Validates that every runtime backend has exactly one matching authoring provider.
    /// </summary>
    /// <param name="runtime">
    /// Runtime provider snapshot paired with these authoring tools.
    /// </param>
    /// <param name="providers">
    /// Complete authoring provider set owned by this composition generation.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Provider IDs are missing, duplicated, or do not match the runtime set.
    /// </exception>
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

    /// <summary>
    /// Gets backend registrations available in this type generation.
    /// </summary>
    public IReadOnlyList<RenderingBackendId> supportedBackends { get; }

    /// <summary>
    /// Creates a shader compiler toolchain using this implementation's validated inputs.
    /// </summary>
    /// <param name="backend">
    /// The backend consumed by create shader compiler toolchain; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The validated ishader compiler toolchain that represents the completed operation.
    /// </returns>
    public IShaderCompilerToolchain CreateShaderCompilerToolchain(RenderingBackendId backend)
        => GetProvider(backend).CreateShaderCompilerToolchain()
           ?? throw new InvalidOperationException($"Rendering provider '{backend}' returned no shader compiler.");

    /// <summary>
    /// Creates a texture target compiler using this implementation's validated inputs.
    /// </summary>
    /// <param name="backend">
    /// The backend consumed by create texture target compiler; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The validated itexture target compiler that represents the completed operation.
    /// </returns>
    public ITextureTargetCompiler CreateTextureTargetCompiler(RenderingBackendId backend)
        => GetProvider(backend).CreateTextureTargetCompiler()
           ?? throw new InvalidOperationException($"Rendering provider '{backend}' returned no texture compiler.");

    private RenderingAuthoringBackendProvider GetProvider(RenderingBackendId backend)
        => m_providers.TryGetValue(backend, out RenderingAuthoringBackendProvider? provider)
            ? provider
            : throw new NotSupportedException($"Rendering authoring backend '{backend}' is not registered.");
}
