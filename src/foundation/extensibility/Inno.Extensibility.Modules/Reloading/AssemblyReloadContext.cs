using System;
using System.Collections.Generic;

namespace Inno.Extensibility.Modules;

/// <summary>
/// Provides assembly catalogs and participant-specific state during a reload transaction.
/// </summary>
public sealed class AssemblyReloadContext
{
    private AssemblyCatalogSnapshot? m_previousCatalog;
    private AssemblyCatalogSnapshot? m_candidateCatalog;
    private IReadOnlyList<object>? m_participantContexts;

    internal AssemblyReloadContext(
        AssemblyCatalogSnapshot previousCatalog,
        AssemblyCatalogSnapshot candidateCatalog,
        IReadOnlyList<AssemblyModuleHandle> modules,
        IReadOnlyList<object> participantContexts)
    {
        m_previousCatalog = previousCatalog;
        m_candidateCatalog = candidateCatalog;
        m_participantContexts = participantContexts;
        this.modules = modules;
    }

    /// <summary>
    /// Gets the assembly catalog from before activation.
    /// </summary>
    public AssemblyCatalogSnapshot previousCatalog
        => m_previousCatalog ?? throw CreateCompletedException();

    /// <summary>
    /// Gets the validated candidate assembly catalog.
    /// </summary>
    public AssemblyCatalogSnapshot candidateCatalog
        => m_candidateCatalog ?? throw CreateCompletedException();

    /// <summary>
    /// Gets the first logical module in dependency staging order.
    /// </summary>
    public AssemblyModuleHandle module => modules[0];

    /// <summary>
    /// Gets every logical module staged by this atomic reload transaction.
    /// </summary>
    public IReadOnlyList<AssemblyModuleHandle> modules { get; }

    /// <summary>
    /// Gets a context contributed by a registered catalog participant.
    /// </summary>
    /// <typeparam name="TContext">
    /// The requested participant context type.
    /// </typeparam>
    /// <returns>
    /// The matching context.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no participant supplied the requested context or the reload has completed.
    /// </exception>
    public TContext GetContext<TContext>() where TContext : class
    {
        if (TryGetContext(out TContext? context))
            return context!;
        throw new InvalidOperationException(
            $"No assembly catalog participant supplied context '{typeof(TContext).FullName}'.");
    }

    /// <summary>
    /// Tries to get a context contributed by a registered catalog participant.
    /// </summary>
    /// <typeparam name="TContext">
    /// The requested participant context type.
    /// </typeparam>
    /// <param name="context">
    /// Receives the matching context when found.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when exactly one matching context exists.
    /// </returns>
    public bool TryGetContext<TContext>(out TContext? context) where TContext : class
    {
        IReadOnlyList<object> contexts = m_participantContexts ?? throw CreateCompletedException();
        context = null;
        foreach (object candidate in contexts)
        {
            if (candidate is not TContext match)
                continue;
            if (context is not null)
            {
                throw new InvalidOperationException(
                    $"Multiple assembly catalog participants supplied context '{typeof(TContext).FullName}'.");
            }
            context = match;
        }
        return context is not null;
    }

    internal void Release()
    {
        m_previousCatalog = null;
        m_candidateCatalog = null;
        m_participantContexts = null;
    }

    private static InvalidOperationException CreateCompletedException()
        => new("The assembly reload context is unavailable after the transaction has completed.");
}
