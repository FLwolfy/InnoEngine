using Inno.Runtime.Contracts;
using System;

using Inno.Runtime;

namespace Inno.Storage.Runtime;

/// <summary>
/// Creates one frame-scoped storage service for a runtime session.
/// </summary>
public sealed class StorageRuntimeFactory : IRuntimeSubsystemFactory
{
    private readonly Func<RuntimeSubsystemContext, IApplicationStorage> m_storageFactory;

    /// <summary>
    /// Creates a reusable factory that obtains an exclusively owned storage sandbox for each session.
    /// </summary>
    /// <param name="storageFactory">
    /// The composition callback that creates a distinct storage service for the supplied session context.
    /// </param>
    public StorageRuntimeFactory(Func<RuntimeSubsystemContext, IApplicationStorage> storageFactory)
    {
        m_storageFactory = storageFactory ?? throw new ArgumentNullException(nameof(storageFactory));
    }

    /// <summary>
    /// Gets stable ordering metadata for the frame-scoped storage service.
    /// </summary>
    public RuntimeSubsystemDescriptor descriptor { get; } = new(
        new RuntimeSubsystemId("inno.runtime.storage"),
        order: -900);

    /// <summary>
    /// Creates a storage feature over a newly allocated application sandbox.
    /// </summary>
    /// <param name="context">
    /// The isolated session context passed to the storage factory.
    /// </param>
    /// <returns>
    /// A new storage feature that exclusively owns any disposable storage adapter.
    /// </returns>
    public IRuntimeSubsystem Create(RuntimeSubsystemContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        IApplicationStorage storage = m_storageFactory(context)
            ?? throw new InvalidOperationException("The application storage factory returned null.");
        return new StorageRuntime(storage);
    }
}
