using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Inno.Core.Reflection;

/// <summary>
/// Extension helpers for assembly metadata used by Inno runtime systems.
/// </summary>
public static class AssemblyExtensions
{
    private const string C_ASSEMBLY_GROUP_KEY = "Inno.AssemblyGroup";

    private sealed class AssemblyGroupBox
    {
        public required AssemblyGroup value { get; init; }
    }

    private static readonly ConditionalWeakTable<Assembly, AssemblyGroupBox> CACHE = new();

    /// <summary>
    /// Resolves the <see cref="AssemblyGroup"/> from assembly metadata.
    /// </summary>
    /// <param name="assembly">The source assembly.</param>
    /// <returns>The resolved assembly group, or <see cref="AssemblyGroup.None"/> if not present.</returns>
    public static AssemblyGroup GetInnoAssemblyGroup(this Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return CACHE.GetValue(assembly, static asm => new AssemblyGroupBox { value = ResolveAssemblyGroup(asm) }).value;
    }

    private static AssemblyGroup ResolveAssemblyGroup(Assembly asm)
    {
        foreach (var meta in asm.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (!string.Equals(meta.Key, C_ASSEMBLY_GROUP_KEY, StringComparison.Ordinal))
                continue;

            return Enum.TryParse(meta.Value, ignoreCase: true, out AssemblyGroup group)
                ? group
                : AssemblyGroup.None;
        }

        return AssemblyGroup.None;
    }
}
