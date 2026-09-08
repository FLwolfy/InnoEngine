using System;
using System.Collections.Generic;
using Inno.Core.Execution;

namespace Inno.Audio.Runtime;

internal sealed class AudioDeviceCandidate : IDisposable
{
    private readonly List<Exception> m_failures = [];
    private IAudioDevice? m_device;
    private AudioMixerOwner? m_mixer;

    internal AudioDeviceCandidate(IAudioDevice device) => m_device = device;

    internal AudioMixerOwner Prepare(AudioMixerOwner previous)
        => m_mixer = previous.PrepareReplacement(m_device!);

    internal void Commit()
    {
        m_mixer = null;
        m_device = null;
    }

    /// <summary>
    /// Retires an unpublished graph before its device, retaining both while retirement remains pending.
    /// </summary>
    public void Dispose()
    {
        try { m_mixer?.Dispose(); }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
        catch (Exception exception) { m_failures.Add(exception); }
        m_mixer = null;
        try { m_device?.Dispose(); }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
        catch (Exception exception) { m_failures.Add(exception); }
        m_device = null;
        if (m_failures.Count == 0)
            return;
        Exception[] failures = m_failures.ToArray();
        m_failures.Clear();
        throw new AggregateException("Audio candidate retirement failed after every owner was attempted.", failures);
    }
}
