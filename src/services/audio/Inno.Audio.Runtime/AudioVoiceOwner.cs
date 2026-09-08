using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Assets;
using Inno.Core.Diagnostics;
using Inno.Core.Events;
using Inno.Core.Execution;

namespace Inno.Audio.Runtime;

internal sealed class AudioVoiceOwner
{
    private readonly HashSet<AudioVoiceHandle> m_terminalVoices = [];
    private readonly Queue<AudioVoiceHandle> m_terminalVoiceOrder = [];
    private readonly Dictionary<AudioVoiceHandle, VoiceRecord> m_voices = [];
    private readonly AudioVoiceAllocator m_voiceAllocator = new();
    private long m_nextVoiceSequence;
    private readonly IAudioDevice m_device;
    private readonly IAssetArtifactLookup m_artifacts;
    private readonly AudioClipCache m_clipCache;
    private readonly AudioMixerOwner m_mixer;
    private readonly EventDispatcher m_events;
    private readonly IDiagnosticReporter m_diagnostics;
    private readonly AudioRuntimeOptions m_options;
    private readonly Action<Action> m_retireResources;
    private long m_stolenVoiceCount;
    private readonly List<Exception> m_retirementFailures = [];

    internal AudioVoiceOwner(IAudioDevice device, IAssetArtifactLookup artifacts, AudioClipCache clipCache,
        AudioMixerOwner mixer, EventDispatcher events, IDiagnosticReporter diagnostics, AudioRuntimeOptions options,
        Action<Action> retireResources,
        AudioVoiceOwner? previous = null)
    {
        m_device = device;
        m_artifacts = artifacts;
        m_clipCache = clipCache;
        m_mixer = mixer;
        m_events = events;
        m_diagnostics = diagnostics;
        m_options = options;
        m_retireResources = retireResources;
        if (previous is not null)
        {
            m_terminalVoices = previous.m_terminalVoices;
            m_terminalVoiceOrder = previous.m_terminalVoiceOrder;
        }
    }

    internal int count => m_voices.Count;
    internal long stolenCount => m_stolenVoiceCount;
    internal IEnumerable<AudioBusHandle> usedBuses => m_voices.Values.Select(static voice => voice.backendBus);

    internal void RefreshStates()
    {
        foreach (VoiceRecord voice in m_voices.Values)
            UpdateVoiceState(voice);
    }

    internal bool Stop(AudioVoiceHandle voice)
    {
        if (!m_voices.TryGetValue(voice, out VoiceRecord? record))
            return false;
        if (!record.backendVoice.isValid)
        {
            FinishVoice(record, AudioCompletionReason.Stopped);
            return true;
        }
        record.completionOverride = AudioCompletionReason.Stopped;
        if (m_device.Stop(record.backendVoice))
            return true;
        FinishVoice(record, AudioCompletionReason.Stopped);
        return true;
    }

    internal bool Pause(AudioVoiceHandle voice)
    {
        if (!m_voices.TryGetValue(voice, out VoiceRecord? record))
            return false;
        record.pauseRequested = true;
        if (!record.backendVoice.isValid)
        {
            record.state = AudioPlaybackState.Paused;
            return true;
        }
        bool result = m_device.Pause(record.backendVoice);
        if (result)
            record.state = AudioPlaybackState.Paused;
        return result;
    }

    internal bool Resume(AudioVoiceHandle voice)
    {
        if (!m_voices.TryGetValue(voice, out VoiceRecord? record) || !record.pauseRequested)
            return false;
        record.pauseRequested = false;
        if (!record.backendVoice.isValid)
        {
            record.state = AudioPlaybackState.Preparing;
            return true;
        }
        bool result = m_device.Resume(record.backendVoice);
        if (result)
            UpdateVoiceState(record);
        return result;
    }

    internal bool Seek(AudioVoiceHandle voice, TimeSpan position)
    {
        if (position < TimeSpan.Zero || !m_voices.TryGetValue(voice, out VoiceRecord? record))
            return false;
        record.seekPosition = position;
        return !record.backendVoice.isValid || m_device.Seek(record.backendVoice, position);
    }

    internal bool SetVoiceParameters(AudioVoiceHandle voice, AudioVoiceParameters parameters)
    {
        if (parameters.pitch <= 0f)
            return false;
        if (!m_voices.TryGetValue(voice, out VoiceRecord? record))
            return false;
        record.parameters = parameters;
        return !record.backendVoice.isValid || m_device.SetVoiceParameters(record.backendVoice, parameters);
    }

