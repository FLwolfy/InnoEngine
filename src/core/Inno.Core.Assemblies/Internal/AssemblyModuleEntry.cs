using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace Inno.Core.Assemblies.Internal;

internal sealed class AssemblyModuleEntry
{
    internal required AssemblyModuleHandle handle { get; init; }
    internal required string moduleName { get; init; }
    internal required int generation { get; init; }
    internal required bool externallyOwned { get; init; }
    internal required bool collectible { get; init; }
    internal required Assembly[] assemblies { get; init; }
    internal AssemblyLoadContext? loadContext { get; init; }
    internal string? shadowDirectory { get; init; }

    internal AssemblyModuleInfo CreateInfo()
        => new(
            handle,
            moduleName,
            generation,
            collectible,
            externallyOwned,
            AssemblyModuleStatus.Active,
            assemblies
                .Select(static assembly => assembly.GetName().Name ?? assembly.FullName ?? "Unknown")
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray());
}
