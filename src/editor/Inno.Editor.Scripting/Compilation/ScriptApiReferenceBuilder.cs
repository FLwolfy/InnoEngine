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
    IReadOnlyList<string> referencePaths);

internal static class ScriptApiReferenceBuilder
{
    private const string C_REFERENCE_SCHEMA_VERSION = "2";

    internal static ScriptApiReferenceSet Build(
        ScriptManagerOptions options,
        ScriptApiProfile profile)
    {
        string fingerprint = CreateFingerprint(profile);
        string directory = Path.Combine(options.scriptApiDirectory, profile.name, fingerprint);
        Directory.CreateDirectory(directory);
        string[] implementationPaths = GetImplementationPaths(profile);
        HashSet<Type> exportedTypes = profile.exports
            .SelectMany(static export => export.exportedTypes)
            .ToHashSet();
        var referencePaths = new List<string>(profile.exports.Count);
        foreach (ScriptApiAssembly export in profile.exports)
        {
            string assemblyName = export.assembly.GetName().Name
                ?? throw new InvalidOperationException("A script API assembly has no simple name.");
            string referencePath = Path.Combine(directory, assemblyName + ".dll");
            if (!File.Exists(referencePath))
                EmitReferenceAssembly(export, referencePath, implementationPaths, exportedTypes);
            referencePaths.Add(referencePath);
        }
        return new ScriptApiReferenceSet(referencePaths);
    }

    private static void EmitReferenceAssembly(
        ScriptApiAssembly export,
        string referencePath,
        IReadOnlyList<string> implementationPaths,
        IReadOnlySet<Type> exportedTypes)
    {
        string assemblyName = export.assembly.GetName().Name!;
        string source = ScriptApiStubSourceBuilder.Build(export, exportedTypes);
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
