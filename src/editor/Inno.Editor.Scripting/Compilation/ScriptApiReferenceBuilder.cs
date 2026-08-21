using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace Inno.Editor.Scripting;

internal sealed record ScriptApiReferenceSet(
    IReadOnlyList<string> runtimeReferencePaths,
    IReadOnlyList<string> ideReferencePaths);

internal static class ScriptApiReferenceBuilder
{
    internal static ScriptApiReferenceSet Build(
        ScriptManagerOptions options,
        ScriptApiProfile profile,
        ScriptApiProfile? baseProfile = null,
        ScriptApiReferenceSet? baseReferences = null)
    {
        string fingerprint = CreateFingerprint(profile);
        string directory = Path.Combine(options.scriptApiDirectory, profile.name, fingerprint);
        Directory.CreateDirectory(directory);
        string[] implementationPaths = GetImplementationPaths(profile);
        ScriptApiTypeExport[] allExports = profile.exports
            .SelectMany(static export => export.exports)
            .ToArray();
        HashSet<Type> exportedTypes = allExports
            .Select(static export => export.type)
            .ToHashSet();
        var runtimeReferencePaths = new List<string>(profile.exports.Count);
        string runtimeDirectory = Path.Combine(directory, "Runtime");
        Directory.CreateDirectory(runtimeDirectory);
        foreach (ScriptApiAssembly export in profile.exports)
        {
            string assemblyName = export.assembly.GetName().Name
                ?? throw new InvalidOperationException("A script API assembly has no simple name.");
            string referencePath = Path.Combine(runtimeDirectory, assemblyName + ".dll");
            if (!File.Exists(referencePath))
                EmitImplementationReferenceAssembly(export, referencePath, implementationPaths, exportedTypes);
            runtimeReferencePaths.Add(referencePath);
        }

        HashSet<Type> baseTypes = baseProfile?.exports
            .SelectMany(static export => export.exports)
            .Select(static export => export.type)
            .ToHashSet() ?? [];
        ScriptApiTypeExport[] logicalExports = allExports
            .Where(export => !baseTypes.Contains(export.type))
            .OrderBy(static export => export.type.FullName, StringComparer.Ordinal)
            .ToArray();
        var ideReferencePaths = new List<string>();
        if (baseReferences is not null)
            ideReferencePaths.AddRange(baseReferences.ideReferencePaths);
        if (logicalExports.Length > 0)
        {
            string logicalDirectory = Path.Combine(directory, "IDE");
            Directory.CreateDirectory(logicalDirectory);
            string assemblyName = "Inno.ScriptApi." + profile.name;
            string referencePath = Path.Combine(logicalDirectory, assemblyName + ".dll");
            if (!File.Exists(referencePath))
            {
                EmitLogicalReferenceAssembly(
                    assemblyName,
                    referencePath,
                    logicalExports,
                    exportedTypes,
                    profile.namespaceMappings,
                    baseReferences?.ideReferencePaths ?? []);
            }
            WriteLogicalDocumentation(
                referencePath,
                assemblyName,
                logicalExports,
                profile.namespaceMappings,
                profile.typeMappings);
            ideReferencePaths.Add(referencePath);
        }
        return new ScriptApiReferenceSet(runtimeReferencePaths, ideReferencePaths);
    }

