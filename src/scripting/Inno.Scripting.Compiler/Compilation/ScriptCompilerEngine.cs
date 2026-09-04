using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Plugins.Authoring;
using Inno.Extensibility.Modules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace Inno.Scripting.Compiler;

internal static class ScriptCompilerEngine
{
    private const string C_GAME_ASSEMBLY_NAME = "Inno.GameScripts";
    private const string C_EDITOR_ASSEMBLY_NAME = "Inno.EditorScripts";
    private const string C_RUNTIME_MODULE_NAME = "RuntimeScripts";
    private const string C_EDITOR_MODULE_NAME = "EditorScripts";
    private static readonly object S_CACHE_SYNC = new();

    internal static async ValueTask<ScriptCompilationResult> CompileAsync(
        ScriptCompilerOptions options,
        AssetPipeline assets,
        PluginEnvironment plugins,
        bool includeEditor,
        string? targetRuntimeDirectory,
        Action<float, string>? reportProgress,
        CancellationToken cancellationToken)
    {
        if (includeEditor == (targetRuntimeDirectory is not null))
        {
            throw new ArgumentException(
                includeEditor
                    ? "Authoring compilation cannot bind to a Player runtime."
                    : "Runtime deployment compilation requires a target Player runtime directory.",
                nameof(targetRuntimeDirectory));
        }
        cancellationToken.ThrowIfCancellationRequested();
        reportProgress?.Invoke(0f, "Discovering project scripts...");
        ScriptSourceSet sources = ScriptSourceSet.Discover(assets, plugins, includeEditor);
        var progress = new CompilationProgress(
            sources.assemblies.Sum(static assembly => assembly.sources.Count) +
            sources.assemblies.Count * 6 +
            8,
            reportProgress,
            initialCompleted: 1);
        progress.Complete("Project scripts discovered.");

        progress.Begin("Building the script API profile...");
        ScriptApiProfile runtimeApi = ScriptApiCatalog.Build(includeEditor: false);
        ScriptApiProfile? editorApi = includeEditor
            ? ScriptApiCatalog.Build(includeEditor: true)
            : null;
        progress.Complete("Script API profile built.");
        progress.Begin("Resolving script references...");
        ScriptApiReferenceSet runtimeApiReferences = ScriptApiReferenceBuilder.Build(options, runtimeApi);
        ScriptApiReferenceSet? editorApiReferences = editorApi is null
            ? null
            : ScriptApiReferenceBuilder.Build(
                options,
                editorApi,
                runtimeApi,
                runtimeApiReferences);
        progress.Complete("Script references resolved.");
        ScriptDeploymentReferenceSet? deploymentReferences = null;
        if (!includeEditor)
        {
            progress.Begin("Resolving target Player runtime references...");
            deploymentReferences = ScriptDeploymentReferenceResolver.Resolve(targetRuntimeDirectory!);
            progress.Complete("Target Player runtime references resolved.");
        }
        var assemblyKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (ScriptAssemblyInput assembly in sources.assemblies)
        {
            ScriptApiReferenceSet api = assembly.scope == ScriptAssemblyScope.Editor
                ? editorApiReferences ?? throw new InvalidOperationException(
                    "A runtime deployment cannot contain an editor script assembly.")
                : runtimeApiReferences;
            assemblyKeys.Add(
                assembly.name,
                ComputeAssemblyBuildKey(
                    assembly,
                    api,
                    deploymentReferences?.fingerprint,
                    assemblyKeys));
        }
        string buildKey = ComputeGenerationBuildKey(sources, assemblyKeys);
        string outputDirectory = Path.Combine(options.outputDirectory, buildKey);
        if (TryCreateCachedResult(
                outputDirectory,
                sources,
                out ScriptCompilationResult? cachedResult))
        {
            progress.Complete("Reused cached script assembly artifact.");
            return new ScriptCompilationResult(
                true,
                cachedResult!.diagnostics,
                cachedResult.outputDirectory,
                cachedResult.reloadRequests,
                compiledAssemblies: [],
                reusedAssemblies: sources.assemblies.Select(static assembly => assembly.name).ToArray(),
                stageTimings: progress.Snapshot());
        }

        IReadOnlyList<MetadataReference> platformReferences = FrameworkReferenceResolver.CreateReferencePackReferences();

        string stagingRoot = Path.Combine(options.outputDirectory, ".staging");
        using var generationStaging = new TemporaryDirectory(stagingRoot);
        string stagingDirectory = generationStaging.path;
        var diagnostics = new List<ScriptDiagnostic>();
        var compiledAssemblies = new List<string>();
        var reusedAssemblies = new List<string>();
        progress.Begin("Preparing isolated Plugin module boundaries...");
        progress.Complete("Isolated Plugin module boundaries prepared.");

        var compiledPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (ScriptAssemblyInput assembly in sources.assemblies)
        {
            bool editor = assembly.scope == ScriptAssemblyScope.Editor;
            ScriptApiProfile api = editor
                ? editorApi ?? throw new InvalidOperationException(
                    "A runtime deployment cannot contain an editor script assembly.")
                : runtimeApi;
            ScriptApiReferenceSet apiReferences = editor
                ? editorApiReferences ?? throw new InvalidOperationException(
                    "A runtime deployment cannot contain editor API references.")
                : runtimeApiReferences;
            string assemblyPath = Path.Combine(stagingDirectory, assembly.name + ".dll");
            string assemblyCacheDirectory = Path.Combine(
                options.outputDirectory,
                ".assemblies",
                assemblyKeys[assembly.name]);
            if (TryCopyCachedAssembly(
                    assemblyCacheDirectory,
                    assembly.name,
                    stagingDirectory,
                    out ScriptDiagnostic[] cachedDiagnostics))
            {
                diagnostics.AddRange(cachedDiagnostics);
                compiledPaths.Add(assembly.name, assemblyPath);
                reusedAssemblies.Add(assembly.name);
                progress.Complete($"Reused cached {assembly.name} assembly artifact.");
                continue;
            }

            MetadataReference[] dependencyReferences = assembly.references
                .Select(reference => MetadataReference.CreateFromFile(compiledPaths[reference]))
                .ToArray();
            string assemblyStagingRoot = Path.Combine(options.outputDirectory, ".assembly-staging");
            using var assemblyStaging = new TemporaryDirectory(assemblyStagingRoot);
            string assemblyStagingDirectory = assemblyStaging.path;
            string assemblyStagingPath = Path.Combine(assemblyStagingDirectory, assembly.name + ".dll");
            CompilationResult result = await CompileAssemblyAsync(
                assembly.name,
                assembly.sources,
                api,
                apiReferences,
                platformReferences.Concat(dependencyReferences).ToArray(),
                editor ? null : deploymentReferences,
                assemblyStagingPath,
                assembly.scope,
                assembly.domain,
                string.IsNullOrEmpty(assembly.ownerPluginId)
                    ? AssetSourceId.project.value
                    : assembly.ownerPluginId,
                assembly.defines,
                assembly.nullable,
                assembly.allowUnsafe,
                progress,
                cancellationToken).ConfigureAwait(false);
            diagnostics.AddRange(result.diagnostics);
            if (!result.success)
            {
                DeleteStagingDirectory(assemblyStagingDirectory);
                DeleteStagingDirectory(stagingDirectory);
                return new ScriptCompilationResult(
                    false,
                    diagnostics,
                    outputDirectory: null,
                    reloadRequests: null,
                    stageTimings: progress.Snapshot());
            }
            File.WriteAllBytes(
                GetDiagnosticsPath(assemblyStagingDirectory),
                ScriptCompilerCacheSerialization.EncodeDiagnostics(result.diagnostics));
            CommitAssemblyCache(
                assemblyStagingDirectory,
                assemblyCacheDirectory,
                assembly.name);
            if (!TryCopyCachedAssembly(
                    assemblyCacheDirectory,
                    assembly.name,
                    stagingDirectory,
                    out _))
            {
                DeleteStagingDirectory(stagingDirectory);
                throw new IOException($"Committed script assembly cache '{assembly.name}' is incomplete.");
            }
            compiledPaths.Add(assembly.name, assemblyPath);
            compiledAssemblies.Add(assembly.name);
        }

        File.WriteAllBytes(
            GetDiagnosticsPath(stagingDirectory),
            ScriptCompilerCacheSerialization.EncodeDiagnostics(diagnostics));
        CommitGenerationCache(stagingDirectory, outputDirectory, sources);

        progress.Begin("Preparing the script reload...");
        IReadOnlyList<AssemblyLoadRequest> requests = CreateReloadRequests(
            outputDirectory,
            sources);
        progress.Complete("Script reload prepared.");
        return new ScriptCompilationResult(
            true,
            diagnostics,
            outputDirectory,
            requests,
            compiledAssemblies,
            reusedAssemblies,
            progress.Snapshot());
    }

