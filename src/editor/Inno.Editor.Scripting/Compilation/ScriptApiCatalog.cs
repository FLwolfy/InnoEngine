using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

using Inno.Core.Scripting;

namespace Inno.Editor.Scripting;

internal sealed record ScriptApiAssembly(
    Assembly assembly,
    IReadOnlyList<Type> exportedTypes);

internal sealed record ScriptApiNamespaceMapping(
    string apiNamespace,
    string implementationNamespace);

internal sealed record ScriptApiProfile(
    string name,
    IReadOnlyList<ScriptApiAssembly> exports,
    IReadOnlyList<Assembly> implementationAssemblies,
    IReadOnlyList<string> apiNamespaces,
    IReadOnlyList<ScriptApiNamespaceMapping> namespaceMappings);

internal static class ScriptApiCatalog
{
    internal static ScriptApiProfile Build(bool includeEditor)
    {
        LoadReferencedInnoAssemblies();
        Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly =>
                !assembly.IsDynamic &&
                !string.IsNullOrWhiteSpace(assembly.Location) &&
                AssemblyLoadContext.GetLoadContext(assembly) == AssemblyLoadContext.Default)
            .ToArray();
        var byName = loaded
            .Where(static assembly => assembly.GetName().Name is not null)
            .GroupBy(static assembly => assembly.GetName().Name!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

        var namespaceMappings = loaded
            .SelectMany(assembly => assembly
                .GetCustomAttributes<ScriptingApiNamespaceAttribute>()
                .Where(attribute => Includes(attribute.scope, includeEditor))
                .Select(attribute => new NamespaceMapping(
                    attribute.name,
                    attribute.implementationNamespace,
                    assembly)))
            .OrderBy(static mapping => mapping.apiNamespace, StringComparer.Ordinal)
            .ThenBy(static mapping => mapping.implementationNamespace, StringComparer.Ordinal)
            .ToArray();
        ValidateNamespaceMappings(namespaceMappings);

        var exports = new List<ScriptApiAssembly>();
        var implementationAssemblies = new HashSet<Assembly>();
        foreach (Assembly assembly in loaded.OrderBy(static value => value.GetName().Name, StringComparer.Ordinal))
        {
            Type[] exportedTypes = assembly
                .GetCustomAttributes<ScriptingApiExportAttribute>()
                .Where(attribute => Includes(attribute.scope, includeEditor))
                .Select(static attribute => attribute.type)
                .Distinct()
                .OrderBy(static type => type.FullName, StringComparer.Ordinal)
                .ToArray();
            if (exportedTypes.Length == 0)
                continue;
            ValidateExports(assembly, exportedTypes, namespaceMappings);
            exports.Add(new ScriptApiAssembly(assembly, exportedTypes));
            AddWithDependencies(assembly, byName, implementationAssemblies);
        }

        string[] apiNamespaces = namespaceMappings
            .Select(static mapping => mapping.apiNamespace)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        ScriptApiNamespaceMapping[] publicMappings = namespaceMappings
            .Select(static mapping => new ScriptApiNamespaceMapping(
                mapping.apiNamespace,
                mapping.implementationNamespace))
            .Distinct()
            .ToArray();

        return new ScriptApiProfile(
            includeEditor ? "Editor" : "Runtime",
            exports,
            implementationAssemblies
                .OrderBy(static assembly => assembly.GetName().Name, StringComparer.Ordinal)
                .ToArray(),
            apiNamespaces,
            publicMappings);
    }

    private static bool Includes(ScriptingApiScope scope, bool includeEditor)
        => scope == ScriptingApiScope.Runtime || includeEditor && scope == ScriptingApiScope.Editor;

    private static void ValidateExports(
        Assembly assembly,
        IReadOnlyList<Type> exportedTypes,
        IReadOnlyList<NamespaceMapping> mappings)
    {
        foreach (Type type in exportedTypes)
        {
            if (type.Assembly != assembly)
            {
                throw new InvalidOperationException(
                    $"Assembly '{assembly.GetName().Name}' cannot export type '{type.FullName}' owned by another assembly.");
            }
            if (!type.IsPublic && !type.IsNestedPublic)
                throw new InvalidOperationException($"Script API type '{type.FullName}' must be public.");
            string implementationNamespace = type.Namespace ?? string.Empty;
            if (!mappings.Any(mapping =>
                    mapping.assembly == assembly &&
                    string.Equals(mapping.implementationNamespace, implementationNamespace, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"Script API type '{type.FullName}' is not assigned to a declared script API namespace.");
            }
        }
    }

    private static void ValidateNamespaceMappings(IReadOnlyList<NamespaceMapping> mappings)
    {
        foreach (IGrouping<(Assembly assembly, string implementationNamespace), NamespaceMapping> group in mappings
                     .GroupBy(static mapping => (mapping.assembly, mapping.implementationNamespace)))
        {
            string[] apiNamespaces = group
                .Select(static mapping => mapping.apiNamespace)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (apiNamespaces.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Implementation namespace '{group.Key.implementationNamespace}' in assembly " +
                    $"'{group.Key.assembly.GetName().Name}' maps to multiple script API namespaces.");
            }
        }
    }

    private static void AddWithDependencies(
        Assembly assembly,
        IReadOnlyDictionary<string, Assembly> byName,
        ISet<Assembly> selected)
    {
        if (!selected.Add(assembly))
            return;
        foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
        {
            if (reference.Name is not null && byName.TryGetValue(reference.Name, out Assembly? dependency))
                AddWithDependencies(dependency, byName, selected);
        }
    }

    private static void LoadReferencedInnoAssemblies()
    {
        Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly =>
                !assembly.IsDynamic &&
                AssemblyLoadContext.GetLoadContext(assembly) == AssemblyLoadContext.Default)
            .ToArray();
        var byName = loaded
            .Where(static assembly => assembly.GetName().Name is not null)
            .GroupBy(static assembly => assembly.GetName().Name!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<Assembly>(loaded);
        while (pending.Count > 0)
        {
            Assembly assembly = pending.Dequeue();
            foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
            {
                string name = reference.Name ?? string.Empty;
                if (!name.StartsWith("Inno.", StringComparison.Ordinal) || byName.ContainsKey(name))
                    continue;
                try
                {
                    Assembly dependency = Assembly.Load(reference);
                    byName.Add(name, dependency);
                    pending.Enqueue(dependency);
                }
                catch (FileNotFoundException)
                {
                    // Optional API modules can be absent from a custom editor deployment.
                }
            }
        }
    }

    private sealed record NamespaceMapping(
        string apiNamespace,
        string implementationNamespace,
        Assembly assembly);
}
