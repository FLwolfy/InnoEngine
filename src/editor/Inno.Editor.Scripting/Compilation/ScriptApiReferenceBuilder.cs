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
    private const string C_REFERENCE_SCHEMA_VERSION = "3";

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
        HashSet<Type> exportedTypes = profile.exports
            .SelectMany(static export => export.exportedTypes)
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
            .SelectMany(static export => export.exportedTypes)
            .ToHashSet() ?? [];
        Type[] logicalTypes = exportedTypes
            .Where(type => !baseTypes.Contains(type))
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToArray();
        var ideReferencePaths = new List<string>();
        if (baseReferences is not null)
            ideReferencePaths.AddRange(baseReferences.ideReferencePaths);
        if (logicalTypes.Length > 0)
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
                    logicalTypes,
                    exportedTypes,
                    profile.namespaceMappings,
                    baseReferences?.ideReferencePaths ?? []);
            }
            WriteLogicalDocumentation(
                referencePath,
                assemblyName,
                logicalTypes,
                profile.namespaceMappings);
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
                concurrentBuild: true,
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
        IReadOnlyList<Type> types,
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
        string source = ScriptApiStubSourceBuilder.BuildLogical(types, exportedTypes, mappings);
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
                concurrentBuild: true,
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
        IReadOnlyList<Type> types,
        IReadOnlyList<ScriptApiNamespaceMapping> namespaceMappings)
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
            types,
            mappings);
    }

    private static string CreateFingerprint(ScriptApiProfile profile)
    {
        var builder = new StringBuilder(C_REFERENCE_SCHEMA_VERSION)
            .Append('|')
            .Append(profile.name);
        foreach (ScriptApiAssembly export in profile.exports)
        {
            builder.Append('|').Append(export.assembly.ManifestModule.ModuleVersionId.ToString("D"));
            foreach (Type type in export.exportedTypes)
                builder.Append('|').Append(type.AssemblyQualifiedName);
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