    private static async ValueTask<CompilationResult> CompileAssemblyAsync(
        string assemblyName,
        IReadOnlyList<ScriptSourceInput> sources,
        ScriptApiProfile api,
        ScriptApiReferenceSet apiReferences,
        IReadOnlyList<MetadataReference> platformReferences,
        ScriptDeploymentReferenceSet? deploymentReferences,
        string outputPath,
        ScriptAssemblyScope scope,
        AssemblyDomain domain,
        string assetSourceId,
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
                    scope == ScriptAssemblyScope.Editor,
                    domain,
                    assetSourceId),
                Encoding.UTF8),
            parseOptions,
            $"<{assemblyName}.Generated.g.cs>",
            cancellationToken));
        progress.Complete($"Prepared generated {assemblyName} sources.");

        var validationReferences = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);
        foreach (MetadataReference reference in platformReferences)
        {
            if (!string.IsNullOrWhiteSpace(reference.Display))
                validationReferences[reference.Display!] = reference;
        }
        foreach (string referencePath in apiReferences.runtimeReferencePaths)
            validationReferences[referencePath] = MetadataReference.CreateFromFile(referencePath);

        var validationCompilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            validationReferences.Values,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug,
                checkOverflow: true,
                allowUnsafe: allowUnsafe,
                deterministic: true,
                concurrentBuild: false,
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
        IReadOnlyCollection<MetadataReference> runtimeReferences;
        if (deploymentReferences is null)
        {
            runtimeReferences = validationReferences.Values;
        }
        else
        {
            var targetReferences = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);
            foreach (MetadataReference reference in platformReferences)
            {
                if (!string.IsNullOrWhiteSpace(reference.Display))
                    targetReferences[reference.Display!] = reference;
            }
            foreach (string referencePath in deploymentReferences.paths)
                targetReferences[referencePath] = MetadataReference.CreateFromFile(referencePath);
            runtimeReferences = targetReferences.Values;
        }
        var runtimeCompilation = CSharpCompilation.Create(
            assemblyName,
            runtimeTrees,
            runtimeReferences,
            validationCompilation.Options);
        progress.Begin($"Resolving {assemblyName} script type identities...");
        ScriptTypeAnalysisResult typeAnalysis = ScriptTypeAnalyzer.Analyze(
            runtimeCompilation,
            sources,
            api.attachableTypes.ToDictionary(
                static value => value.implementationName,
                static value => value.kind,
                StringComparer.Ordinal),
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
            await File.WriteAllBytesAsync(
                GetTypeManifestPath(outputPath),
                ScriptCompilerCacheSerialization.EncodeTypeManifest(typeAnalysis.manifest),
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
        private readonly List<ScriptCompilationStageTiming> m_timings = [];
        private readonly Stopwatch m_stageStopwatch = new();
        private readonly int m_total;
        private string? m_stage;
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
            FinishCurrentStage();
            m_stage = status;
            m_stageStopwatch.Restart();
            m_report?.Invoke((float)m_completed / m_total, status);
        }

        internal void Complete(string status)
        {
            FinishCurrentStage();
            m_completed++;
            m_report?.Invoke(Math.Min(1f, (float)m_completed / m_total), status);
        }

        internal IReadOnlyList<ScriptCompilationStageTiming> Snapshot()
        {
            FinishCurrentStage();
            return m_timings.ToArray();
        }

        private void FinishCurrentStage()
        {
            if (m_stage is null)
                return;
            m_stageStopwatch.Stop();
            m_timings.Add(new ScriptCompilationStageTiming(m_stage, m_stageStopwatch.Elapsed));
            m_stage = null;
        }
    }

    private static string ComputeAssemblyBuildKey(
        ScriptAssemblyInput assembly,
        ScriptApiReferenceSet api,
        string? deploymentReferenceFingerprint,
        IReadOnlyDictionary<string, string> dependencyKeys)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHash(hash, "Inno.ScriptAssemblyArtifact.SourceOwned");
        AppendHash(hash, System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
        AppendHash(hash, Environment.Version.ToString());
        AppendHash(hash, assembly.name);
        AppendHash(hash, assembly.scope.ToString());
        AppendHash(hash, assembly.domain.ToString());
        AppendHash(hash, assembly.ownerPluginId);
        AppendHash(hash, assembly.definitionHash);
        AppendHash(hash, assembly.nullable.ToString());
        AppendHash(hash, assembly.allowUnsafe.ToString());
        foreach (string define in assembly.defines.OrderBy(static value => value, StringComparer.Ordinal))
            AppendHash(hash, define);
        foreach (ScriptSourceInput source in assembly.sources.OrderBy(static value => value.relativePath, StringComparer.Ordinal))
        {
            AppendHash(hash, source.relativePath);
            AppendHash(hash, source.contentHash);
        }
        foreach (string reference in assembly.references.OrderBy(static value => value, StringComparer.Ordinal))
        {
            AppendHash(hash, reference);
            AppendHash(hash, dependencyKeys[reference]);
        }
        AppendHash(hash, api.contractFingerprint);
        AppendHash(hash, deploymentReferenceFingerprint ?? "Authoring");
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string ComputeGenerationBuildKey(
        ScriptSourceSet sources,
        IReadOnlyDictionary<string, string> assemblyKeys)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHash(hash, "Inno.ScriptGenerationArtifact.Incremental");
        foreach (ScriptAssemblyInput assembly in sources.assemblies)
        {
            AppendHash(hash, assembly.name);
            AppendHash(hash, assemblyKeys[assembly.name]);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static bool TryCopyCachedAssembly(
        string cacheDirectory,
        string assemblyName,
        string destinationDirectory,
        out ScriptDiagnostic[] diagnostics)
    {
        diagnostics = [];
        string sourceAssemblyPath = Path.Combine(cacheDirectory, assemblyName + ".dll");
        string[] sourcePaths =
        [
            sourceAssemblyPath,
            Path.ChangeExtension(sourceAssemblyPath, ".pdb"),
            Path.ChangeExtension(sourceAssemblyPath, ".xml"),
            GetTypeManifestPath(sourceAssemblyPath)
        ];
        string diagnosticsPath = GetDiagnosticsPath(cacheDirectory);
        if (!IsCachedAssemblyComplete(cacheDirectory, assemblyName))
            return false;
        try
        {
            diagnostics = ScriptCompilerCacheSerialization.DecodeDiagnostics(
                File.ReadAllBytes(diagnosticsPath));
            Directory.CreateDirectory(destinationDirectory);
            for (int i = 0; i < sourcePaths.Length; i++)
            {
                File.Copy(
                    sourcePaths[i],
                    Path.Combine(destinationDirectory, Path.GetFileName(sourcePaths[i])),
                    overwrite: true);
            }
            TryTouchCacheDirectory(cacheDirectory);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }

    private static void CommitAssemblyCache(
        string stagingDirectory,
        string cacheDirectory,
        string assemblyName)
    {
        lock (S_CACHE_SYNC)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cacheDirectory)!);
            if (Directory.Exists(cacheDirectory))
            {
                if (IsCachedAssemblyComplete(cacheDirectory, assemblyName))
                {
                    DeleteStagingDirectory(stagingDirectory);
                    TryTouchCacheDirectory(cacheDirectory);
                    return;
                }
                DeleteStagingDirectory(cacheDirectory);
            }
            Directory.Move(stagingDirectory, cacheDirectory);
        }
    }

    private static bool IsCachedAssemblyComplete(string cacheDirectory, string assemblyName)
    {
        string assemblyPath = Path.Combine(cacheDirectory, assemblyName + ".dll");
        if (!File.Exists(assemblyPath) ||
            !File.Exists(Path.ChangeExtension(assemblyPath, ".pdb")) ||
            !File.Exists(Path.ChangeExtension(assemblyPath, ".xml")) ||
            !File.Exists(GetTypeManifestPath(assemblyPath)) ||
            !File.Exists(GetDiagnosticsPath(cacheDirectory)))
        {
            return false;
        }
        try
        {
            if (!HasExpectedAssemblyIdentity(assemblyPath, assemblyName))
                return false;
            _ = ScriptCompilerCacheSerialization.DecodeDiagnostics(
                File.ReadAllBytes(GetDiagnosticsPath(cacheDirectory)));
            ScriptTypeManifest manifest = ScriptCompilerCacheSerialization.DecodeTypeManifest(
                File.ReadAllBytes(GetTypeManifestPath(assemblyPath)));
            if (!string.Equals(
                    manifest.assemblyName,
                    assemblyName,
                    StringComparison.Ordinal))
            {
                return false;
            }
            return true;
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or
            IOException or
            BadImageFormatException)
        {
            return false;
        }
    }

    private static void CommitGenerationCache(
        string stagingDirectory,
        string outputDirectory,
        ScriptSourceSet sources)
    {
        lock (S_CACHE_SYNC)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputDirectory)!);
            if (Directory.Exists(outputDirectory))
            {
                if (TryCreateCachedResult(outputDirectory, sources, out _))
                {
                    DeleteStagingDirectory(stagingDirectory);
                    TryTouchCacheDirectory(outputDirectory);
                    return;
                }
                DeleteStagingDirectory(outputDirectory);
            }
            Directory.Move(stagingDirectory, outputDirectory);
        }
    }

    private static bool TryCreateCachedResult(
        string outputDirectory,
        ScriptSourceSet sources,
        out ScriptCompilationResult? result)
    {
        result = null;
        string diagnosticsPath = GetDiagnosticsPath(outputDirectory);
        if (!File.Exists(diagnosticsPath))
            return false;
        ScriptDiagnostic[] diagnostics;
        try
        {
            diagnostics = ScriptCompilerCacheSerialization.DecodeDiagnostics(
                File.ReadAllBytes(diagnosticsPath));
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        foreach (ScriptAssemblyInput assembly in sources.assemblies)
        {
            if (!IsCachedAssemblyComplete(outputDirectory, assembly.name))
                return false;
        }
        result = new ScriptCompilationResult(
            success: true,
            diagnostics: diagnostics,
            outputDirectory: outputDirectory,
            reloadRequests: CreateReloadRequests(
                outputDirectory,
                sources));
        TryTouchCacheDirectory(outputDirectory);
        return true;
    }

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

    private static void TryTouchCacheDirectory(string path)
    {
        try
        {
            Directory.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }
        catch (UnauthorizedAccessException)
        {
            // Reuse remains valid; cache collection can rebuild this immutable entry later.
        }
        catch (IOException)
        {
            // Reuse remains valid; cache collection can rebuild this immutable entry later.
        }
    }

    private static bool HasExpectedAssemblyIdentity(string assemblyPath, string assemblyName)
    {
        try
        {
            AssemblyName actualName = AssemblyName.GetAssemblyName(assemblyPath);
            return string.Equals(actualName.Name, assemblyName, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or BadImageFormatException)
        {
            return false;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory(string root)
        {
            Directory.CreateDirectory(root);
            path = Path.Combine(root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
        }

        internal string path { get; }

        /// <summary>
        /// Releases the resources owned by this instance.
        /// </summary>
        public void Dispose() => DeleteStagingDirectory(path);
    }

    private static string GetDiagnosticsPath(string outputDirectory)
        => Path.Combine(outputDirectory, "diagnostics.cache");

    private static string GetTypeManifestPath(string assemblyPath)
        => Path.ChangeExtension(assemblyPath, ".types.cache");

    private static void AppendHash(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }

    private static string CreateGeneratedSource(
        string assemblyName,
        bool isEditorAssembly,
        AssemblyDomain domain,
        string assetSourceId)
    {
        string assemblyScope = isEditorAssembly ? "Editor" : "Runtime";
        return $"""
            #nullable enable
            [assembly: System.Reflection.AssemblyMetadata("Inno.AssemblyDomain", "{domain}")]
            [assembly: System.Reflection.AssemblyMetadata("Inno.AssemblyScope", "{assemblyScope}")]
            [assembly: System.Reflection.AssemblyMetadata("Inno.ScriptAssembly", "{assemblyName}")]
            [assembly: System.Reflection.AssemblyMetadata("Inno.AssetSource", "{assetSourceId}")]
            """;
    }

    private static IReadOnlyList<AssemblyLoadRequest> CreateReloadRequests(
        string outputDirectory,
        ScriptSourceSet sources)
    {
        ScriptAssemblyInput[] pluginAssemblies = sources.assemblies
            .Where(static assembly => assembly.domain == AssemblyDomain.InnoPlugin)
            .ToArray();
        string AssemblyPath(string name) => Path.Combine(outputDirectory, name + ".dll");
        Dictionary<string, string> pluginModules = pluginAssemblies
            .Select(static assembly => assembly.ownerPluginId)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(
                static pluginId => pluginId,
                static pluginId => "Plugin." + pluginId,
                StringComparer.Ordinal);
        Dictionary<string, ScriptAssemblyInput> assembliesByName = sources.assemblies.ToDictionary(
            static assembly => assembly.name,
            StringComparer.OrdinalIgnoreCase);
        var requests = new List<AssemblyLoadRequest>();
        foreach (IGrouping<string, ScriptAssemblyInput> group in pluginAssemblies
                     .GroupBy(static assembly => assembly.ownerPluginId, StringComparer.Ordinal)
                     .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            ScriptAssemblyInput[] owned = group
                .OrderBy(static assembly => assembly.scope)
                .ThenBy(static assembly => assembly.name, StringComparer.Ordinal)
                .ToArray();
            ScriptAssemblyInput main = owned[0];
            string[] dependencies = owned
                .SelectMany(static assembly => assembly.references)
                .Select(reference => assembliesByName[reference].ownerPluginId)
                .Where(owner => !string.IsNullOrEmpty(owner) &&
                                !string.Equals(owner, group.Key, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .Select(owner => pluginModules[owner])
                .ToArray();
            requests.Add(new AssemblyLoadRequest
            {
                moduleName = pluginModules[group.Key],
                mainAssemblyPath = AssemblyPath(main.name),
                preloadAssemblyPaths = owned
                    .Where(assembly => !ReferenceEquals(assembly, main))
                    .Select(assembly => AssemblyPath(assembly.name))
                    .ToArray(),
                upstreamModuleNames = dependencies,
                collectible = true,
                domain = AssemblyDomain.InnoPlugin,
                scope = main.scope == ScriptAssemblyScope.Editor
                    ? AssemblyScope.Editor
                    : AssemblyScope.Runtime,
                assemblyScopes = owned.ToDictionary(
                    static assembly => assembly.name,
                    static assembly => assembly.scope == ScriptAssemblyScope.Editor
                        ? AssemblyScope.Editor
                        : AssemblyScope.Runtime,
                    StringComparer.OrdinalIgnoreCase)
            });
        }
        ScriptAssemblyInput[] runtimeAssemblies = sources.assemblies
            .Where(static assembly => assembly.domain == AssemblyDomain.InnoScripting
                && assembly.scope == ScriptAssemblyScope.Runtime)
            .ToArray();
        ScriptAssemblyInput[] editorAssemblies = sources.assemblies
            .Where(static assembly => assembly.domain == AssemblyDomain.InnoScripting
                && assembly.scope == ScriptAssemblyScope.Editor)
            .ToArray();
        string[] pluginModuleNames = pluginModules.Values
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        requests.Add(
            new AssemblyLoadRequest
            {
                moduleName = C_RUNTIME_MODULE_NAME,
                mainAssemblyPath = AssemblyPath(C_GAME_ASSEMBLY_NAME),
                preloadAssemblyPaths = runtimeAssemblies
                    .Where(static assembly => assembly.name != C_GAME_ASSEMBLY_NAME)
                    .Select(assembly => AssemblyPath(assembly.name))
                    .ToArray(),
                upstreamModuleNames = pluginModuleNames,
                collectible = true,
                domain = AssemblyDomain.InnoScripting,
                scope = AssemblyScope.Runtime
            });
        if (sources.includesEditor)
        {
            requests.Add(
                new AssemblyLoadRequest
                {
                    moduleName = C_EDITOR_MODULE_NAME,
                    mainAssemblyPath = AssemblyPath(C_EDITOR_ASSEMBLY_NAME),
                    preloadAssemblyPaths = editorAssemblies
                        .Where(static assembly => assembly.name != C_EDITOR_ASSEMBLY_NAME)
                        .Select(assembly => AssemblyPath(assembly.name))
                        .ToArray(),
                    upstreamModuleNames = pluginModuleNames.Concat([C_RUNTIME_MODULE_NAME]).ToArray(),
                    collectible = true,
                    domain = AssemblyDomain.InnoScripting,
                    scope = AssemblyScope.Editor,
                    assemblyScopes = editorAssemblies.ToDictionary(
                        static assembly => assembly.name,
                        static _ => AssemblyScope.Editor,
                        StringComparer.OrdinalIgnoreCase)
                });
        }
        return requests;
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

        /// <summary>
        /// Gets the normalized path used by the current operation.
        /// </summary>
        public override string Path { get; }

        /// <summary>
        /// Retrieves the requested text value from current authoritative state.
        /// </summary>
        /// <param name="cancellationToken">
        /// The token that cancels the operation before it commits.
        /// </param>
        /// <returns>
        /// The validated source text that represents the completed operation.
        /// </returns>
        public override SourceText GetText(CancellationToken cancellationToken = default)
            => m_text;
    }

    private sealed class DiagnosticComparer : IEqualityComparer<Diagnostic>
    {
        internal static readonly DiagnosticComparer Instance = new();

        /// <summary>
        /// Determines whether this value and the supplied value represent the same logical state.
        /// </summary>
        /// <param name="left">
        /// The left consumed by equals; ownership remains with the caller unless explicitly stated otherwise.
        /// </param>
        /// <param name="right">
        /// The right consumed by equals; ownership remains with the caller unless explicitly stated otherwise.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
        /// </returns>
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

        /// <summary>
        /// Computes a hash code consistent with the implemented equality contract.
        /// </summary>
        /// <param name="diagnostic">
        /// The diagnostic consumed by get hash code; ownership remains with the caller unless explicitly stated otherwise.
        /// </param>
        /// <returns>
        /// The scalar result calculated from the supplied inputs.
        /// </returns>
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
