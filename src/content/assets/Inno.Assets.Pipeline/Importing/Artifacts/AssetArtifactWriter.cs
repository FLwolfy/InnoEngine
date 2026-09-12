using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Inno.Assets.Pipeline;

/// <summary>
/// Collects immutable named outputs for an aggregate asset build.
/// </summary>
public sealed class AssetArtifactWriter
{
    private readonly Dictionary<string, ReadOnlyMemory<byte>> m_outputs =
        new(StringComparer.Ordinal);
    private readonly List<string> m_diagnostics = [];
    private readonly HashSet<string> m_authoringOutputs = new(StringComparer.Ordinal);

    /// <summary>
    /// Writes one named build output.
    /// </summary>
    /// <param name="outputName">
    /// The stable output name.
    /// </param>
    /// <param name="bytes">
    /// The output content.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation for the write operation.
    /// </param>
    /// <param name="deploymentScope">Whether runtime packages retain this output.</param>
    /// <returns>
    /// A completed operation after the output has been staged.
    /// </returns>
    public ValueTask WriteAsync(
        string outputName,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken = default,
        AssetDeploymentScope deploymentScope = AssetDeploymentScope.Runtime)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(outputName))
            throw new ArgumentException("An artifact output name is required.", nameof(outputName));
        if (!Enum.IsDefined(deploymentScope) || (deploymentScope == AssetDeploymentScope.AuthoringOnly && outputName is "runtime" or "asset-state"))
            throw new ArgumentException("The output deployment scope is invalid.", nameof(deploymentScope));
        if (!m_outputs.TryAdd(outputName, bytes.ToArray()))
            throw new InvalidOperationException($"Artifact output '{outputName}' was written more than once.");
        if (deploymentScope == AssetDeploymentScope.AuthoringOnly) m_authoringOutputs.Add(outputName);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Adds a build diagnostic.
    /// </summary>
    /// <param name="message">
    /// The diagnostic message.
    /// </param>
    public void ReportDiagnostic(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("A diagnostic message is required.", nameof(message));
        m_diagnostics.Add(message);
    }

    internal IReadOnlyDictionary<string, ReadOnlyMemory<byte>> outputs => m_outputs;
    internal IReadOnlyList<string> diagnostics => m_diagnostics;
    internal IReadOnlySet<string> authoringOutputs => m_authoringOutputs;
}
