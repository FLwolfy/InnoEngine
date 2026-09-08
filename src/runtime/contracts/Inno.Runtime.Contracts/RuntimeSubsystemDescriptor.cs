using System;
using System.Collections.Generic;
using System.Linq;

namespace Inno.Runtime.Contracts;

/// <summary>
/// Describes stable ordering and dependency requirements for one runtime subsystem.
/// </summary>
public sealed class RuntimeSubsystemDescriptor
{
    /// <summary>
    /// Creates an immutable runtime subsystem descriptor.
    /// </summary>
    /// <param name="id">
    /// The unique subsystem protocol identifier.
    /// </param>
    /// <param name="order">
    /// The deterministic tie-break order among otherwise independent subsystems.
    /// </param>
    /// <param name="dependencies">
    /// Features that must attach and execute before this subsystem.
    /// </param>
    /// <param name="lifetime">
    /// The exclusive Host or Session lifetime required by this subsystem.
    /// </param>
    /// <param name="requirement">
    /// Whether startup must succeed or may remain explicitly unavailable after complete compensation.
    /// </param>
    /// <param name="requiredCapabilities">
    /// Backend-neutral capabilities that composition must supply before factory creation.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="id"/> is invalid or a dependency is invalid, duplicated, or self-referential.
    /// </exception>
    public RuntimeSubsystemDescriptor(
        RuntimeSubsystemId id,
        int order = 0,
        IReadOnlyList<RuntimeSubsystemId>? dependencies = null,
        RuntimeSubsystemLifetime lifetime = RuntimeSubsystemLifetime.Session,
        RuntimeSubsystemRequirement requirement = RuntimeSubsystemRequirement.Required,
        IReadOnlyList<RuntimeCapabilityId>? requiredCapabilities = null)
    {
        if (!id.isValid)
            throw new ArgumentException("A runtime subsystem requires a valid ID.", nameof(id));
        if (!Enum.IsDefined(lifetime))
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        if (!Enum.IsDefined(requirement))
            throw new ArgumentOutOfRangeException(nameof(requirement));
        RuntimeCapabilityId[] capabilities = requiredCapabilities?.ToArray() ?? [];
        if (capabilities.Any(static capability => !capability.isValid) || capabilities.Distinct().Count() != capabilities.Length)
            throw new ArgumentException("Required capabilities must be valid and unique.", nameof(requiredCapabilities));
        RuntimeSubsystemId[] dependencySnapshot = dependencies?.ToArray() ?? [];
        if (dependencySnapshot.Any(static dependency => !dependency.isValid))
            throw new ArgumentException("Runtime subsystem dependencies must be valid.", nameof(dependencies));
        if (dependencySnapshot.Contains(id))
            throw new ArgumentException("A runtime subsystem cannot depend on itself.", nameof(dependencies));
        if (dependencySnapshot.Distinct().Count() != dependencySnapshot.Length)
            throw new ArgumentException("Runtime subsystem dependencies must be unique.", nameof(dependencies));
        this.id = id;
        this.order = order;
        this.dependencies = Array.AsReadOnly(dependencySnapshot);
        this.lifetime = lifetime;
        this.requirement = requirement;
        this.requiredCapabilities = Array.AsReadOnly(capabilities);
    }

    /// <summary>
    /// Gets the unique subsystem protocol identifier.
    /// </summary>
    public RuntimeSubsystemId id { get; }

    /// <summary>
    /// Gets the exclusive owner scope required by this subsystem.
    /// </summary>
    public RuntimeSubsystemLifetime lifetime { get; }

    /// <summary>
    /// Gets the deterministic order used after dependency constraints.
    /// </summary>
    public int order { get; }

    /// <summary>
    /// Gets the immutable required-subsystem identifiers.
    /// </summary>
    public IReadOnlyList<RuntimeSubsystemId> dependencies { get; }

    /// <summary>
    /// Gets whether this subsystem must start for its owner to become available.
    /// </summary>
    public RuntimeSubsystemRequirement requirement { get; }

    /// <summary>
    /// Gets the immutable capability prerequisites checked before allocating subsystem resources.
    /// </summary>
    public IReadOnlyList<RuntimeCapabilityId> requiredCapabilities { get; }
}
