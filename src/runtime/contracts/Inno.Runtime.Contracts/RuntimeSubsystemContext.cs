using System;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.Linq;
using Inno.Core.Diagnostics;
using Inno.Core.Events;
using Inno.Core.Execution;
using Inno.Core.Identity;
using Inno.Extensibility.Types;

namespace Inno.Runtime.Contracts;

/// <summary>
/// Exposes only foundation services and owner metadata, never a complete RuntimeSession or domain service locator.
/// </summary>
public sealed class RuntimeSubsystemContext
{
    /// <summary>
    /// Creates the explicit construction boundary for a host or session pipeline.
    /// </summary>
    /// <param name="events">
    /// The owner event dispatcher.
    /// </param>
    /// <param name="diagnostics">
    /// The shared diagnostic state owner.
    /// </param>
    /// <param name="identities">
    /// The isolated identity domain.
    /// </param>
    /// <param name="types">
    /// The active extension type catalog.
    /// </param>
    /// <param name="resources">
    /// The pipeline-owned construction lifetime.
    /// </param>
    /// <param name="lifetime">
    /// The scope in which created subsystems will live.
    /// </param>
    /// <param name="persistentDataDirectory">
    /// The owner-selected persistent root.
    /// </param>
    /// <param name="isEditMode">
    /// Whether simulation belongs to an authoring session.
    /// </param>
    /// <param name="capabilities">
    /// Verified backend-neutral capabilities supplied by composition; copied into an immutable set.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// A required foundation service is null.
    /// </exception>
    public RuntimeSubsystemContext(EventDispatcher events, DiagnosticHub diagnostics,
        IdentityAllocator identities, TypeCatalog types, LifetimeScope resources,
        RuntimeSubsystemLifetime lifetime, string persistentDataDirectory = "", bool isEditMode = false,
        IEnumerable<RuntimeCapabilityId>? capabilities = null)
    {
        this.events = events ?? throw new ArgumentNullException(nameof(events));
        this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        this.identities = identities ?? throw new ArgumentNullException(nameof(identities));
        this.types = types ?? throw new ArgumentNullException(nameof(types));
        this.resources = resources ?? throw new ArgumentNullException(nameof(resources));
        this.lifetime = lifetime;
        this.persistentDataDirectory = persistentDataDirectory ?? throw new ArgumentNullException(nameof(persistentDataDirectory));
        this.isEditMode = isEditMode;
        this.capabilities = (capabilities ?? []).ToFrozenSet();
        if (this.capabilities.Any(static capability => !capability.isValid))
            throw new ArgumentException("Available capabilities must be valid.", nameof(capabilities));
    }

    /// <summary>
    /// Gets the owner-thread event dispatcher.
    /// </summary>
    public EventDispatcher events { get; }
    /// <summary>
    /// Gets the shared diagnostic state owner.
    /// </summary>
    public DiagnosticHub diagnostics { get; }
    /// <summary>
    /// Gets the isolated live-object identity domain.
    /// </summary>
    public IdentityAllocator identities { get; }
    /// <summary>
    /// Gets the current foundation type catalog.
    /// </summary>
    public TypeCatalog types { get; }
    /// <summary>
    /// Gets resources owned by this pipeline, not by a global container.
    /// </summary>
    public LifetimeScope resources { get; }
    /// <summary>
    /// Gets the owner scope used to validate factory lifetimes.
    /// </summary>
    public RuntimeSubsystemLifetime lifetime { get; }
    /// <summary>
    /// Gets the explicit owner-selected persistent directory.
    /// </summary>
    public string persistentDataDirectory { get; }
    /// <summary>
    /// Gets whether scaled simulation should be disabled for authoring.
    /// </summary>
    public bool isEditMode { get; }

    /// <summary>
    /// Gets the immutable capabilities verified by the owner, never a mutable service container.
    /// </summary>
    public IReadOnlySet<RuntimeCapabilityId> capabilities { get; }
}
