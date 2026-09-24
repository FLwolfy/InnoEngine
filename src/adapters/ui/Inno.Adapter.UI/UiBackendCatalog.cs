using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

using Inno.UI;

namespace Inno.Adapter.UI;

/// <summary>Resolves UI providers from one immutable composition-owned registration snapshot.</summary>
public sealed class UiBackendCatalog : IUiBackendFactory
{
    private readonly Dictionary<UiBackendId, UiBackendProvider> m_providers = [];

    /// <summary>Validates and captures a complete provider set.</summary>
    /// <param name="providers">Providers owned by this composition generation.</param>
    public UiBackendCatalog(IEnumerable<UiBackendProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        foreach (UiBackendProvider provider in providers)
            if (provider is null || !provider.id.isValid || !m_providers.TryAdd(provider.id, provider))
                throw new ArgumentException("UI providers require unique, assigned backend IDs.", nameof(providers));
        supportedBackends = new ReadOnlyCollection<UiBackendId>([.. m_providers.Keys]);
    }

    /// <inheritdoc />
    public IReadOnlyList<UiBackendId> supportedBackends { get; }

    /// <inheritdoc />
    public IUiBackend CreateBackend(UiBackendId backend)
    {
        if (!m_providers.TryGetValue(backend, out UiBackendProvider? provider))
            throw new NotSupportedException($"UI backend '{backend}' is not registered.");
        return provider.CreateBackend()
            ?? throw new InvalidOperationException($"UI provider '{backend}' returned no backend.");
    }
}
