using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Inno.Core.Assemblies;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace Inno.Editor.Scripting;

internal static class ScriptCompiler
{
    private const string C_GAME_ASSEMBLY_NAME = "Inno.GameScripts";
    private const string C_EDITOR_ASSEMBLY_NAME = "Inno.EditorScripts";

    internal static async ValueTask<ScriptCompilationResult> CompileAsync(
        ScriptManagerOptions options,
        Action<float, string>? reportProgress,
        CancellationToken cancellationToken)
    {
        reportProgress?.Invoke(0f, "Discovering project scripts...");
        ScriptSourceSet sources = ScriptSourceSet.Discover();
        var progress = new CompilationProgress(
            sources.assemblies.Sum(static assembly => assembly.sources.Count) +
            sources.assemblies.Count * 6 +
            8,
            reportProgress,
            initialCompleted: 1);
        progress.Complete("Project scripts discovered.");

        progress.Begin("Building the script API profile...");
        ScriptApiProfile runtimeApi = ScriptApiCatalog.Build(includeEditor: false);
        ScriptApiProfile editorApi = ScriptApiCatalog.Build(includeEditor: true);
        progress.Complete("Script API profile built.");
        progress.Begin("Resolving script references...");
        ScriptApiReferenceSet runtimeApiReferences = ScriptApiReferenceBuilder.Build(options, runtimeApi);
        ScriptApiReferenceSet editorApiReferences = ScriptApiReferenceBuilder.Build(
            options,
            editorApi,
            runtimeApi,
            runtimeApiReferences);
        IReadOnlyList<MetadataReference> platformReferences = FrameworkReferenceResolver.CreateRuntimeReferences();
        progress.Complete("Script references resolved.");
        string buildKey = ComputeBuildKey(sources, runtimeApi, editorApi);
        string outputDirectory = Path.Combine(options.outputDirectory, buildKey);
        if (TryCreateCachedResult(
                outputDirectory,
                sources,
                out ScriptCompilationResult? cachedResult))
        {
            progress.Complete("Reused cached script assembly artifact.");
            return cachedResult!;
        }

        string stagingRoot = Path.Combine(options.outputDirectory, ".staging");
        string stagingDirectory = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDirectory);
        var diagnostics = new List<ScriptDiagnostic>();
        progress.Begin("Copying script plugins...");
        if (!TryCopyPlugins(sources, stagingDirectory, diagnostics, out string[] runtimePlugins, out string[] editorPlugins))
        {
            DeleteStagingDirectory(stagingDirectory);
            return new ScriptCompilationResult(false, diagnostics, outputDirectory: null, loadRequest: null);
        }
        progress.Complete("Script plugins copied.");

        var compiledPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (ScriptAssemblyInput assembly in sources.assemblies)
        {
            bool editor = assembly.scope == ScriptAssemblyScope.Editor;
            ScriptApiProfile api = editor ? editorApi : runtimeApi;
            ScriptApiReferenceSet apiReferences = editor ? editorApiReferences : runtimeApiReferences;
            IReadOnlyList<string> plugins = editor
                ? runtimePlugins.Concat(editorPlugins).ToArray()
                : runtimePlugins;
            MetadataReference[] dependencyReferences = assembly.references
                .Select(reference => MetadataReference.CreateFromFile(compiledPaths[reference]))
                .ToArray();
            string assemblyPath = Path.Combine(stagingDirectory, assembly.name + ".dll");
            CompilationResult result = await CompileAssemblyAsync(
                assembly.name,
                assembly.sources,
                api,
                apiReferences,
                platformReferences.Concat(dependencyReferences).ToArray(),
                plugins,
                assemblyPath,
                assembly.scope,
                assembly.defines,
                assembly.nullable,
                assembly.allowUnsafe,
                progress,
                cancellationToken).ConfigureAwait(false);
            diagnostics.AddRange(result.diagnostics);
            if (!result.success)
            {
                DeleteStagingDirectory(stagingDirectory);
                return new ScriptCompilationResult(false, diagnostics, outputDirectory: null, loadRequest: null);
            }
            compiledPaths.Add(assembly.name, assemblyPath);
        }

