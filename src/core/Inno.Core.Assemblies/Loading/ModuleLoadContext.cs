using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace Inno.Core.Assemblies.Loading;

internal sealed class ModuleLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver m_resolver;
    private readonly IReadOnlyDictionary<string, Assembly> m_sharedAssemblies;
    private readonly IReadOnlyDictionary<string, string> m_moduleAssemblyPaths;

    internal ModuleLoadContext(
        string name,
        string mainAssemblyPath,
        bool collectible,
        IReadOnlyDictionary<string, Assembly> sharedAssemblies,
        IEnumerable<string> moduleAssemblyPaths)
        : base(name, collectible)
    {
        m_resolver = new AssemblyDependencyResolver(mainAssemblyPath);
        m_sharedAssemblies = sharedAssemblies;
        m_moduleAssemblyPaths = moduleAssemblyPaths.ToDictionary(
            static path => AssemblyName.GetAssemblyName(path).Name
                ?? throw new InvalidOperationException($"Assembly '{path}' has no simple name."),
            static path => path,
            StringComparer.OrdinalIgnoreCase);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        string simpleName = assemblyName.Name ?? string.Empty;
        if (m_sharedAssemblies.TryGetValue(simpleName, out Assembly? sharedAssembly))
        {
            ValidateSharedIdentity(assemblyName, sharedAssembly.GetName());
            return sharedAssembly;
        }
        if (m_moduleAssemblyPaths.TryGetValue(simpleName, out string? modulePath))
            return LoadFromAssemblyPath(modulePath);

        return null;
    }

    private static void ValidateSharedIdentity(AssemblyName requested, AssemblyName shared)
    {
        bool versionMatches = requested.Version is null || requested.Version == shared.Version;
        string requestedCulture = requested.CultureName ?? string.Empty;
        string sharedCulture = shared.CultureName ?? string.Empty;
        byte[] requestedToken = requested.GetPublicKeyToken() ?? [];
        byte[] sharedToken = shared.GetPublicKeyToken() ?? [];
        if (versionMatches &&
            string.Equals(requestedCulture, sharedCulture, StringComparison.OrdinalIgnoreCase) &&
            requestedToken.SequenceEqual(sharedToken))
        {
            return;
        }

        throw new FileLoadException(
            $"Module dependency '{requested.FullName}' is incompatible with shared engine assembly '{shared.FullName}'.");
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string? dependencyPath = m_resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return dependencyPath is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(dependencyPath);
    }
}
