using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inno.Assets;
using Inno.Core.Execution;

namespace Inno.Audio.Runtime;

internal sealed class AudioClipCache : IDisposable
{
    private readonly IAudioDevice m_device;
    private readonly IAssetArtifactLookup m_artifacts;
    private readonly AudioRuntimeOptions m_options;
    private readonly Action<Action> m_retireResources;
    private readonly Dictionary<ClipCacheKey, ClipCacheEntry> m_clips = [];
    private readonly List<PendingPreload> m_preloads = [];
    private readonly List<Exception> m_retirementFailures = [];

    internal AudioClipCache(IAudioDevice device, IAssetArtifactLookup artifacts, AudioRuntimeOptions options,
        Action<Action> retireResources)
    {
        m_device = device;
        m_artifacts = artifacts;
        m_options = options;
        m_retireResources = retireResources;
    }

    internal int count => m_clips.Count;
    internal long decodedBytes => m_clips.Values.Sum(static clip => clip.decodedByteLength);

    internal ValueTask PreloadAsync(
        AudioClipAsset clip,
        AudioClipLoadMode loadMode = AudioClipLoadMode.Automatic,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clip);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(loadMode))
            throw new ArgumentOutOfRangeException(nameof(loadMode));
        if (m_preloads.Count >= m_options.maxPendingPreloads)
            throw new InvalidOperationException("Audio preload waiters are at capacity; retry after an update safety point.");
        var request = new AudioClipRequest(clip, m_artifacts);
        Exception? preparationFailure = null;
        try { return PreparePreload(request, loadMode, cancellationToken); }
        catch (Exception exception)
        {
            preparationFailure = exception;
            throw;
        }
        finally
        {
            try { m_retireResources(request.Dispose); }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
            catch (Exception cleanup) when (preparationFailure is not null)
            {
                throw new AggregateException("Audio preload preparation and request retirement failed.",
                    preparationFailure, cleanup);
            }
        }
    }
    internal void ReleasePreload(AudioClipAsset clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        ClipCacheEntry? retiring = m_clips.Values.FirstOrDefault(entry =>
            entry.key.persistentId == clip.identity.persistentId && entry.isRetiring);
        if (retiring is not null)
        {
            TryReleaseClip(retiring);
            return;
        }
        ClipCacheEntry? entry = m_clips
            .Where(pair => pair.Key.persistentId == clip.identity.persistentId &&
                           pair.Value.preloadReferences > pair.Value.pendingPreloadReferences)
            .Select(static pair => pair.Value)
            .FirstOrDefault();
        if (entry is null)
            return;
        entry.preloadReferences--;
        TryReleaseClip(entry);
    }
    internal ClipCacheEntry GetOrCreateClip(AudioClipRequest clip, AudioClipLoadMode requestedMode)
    {
        AudioClipMetadata metadata = clip.metadata
            ?? throw new InvalidOperationException("The audio clip has no imported runtime metadata.");
        AssetArtifactInfo artifact = clip.artifact;
        AudioClipLoadMode resolvedMode = requestedMode == AudioClipLoadMode.Automatic
            ? (artifact.length >= m_options.automaticStreamingThresholdBytes ||
               WouldExceedDecodedBudget(EstimateDecodedByteLength(metadata, artifact.length))
                ? AudioClipLoadMode.Stream
                : AudioClipLoadMode.Decode)
            : requestedMode;
        var key = new ClipCacheKey(clip.persistentId, clip.contentVersion, artifact.key, resolvedMode);
        if (m_clips.TryGetValue(key, out ClipCacheEntry? cached))
        {
            if (!cached.isRetiring)
                return cached;
            TryReleaseClip(cached);
        }
        long decodedByteLength = resolvedMode == AudioClipLoadMode.Decode
            ? EstimateDecodedByteLength(metadata, artifact.length)
            : 0;
        if (resolvedMode == AudioClipLoadMode.Decode && WouldExceedDecodedBudget(decodedByteLength))
        {
            throw new InvalidOperationException(
                $"Decoded audio clip '{clip.assetPath}' exceeds the configured cache budget.");
        }
        AudioClipHandle handle = m_device.CreateClip(new AudioClipDescriptor(
            artifact.absolutePath,
            metadata.codec,
            resolvedMode,
            metadata.channels,
            metadata.sampleRate,
            metadata.frameCount,
            artifact.length));
        if (!handle.isValid)
            throw new InvalidOperationException("The audio backend rejected clip creation.");
        var entry = new ClipCacheEntry(key, handle, decodedByteLength, clip.TakeArtifact(), m_device);
        m_clips.Add(key, entry);
        return entry;
    }
    internal void TryReleaseClip(ClipCacheEntry entry)
    {
        if (entry.preloadReferences > 0 || entry.voiceReferences > 0)
            return;
        entry.Dispose();
        if (m_clips.TryGetValue(entry.key, out ClipCacheEntry? current) && ReferenceEquals(current, entry))
            m_clips.Remove(entry.key);
    }
    internal void CompletePreloads()
    {
        for (int index = m_preloads.Count - 1; index >= 0; index--)
        {
            PendingPreload pending = m_preloads[index];
            if (!pending.completing)
            {
                AudioClipState state = m_device.GetClipState(pending.clip.handle);
                pending.canceled = pending.cancellationToken.IsCancellationRequested;
                if (!pending.canceled && state == AudioClipState.Preparing)
                    continue;
                pending.failed = state == AudioClipState.Failed;
                pending.completing = true;
                pending.clip.pendingPreloadReferences--;
                if (pending.canceled || pending.failed)
                    pending.clip.preloadReferences--;
            }
            if (pending.canceled || pending.failed)
            {
                try
                {
                    TryReleaseClip(pending.clip);
                    if (pending.canceled)
                        pending.completion.TrySetCanceled(pending.cancellationToken);
                    else
                        pending.completion.TrySetException(new InvalidOperationException("Native audio preparation failed."));
                }
                catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
                catch (Exception exception)
                {
                    pending.completion.TrySetException(exception);
                    m_preloads.RemoveAt(index);
                    throw;
                }
            }
            else
                pending.completion.TrySetResult();
            m_preloads.RemoveAt(index);
        }
        DrainRetiringClips();
    }
    /// <summary>
    /// Cancels waiters and releases every native clip and immutable artifact owned by this device cache.
    /// </summary>
    public void Dispose()
    {
        foreach (PendingPreload preload in m_preloads)
            preload.completion.TrySetCanceled();
        m_preloads.Clear();
        foreach (ClipCacheEntry clip in m_clips.Values.ToArray())
        {
            try { clip.Dispose(); }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
            catch (Exception exception) { m_retirementFailures.Add(exception); }
            m_clips.Remove(clip.key);
        }
        m_clips.Clear();
        if (m_retirementFailures.Count > 0)
        {
            Exception[] failures = m_retirementFailures.ToArray();
            m_retirementFailures.Clear();
            throw new AggregateException("Audio cache retirement failed after every clip and artifact was attempted.", failures);
        }
    }
    private ValueTask PreparePreload(AudioClipRequest request, AudioClipLoadMode loadMode,
        CancellationToken cancellationToken)
    {
        ClipCacheEntry entry = GetOrCreateClip(request, loadMode);
        AudioClipState state;
        try
        {
            state = m_device.GetClipState(entry.handle);
            if (state == AudioClipState.Failed)
                throw new InvalidOperationException("Native audio clip preparation failed.");
        }
        catch (Exception failure)
        {
            try { m_retireResources(() => TryReleaseClip(entry)); }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
            catch (Exception cleanup)
            {
                throw new AggregateException("Audio preload preparation and cache retirement failed.", failure, cleanup);
            }
            throw;
        }
        entry.preloadReferences++;
        if (state == AudioClipState.Ready)
            return ValueTask.CompletedTask;
        var pending = new PendingPreload(entry, cancellationToken);
        entry.pendingPreloadReferences++;
        m_preloads.Add(pending);
        return new ValueTask(pending.completion.Task);
    }
    private void DrainRetiringClips()
    {
        foreach (ClipCacheEntry clip in m_clips.Values.Where(static clip => clip.isRetiring).ToArray())
            TryReleaseClip(clip);
    }
    private bool WouldExceedDecodedBudget(long additionalBytes)
    {
        if (additionalBytes > m_options.decodedCacheBudgetBytes)
            return true;
        long currentBytes = m_clips.Values.Sum(static clip => clip.decodedByteLength);
        return currentBytes > m_options.decodedCacheBudgetBytes - additionalBytes;
    }
    private static long EstimateDecodedByteLength(AudioClipMetadata metadata, long encodedByteLength)
    {
        try
        {
            long decoded = checked(metadata.frameCount * metadata.channels * sizeof(float));
            return Math.Max(encodedByteLength, decoded);
        }
        catch (OverflowException)
        {
            return long.MaxValue;
        }
    }
    internal readonly record struct ClipCacheKey(
        Guid persistentId,
        long contentVersion,
        AssetArtifactKey artifactKey,
        AudioClipLoadMode loadMode);
    internal sealed class ClipCacheEntry : IDisposable
    {
        private readonly LifetimeScope m_lifetime = new();

        internal ClipCacheEntry(ClipCacheKey key, AudioClipHandle handle, long decodedByteLength,
            ArtifactLease artifact, IAudioDevice device)
        {
            this.key = key;
            this.handle = handle;
            this.decodedByteLength = decodedByteLength;
            m_lifetime.Own(artifact);
            m_lifetime.Own(new NativeClipRetirement(device, handle));
        }

        /// <summary>
        /// Retires the native clip before its artifact, retaining the current step across pending retries.
        /// </summary>
        public void Dispose()
        {
            isRetiring = true;
            m_lifetime.Dispose();
        }

        internal ClipCacheKey key { get; }
        internal AudioClipHandle handle { get; }
        internal long decodedByteLength { get; }
        internal bool isRetiring { get; private set; }
        internal int preloadReferences { get; set; }
        internal int pendingPreloadReferences { get; set; }
        internal int voiceReferences { get; set; }
    }
    private sealed class NativeClipRetirement(IAudioDevice device, AudioClipHandle handle) : IDisposable
    {
        /// <summary>
        /// Releases one backend clip under the containing cache entry's lifetime.
        /// </summary>
        public void Dispose()
        {
            if (!device.DestroyClip(handle))
                throw new InvalidOperationException("The audio backend refused clip retirement.");
        }
    }
    private sealed class PendingPreload(ClipCacheEntry clip, CancellationToken cancellationToken)
    {
        internal bool completing { get; set; }
        internal bool canceled { get; set; }
        internal bool failed { get; set; }
        internal ClipCacheEntry clip { get; } = clip;
        internal CancellationToken cancellationToken { get; } = cancellationToken;
        internal TaskCompletionSource completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
