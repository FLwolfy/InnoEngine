using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Inno.Core.Assemblies;

/// <summary>
/// Provides ownership and scope metadata helpers for assemblies participating in the Inno runtime.
/// </summary>
public static class AssemblyExtensions
{
    private const string C_ASSEMBLY_DOMAIN_KEY = "Inno.AssemblyDomain";
    private const string C_ASSEMBLY_SCOPE_KEY = "Inno.AssemblyScope";

    private static readonly ConditionalWeakTable<Assembly, AssemblyClassification> S_CACHE = new();

    /// <summary>
    /// Resolves the reload domain declared by an assembly.
    /// </summary>
    /// <param name="assembly">The assembly whose metadata should be inspected.</param>
    /// <returns>The declared assembly domain.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="assembly"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the metadata is absent or invalid.</exception>
    public static AssemblyDomain GetInnoAssemblyDomain(this Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        AssemblyClassification classification = GetClassification(assembly);
        return classification.domain ?? throw new InvalidOperationException(
            $"Assembly '{assembly.GetName().Name}' has no valid {C_ASSEMBLY_DOMAIN_KEY} metadata.");
    }

    /// <summary>
    /// Resolves the dependency scope declared by an assembly.
    /// </summary>
    /// <param name="assembly">The assembly whose metadata should be inspected.</param>
    /// <returns>The declared assembly scope.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="assembly"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the metadata is absent or invalid.</exception>
    public static AssemblyScope GetInnoAssemblyScope(this Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        AssemblyClassification classification = GetClassification(assembly);
        return classification.scope ?? throw new InvalidOperationException(
            $"Assembly '{assembly.GetName().Name}' has no valid {C_ASSEMBLY_SCOPE_KEY} metadata.");
    }

    internal static bool TryGetInnoAssemblyClassification(
        this Assembly assembly,
        out AssemblyDomain domain,
        out AssemblyScope scope)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        AssemblyClassification classification = GetClassification(assembly);
        domain = classification.domain.GetValueOrDefault();
        scope = classification.scope.GetValueOrDefault();
        return classification.domain.HasValue && classification.scope.HasValue;
    }

    internal static void RegisterInnoAssemblyClassification(
        this Assembly assembly,
        AssemblyDomain domain,
        AssemblyScope scope)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        S_CACHE.Remove(assembly);
        S_CACHE.Add(assembly, new AssemblyClassification(domain, scope));
    }

    private static AssemblyClassification GetClassification(Assembly assembly)
        => S_CACHE.GetValue(assembly, static value => ResolveClassification(value));

    private static AssemblyClassification ResolveClassification(Assembly assembly)
    {
        AssemblyDomain? domain = null;
        AssemblyScope? scope = null;
        foreach (AssemblyMetadataAttribute metadata in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (string.Equals(metadata.Key, C_ASSEMBLY_DOMAIN_KEY, StringComparison.Ordinal))
            {
                if (Enum.TryParse(metadata.Value, ignoreCase: false, out AssemblyDomain parsedDomain))
                    domain = parsedDomain;
                continue;
            }
            if (string.Equals(metadata.Key, C_ASSEMBLY_SCOPE_KEY, StringComparison.Ordinal) &&
                Enum.TryParse(metadata.Value, ignoreCase: false, out AssemblyScope parsedScope))
            {
                scope = parsedScope;
            }
        }
        return new AssemblyClassification(domain, scope);
    }

    private sealed record AssemblyClassification(AssemblyDomain? domain, AssemblyScope? scope);
}
