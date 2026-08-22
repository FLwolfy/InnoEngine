using System;

using Inno.Core.Diagnose;

namespace Inno.Assets;

internal static class AssetManagerDiagnosticPublisher
{
    private const string C_SOURCE_DATABASE_GROUP = "Asset Source Database";

    internal static void PublishSourceDatabaseFailure(
        Exception refreshException,
        Exception recoveryException)
    {
        ArgumentNullException.ThrowIfNull(refreshException);
        ArgumentNullException.ThrowIfNull(recoveryException);
        Diagnostics.Set(
            C_SOURCE_DATABASE_GROUP,
            Diagnostic.Error(
                "ASSET-SOURCE",
                $"Incremental refresh failed: {refreshException.Message} " +
                $"Recovery rescan failed: {recoveryException.Message}"));
    }

    internal static void ResolveSourceDatabase()
        => Diagnostics.Clear(C_SOURCE_DATABASE_GROUP);
}
