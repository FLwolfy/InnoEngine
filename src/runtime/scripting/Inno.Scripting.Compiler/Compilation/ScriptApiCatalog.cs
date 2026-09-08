using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

using Inno.Scripting.Api;

namespace Inno.Scripting.Compiler;

internal sealed record ScriptApiAssembly(
    Assembly assembly,
    IReadOnlyList<ScriptApiTypeExport> exports);

internal sealed record ScriptApiTypeExport(
    Type type,
    string name);

internal sealed record ScriptApiNamespaceMapping(
    string apiNamespace,
    string implementationNamespace);

internal sealed record ScriptApiTypeMapping(
    string apiNamespace,
    string apiName,
    string implementationNamespace,
    string implementationName,
    int arity);

internal sealed record ScriptApiAttachableType(
    string implementationName,
    string kind);

internal sealed record ScriptApiProfile(
    string name,
    IReadOnlyList<ScriptApiAssembly> exports,
    IReadOnlyList<Assembly> implementationAssemblies,
    IReadOnlyList<string> apiNamespaces,
    IReadOnlyList<ScriptApiNamespaceMapping> namespaceMappings,
    IReadOnlyList<ScriptApiTypeMapping> typeMappings,
    IReadOnlyList<ScriptApiAttachableType> attachableTypes);

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

        NamespaceMapping[] declaredNamespaceMappings = loaded
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
        ValidateNamespaceMappings(declaredNamespaceMappings);
        NamespaceMapping[] namespaceMappings = declaredNamespaceMappings
            .GroupBy(static mapping => (
                mapping.apiNamespace,
                mapping.implementationNamespace))
            .Select(static group => group.First())
            .ToArray();

        DeclaredTypeExport[] declaredExports = loaded
            .SelectMany(declarationAssembly => declarationAssembly
                .GetCustomAttributes<ScriptingApiExportAttribute>()
                .Where(attribute => Includes(attribute.scope, includeEditor))
                .Select(attribute => new DeclaredTypeExport(
                    declarationAssembly,
                    attribute.type,
                    attribute.name)))
            .OrderBy(static export => export.type.Assembly.GetName().Name, StringComparer.Ordinal)
            .ThenBy(static export => export.type.FullName, StringComparer.Ordinal)
            .ThenBy(static export => export.name, StringComparer.Ordinal)
            .ToArray();
        ValidateExports(declaredExports, namespaceMappings);

        var exports = new List<ScriptApiAssembly>();
        var implementationAssemblies = new HashSet<Assembly>();
        foreach (IGrouping<Assembly, DeclaredTypeExport> implementationGroup in declaredExports
                     .GroupBy(static export => export.type.Assembly)
                     .OrderBy(static group => group.Key.GetName().Name, StringComparer.Ordinal))
        {
            ScriptApiTypeExport[] assemblyExports = implementationGroup
                .Select(static export => new ScriptApiTypeExport(export.type, export.name))
                .Distinct()
                .OrderBy(static export => export.type.FullName, StringComparer.Ordinal)
                .ToArray();
            Assembly implementationAssembly = implementationGroup.Key;
            exports.Add(new ScriptApiAssembly(implementationAssembly, assemblyExports));
            AddWithDependencies(implementationAssembly, byName, implementationAssemblies);
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
        ScriptApiTypeMapping[] allTypeMappings = CreateTypeMappings(exports, namespaceMappings);
        ValidateTypeMappings(allTypeMappings);
        ScriptApiTypeMapping[] typeMappings = allTypeMappings;
        ScriptApiAttachableType[] attachableTypes = exports
            .SelectMany(static export => export.exports)
            .Select(static export => (
                export.type,
                metadata: export.type.GetCustomAttribute<ScriptingAttachableTypeAttribute>(inherit: false)))
            .Where(static value => value.metadata is not null)
            .Select(static value => new ScriptApiAttachableType(
                value.type.FullName
                    ?? throw new InvalidOperationException("An attachable scripting API type has no full name."),
                value.metadata!.kind))
            .Distinct()
            .OrderBy(static value => value.implementationName, StringComparer.Ordinal)
            .ToArray();

        return new ScriptApiProfile(
            includeEditor ? "Editor" : "Runtime",
            exports,
            implementationAssemblies
                .OrderBy(static assembly => assembly.GetName().Name, StringComparer.Ordinal)
                .ToArray(),
            apiNamespaces,
            publicMappings,
            typeMappings,
            attachableTypes);
    }

    private static bool Includes(ScriptingApiScope scope, bool includeEditor)
        => scope == ScriptingApiScope.Runtime || includeEditor && scope == ScriptingApiScope.Editor;

    private static void ValidateExports(
        IReadOnlyList<DeclaredTypeExport> exports,
        IReadOnlyList<NamespaceMapping> mappings)
    {
        foreach (IGrouping<Type, DeclaredTypeExport> group in exports.GroupBy(static export => export.type))
        {
            if (group.Select(static export => export.name).Distinct(StringComparer.Ordinal).Skip(1).Any())
            {
                throw new InvalidOperationException(
                    $"Script API type '{group.Key.FullName}' cannot be exported with multiple names.");
            }
        }
        foreach (DeclaredTypeExport export in exports)
        {
            Type type = export.type;
            if (!type.IsPublic && !type.IsNestedPublic)
                throw new InvalidOperationException($"Script API type '{type.FullName}' must be public.");
            if (!Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsValidIdentifier(export.name))
            {
                throw new InvalidOperationException(
                    $"Script API name '{export.name}' for type '{type.FullName}' is not a valid C# identifier.");
            }
            if (type.IsGenericTypeDefinition && !string.Equals(
                    export.name,
                    GetTypeName(type),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Generic script API type '{type.FullName}' cannot use a script-facing alias.");
            }
            string implementationNamespace = type.Namespace ?? string.Empty;
            if (!mappings.Any(mapping =>
                    string.Equals(mapping.implementationNamespace, implementationNamespace, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"Script API type '{type.FullName}' exported by " +
                    $"'{export.declarationAssembly.GetName().Name}' is not assigned to a declared script API namespace.");
            }
        }
    }

    private static ScriptApiTypeMapping[] CreateTypeMappings(
        IReadOnlyList<ScriptApiAssembly> exports,
        IReadOnlyList<NamespaceMapping> namespaceMappings)
        => exports
            .SelectMany(assemblyExport => assemblyExport.exports.Select(typeExport =>
            {
                NamespaceMapping mapping = namespaceMappings.Single(value =>
                    string.Equals(
                        value.implementationNamespace,
                        typeExport.type.Namespace ?? string.Empty,
                        StringComparison.Ordinal));
                return new ScriptApiTypeMapping(
                    mapping.apiNamespace,
                    typeExport.name,
                    typeExport.type.Namespace ?? string.Empty,
                    GetTypeName(typeExport.type),
                    typeExport.type.IsGenericTypeDefinition
                        ? typeExport.type.GetGenericArguments().Length
                        : 0);
            }))
            .OrderBy(static mapping => mapping.apiNamespace, StringComparer.Ordinal)
            .ThenBy(static mapping => mapping.apiName, StringComparer.Ordinal)
            .ToArray();

    private static void ValidateTypeMappings(IReadOnlyList<ScriptApiTypeMapping> mappings)
    {
        foreach (IGrouping<(string apiNamespace, string apiName, int arity), ScriptApiTypeMapping> group in mappings
                     .GroupBy(static mapping => (mapping.apiNamespace, mapping.apiName, mapping.arity)))
        {
            if (group.Select(static mapping =>
                    (mapping.implementationNamespace, mapping.implementationName))
                .Distinct()
                .Skip(1)
                .Any())
            {
                throw new InvalidOperationException(
                    $"Script API type name '{group.Key.apiNamespace}.{group.Key.apiName}' maps to multiple runtime types.");
            }
        }
    }

    private static string GetTypeName(Type type)
    {
        int aritySeparator = type.Name.IndexOf('`');
        return aritySeparator < 0 ? type.Name : type.Name[..aritySeparator];
    }

    private static void ValidateNamespaceMappings(IReadOnlyList<NamespaceMapping> mappings)
    {
        foreach (IGrouping<string, NamespaceMapping> group in mappings
                     .GroupBy(static mapping => mapping.implementationNamespace, StringComparer.Ordinal))
        {
            string[] apiNamespaces = group
                .Select(static mapping => mapping.apiNamespace)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (apiNamespaces.Length > 1)
            {
                string declarations = string.Join(
                    ", ",
                    group.Select(static mapping => mapping.declarationAssembly.GetName().Name)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(static name => name, StringComparer.Ordinal));
                throw new InvalidOperationException(
                    $"Implementation namespace '{group.Key}' maps to multiple script API namespaces " +
                    $"across declarations in: {declarations}.");
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
        Assembly declarationAssembly);

    private sealed record DeclaredTypeExport(
        Assembly declarationAssembly,
        Type type,
        string name);
}
