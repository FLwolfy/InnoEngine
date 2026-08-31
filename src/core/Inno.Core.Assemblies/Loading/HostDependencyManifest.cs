using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Inno.Core.Assemblies.Loading;

internal static class HostDependencyManifest
{
    private const string C_DEPENDENCY_FILES_KEY = "APP_CONTEXT_DEPS_FILES";

    internal static IReadOnlyList<AssemblyName> GetInnoRuntimeAssemblies(
        IReadOnlyList<Assembly> rootAssemblies)
    {
        ArgumentNullException.ThrowIfNull(rootAssemblies);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string dependencyFile in GetDependencyFiles(rootAssemblies))
            ReadRuntimeAssemblyNames(dependencyFile, names);
        return names
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .Select(static name => new AssemblyName(name))
            .ToArray();
    }

    private static IReadOnlyList<string> GetDependencyFiles(IReadOnlyList<Assembly> rootAssemblies)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (AppContext.GetData(C_DEPENDENCY_FILES_KEY) is string dependencyFiles)
        {
            foreach (string path in dependencyFiles.Split(
                         Path.PathSeparator,
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (File.Exists(path))
                    paths.Add(Path.GetFullPath(path));
            }
        }

        foreach (Assembly rootAssembly in rootAssemblies)
        {
            if (string.IsNullOrWhiteSpace(rootAssembly.Location))
                continue;
            string dependencyFile = Path.ChangeExtension(rootAssembly.Location, ".deps.json");
            if (File.Exists(dependencyFile))
                paths.Add(Path.GetFullPath(dependencyFile));
        }
        return paths.ToArray();
    }

    private static void ReadRuntimeAssemblyNames(string dependencyFile, ISet<string> names)
    {
        try
        {
            using FileStream stream = File.OpenRead(dependencyFile);
            using JsonDocument document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("targets", out JsonElement targets))
                return;

            foreach (JsonProperty target in targets.EnumerateObject())
            {
                foreach (JsonProperty library in target.Value.EnumerateObject())
                {
                    if (!library.Value.TryGetProperty("runtime", out JsonElement runtime))
                        continue;
                    foreach (JsonProperty asset in runtime.EnumerateObject())
                    {
                        string fileName = Path.GetFileName(asset.Name);
                        if (!fileName.StartsWith("Inno.", StringComparison.Ordinal) ||
                            !fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        names.Add(Path.GetFileNameWithoutExtension(fileName));
                    }
                }
            }
        }
        catch (IOException)
        {
            // Dependency manifests are an optional host discovery source.
        }
        catch (UnauthorizedAccessException)
        {
            // The regular CLR reference graph remains available when a manifest cannot be read.
        }
        catch (JsonException)
        {
            // Ignore an invalid optional manifest and continue with the CLR reference graph.
        }
    }
}
