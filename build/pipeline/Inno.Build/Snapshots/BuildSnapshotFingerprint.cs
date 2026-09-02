using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Inno.Plugins.Authoring;

namespace Inno.Build;

internal static class BuildSnapshotFingerprint
{
    internal static async ValueTask<string> ComputeAsync(
        long assetRevision,
        IReadOnlyList<string> runtimeAssemblies,
        IReadOnlyList<PluginCandidate> plugins,
        ReadOnlyMemory<byte> projectSettings,
        CancellationToken cancellationToken)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, assetRevision.ToString(System.Globalization.CultureInfo.InvariantCulture));
        hash.AppendData(projectSettings.Span);
        foreach (PluginCandidate plugin in plugins)
        {
            Append(hash, plugin.manifest.pluginId);
            Append(hash, plugin.contentHash);
        }
        foreach (string assembly in runtimeAssemblies.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Append(hash, Path.GetFileName(assembly));
            await using FileStream stream = new(
                assembly,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            byte[] fileHash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            hash.AppendData(fileHash);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    internal static bool MatchesPlugins(
        IReadOnlyList<PluginCandidate> expected,
        IReadOnlyList<PluginCandidate> actual)
    {
        if (expected.Count != actual.Count)
            return false;
        for (int index = 0; index < expected.Count; index++)
        {
            if (!string.Equals(expected[index].manifest.pluginId, actual[index].manifest.pluginId, StringComparison.Ordinal)
                || !string.Equals(expected[index].contentHash, actual[index].contentHash, StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    private static void Append(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }
}
