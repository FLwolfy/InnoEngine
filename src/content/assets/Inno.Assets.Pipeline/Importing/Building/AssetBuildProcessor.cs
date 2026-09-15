using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets;

namespace Inno.Assets.Pipeline;

/// <summary>
/// Declares the immutable cache protocol identity of an automatically discovered asset build processor.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AssetBuildProcessorAttribute : Attribute
{
    /// <summary>
    /// Creates build-processor discovery metadata.
    /// </summary>
    /// <param name="id">Globally stable build processor identifier.</param>
    public AssetBuildProcessorAttribute(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        this.id = id.Trim();
    }

    /// <summary>
    /// Gets the globally stable build processor identifier.
    /// </summary>
    public string id { get; }
}

/// <summary>
/// Defines an automatically discovered aggregate asset build processor.
/// </summary>
public abstract class AssetBuildProcessor
{
    private string? m_processorId;

    /// <summary>
    /// Gets the stable processor identifier used by build cache keys.
    /// </summary>
    public string processorId => m_processorId
        ?? throw new InvalidOperationException(
            $"Asset build processor '{GetType().FullName}' has not been bound to discovery metadata.");

    /// <summary>
    /// Gets the definition type accepted by this processor.
    /// </summary>
    public abstract Type definitionType { get; }

    internal abstract ValueTask BuildInternalAsync(
        AssetObject definition,
        IReadOnlyList<AssetInfo> inputs,
        AssetArtifactWriter output,
        CancellationToken cancellationToken);

    internal void BindProcessorId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (m_processorId is not null && !string.Equals(m_processorId, id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Asset build processor '{GetType().FullName}' cannot be bound to more than one protocol ID.");
        }
        m_processorId = id;
    }
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
