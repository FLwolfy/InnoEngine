using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Plugins.Authoring;
using Inno.Core.Serialization;
using Inno.Core.Settings;
using Inno.Scripting.Compiler;

namespace Inno.Build;

/// <summary>
/// Orchestrates deterministic Plugin and game builds over isolated staging and atomic commits.
/// </summary>
public sealed class BuildPipeline
{
    private readonly GameBuildPipeline m_game;
    private readonly PluginPackageBuilder m_plugins;
    private readonly PlayerSupportPackCatalog m_supportPacks;

    /// <summary>
    /// Creates a build pipeline from installed Player Support Packs and platform packagers.
    /// </summary>
    /// <param name="supportPackRoot">
    /// The directory containing one child directory per <see cref="BuildTargetId"/>.
    /// </param>
    /// <param name="assets">
    /// The active authoring asset pipeline captured by builds.
    /// </param>
    /// <param name="plugins">
    /// The active Plugin environment captured by builds.
    /// </param>
    /// <param name="settings">
    /// The current project settings store captured by builds.
    /// </param>
    /// <param name="serialization">
    /// The serialization registry used to pin and encode the build snapshot.
    /// </param>
    /// <param name="compiler">
    /// The project compiler used to produce a fresh runtime-only assembly generation for each game build.
    /// </param>
    /// <param name="gameTargets">
    /// The complete set of replaceable platform package implementations available to this host.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when the Support Pack root is empty or a target identity is duplicated.
    /// </exception>
    public BuildPipeline(
        AssetPipeline assets,
        PluginEnvironment plugins,
        ProjectSettingsStore settings,
        SerializationRegistry serialization,
        ScriptCompiler compiler,
        string supportPackRoot,
        IEnumerable<IGameBuildTarget> gameTargets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(serialization);
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentException.ThrowIfNullOrWhiteSpace(supportPackRoot);
        ArgumentNullException.ThrowIfNull(gameTargets);
        IGameBuildTarget[] targets = gameTargets.ToArray();
        if (targets.Any(static value => value is null))
            throw new ArgumentException("Game target collection cannot contain null values.", nameof(gameTargets));
        IGrouping<BuildTargetId, IGameBuildTarget>? duplicate = targets
            .GroupBy(static value => value.id)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Game target '{duplicate.Key}' is registered more than once.", nameof(gameTargets));
        m_supportPacks = new PlayerSupportPackCatalog(supportPackRoot);
        m_game = new GameBuildPipeline(
            assets,
            plugins,
            settings,
            serialization,
            compiler,
            targets.ToDictionary(static value => value.id),
            m_supportPacks);
        m_plugins = new PluginPackageBuilder(
            assets,
            plugins,
            settings,
            serialization);
    }

    /// <summary>
    /// Builds and atomically commits one source-free game deployment.
    /// </summary>
    /// <param name="request">
    /// The exact profile, destination, and activated runtime compilation generation.
    /// </param>
    /// <param name="progress">
    /// Optional observer for monotonic stage progress.
    /// </param>
    /// <param name="cancellationToken">
    /// The token that cancels work before atomic commit.
    /// </param>
    /// <returns>
    /// The durable output identity and deployment metrics.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown when cancellation is requested before commit.
    /// </exception>
    public ValueTask<BuildResult> BuildGameAsync(
        GameBuildRequest request,
        IProgress<BuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => m_game.BuildAsync(request, progress, cancellationToken);

    /// <summary>
    /// Builds and atomically commits one deterministic project Plugin package.
    /// </summary>
    /// <param name="request">
    /// The package identity, destination, and dependency embedding policy.
    /// </param>
    /// <param name="progress">
    /// Optional observer for monotonic stage progress.
    /// </param>
    /// <param name="cancellationToken">
    /// The token that cancels work before atomic commit.
    /// </param>
    /// <returns>
    /// The durable package identity and source-content metrics.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown when cancellation is requested before commit.
    /// </exception>
    public ValueTask<BuildResult> BuildPluginAsync(
        PluginBuildRequest request,
        IProgress<BuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => m_plugins.BuildAsync(request, progress, cancellationToken);
}
