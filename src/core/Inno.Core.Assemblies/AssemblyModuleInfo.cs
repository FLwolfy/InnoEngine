using System;
using System.Collections.Generic;

namespace Inno.Core.Assemblies;

/// <summary>
/// Describes the catalog state of a published assembly module generation.
/// </summary>
public enum AssemblyModuleStatus
{
    /// <summary>
    /// The module generation is visible to TypeCache and registries.
    /// </summary>
    Active
}

/// <summary>
/// Provides non-owning diagnostic information about an active assembly module.
/// </summary>
public sealed record AssemblyModuleInfo(
    AssemblyModuleHandle handle,
    string moduleName,
    int generation,
    bool collectible,
    bool externallyOwned,
    AssemblyModuleStatus status,
    IReadOnlyList<string> assemblyNames);
