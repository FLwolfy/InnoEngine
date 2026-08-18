using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Inno.Core.Assemblies;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
        CancellationToken cancellationToken)
    {
        ScriptSourceSet sources = ScriptSourceSet.Discover(options.assetDirectory);
        string outputDirectory = Path.Combine(
            options.outputDirectory,
            generation.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(outputDirectory);

        var diagnostics = new List<ScriptDiagnostic>();
        if (!TryCopyPlugins(sources, outputDirectory, diagnostics, out string[] runtimePlugins, out string[] editorPlugins))
            return new ScriptCompilationResult(false, diagnostics, outputDirectory, loadRequest: null);

        ScriptApiProfile runtimeApi = ScriptPluginMetadata.AddGlobalUsings(
            ScriptApiCatalog.Build(includeEditor: false),
            runtimePlugins);
        ScriptApiProfile editorApi = ScriptPluginMetadata.AddGlobalUsings(
            ScriptApiCatalog.Build(includeEditor: true),
            runtimePlugins.Concat(editorPlugins));
        ScriptApiReferenceSet runtimeApiReferences = ScriptApiReferenceBuilder.Build(options, runtimeApi);
        ScriptApiReferenceSet editorApiReferences = ScriptApiReferenceBuilder.Build(options, editorApi);
        IReadOnlyList<MetadataReference> platformReferences = FrameworkReferenceResolver.CreateRuntimeReferences();
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
            cancellationToken).ConfigureAwait(false);
        diagnostics.AddRange(editorResult.diagnostics);
        if (!editorResult.success)
            return new ScriptCompilationResult(false, diagnostics, outputDirectory, loadRequest: null);

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
        CancellationToken cancellationToken)
    {
        var parseOptions = new CSharpParseOptions(
            LanguageVersion.Latest,
            DocumentationMode.Parse,
            SourceCodeKind.Regular);
        var syntaxTrees = new List<SyntaxTree>(sourcePaths.Count + 1);
        foreach (string sourcePath in sourcePaths)
        {
            string source = await File.ReadAllTextAsync(sourcePath, cancellationToken).ConfigureAwait(false);
            syntaxTrees.Add(CSharpSyntaxTree.ParseText(
                SourceText.From(source, Encoding.UTF8),
                parseOptions,
                sourcePath,
                cancellationToken));
        }
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

        var references = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);
        foreach (MetadataReference reference in platformReferences)
        {
            if (!string.IsNullOrWhiteSpace(reference.Display))
                references[reference.Display!] = reference;
        }
        foreach (string referencePath in apiReferences.referencePaths)
            references[referencePath] = MetadataReference.CreateFromFile(referencePath);
        foreach (string pluginPath in pluginPaths)
            references[pluginPath] = MetadataReference.CreateFromFile(pluginPath);

        var compilation = CSharpCompilation.Create(
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
        string pdbPath = Path.ChangeExtension(outputPath, ".pdb");
        await using FileStream assemblyStream = File.Create(outputPath);
        await using FileStream pdbStream = File.Create(pdbPath);
        EmitResult emit = compilation.Emit(
            assemblyStream,
            pdbStream,
            options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb),
            cancellationToken: cancellationToken);
        ScriptDiagnostic[] diagnostics = emit.Diagnostics
            .Where(static diagnostic => diagnostic.Severity != DiagnosticSeverity.Hidden)
            .Select(ToDiagnostic)
            .ToArray();
        return new CompilationResult(emit.Success, diagnostics);
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
}
