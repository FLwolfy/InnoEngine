using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Inno.Rendering;

namespace Inno.Adapter.Rendering;

/// <summary>Resolves runtime providers from one immutable composition-owned registration snapshot.</summary>
public sealed class RenderingBackendCatalog : IRenderingBackendFactory
{
    private readonly Dictionary<RenderingBackendId, RenderingBackendProvider> m_providers = [];

    /// <summary>Validates and captures a complete set of runtime providers.</summary>
    /// <param name="providers">Providers whose lifetime is owned by this composition generation.</param>
    /// <exception cref="ArgumentException">A provider is null, has no ID, or duplicates an ID.</exception>
    public RenderingBackendCatalog(IEnumerable<RenderingBackendProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        foreach (RenderingBackendProvider provider in providers)
        {
            if (provider is null || !provider.id.isValid || !m_providers.TryAdd(provider.id, provider))
                throw new ArgumentException("Rendering providers require unique, assigned backend IDs.", nameof(providers));
        }
        supportedBackends = new ReadOnlyCollection<RenderingBackendId>(new List<RenderingBackendId>(m_providers.Keys));
    }

    /// <inheritdoc />
    public IReadOnlyList<RenderingBackendId> supportedBackends { get; }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">No provider is registered for this exact backend ID.</exception>
    public IRenderDevice CreateDevice(RenderingBackendId backend, RenderingBackendOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!m_providers.TryGetValue(backend, out RenderingBackendProvider? provider))
            throw new NotSupportedException($"Rendering backend '{backend}' is not registered.");
        return provider.CreateDevice(options)
            ?? throw new InvalidOperationException($"Rendering provider '{backend}' returned no device.");
    }
}