        File.WriteAllLines(
            Path.Combine(stagingDirectory, "diagnostics"),
            diagnostics.Select(FormatDiagnostic));
        Directory.CreateDirectory(Path.GetDirectoryName(outputDirectory)!);
        if (Directory.Exists(outputDirectory))
            DeleteStagingDirectory(stagingDirectory);
        else
            Directory.Move(stagingDirectory, outputDirectory);

        string gameAssemblyPath = Path.Combine(outputDirectory, C_GAME_ASSEMBLY_NAME + ".dll");
        runtimePlugins = ResolveCommittedPluginPaths(outputDirectory, runtimePlugins);
        editorPlugins = ResolveCommittedPluginPaths(outputDirectory, editorPlugins);

        progress.Begin("Preparing the script reload...");
        var preloadPaths = sources.assemblies
            .Where(static assembly => !string.Equals(
                assembly.name,
                C_GAME_ASSEMBLY_NAME,
                StringComparison.Ordinal))
            .Select(assembly => Path.Combine(outputDirectory, assembly.name + ".dll"))
            .ToList();
        preloadPaths.AddRange(runtimePlugins);
        preloadPaths.AddRange(editorPlugins);
        var request = new AssemblyLoadRequest
        {
            moduleName = "ProjectScripts",
            mainAssemblyPath = gameAssemblyPath,
            preloadAssemblyPaths = preloadPaths,
            collectible = true
        };
        progress.Complete("Script reload prepared.");
        return new ScriptCompilationResult(true, diagnostics, outputDirectory, request);
    }

    private static async ValueTask<CompilationResult> CompileAssemblyAsync(
        string assemblyName,
        IReadOnlyList<ScriptSourceInput> sources,
        ScriptApiProfile api,
        ScriptApiReferenceSet apiReferences,
        IReadOnlyList<MetadataReference> platformReferences,
        IReadOnlyList<string> pluginPaths,
        string outputPath,
        ScriptAssemblyScope scope,
        IReadOnlyList<string> defines,
        bool nullable,
        bool allowUnsafe,
        CompilationProgress progress,
        CancellationToken cancellationToken)
    {
        var parseOptions = new CSharpParseOptions(
            LanguageVersion.Latest,
            DocumentationMode.Parse,
            SourceCodeKind.Regular,
            preprocessorSymbols: defines
                .Concat(["DEBUG", "TRACE"])
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static value => value, StringComparer.Ordinal));
        var syntaxTrees = new List<SyntaxTree>(sources.Count + 1);
        for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            ScriptSourceInput sourceInput = sources[sourceIndex];
            progress.Begin($"Parsing {assemblyName} sources ({sourceIndex + 1}/{sources.Count})...");
            string source = await File.ReadAllTextAsync(sourceInput.snapshotPath, cancellationToken)
                .ConfigureAwait(false);
            syntaxTrees.Add(CSharpSyntaxTree.ParseText(
                SourceText.From(source, Encoding.UTF8),
                parseOptions,
                sourceInput.sourcePath,
                cancellationToken));
            progress.Complete($"Parsed {assemblyName} source {sourceIndex + 1}/{sources.Count}.");
        }
        progress.Begin($"Preparing generated {assemblyName} sources...");
        syntaxTrees.Add(CSharpSyntaxTree.ParseText(
            SourceText.From(
                CreateGeneratedSource(
                    assemblyName,
                    scope == ScriptAssemblyScope.Editor),
                Encoding.UTF8),
            parseOptions,
            $"<{assemblyName}.Generated.g.cs>",
            cancellationToken));
        progress.Complete($"Prepared generated {assemblyName} sources.");

        var references = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);
        foreach (MetadataReference reference in platformReferences)
        {
            if (!string.IsNullOrWhiteSpace(reference.Display))
                references[reference.Display!] = reference;
        }
        foreach (string referencePath in apiReferences.runtimeReferencePaths)
            references[referencePath] = MetadataReference.CreateFromFile(referencePath);
        foreach (string pluginPath in pluginPaths)
            references[pluginPath] = MetadataReference.CreateFromFile(pluginPath);

        var validationCompilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            references.Values,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug,
                checkOverflow: true,
                allowUnsafe: allowUnsafe,
                deterministic: true,
                concurrentBuild: true,
                nullableContextOptions: nullable
                    ? NullableContextOptions.Enable
                    : NullableContextOptions.Disable));
        var apiMapFile = new InMemoryAdditionalText(
            assemblyName + ScriptApiMapBuilder.C_FILE_EXTENSION,
            ScriptApiMapBuilder.Build(api));
        var analyzerOptions = new AnalyzerOptions(ImmutableArray.Create<AdditionalText>(apiMapFile));
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new LogicalScriptingApiAnalyzer());
        progress.Begin($"Validating the {assemblyName} API surface...");
        ImmutableArray<Diagnostic> analyzerDiagnostics = await validationCompilation
            .WithAnalyzers(analyzers, analyzerOptions)
            .GetAnalyzerDiagnosticsAsync(cancellationToken)
            .ConfigureAwait(false);
        progress.Complete($"Validated the {assemblyName} API surface.");
        if (analyzerDiagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new CompilationResult(
                false,
                analyzerDiagnostics
                    .Where(static diagnostic => diagnostic.Severity != DiagnosticSeverity.Hidden)
                    .Select(ToDiagnostic)
                    .ToArray());
        }

        var usingRewriter = new ScriptApiUsingRewriter(api.namespaceMappings, api.typeMappings);
        var propertyOrderRewriter = new SerializablePropertyOrderRewriter();
        SyntaxTree[] runtimeTrees = syntaxTrees
            .Select(tree => CSharpSyntaxTree.Create(
                (CSharpSyntaxNode)usingRewriter.Visit(
                    propertyOrderRewriter.Visit(tree.GetRoot(cancellationToken)))!,
                parseOptions,
                tree.FilePath,
                Encoding.UTF8))
            .ToArray();
        var runtimeCompilation = CSharpCompilation.Create(
            assemblyName,
            runtimeTrees,
            references.Values,
            validationCompilation.Options);
        progress.Begin($"Resolving {assemblyName} script type identities...");
        ScriptTypeAnalysisResult typeAnalysis = ScriptTypeAnalyzer.Analyze(
            runtimeCompilation,
            sources,
            cancellationToken);
        if (typeAnalysis.mappings.Count > 0)
        {
            SyntaxTree mappingTree = CSharpSyntaxTree.ParseText(
                SourceText.From(
                    ScriptTypeAnalyzer.CreateMappingSource(typeAnalysis.mappings),
                    Encoding.UTF8),
                parseOptions,
                $"<{assemblyName}.ScriptTypeIds.g.cs>",
                cancellationToken);
            runtimeCompilation = runtimeCompilation.AddSyntaxTrees(mappingTree);
        }
        progress.Complete($"Resolved {assemblyName} script type identities.");
        if (typeAnalysis.diagnostics.Any(static diagnostic =>
                diagnostic.severity == ScriptDiagnosticSeverity.Error))
        {
            return new CompilationResult(false, typeAnalysis.diagnostics);
        }
        progress.Begin($"Checking {assemblyName} compilation diagnostics...");
        Diagnostic[] preEmitDiagnostics = runtimeCompilation
            .GetDiagnostics(cancellationToken)
            .Concat(analyzerDiagnostics)
            .Where(static diagnostic => diagnostic.Severity != DiagnosticSeverity.Hidden)
            .Distinct(DiagnosticComparer.Instance)
            .ToArray();
        progress.Complete($"Checked {assemblyName} compilation diagnostics.");
        if (preEmitDiagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new CompilationResult(
                false,
                typeAnalysis.diagnostics
                    .Concat(preEmitDiagnostics.Select(ToDiagnostic))
                    .ToArray());
        }

        string pdbPath = Path.ChangeExtension(outputPath, ".pdb");
        string documentationPath = Path.ChangeExtension(outputPath, ".xml");
        progress.Begin($"Emitting {assemblyName}...");
        await using FileStream assemblyStream = File.Create(outputPath);
        await using FileStream pdbStream = File.Create(pdbPath);
        await using FileStream documentationStream = File.Create(documentationPath);
        EmitResult emit = runtimeCompilation.Emit(
            assemblyStream,
            pdbStream,
            xmlDocumentationStream: documentationStream,
            options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb),
            cancellationToken: cancellationToken);
        if (emit.Success)
        {
            await File.WriteAllTextAsync(
                GetTypeManifestPath(outputPath),
                JsonSerializer.Serialize(
                    typeAnalysis.manifest,
                    new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken).ConfigureAwait(false);
        }
        progress.Complete($"Emitted {assemblyName}.");
        ScriptDiagnostic[] diagnostics = typeAnalysis.diagnostics
            .Concat(preEmitDiagnostics
                .Concat(emit.Diagnostics)
                .Where(static diagnostic => diagnostic.Severity != DiagnosticSeverity.Hidden)
                .Distinct(DiagnosticComparer.Instance)
                .Select(ToDiagnostic))
            .ToArray();
        return new CompilationResult(emit.Success, diagnostics);
    }

    private sealed class CompilationProgress
    {
        private readonly Action<float, string>? m_report;
        private readonly int m_total;
        private int m_completed;

        internal CompilationProgress(
            int total,
            Action<float, string>? report,
            int initialCompleted = 0)
        {
            m_total = Math.Max(1, total);
            m_report = report;
            m_completed = Math.Clamp(initialCompleted, 0, m_total);
        }

        internal void Begin(string status)
        {
            m_report?.Invoke((float)m_completed / m_total, status);
        }

        internal void Complete(string status)
        {
            m_completed++;
            m_report?.Invoke(Math.Min(1f, (float)m_completed / m_total), status);
        }
    }

    private static bool TryCopyPlugins(
        ScriptSourceSet sources,
        string outputDirectory,
        ICollection<ScriptDiagnostic> diagnostics,
        out string[] runtimePlugins,
        out string[] editorPlugins)
    {
        var copiedByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        runtimePlugins = CopySet(sources.runtimePlugins);
        editorPlugins = CopySet(sources.editorPlugins);
        return diagnostics.All(static diagnostic => diagnostic.severity != ScriptDiagnosticSeverity.Error);

        string[] CopySet(IReadOnlyList<ScriptPluginInput> pluginInputs)
        {
            var result = new List<string>(pluginInputs.Count);
            foreach (ScriptPluginInput plugin in pluginInputs)
            {
                try
                {
                    AssemblyName assemblyName = AssemblyName.GetAssemblyName(plugin.assemblyArtifactPath);
                    string simpleName = assemblyName.Name
                        ?? throw new BadImageFormatException("Managed assembly has no simple name.");
                    if (copiedByName.TryGetValue(simpleName, out string? existing))
                    {
                        diagnostics.Add(new ScriptDiagnostic(
                            "INNO1001",
                            ScriptDiagnosticSeverity.Error,
                            $"Plugin assembly name '{simpleName}' is duplicated by '{existing}' and '{plugin.sourcePath}'.",
                            plugin.sourcePath,
                            0,
                            0));
                        continue;
                    }

                    string destinationPath = Path.Combine(outputDirectory, simpleName + ".dll");
                    File.Copy(plugin.assemblyArtifactPath, destinationPath, overwrite: true);
                    CopyCompanion(plugin.symbolsArtifactPath, Path.ChangeExtension(destinationPath, ".pdb"));
                    CopyCompanion(
                        plugin.dependenciesArtifactPath,
                        Path.ChangeExtension(destinationPath, ".deps.json"));
                    copiedByName.Add(simpleName, plugin.sourcePath);
                    result.Add(destinationPath);
                }
                catch (Exception exception) when (exception is BadImageFormatException or FileLoadException or IOException)
                {
                    diagnostics.Add(new ScriptDiagnostic(
                        "INNO1000",
                        ScriptDiagnosticSeverity.Error,
                        $"Plugin '{plugin.sourcePath}' is not a readable managed assembly: {exception.Message}",
                        plugin.sourcePath,
                        0,
                        0));
                }
            }
            return result.ToArray();
        }
    }

    private static void CopyCompanion(string? sourcePath, string destinationPath)
    {
        if (!string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath))
            File.Copy(sourcePath, destinationPath, overwrite: true);
    }

    private static string ComputeBuildKey(
        ScriptSourceSet sources,
        ScriptApiProfile runtimeApi,
        ScriptApiProfile editorApi)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHash(hash, "Inno.ScriptAssemblyArtifact");
        AppendHash(hash, sources.fingerprint);
        AppendProfile(runtimeApi);
        AppendProfile(editorApi);
        return Convert.ToHexString(hash.GetHashAndReset());

        void AppendProfile(ScriptApiProfile profile)
        {
            AppendHash(hash, profile.name);
            foreach (ScriptApiAssembly export in profile.exports
                         .OrderBy(static value => value.assembly.GetName().Name, StringComparer.Ordinal))
            {
                AppendHash(hash, export.assembly.ManifestModule.ModuleVersionId.ToString("D"));
                foreach (ScriptApiTypeExport typeExport in export.exports)
                {
                    Type type = typeExport.type;
                    AppendHash(hash, type.AssemblyQualifiedName ?? type.FullName ?? type.Name);
                    AppendHash(hash, typeExport.name);
                }
            }
            foreach (ScriptApiNamespaceMapping mapping in profile.namespaceMappings)
            {
                AppendHash(hash, mapping.apiNamespace);
                AppendHash(hash, mapping.implementationNamespace);
            }
        }
    }

    private static bool TryCreateCachedResult(
        string outputDirectory,
        ScriptSourceSet sources,
        out ScriptCompilationResult? result)
    {
        result = null;
        foreach (ScriptAssemblyInput assembly in sources.assemblies)
        {
            string path = Path.Combine(outputDirectory, assembly.name + ".dll");
            if (!File.Exists(path) ||
                !File.Exists(Path.ChangeExtension(path, ".pdb")) ||
                !File.Exists(Path.ChangeExtension(path, ".xml")) ||
                !File.Exists(GetTypeManifestPath(path)))
            {
                return false;
            }
        }

        string[] runtimePlugins = ResolveCachedPluginPaths(
            outputDirectory,
            sources.runtimePlugins.Select(static plugin => plugin.sourcePath).ToArray());
        string[] editorPlugins = ResolveCachedPluginPaths(
            outputDirectory,
            sources.editorPlugins.Select(static plugin => plugin.sourcePath).ToArray());
        if (runtimePlugins.Length != sources.runtimePlugins.Count ||
            editorPlugins.Length != sources.editorPlugins.Count)
        {
            return false;
        }
        string gameAssemblyPath = Path.Combine(outputDirectory, C_GAME_ASSEMBLY_NAME + ".dll");
        var preloadPaths = sources.assemblies
            .Where(static assembly => !string.Equals(
                assembly.name,
                C_GAME_ASSEMBLY_NAME,
                StringComparison.Ordinal))
            .Select(assembly => Path.Combine(outputDirectory, assembly.name + ".dll"))
            .ToList();
        preloadPaths.AddRange(runtimePlugins);
        preloadPaths.AddRange(editorPlugins);
        result = new ScriptCompilationResult(
            success: true,
            diagnostics: [],
            outputDirectory: outputDirectory,
            loadRequest: new AssemblyLoadRequest
            {
                moduleName = "ProjectScripts",
                mainAssemblyPath = gameAssemblyPath,
                preloadAssemblyPaths = preloadPaths,
                collectible = true
            });
        return true;
    }

    private static string[] ResolveCachedPluginPaths(
        string outputDirectory,
        IReadOnlyList<string> sourcePaths)
    {
        var result = new List<string>(sourcePaths.Count);
        for (int i = 0; i < sourcePaths.Count; i++)
        {
            try
            {
                string? name = AssemblyName.GetAssemblyName(sourcePaths[i]).Name;
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                string path = Path.Combine(outputDirectory, name + ".dll");
                if (File.Exists(path))
                    result.Add(path);
            }
            catch (BadImageFormatException)
            {
                return [];
            }
        }
        return result.ToArray();
    }

    private static string[] ResolveCommittedPluginPaths(
        string outputDirectory,
        IReadOnlyList<string> stagingPaths)
        => stagingPaths
            .Select(path => Path.Combine(outputDirectory, Path.GetFileName(path)))
            .ToArray();

    private static void DeleteStagingDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Staging data is unreachable and can be collected on the next editor idle pass.
        }
        catch (UnauthorizedAccessException)
        {
            // Staging data is unreachable and can be collected on the next editor idle pass.
        }
    }

    private static string FormatDiagnostic(ScriptDiagnostic diagnostic)
        => $"{diagnostic.severity}|{diagnostic.id}|{diagnostic.filePath}|" +
           $"{diagnostic.line}|{diagnostic.column}|{diagnostic.message}";

    private static string GetTypeManifestPath(string assemblyPath)
        => Path.ChangeExtension(assemblyPath, ".types.json");

    private static void AppendHash(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }

    private static string CreateGeneratedSource(
        string assemblyName,
        bool isEditorAssembly)
    {
        string assemblyGroup = isEditorAssembly ? "Editor" : "Game";
        return $"""
            #nullable enable
            [assembly: System.Reflection.AssemblyMetadata("Inno.AssemblyGroup", "{assemblyGroup}")]
            [assembly: System.Reflection.AssemblyMetadata("Inno.ScriptAssembly", "{assemblyName}")]
            """;
    }

    private static ScriptDiagnostic ToDiagnostic(Diagnostic diagnostic)
    {
        FileLinePositionSpan location = diagnostic.Location.GetLineSpan();
        return new ScriptDiagnostic(
            diagnostic.Id,
            diagnostic.Severity switch
            {
                DiagnosticSeverity.Error => ScriptDiagnosticSeverity.Error,
                DiagnosticSeverity.Warning => ScriptDiagnosticSeverity.Warning,
                _ => ScriptDiagnosticSeverity.Info
            },
            diagnostic.GetMessage(),
            string.IsNullOrWhiteSpace(location.Path) ? null : location.Path,
            location.IsValid ? location.StartLinePosition.Line + 1 : 0,
            location.IsValid ? location.StartLinePosition.Character + 1 : 0);
    }

    private sealed record CompilationResult(bool success, IReadOnlyList<ScriptDiagnostic> diagnostics);

    private sealed class InMemoryAdditionalText : AdditionalText
    {
        private readonly SourceText m_text;

        internal InMemoryAdditionalText(string path, string text)
        {
            Path = path;
            m_text = SourceText.From(text, Encoding.UTF8);
        }

        public override string Path { get; }

        public override SourceText GetText(CancellationToken cancellationToken = default)
            => m_text;
    }

    private sealed class DiagnosticComparer : IEqualityComparer<Diagnostic>
    {
        internal static readonly DiagnosticComparer Instance = new();

        public bool Equals(Diagnostic? left, Diagnostic? right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left is null || right is null)
                return false;
            FileLinePositionSpan leftLocation = left.Location.GetLineSpan();
            FileLinePositionSpan rightLocation = right.Location.GetLineSpan();
            return string.Equals(left.Id, right.Id, StringComparison.Ordinal) &&
                   string.Equals(left.GetMessage(), right.GetMessage(), StringComparison.Ordinal) &&
                   string.Equals(leftLocation.Path, rightLocation.Path, StringComparison.Ordinal) &&
                   leftLocation.StartLinePosition.Equals(rightLocation.StartLinePosition);
        }

        public int GetHashCode(Diagnostic diagnostic)
        {
            FileLinePositionSpan location = diagnostic.Location.GetLineSpan();
            return HashCode.Combine(
                diagnostic.Id,
                diagnostic.GetMessage(),
                location.Path,
                location.StartLinePosition);
        }
    }
}