    internal bool TryGetVoiceState(AudioVoiceHandle voice, out AudioPlaybackState playbackState)
    {
        if (m_voices.TryGetValue(voice, out VoiceRecord? record))
        {
            UpdateVoiceState(record);
            playbackState = record.state;
            return true;
        }
        if (m_terminalVoices.Contains(voice))
        {
            playbackState = AudioPlaybackState.Completed;
            return true;
        }
        playbackState = AudioPlaybackState.Invalid;
        return false;
    }

    internal AudioVoiceHandle Play(AudioClipAsset clip, AudioPlayOptions options, double? scheduledDspTime = null)
    {
        ArgumentNullException.ThrowIfNull(clip);
        if (!options.bus.isValid)
            throw new ArgumentException("Playback options must be initialized.", nameof(options));
        var request = new AudioClipRequest(clip, m_artifacts);
        AudioVoiceHandle handle;
        try
        {
            handle = m_voiceAllocator.Allocate();
            var record = new VoiceRecord(handle, request, options, scheduledDspTime, m_nextVoiceSequence++);
            while (m_voices.Count >= m_options.maxVoices)
                StealVoice();
            m_voices.Add(handle, record);
        }
        catch (Exception failure)
        {
            try { m_retireResources(request.Dispose); }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
            catch (Exception cleanup)
            {
                throw new AggregateException("Audio voice admission and request retirement failed.", failure, cleanup);
            }
            throw;
        }
        return handle;
    }

    private void StealVoice()
    {
        VoiceRecord victim = m_voices.Values
            .OrderBy(static voice => voice.options.priority)
            .ThenBy(static voice => voice.sequence)
            .First();
        if (victim.finishingReason is AudioCompletionReason finishingReason)
        {
            FinishVoice(victim, finishingReason);
            return;
        }
        if (victim.backendVoice.isValid)
            _ = m_device.Stop(victim.backendVoice);
        m_stolenVoiceCount++;
        FinishVoice(victim, AudioCompletionReason.Stolen);
    }

    internal void PrepareVoices()
    {
        foreach (VoiceRecord voice in m_voices.Values.Where(static voice =>
                     !voice.backendVoice.isValid && voice.finishingReason is null).ToArray())
        {
            try
            {
                AudioClipCache.ClipCacheEntry clip;
                if (voice.clipCache is null)
                {
                    clip = m_clipCache.GetOrCreateClip(voice.clip, voice.options.loadMode);
                    clip.voiceReferences++;
                    voice.clipCache = clip;
                    voice.clip.Dispose();
                }
                else
                    clip = voice.clipCache;
                AudioClipState preparation = m_device.GetClipState(clip.handle);
                if (preparation == AudioClipState.Preparing)
                    continue;
                if (preparation == AudioClipState.Failed)
                    throw new InvalidOperationException("Native audio preparation failed.");
                if (!m_mixer.TryGetBus(voice.options.bus, out AudioBusHandle bus))
                    throw new InvalidOperationException($"Audio bus '{voice.options.bus}' is not present in the active mixer.");
                voice.backendVoice = m_device.Play(clip.handle, bus, voice.options, voice.scheduledDspTime);
                voice.backendBus = bus;
                if (!voice.backendVoice.isValid)
                    throw new InvalidOperationException("The audio backend rejected voice creation.");
                if (voice.seekPosition is TimeSpan seek)
                    _ = m_device.Seek(voice.backendVoice, seek);
                _ = m_device.SetVoiceParameters(voice.backendVoice, voice.parameters);
                if (voice.pauseRequested)
                    _ = m_device.Pause(voice.backendVoice);
                UpdateVoiceState(voice);
                m_diagnostics.Resolve("AUDIO_CLIP_PREPARATION_FAILED", voice.clip.persistentId.ToString("D"));
            }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
            catch (Exception exception)
            {
                Publish(
                    "AUDIO_CLIP_PREPARATION_FAILED",
                    $"Audio clip '{voice.clip.assetPath}' could not be prepared: {exception.Message}",
                    DiagnosticSeverity.Error,
                    voice.clip.persistentId.ToString("D"));
                FinishVoice(voice, AudioCompletionReason.DecodeFailed);
            }
        }
    }

