using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
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
        long generation,
        Action<float, string>? reportProgress,
        CancellationToken cancellationToken)
    {
        reportProgress?.Invoke(0f, "Discovering project scripts...");
        ScriptSourceSet sources = ScriptSourceSet.Discover(options.assetDirectory);
        var progress = new CompilationProgress(
            sources.gameSources.Count + sources.editorSources.Count + 14,
            reportProgress,
            initialCompleted: 1);
        progress.Complete("Project scripts discovered.");
        string outputDirectory = Path.Combine(
            options.outputDirectory,
            generation.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(outputDirectory);

        var diagnostics = new List<ScriptDiagnostic>();
        progress.Begin("Copying script plugins...");
        if (!TryCopyPlugins(sources, outputDirectory, diagnostics, out string[] runtimePlugins, out string[] editorPlugins))
            return new ScriptCompilationResult(false, diagnostics, outputDirectory, loadRequest: null);
        progress.Complete("Script plugins copied.");

        progress.Begin("Building the script API profile...");
        ScriptApiProfile runtimeApi = ScriptPluginMetadata.AddGlobalUsings(
            ScriptApiCatalog.Build(includeEditor: false),
            runtimePlugins);
        ScriptApiProfile editorApi = ScriptPluginMetadata.AddGlobalUsings(
            ScriptApiCatalog.Build(includeEditor: true),
            runtimePlugins.Concat(editorPlugins));
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
        string gameAssemblyPath = Path.Combine(outputDirectory, C_GAME_ASSEMBLY_NAME + ".dll");
        string editorAssemblyPath = Path.Combine(outputDirectory, C_EDITOR_ASSEMBLY_NAME + ".dll");

        CompilationResult gameResult = await CompileAssemblyAsync(
            C_GAME_ASSEMBLY_NAME,
            sources.gameSources,
            runtimeApi,
            runtimeApiReferences,
            platformReferences,
            runtimePlugins,
            gameAssemblyPath,
            progress,
            cancellationToken).ConfigureAwait(false);
        diagnostics.AddRange(gameResult.diagnostics);
        if (!gameResult.success)
            return new ScriptCompilationResult(false, diagnostics, outputDirectory, loadRequest: null);

        MetadataReference gameReference = MetadataReference.CreateFromFile(gameAssemblyPath);
        CompilationResult editorResult = await CompileAssemblyAsync(
            C_EDITOR_ASSEMBLY_NAME,
            sources.editorSources,
            editorApi,
            editorApiReferences,
            platformReferences.Concat([gameReference]).ToArray(),
            runtimePlugins.Concat(editorPlugins).ToArray(),
            editorAssemblyPath,
            progress,
            cancellationToken).ConfigureAwait(false);
        diagnostics.AddRange(editorResult.diagnostics);
        if (!editorResult.success)
            return new ScriptCompilationResult(false, diagnostics, outputDirectory, loadRequest: null);

        progress.Begin("Preparing the script reload...");
        var preloadPaths = new List<string> { editorAssemblyPath };
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
        IReadOnlyList<string> sourcePaths,
        ScriptApiProfile api,
        ScriptApiReferenceSet apiReferences,
        IReadOnlyList<MetadataReference> platformReferences,
        IReadOnlyList<string> pluginPaths,
        string outputPath,
        CompilationProgress progress,
        CancellationToken cancellationToken)
    {
        var parseOptions = new CSharpParseOptions(
            LanguageVersion.Latest,
            DocumentationMode.Parse,
            SourceCodeKind.Regular,
            preprocessorSymbols: ["DEBUG", "TRACE"]);
        var syntaxTrees = new List<SyntaxTree>(sourcePaths.Count + 1);
        for (int sourceIndex = 0; sourceIndex < sourcePaths.Count; sourceIndex++)
        {
            string sourcePath = sourcePaths[sourceIndex];
            progress.Begin($"Parsing {assemblyName} sources ({sourceIndex + 1}/{sourcePaths.Count})...");
            string source = await File.ReadAllTextAsync(sourcePath, cancellationToken).ConfigureAwait(false);
            syntaxTrees.Add(CSharpSyntaxTree.ParseText(
                SourceText.From(source, Encoding.UTF8),
                parseOptions,
                sourcePath,
                cancellationToken));
            progress.Complete($"Parsed {assemblyName} source {sourceIndex + 1}/{sourcePaths.Count}.");
        }
        progress.Begin($"Preparing generated {assemblyName} sources...");
        syntaxTrees.Add(CSharpSyntaxTree.ParseText(
            SourceText.From(
                CreateGeneratedSource(
                    assemblyName,
                    api.globalUsings,
                    string.Equals(assemblyName, C_EDITOR_ASSEMBLY_NAME, StringComparison.Ordinal)),
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
                allowUnsafe: true,
                deterministic: true,
                concurrentBuild: true,
                nullableContextOptions: NullableContextOptions.Enable));
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

        var usingRewriter = new ScriptApiUsingRewriter(api.namespaceMappings);
        var propertyOrderRewriter = new SerializablePropertyOrderRewriter();
        SyntaxTree[] runtimeTrees = syntaxTrees
            .Select(tree => CSharpSyntaxTree.Create(
                (CSharpSyntaxNode)usingRewriter.Visit(
                    propertyOrderRewriter.Visit(tree.GetRoot(cancellationToken)))!,
                parseOptions,
                tree.FilePath,
                Encoding.UTF8))
            .ToArray();
        if (usingRewriter.additionalGlobalUsings.Count > 0)
        {
            string additionalUsings = string.Join(
                Environment.NewLine,
                usingRewriter.additionalGlobalUsings.Select(static value => $"global using global::{value};"));
            runtimeTrees = runtimeTrees
                .Append(CSharpSyntaxTree.ParseText(
                    SourceText.From(additionalUsings, Encoding.UTF8),
                    parseOptions,
                    $"<{assemblyName}.ScriptApiUsings.g.cs>",
                    cancellationToken))
                .ToArray();
        }
        var runtimeCompilation = CSharpCompilation.Create(
            assemblyName,
            runtimeTrees,
            references.Values,
            validationCompilation.Options);
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
                preEmitDiagnostics.Select(ToDiagnostic).ToArray());
        }

        string pdbPath = Path.ChangeExtension(outputPath, ".pdb");
        progress.Begin($"Emitting {assemblyName}...");
        await using FileStream assemblyStream = File.Create(outputPath);
        await using FileStream pdbStream = File.Create(pdbPath);
        EmitResult emit = runtimeCompilation.Emit(
            assemblyStream,
            pdbStream,
            options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb),
            cancellationToken: cancellationToken);
        progress.Complete($"Emitted {assemblyName}.");
        ScriptDiagnostic[] diagnostics = preEmitDiagnostics
            .Concat(emit.Diagnostics)
            .Where(static diagnostic => diagnostic.Severity != DiagnosticSeverity.Hidden)
            .Distinct(DiagnosticComparer.Instance)
            .Select(ToDiagnostic)
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

        string[] CopySet(IReadOnlyList<string> sourcePaths)
        {
            var result = new List<string>(sourcePaths.Count);
            foreach (string sourcePath in sourcePaths)
            {
                try
                {
                    AssemblyName assemblyName = AssemblyName.GetAssemblyName(sourcePath);
                    string simpleName = assemblyName.Name
                        ?? throw new BadImageFormatException("Managed assembly has no simple name.");
                    if (copiedByName.TryGetValue(simpleName, out string? existing))
                    {
                        diagnostics.Add(new ScriptDiagnostic(
                            "INNO1001",
                            ScriptDiagnosticSeverity.Error,
                            $"Plugin assembly name '{simpleName}' is duplicated by '{existing}' and '{sourcePath}'.",
                            sourcePath,
                            0,
                            0));
                        continue;
                    }

                    string destinationPath = Path.Combine(outputDirectory, simpleName + ".dll");
                    File.Copy(sourcePath, destinationPath, overwrite: true);
                    CopyCompanion(sourcePath, destinationPath, ".pdb");
                    CopyCompanion(sourcePath, destinationPath, ".deps.json");
                    copiedByName.Add(simpleName, sourcePath);
                    result.Add(destinationPath);
                }
                catch (Exception exception) when (exception is BadImageFormatException or FileLoadException or IOException)
                {
                    diagnostics.Add(new ScriptDiagnostic(
                        "INNO1000",
                        ScriptDiagnosticSeverity.Error,
                        $"Plugin '{sourcePath}' is not a readable managed assembly: {exception.Message}",
                        sourcePath,
                        0,
                        0));
                }
            }
            return result.ToArray();
        }
    }

    private static void CopyCompanion(string sourcePath, string destinationPath, string extension)
    {
        string source = Path.ChangeExtension(sourcePath, extension);
        if (File.Exists(source))
            File.Copy(source, Path.ChangeExtension(destinationPath, extension), overwrite: true);
    }

    private static string CreateGeneratedSource(
        string assemblyName,
        IReadOnlyList<string> globalUsings,
        bool isEditorAssembly)
    {
        string usings = string.Join(Environment.NewLine, globalUsings.Select(static value => $"global using {value};"));
        string assemblyGroup = isEditorAssembly ? "Editor" : "Game";
        return $"""
            #nullable enable
            {usings}
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
