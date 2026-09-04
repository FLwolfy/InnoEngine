using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using Microsoft.CodeAnalysis;

namespace Inno.Scripting.Compiler;

internal static class FrameworkReferenceResolver
{
    internal static IReadOnlyList<MetadataReference> CreateReferencePackReferences()
    {
        string referenceDirectory = FindReferenceDirectory();
        return Directory
            .EnumerateFiles(referenceDirectory, "*.dll", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    private static string FindReferenceDirectory()
    {
        Version runtimeVersion = Environment.Version;
        string targetFramework = $"net{runtimeVersion.Major}.{runtimeVersion.Minor}";
        foreach (string dotnetRoot in GetDotnetRoots())
        {
            string packRoot = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref");
            if (!Directory.Exists(packRoot))
                continue;
            string? directory = Directory
                .EnumerateDirectories(packRoot)
                .Select(path => new
                {
                    path,
                    version = ParseVersion(Path.GetFileName(path))
                })
                .Where(candidate => candidate.version is not null &&
                                    candidate.version.Major == runtimeVersion.Major &&
                                    candidate.version.Minor == runtimeVersion.Minor)
                .OrderByDescending(static candidate => candidate.version)
                .Select(candidate => Path.Combine(candidate.path, "ref", targetFramework))
                .FirstOrDefault(Directory.Exists);
            if (directory is not null)
                return directory;
        }
        throw new InvalidOperationException(
            $"The {targetFramework} reference pack is required to generate script API assemblies. " +
            "Install the matching .NET SDK or configure DOTNET_ROOT.");
    }

    private static IEnumerable<string> GetDotnetRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Add(Environment.GetEnvironmentVariable("DOTNET_ROOT"));
        Add(Environment.GetEnvironmentVariable("DOTNET_ROOT_X64"));
        string runtimeDirectory = Path.GetFullPath(RuntimeEnvironment.GetRuntimeDirectory());
        Add(Path.GetFullPath(Path.Combine(runtimeDirectory, "..", "..", "..")));
        return roots;

        void Add(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                roots.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)));
        }
    }

    private static Version? ParseVersion(string value)
    {
        int suffixIndex = value.IndexOf('-', StringComparison.Ordinal);
        string version = suffixIndex >= 0 ? value[..suffixIndex] : value;
        return Version.TryParse(version, out Version? result) ? result : null;
    }
}
