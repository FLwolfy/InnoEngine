using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Inno.Assets;

/// <summary>
/// Separates immutable artifact preparation from owner-thread asset materialization.
/// </summary>
public sealed partial class AssetDatabase
{
    private const int C_MAX_PENDING_LOADS = 32;
    private readonly int m_ownerThread = Environment.CurrentManagedThreadId;
    private readonly List<PendingAcquisition> m_pendingLoads = [];
    private readonly Dictionary<Guid, SharedPreparation> m_preparations = [];
    private readonly Dictionary<PayloadRead, SharedPayload> m_payloadReads = [];
    private readonly SemaphoreSlim m_payloadReadSlots = new(4, 4);
    private long m_preparingBytes;
    private long m_peakPreparingBytes;
    private int m_peakPendingLoads;
    private long m_rejectedLoads;
    private long m_payloadReadsStarted;
    private long m_payloadReadsShared;

    /// <summary>
    /// Gets immutable cold-load admission, unique IO and peak reservation counters for this database lifetime.
    /// </summary>
    public AssetPreparationStatistics preparationStatistics
    {
        get
        {
            lock (m_sync)
                return new(m_pendingLoads.Count, m_peakPendingLoads, m_rejectedLoads, m_payloadReads.Count,
                    m_payloadReadsStarted, m_payloadReadsShared, m_preparingBytes, m_peakPreparingBytes);
        }
    }

    /// <summary>
    /// Gets encoded bytes reserved by cold-load closures, including completed IO awaiting owner publication.
    /// </summary>
    public long preparingBytes
    {
        get { lock (m_sync) return m_preparingBytes; }
    }

