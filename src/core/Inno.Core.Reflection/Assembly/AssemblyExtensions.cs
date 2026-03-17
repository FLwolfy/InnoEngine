using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace Inno.Core.Reflection;

public static class AssemblyExtensions
{
    private const string C_ASSEMBLY_GROUP_KEY = "Inno.AssemblyGroup";

    private static readonly ConcurrentDictionary<Assembly, AssemblyGroup> CACHE = new();

    public static AssemblyGroup GetInnoAssemblyGroup(this Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return CACHE.GetOrAdd(assembly, static asm =>
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
        });
    }
}