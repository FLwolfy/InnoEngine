using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Inno.Core.Assemblies;

/// <summary>
/// Provides metadata helpers for assemblies participating in the Inno runtime.
/// </summary>
public static class AssemblyExtensions
{
    private const string C_ASSEMBLY_GROUP_KEY = "Inno.AssemblyGroup";

    private static readonly ConditionalWeakTable<Assembly, AssemblyGroupBox> S_CACHE = new();

    /// <summary>
    /// Resolves the <see cref="AssemblyGroup"/> declared by an assembly.
    /// </summary>
    /// <param name="assembly">The assembly whose metadata should be inspected.</param>
    /// <returns>The declared group, or <see cref="AssemblyGroup.None"/> when no valid value exists.</returns>
    public static AssemblyGroup GetInnoAssemblyGroup(this Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return S_CACHE.GetValue(
            assembly,
            static value => new AssemblyGroupBox { value = ResolveAssemblyGroup(value) }).value;
    }

    private static AssemblyGroup ResolveAssemblyGroup(Assembly assembly)
    {
        foreach (AssemblyMetadataAttribute metadata in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (!string.Equals(metadata.Key, C_ASSEMBLY_GROUP_KEY, StringComparison.Ordinal))
                continue;
            return Enum.TryParse(metadata.Value, ignoreCase: true, out AssemblyGroup group)
                ? group
                : AssemblyGroup.None;
        }

        return AssemblyGroup.None;
    }

    private sealed class AssemblyGroupBox
    {
        internal required AssemblyGroup value { get; init; }
    }
}