    /// <summary>
    /// Publishes completed cold loads on the database owner thread without waiting for outstanding IO.
    /// </summary>
    /// <param name="completionBudget">
    /// The maximum number of ready requests committed at this frame safety point.
    /// </param>
    /// <returns>
    /// The number of requests completed, failed, or canceled during this call.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The caller is not the owner thread that created this database.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// The database has already retired.
    /// </exception>
    public int CompletePendingLoads(int completionBudget = 8)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(completionBudget);
        EnsureResidencyOwner();
        lock (m_sync)
        {
            EnsureActive();
            int completed = 0;
            for (int index = 0; index < m_pendingLoads.Count && completed < completionBudget;)
            {
                PendingAcquisition request = m_pendingLoads[index];
                if (!request.preparation.IsCompleted)
                {
                    index++;
                    continue;
                }
                m_pendingLoads.RemoveAt(index);
                request.Complete();
                completed++;
            }
            return completed;
        }
    }

    private ValueTask<AssetLease<TAsset>> QueueAcquisition<TAsset>(
        RuntimeAssetRecord record, CancellationToken cancellationToken) where TAsset : AssetObject
    {
        EnsureResidencyOwner();
        cancellationToken.ThrowIfCancellationRequested();
        if (record.asset is not null)
        {
            TAsset loaded = LoadRecord<TAsset>(record, pin: false);
            record.leaseCount++;
            return ValueTask.FromResult(CreateResidencyLease(record, loaded));
        }
        if (m_pendingLoads.Count >= C_MAX_PENDING_LOADS)
        {
            m_rejectedLoads++;
            throw new InvalidOperationException("Runtime asset preparation is at capacity; retry after the next completion safety point.");
        }
        if (!typeof(TAsset).IsAssignableFrom(ResolveType(record)))
            throw new InvalidOperationException($"Runtime asset '{record.path}' is not compatible with '{typeof(TAsset).FullName}'.");

        if (!m_preparations.TryGetValue(record.persistentId, out SharedPreparation? shared))
        {
            // Only immutable paths, hashes and IDs cross into IO; serializers and identities stay here.
            List<PayloadRead> reads = [];
            CollectPayloadReads(record, new HashSet<Guid>(), reads);
            long bytes = reads.Where(read => !m_payloadReads.ContainsKey(read)).Sum(static read => read.length);
            if (bytes > m_preparationBudgetBytes - m_preparingBytes)
            {
                m_rejectedLoads++;
                throw new InvalidOperationException("Runtime asset preparation exceeds its encoded-byte budget; retry after publication.");
            }
            List<SharedPayload> payloads = [];
            try
            {
                foreach (PayloadRead read in reads)
                {
                    if (!m_payloadReads.TryGetValue(read, out SharedPayload? payload))
                    {
                        payload = new SharedPayload(read, m_payloadReadSlots);
                        m_payloadReads.Add(read, payload);
                        m_preparingBytes += read.length;
                        m_payloadReadsStarted++;
                    }
                    else
                        m_payloadReadsShared++;
                    payload.owners++;
                    payloads.Add(payload);
                }
                shared = new SharedPreparation(StartPayloadPreparation(payloads.ToArray()), payloads.ToArray());
                m_preparations.Add(record.persistentId, shared);
                m_peakPreparingBytes = Math.Max(m_peakPreparingBytes, m_preparingBytes);
            }
            catch
            {
                foreach (SharedPayload payload in payloads)
                    ReleasePayload(payload);
                throw;
            }
        }
        shared.waiters++;
        var completion = new TaskCompletionSource<AssetLease<TAsset>>(TaskCreationOptions.RunContinuationsAsynchronously);
        m_pendingLoads.Add(new PendingAcquisition(shared.preparation, cancellationToken, payloads =>
        {
            TAsset asset = LoadRecord<TAsset>(record, pin: false, payloads);
            record.leaseCount++;
            completion.SetResult(CreateResidencyLease(record, asset));
        }, exception => completion.TrySetException(exception),
            () => completion.TrySetCanceled(cancellationToken.IsCancellationRequested ? cancellationToken : new CancellationToken(true)),
            () => ReleasePreparation(record.persistentId, shared)));
        m_peakPendingLoads = Math.Max(m_peakPendingLoads, m_pendingLoads.Count);
        return new ValueTask<AssetLease<TAsset>>(completion.Task);
    }

    private AssetLease<TAsset> CreateResidencyLease<TAsset>(RuntimeAssetRecord record, TAsset asset)
        where TAsset : AssetObject
    {
        bool released = false;
        return CreateAssetLease(asset, () => ReleaseLease(record, ref released));
    }

    private void CollectPayloadReads(RuntimeAssetRecord record, HashSet<Guid> visited, List<PayloadRead> reads)
    {
        if (!visited.Add(record.persistentId))
            return;
        RuntimeArtifactOutput output = ReadArtifactManifest(record).outputs.Single(candidate => candidate.name == "runtime");
        if (output.length < 0 || output.length > int.MaxValue)
            throw new InvalidDataException("A runtime payload length is outside the supported allocation range.");
        if (string.IsNullOrWhiteSpace(output.fileName) || Path.GetFileName(output.fileName) != output.fileName)
            throw new InvalidDataException("A runtime artifact contains an invalid output path.");
        reads.Add(new PayloadRead(record.persistentId,
            Path.Combine(GetBundleRoot(record.artifactKey), "outputs", output.fileName), output.length, output.contentHash));
        foreach (AssetDependency dependency in record.dependencies)
            CollectPayloadReads(m_recordsById[dependency.persistentId], visited, reads);
    }

    private static async Task<byte[]> ReadPayloadAsync(PayloadRead read, CancellationToken token, SemaphoreSlim slots)
    {
        await slots.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await ReadPayloadFileAsync(read, token).ConfigureAwait(false);
        }
        finally { slots.Release(); }
    }

    private static async Task<byte[]> ReadPayloadFileAsync(PayloadRead read, CancellationToken token)
    {
        await using var stream = new FileStream(read.path, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length != read.length)
            throw new InvalidDataException($"Runtime payload '{read.id:D}' has an unexpected length.");
        byte[] bytes = new byte[checked((int)read.length)];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (bytes.LongLength != read.length ||
            !string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), read.hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Runtime payload '{read.id:D}' failed integrity verification.");
        return bytes;
    }

    private static Task<Dictionary<Guid, byte[]>> StartPayloadPreparation(SharedPayload[] reads)
    {
        if (ExecutionContext.IsFlowSuppressed())
            return AssemblePayloadsAsync(reads);
        using (ExecutionContext.SuppressFlow())
            return AssemblePayloadsAsync(reads);
    }

    private static async Task<Dictionary<Guid, byte[]>> AssemblePayloadsAsync(SharedPayload[] reads)
    {
        // WhenAll observes every shared IO task before a failed closure relinquishes its reservations.
        await Task.WhenAll(reads.Select(static read => read.preparation)).ConfigureAwait(false);
        return reads.ToDictionary(static read => read.read.id, static read => read.preparation.GetAwaiter().GetResult());
    }

    private void CancelPendingLoads()
    {
        foreach (SharedPayload preparation in m_payloadReads.Values)
            preparation.cancellation.Cancel();
        foreach (PendingAcquisition request in m_pendingLoads)
            request.Cancel();
        m_pendingLoads.Clear();
    }

    private void ReleasePreparation(Guid id, SharedPreparation preparation)
    {
        if (--preparation.waiters != 0)
            return;
        m_preparations.Remove(id);
        foreach (SharedPayload payload in preparation.payloads)
            ReleasePayload(payload);
    }

    private void ReleasePayload(SharedPayload payload)
    {
        if (--payload.owners != 0)
            return;
        payload.cancellation.Cancel();
        try { payload.preparation.GetAwaiter().GetResult(); }
        catch (Exception) { /* The acquisition reports IO failures; rollback only drains ownership. */ }
        m_payloadReads.Remove(payload.read);
        m_preparingBytes -= payload.read.length;
        payload.cancellation.Dispose();
    }

    private void EnsureResidencyOwner()
    {
        if (Environment.CurrentManagedThreadId != m_ownerThread)
            throw new InvalidOperationException("Runtime asset acquisition and publication require the database owner thread.");
    }

    private sealed record PayloadRead(Guid id, string path, long length, string hash);

    private sealed class SharedPreparation(
        Task<Dictionary<Guid, byte[]>> preparation, SharedPayload[] payloads)
    {
        internal Task<Dictionary<Guid, byte[]>> preparation { get; } = preparation;
        internal SharedPayload[] payloads { get; } = payloads;
        internal int waiters { get; set; }
    }

    private sealed class SharedPayload
    {
        internal SharedPayload(PayloadRead read, SemaphoreSlim slots)
        {
            this.read = read;
            try
            {
                if (ExecutionContext.IsFlowSuppressed())
                    preparation = Task.Run(() => ReadPayloadAsync(read, cancellation.Token, slots), cancellation.Token);
                else
                {
                    using (ExecutionContext.SuppressFlow())
                        preparation = Task.Run(() => ReadPayloadAsync(read, cancellation.Token, slots), cancellation.Token);
                }
            }
            catch { cancellation.Dispose(); throw; }
        }

        internal PayloadRead read { get; }
        internal CancellationTokenSource cancellation { get; } = new();
        internal Task<byte[]> preparation { get; }
        internal int owners { get; set; }
    }

    private sealed class PendingAcquisition(
        Task<Dictionary<Guid, byte[]>> preparation, CancellationToken cancellation,
        Action<Dictionary<Guid, byte[]>> publish, Action<Exception> fail, Action cancel, Action release)
    {
        internal Task<Dictionary<Guid, byte[]>> preparation { get; } = preparation;

        internal void Complete()
        {
            try
            {
                cancellation.ThrowIfCancellationRequested();
                publish(preparation.GetAwaiter().GetResult());
            }
            catch (OperationCanceledException) { cancel(); }
            catch (Exception exception) { fail(exception); }
            finally { release(); }
        }

        internal void Cancel()
        {
            try { preparation.GetAwaiter().GetResult(); }
            catch (Exception) { /* The retiring owner reports cancellation instead of publishing. */ }
            cancel();
            release();
        }
    }
}
