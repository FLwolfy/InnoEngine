using System;

using Inno.Core.Diagnostics;

namespace Inno.Assets.Pipeline;

internal sealed class AssetPipelineDiagnosticPublisher
{
    private const string C_SOURCE_DATABASE_GROUP = "Asset Source Database";
    private static readonly DiagnosticSource S_SOURCE_DATABASE = new(
        "inno.assets.pipeline/source-database",
        C_SOURCE_DATABASE_GROUP);

    private readonly DiagnosticHub m_diagnostics;

    internal AssetPipelineDiagnosticPublisher(DiagnosticHub diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        m_diagnostics = diagnostics;
    }

    internal void PublishSourceDatabaseFailure(
        Exception refreshException,
        Exception recoveryException)
    {
        ArgumentNullException.ThrowIfNull(refreshException);
        ArgumentNullException.ThrowIfNull(recoveryException);
        m_diagnostics.Set(
            S_SOURCE_DATABASE,
            [
            Diagnostic.Error(
                "ASSET-SOURCE",
                $"Incremental refresh failed: {refreshException.Message} " +
                $"Recovery rescan failed: {recoveryException.Message}")
            ]);
    }

    internal void ResolveSourceDatabase()
        => m_diagnostics.Clear(S_SOURCE_DATABASE);
}
