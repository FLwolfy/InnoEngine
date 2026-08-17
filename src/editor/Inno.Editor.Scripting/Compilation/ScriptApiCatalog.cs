using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace Inno.Editor.Scripting;

internal sealed record ScriptApiProfile(
    IReadOnlyList<Assembly> assemblies,
    IReadOnlyList<string> globalUsings);

internal static class ScriptApiCatalog
{
    private const string C_API_KEY = "Inno.ScriptApi";
    private const string C_GLOBAL_USINGS_KEY = "Inno.ScriptGlobalUsings";

    internal static ScriptApiProfile Build(bool includeEditor)
    {
        LoadReferencedInnoAssemblies();
        Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .ToArray();
        var byName = loaded
            .GroupBy(static assembly => assembly.GetName().Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        var selected = new HashSet<Assembly>();
        var globalUsings = new HashSet<string>(StringComparer.Ordinal);
        foreach (Assembly assembly in loaded)
        {
            string api = GetMetadata(assembly, C_API_KEY) ?? "None";
            if (!string.Equals(api, "Runtime", StringComparison.OrdinalIgnoreCase) &&
                !(includeEditor && string.Equals(api, "Editor", StringComparison.OrdinalIgnoreCase)))
                continue;
            AddWithDependencies(assembly, byName, selected);
            string? declaredUsings = GetMetadata(assembly, C_GLOBAL_USINGS_KEY);
            if (declaredUsings is null)
                continue;
            foreach (string declaredUsing in declaredUsings.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                globalUsings.Add(declaredUsing);
        }

        return new ScriptApiProfile(
            selected.OrderBy(static assembly => assembly.GetName().Name, StringComparer.Ordinal).ToArray(),
            globalUsings.OrderBy(static value => value, StringComparer.Ordinal).ToArray());
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

    private static string? GetMetadata(Assembly assembly, string key)
        => assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.Ordinal))?.Value;

    private static void LoadReferencedInnoAssemblies()
    {
        Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly =>
                !assembly.IsDynamic &&
                AssemblyLoadContext.GetLoadContext(assembly) == AssemblyLoadContext.Default)
            .ToArray();
        var byName = loaded
            .Where(static assembly => assembly.GetName().Name is not null)
            .ToDictionary(
                static assembly => assembly.GetName().Name!,
                static assembly => assembly,
                StringComparer.OrdinalIgnoreCase);
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
}
