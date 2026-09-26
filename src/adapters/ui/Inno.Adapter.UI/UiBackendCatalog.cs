using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

using Inno.UI;

namespace Inno.Adapter.UI;

/// <summary>
/// Resolves UI providers from one immutable composition-owned registration snapshot.
/// </summary>
public sealed class UiBackendCatalog : IUiBackendFactory
{
    private readonly Dictionary<UiBackendId, UiBackendProvider> m_providers = [];

    /// <summary>
    /// Validates and captures a complete provider set.
    /// </summary>
    /// <param name="providers">
    /// Providers owned by this composition generation.
    /// </param>
    public UiBackendCatalog(IEnumerable<UiBackendProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        foreach (UiBackendProvider provider in providers)
            if (provider is null || !provider.id.isValid || !m_providers.TryAdd(provider.id, provider))
                throw new ArgumentException("UI providers require unique, assigned backend IDs.", nameof(providers));
        supportedBackends = new ReadOnlyCollection<UiBackendId>([.. m_providers.Keys]);
    }

    /// <summary>
    /// Gets backend registrations available in this type generation.
    /// </summary>
    public IReadOnlyList<UiBackendId> supportedBackends { get; }

    /// <summary>
    /// Creates a backend using this implementation's validated inputs.
    /// </summary>
    /// <param name="backend">
    /// The backend consumed by create backend; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The validated iui backend that represents the completed operation.
    /// </returns>
    public IUiBackend CreateBackend(UiBackendId backend)
    {
        if (!m_providers.TryGetValue(backend, out UiBackendProvider? provider))
            throw new NotSupportedException($"UI backend '{backend}' is not registered.");
        return provider.CreateBackend()
            ?? throw new InvalidOperationException($"UI provider '{backend}' returned no backend.");
    }
}
