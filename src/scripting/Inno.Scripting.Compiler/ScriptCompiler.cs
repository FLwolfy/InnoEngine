using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Plugins.Authoring;

namespace Inno.Scripting.Compiler;

/// <summary>
/// Produces deterministic runtime and editor script artifacts from one authoring snapshot.
/// </summary>
public sealed class ScriptCompiler
{
    private readonly AssetPipeline m_assets;
    private readonly ScriptArtifactCache m_artifacts;
    private readonly ScriptCompilerOptions m_options;
    private readonly PluginEnvironment m_plugins;
    /// <summary>
    /// Creates a compiler over explicit authoring services owned by one host.
    /// </summary>
    /// <param name="options">
    /// The project and derived-cache locations used by compilation.
    /// </param>
    /// <param name="assets">
    /// The authoring asset pipeline supplying committed source artifacts.
    /// </param>
    /// <param name="plugins">
    /// The Plugin environment supplying the candidate Plugin source generation.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when an owner argument is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the project root is empty.
    /// </exception>
    public ScriptCompiler(
        ScriptCompilerOptions options,
        AssetPipeline assets,
        PluginEnvironment plugins)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.projectRootDirectory);
        m_options = new ScriptCompilerOptions
        {
            projectRootDirectory = System.IO.Path.GetFullPath(options.projectRootDirectory)
        };
        m_assets = assets;
        m_plugins = plugins;
        m_artifacts = new ScriptArtifactCache(m_options.outputDirectory);
    }

    /// <summary>
    /// Regenerates IDE project files from the current source graph and an optional validated binary generation.
    /// </summary>
    /// <param name="referenceGeneration">
    /// The successful generation whose Plugin binaries should be referenced, or <see langword="null"/>
    /// when no validated generation is available.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the authoring asset pipeline is not initialized.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="referenceGeneration"/> did not compile successfully.
    /// </exception>
    public void GenerateProjectFiles(ScriptCompilationResult? referenceGeneration = null)
    {
        if (referenceGeneration is { success: false })
        {
            throw new ArgumentException(
                "IDE Plugin references require a successful script generation.",
                nameof(referenceGeneration));
        }
        ScriptProjectGenerator.Generate(
            m_options,
            m_assets,
            m_plugins,
            referenceGeneration);
    }

    /// <summary>
    /// Compiles a complete runtime and editor candidate generation without activating it.
    /// </summary>
    /// <param name="progress">
    /// Optional observer for monotonic compiler progress.
    /// </param>
    /// <param name="cancellationToken">
    /// The token that cancels compilation before an artifact generation is committed.
    /// </param>
    /// <returns>
    /// The immutable diagnostics and artifacts produced by the compilation attempt.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is canceled.
    /// </exception>
    public ValueTask<ScriptCompilationResult> CompileAuthoringGenerationAsync(
        IProgress<ScriptCompilationProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => ScriptCompilerEngine.CompileAsync(
            m_options,
            m_assets,
            m_plugins,
            includeEditor: true,
            targetRuntimeDirectory: null,
            progress is null
                ? null
                : (fraction, stage) => progress.Report(new ScriptCompilationProgress(fraction, stage)),
            cancellationToken);

    /// <summary>
    /// Compiles only the runtime assembly closure required by a deployed Player and binds it to that Player runtime.
    /// </summary>
    /// <param name="targetRuntimeDirectory">
    /// The verified Player Support Pack directory whose runtime assemblies will execute the emitted scripts.
    /// </param>
    /// <param name="progress">
    /// Optional observer for monotonic compiler progress.
    /// </param>
    /// <param name="cancellationToken">
    /// The token that cancels compilation before an artifact generation is committed.
    /// </param>
    /// <returns>
    /// The immutable diagnostics and runtime-only artifacts produced by the compilation attempt.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is canceled.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="targetRuntimeDirectory"/> is empty.
    /// </exception>
    /// <exception cref="DirectoryNotFoundException">
    /// Thrown when <paramref name="targetRuntimeDirectory"/> does not exist.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// Thrown when the target runtime has no valid Inno runtime assembly closure.
    /// </exception>
    public ValueTask<ScriptCompilationResult> CompileRuntimeDeploymentAsync(
        string targetRuntimeDirectory,
        IProgress<ScriptCompilationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRuntimeDirectory);
        return ScriptCompilerEngine.CompileAsync(
            m_options,
            m_assets,
            m_plugins,
            includeEditor: false,
            targetRuntimeDirectory: targetRuntimeDirectory,
            progress is null
                ? null
                : (fraction, stage) => progress.Report(new ScriptCompilationProgress(fraction, stage)),
            cancellationToken);
    }

    /// <summary>
    /// Removes unreferenced compiler generations while retaining the supplied active directories.
    /// </summary>
    /// <param name="retainedDirectories">
    /// The generation directories that remain reachable from active runtime sessions.
    /// </param>
    /// <returns>
    /// The number of obsolete generation directories removed from the compiler cache.
    /// </returns>
    public int CollectArtifacts(IEnumerable<string?> retainedDirectories)
    {
        ArgumentNullException.ThrowIfNull(retainedDirectories);
        return m_artifacts.Collect(retainedDirectories);
    }
}
