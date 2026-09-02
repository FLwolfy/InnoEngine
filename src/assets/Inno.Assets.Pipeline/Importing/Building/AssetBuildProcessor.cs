using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets;

namespace Inno.Assets.Pipeline;

/// <summary>
/// Defines an automatically discovered aggregate asset build processor.
/// </summary>
public abstract class AssetBuildProcessor
{
    /// <summary>
    /// Gets the stable processor identifier used by build cache keys.
    /// </summary>
    public virtual string processorId => GetType().FullName ?? GetType().Name;

    /// <summary>
    /// Gets the definition type accepted by this processor.
    /// </summary>
    public abstract Type definitionType { get; }

    internal abstract ValueTask BuildInternalAsync(
        AssetObject definition,
        IReadOnlyList<AssetInfo> inputs,
        AssetArtifactWriter output,
        CancellationToken cancellationToken);
}

/// <summary>
/// Provides a strongly typed aggregate asset build processor.
/// </summary>
/// <typeparam name="TDefinition">
/// The definition asset type.
/// </typeparam>
public abstract class AssetBuildProcessor<TDefinition> : AssetBuildProcessor
    where TDefinition : AssetObject
{
    /// <summary>
    /// Gets the concrete type handled by this extension implementation.
    /// </summary>
    public sealed override Type definitionType => typeof(TDefinition);

    /// <summary>
    /// Builds immutable outputs from a consistent asset snapshot.
    /// </summary>
    /// <param name="context">
    /// The build context.
    /// </param>
    /// <param name="output">
    /// The candidate output writer.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation for the build.
    /// </param>
    /// <returns>
    /// An operation that completes when all outputs are staged.
    /// </returns>
    protected abstract ValueTask BuildAsync(
        AssetBuildContext<TDefinition> context,
        AssetArtifactWriter output,
        CancellationToken cancellationToken);

    internal sealed override ValueTask BuildInternalAsync(
        AssetObject definition,
        IReadOnlyList<AssetInfo> inputs,
        AssetArtifactWriter output,
        CancellationToken cancellationToken)
    {
        if (definition is not TDefinition typed)
        {
            throw new ArgumentException(
                $"Build definition must be assignable to '{typeof(TDefinition).FullName}'.",
                nameof(definition));
        }
        return BuildAsync(new AssetBuildContext<TDefinition>(typed, inputs), output, cancellationToken);
    }
}