    private static void EmitImplementationReferenceAssembly(
        ScriptApiAssembly export,
        string referencePath,
        IReadOnlyList<string> implementationPaths,
        IReadOnlySet<Type> exportedTypes)
    {
        string assemblyName = export.assembly.GetName().Name!;
        string source = ScriptApiStubSourceBuilder.BuildImplementation(export, exportedTypes);
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(
            SourceText.From(source, Encoding.UTF8),
            new CSharpParseOptions(LanguageVersion.Latest),
            $"<{assemblyName}.ScriptApi.g.cs>");
        var references = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);
        foreach (MetadataReference reference in FrameworkReferenceResolver.CreateReferencePackReferences())
        {
            if (!string.IsNullOrWhiteSpace(reference.Display))
                references[reference.Display!] = reference;
        }
        foreach (string implementationPath in implementationPaths)
        {
            if (string.Equals(
                    Path.GetFileNameWithoutExtension(implementationPath),
                    assemblyName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            references[implementationPath] = MetadataReference.CreateFromFile(implementationPath);
        }

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            references.Values,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                allowUnsafe: true,
                deterministic: true,
                concurrentBuild: false,
                nullableContextOptions: NullableContextOptions.Enable,
                metadataImportOptions: MetadataImportOptions.Public));
        string temporaryPath = referencePath + ".tmp";
        using (FileStream stream = File.Create(temporaryPath))
        {
            EmitResult result = compilation.Emit(
                peStream: stream,
                options: new EmitOptions(metadataOnly: true, includePrivateMembers: false));
            if (!result.Success)
            {
                string errors = string.Join(
                    Environment.NewLine,
                    result.Diagnostics
                        .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                        .Select(static diagnostic => diagnostic.ToString()));
                throw new InvalidOperationException(
                    $"Failed to build script API reference assembly '{assemblyName}'." +
                    $"{Environment.NewLine}{errors}" +
                    $"{Environment.NewLine}{source}");
            }
        }
        File.Move(temporaryPath, referencePath, overwrite: true);
    }

    private static void EmitLogicalReferenceAssembly(
        string assemblyName,
        string referencePath,
        IReadOnlyList<ScriptApiTypeExport> exports,
        IReadOnlySet<Type> exportedTypes,
        IReadOnlyList<ScriptApiNamespaceMapping> namespaceMappings,
        IReadOnlyList<string> baseReferencePaths)
    {
        Dictionary<string, string> mappings = namespaceMappings
            .GroupBy(static mapping => mapping.implementationNamespace, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.First().apiNamespace,
                StringComparer.Ordinal);
        string source = ScriptApiStubSourceBuilder.BuildLogical(exports, exportedTypes, mappings);
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(
            SourceText.From(source, Encoding.UTF8),
            new CSharpParseOptions(LanguageVersion.Latest),
            $"<{assemblyName}.g.cs>");
        var references = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);
        foreach (MetadataReference reference in FrameworkReferenceResolver.CreateReferencePackReferences())
        {
            if (!string.IsNullOrWhiteSpace(reference.Display))
                references[reference.Display!] = reference;
        }
        foreach (string baseReferencePath in baseReferencePaths)
            references[baseReferencePath] = MetadataReference.CreateFromFile(baseReferencePath);

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            references.Values,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                allowUnsafe: true,
                deterministic: true,
                concurrentBuild: false,
                nullableContextOptions: NullableContextOptions.Enable,
                metadataImportOptions: MetadataImportOptions.Public));
        string temporaryPath = referencePath + ".tmp";
        using (FileStream stream = File.Create(temporaryPath))
        {
            EmitResult result = compilation.Emit(
                peStream: stream,
                options: new EmitOptions(metadataOnly: true, includePrivateMembers: false));
            if (!result.Success)
            {
                string errors = string.Join(
                    Environment.NewLine,
                    result.Diagnostics
                        .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                        .Select(static diagnostic => diagnostic.ToString()));
                throw new InvalidOperationException(
                    $"Failed to build logical script API assembly '{assemblyName}'." +
                    $"{Environment.NewLine}{errors}" +
                    $"{Environment.NewLine}{source}");
            }
        }
        File.Move(temporaryPath, referencePath, overwrite: true);
    }

    private static void WriteLogicalDocumentation(
        string referencePath,
        string assemblyName,
        IReadOnlyList<ScriptApiTypeExport> exports,
        IReadOnlyList<ScriptApiNamespaceMapping> namespaceMappings,
        IReadOnlyList<ScriptApiTypeMapping> typeMappings)
    {
        Dictionary<string, string> mappings = namespaceMappings
            .GroupBy(static mapping => mapping.implementationNamespace, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.First().apiNamespace,
                StringComparer.Ordinal);
        ScriptApiDocumentationBuilder.Write(
            Path.ChangeExtension(referencePath, ".xml"),
            assemblyName,
            exports,
            mappings,
            typeMappings);
    }

    private static string CreateFingerprint(ScriptApiProfile profile)
    {
        var builder = new StringBuilder(
                typeof(ScriptApiReferenceBuilder).Assembly.ManifestModule.ModuleVersionId.ToString("D"))
            .Append('|')
            .Append(profile.name);
        foreach (ScriptApiAssembly export in profile.exports)
        {
            builder.Append('|').Append(export.assembly.ManifestModule.ModuleVersionId.ToString("D"));
            foreach (ScriptApiTypeExport typeExport in export.exports)
            {
                builder.Append('|')
                    .Append(typeExport.type.AssemblyQualifiedName)
                    .Append('>')
                    .Append(typeExport.name);
            }
        }
        foreach (string apiNamespace in profile.apiNamespaces)
            builder.Append('|').Append(apiNamespace);
        foreach (ScriptApiNamespaceMapping mapping in profile.namespaceMappings)
        {
            builder.Append('|')
                .Append(mapping.apiNamespace)
                .Append('>')
                .Append(mapping.implementationNamespace);
        }
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant();
    }

    private static string[] GetImplementationPaths(ScriptApiProfile profile)
    {
        string runtimeDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(RuntimeEnvironment.GetRuntimeDirectory()));
        return profile.implementationAssemblies
            .Select(static assembly => assembly.Location)
            .Where(path => !string.IsNullOrWhiteSpace(path) && !IsFrameworkAssembly(path, runtimeDirectory))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsFrameworkAssembly(string path, string runtimeDirectory)
    {
        string assemblyPath = Path.GetFullPath(path);
        return assemblyPath.StartsWith(
            runtimeDirectory + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }

}
