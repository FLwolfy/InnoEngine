using System;
using System.Collections.Generic;

namespace Inno.Extensibility.Modules;

/// <summary>
/// Describes the catalog state of a published assembly module generation.
/// </summary>
public enum AssemblyModuleStatus
{
    /// <summary>
    /// The module generation is visible to assembly catalog participants.
    /// </summary>
    Active
}

/// <summary>
/// Provides non-owning diagnostic information about an active assembly module.
/// </summary>
/// <param name="handle">
/// The opaque handle validated by this operation.
/// </param>
/// <param name="moduleName">
/// The string value used to initialize this instance.
/// </param>
/// <param name="generation">
/// The owner generation used to reject stale handles or snapshots.
/// </param>
/// <param name="collectible">
/// The bool value used to initialize this instance.
/// </param>
/// <param name="externallyOwned">
/// The bool value used to initialize this instance.
/// </param>
/// <param name="domain">
/// The assembly domain value used to initialize this instance.
/// </param>
/// <param name="scope">
/// The assembly scope value used to initialize this instance.
/// </param>
/// <param name="status">
/// The assembly module status value used to initialize this instance.
/// </param>
/// <param name="assemblyNames">
/// The immutable assembly names exposed for this active module generation.
/// </param>
public sealed record AssemblyModuleInfo(
    AssemblyModuleHandle handle,
    string moduleName,
    int generation,
    bool collectible,
    bool externallyOwned,
    AssemblyDomain domain,
    AssemblyScope scope,
    AssemblyModuleStatus status,
    IReadOnlyList<string> assemblyNames)
{
    /// <summary>
    /// Gets stable module dependencies used by this active generation.
    /// </summary>
    public IReadOnlyList<string> upstreamModuleNames { get; init; } = Array.Empty<string>();
}