    private void FinishVoice(VoiceRecord voice, AudioCompletionReason reason)
    {
        if (!m_voices.ContainsKey(voice.handle))
            return;
        voice.finishingReason ??= reason;
        voice.clip.Dispose();
        if (voice.clipCache is not null)
        {
            if (!voice.cacheReferenceReleased)
            {
                voice.clipCache.voiceReferences--;
                voice.cacheReferenceReleased = true;
            }
            m_clipCache.TryReleaseClip(voice.clipCache);
            voice.clipCache = null;
        }
        m_events.Enqueue(new AudioVoiceCompletedEvent(voice.handle, voice.finishingReason.Value));
        m_voices.Remove(voice.handle);
        if (m_terminalVoices.Add(voice.handle))
            m_terminalVoiceOrder.Enqueue(voice.handle);
        int terminalCapacity = m_options.maxVoices > int.MaxValue / 2
            ? int.MaxValue
            : Math.Max(64, m_options.maxVoices * 2);
        while (m_terminalVoiceOrder.Count > terminalCapacity)
            m_terminalVoices.Remove(m_terminalVoiceOrder.Dequeue());
    }

    internal void DrainCompletions()
    {
        foreach (VoiceRecord pending in m_voices.Values.Where(static voice => voice.finishingReason is not null).ToArray())
            FinishVoice(pending, pending.finishingReason!.Value);
        long budget = Math.Max(64L, 2L * m_options.maxVoices);
        while (budget-- > 0 && m_device.TryDequeueCompletion(out AudioDeviceCompletion completion))
        {
            VoiceRecord? voice = m_voices.Values.FirstOrDefault(candidate => candidate.backendVoice == completion.voice);
            if (voice is null)
                continue;
            FinishVoice(voice, voice.completionOverride ?? completion.reason);
        }
    }

    private void UpdateVoiceState(VoiceRecord voice)
    {
        if (!voice.backendVoice.isValid)
            return;
        if (m_device.TryGetVoiceState(voice.backendVoice, out AudioPlaybackState state))
            voice.state = state;
    }

    internal void CompleteAll(AudioCompletionReason reason)
    {
        foreach (VoiceRecord voice in m_voices.Values.ToArray())
        {
            try
            {
                if (voice.backendVoice.isValid && voice.finishingReason is null)
                    _ = m_device.Stop(voice.backendVoice);
            }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
            catch (Exception exception) { m_retirementFailures.Add(exception); }
            try { FinishVoice(voice, reason); }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
            catch (Exception exception) { m_retirementFailures.Add(exception); }
        }
        if (m_retirementFailures.Count == 0)
            return;
        Exception[] failures = m_retirementFailures.ToArray();
        m_retirementFailures.Clear();
        throw new AggregateException("Audio voice retirement failed after every voice was attempted.", failures);
    }

    private sealed class VoiceRecord
    {
        internal VoiceRecord(
            AudioVoiceHandle handle,
            AudioClipRequest clip,
            AudioPlayOptions options,
            double? scheduledDspTime,
            long sequence)
        {
            this.handle = handle;
            this.clip = clip;
            this.options = options;
            this.scheduledDspTime = scheduledDspTime;
            this.sequence = sequence;
            parameters = new AudioVoiceParameters(options.volume, options.pitch, options.pan, options.spatial);
        }

        internal AudioVoiceHandle handle { get; }
        internal AudioClipRequest clip { get; }
        internal AudioPlayOptions options { get; }
        internal double? scheduledDspTime { get; }
        internal long sequence { get; }
        internal AudioDeviceVoiceHandle backendVoice { get; set; }
        internal AudioBusHandle backendBus { get; set; }
        internal AudioClipCache.ClipCacheEntry? clipCache { get; set; }
        internal AudioCompletionReason? completionOverride { get; set; }
        internal AudioVoiceParameters parameters { get; set; }
        internal TimeSpan? seekPosition { get; set; }
        internal AudioPlaybackState state { get; set; } = AudioPlaybackState.Preparing;
        internal bool pauseRequested { get; set; }
        internal AudioCompletionReason? finishingReason { get; set; }
        internal bool cacheReferenceReleased { get; set; }
    }

    private void Publish(string code, string message, DiagnosticSeverity severity, string? source)
        => m_diagnostics.Publish(new Diagnostic(code, message, severity, source));
}
