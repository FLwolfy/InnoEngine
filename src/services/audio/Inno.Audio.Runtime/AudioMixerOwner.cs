using System;
using Inno.Core.Execution;
using System.Collections.Generic;
using System.Linq;

namespace Inno.Audio.Runtime;

internal sealed class AudioMixerOwner : IDisposable
{
    private readonly IAudioDevice m_device;
    private readonly Action<Exception> m_retirementFailed;
    private readonly Action<Action> m_retire;
    private readonly List<Exception> m_retirementFailures = [];
    private readonly Dictionary<AudioBusId, AudioBusHandle> m_buses = [];
    private readonly Dictionary<AudioBusId, BusControlState> m_busControls = [];
    private readonly List<Dictionary<AudioBusId, AudioBusHandle>> m_retiredBuses = [];
    private AudioMixer m_activeMixer;

    internal AudioMixerOwner(IAudioDevice device, Action<Exception> retirementFailed, Action<Action> retire)
        : this(device, new AudioMixerBuilder().Build(), null, retirementFailed, retire) { }

    private AudioMixerOwner(IAudioDevice device, AudioMixer mixer,
        IReadOnlyDictionary<AudioBusId, BusControlState>? controls, Action<Exception> retirementFailed, Action<Action> retire)
    {
        m_device = device;
        m_retirementFailed = retirementFailed;
        m_retire = retire;
        m_activeMixer = mixer;
        foreach ((AudioBusId id, AudioBusHandle handle) in CreateBusSet(device, mixer, controls))
            m_buses.Add(id, handle);
        foreach (AudioBusDefinition bus in mixer.buses)
        {
            BusControlState state = controls is not null && controls.TryGetValue(bus.id, out BusControlState? current)
                ? current : new BusControlState(bus.volume, bus.muted, paused: false);
            m_busControls.Add(bus.id, new BusControlState(state.volume, state.muted, state.paused));
        }
    }

    internal AudioMixerOwner PrepareReplacement(IAudioDevice device)
        => new(device, m_activeMixer, m_busControls, m_retirementFailed, m_retire);

    internal bool TryGetBus(AudioBusId id, out AudioBusHandle handle) => m_buses.TryGetValue(id, out handle);

    internal bool SetBusVolume(AudioBusId bus, float volume)
    {
        if (volume < 0f || !float.IsFinite(volume) ||
            !m_buses.TryGetValue(bus, out AudioBusHandle handle) ||
            !m_device.SetBusVolume(handle, volume))
        {
            return false;
        }
        m_busControls[bus].volume = volume;
        return true;
    }

    internal bool SetBusMuted(AudioBusId bus, bool muted)
    {
        if (!m_buses.TryGetValue(bus, out AudioBusHandle handle) || !m_device.SetBusMuted(handle, muted))
            return false;
        m_busControls[bus].muted = muted;
        return true;
    }

    internal bool SetBusPaused(AudioBusId bus, bool paused)
    {
        if (!m_buses.TryGetValue(bus, out AudioBusHandle handle) || !m_device.SetBusPaused(handle, paused))
            return false;
        m_busControls[bus].paused = paused;
        return true;
    }

    internal void Install(AudioMixer mixer, IEnumerable<AudioBusHandle> usedBuses)
    {
        CollectRetiredBuses(usedBuses);
        IReadOnlyDictionary<AudioBusId, AudioBusHandle> candidate = CreateBusSet(m_device, mixer, null);
        if (m_buses.Count > 0)
            m_retiredBuses.Add(new Dictionary<AudioBusId, AudioBusHandle>(m_buses));
        m_buses.Clear();
        foreach ((AudioBusId id, AudioBusHandle handle) in candidate)
            m_buses.Add(id, handle);
        m_busControls.Clear();
        foreach (AudioBusDefinition bus in mixer.buses)
            m_busControls.Add(bus.id, new BusControlState(bus.volume, bus.muted, paused: false));
        m_activeMixer = mixer;
    }

    internal void CollectRetiredBuses(IEnumerable<AudioBusHandle> usedBuses)
    {
        HashSet<AudioBusHandle> retained = usedBuses.ToHashSet();
        for (int index = m_retiredBuses.Count - 1; index >= 0; index--)
        {
            Dictionary<AudioBusId, AudioBusHandle> retired = m_retiredBuses[index];
            if (retired.Values.Any(retained.Contains))
                continue;
            try { DestroyBusSet(m_device, retired); m_retiredBuses.RemoveAt(index); }
            catch (Exception exception)
            {
                m_retirementFailed(exception);
                throw;
            }
        }
    }

    /// <summary>
    /// Retires the active graph and all retained graph generations after their voices have stopped.
    /// </summary>
    public void Dispose()
    {
        try { DestroyBusSet(m_device, m_buses); }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
        catch (Exception error) { m_retirementFailures.Add(error); }
        while (m_retiredBuses.Count > 0)
        {
            try { DestroyBusSet(m_device, m_retiredBuses[^1]); }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
            catch (Exception error) { m_retirementFailures.Add(error); }
            m_retiredBuses.RemoveAt(m_retiredBuses.Count - 1);
        }
        m_busControls.Clear();
        if (m_retirementFailures.Count == 0)
            return;
        Exception[] failures = m_retirementFailures.ToArray();
        m_retirementFailures.Clear();
        throw new AggregateException("Audio mixer retirement failed after every graph was attempted.", failures);
    }

    private IReadOnlyDictionary<AudioBusId, AudioBusHandle> CreateBusSet(
        IAudioDevice device,
        AudioMixer mixer,
        IReadOnlyDictionary<AudioBusId, BusControlState>? controls)
    {
        var candidate = new Dictionary<AudioBusId, AudioBusHandle>();
        try
        {
            foreach (AudioBusDefinition bus in mixer.buses)
            {
                AudioBusHandle parent = bus.parent is AudioBusId parentId
                    ? candidate[parentId]
                    : default;
                AudioBusHandle handle = device.CreateBus(bus.id, parent);
                if (!handle.isValid)
                    throw new InvalidOperationException($"The backend rejected audio bus '{bus.id}'.");
                candidate.Add(bus.id, handle);
                BusControlState state = controls is not null && controls.TryGetValue(bus.id, out BusControlState? current)
                    ? current
                    : new BusControlState(bus.volume, bus.muted, paused: false);
                if (!device.SetBusVolume(handle, state.volume) ||
                    !device.SetBusMuted(handle, state.muted) ||
                    state.paused && !device.SetBusPaused(handle, paused: true))
                {
                    throw new InvalidOperationException($"The backend rejected parameters for audio bus '{bus.id}'.");
                }
                foreach (AudioProcessorConfiguration processor in bus.processors)
                {
                    if (!device.AddBusProcessor(handle, processor))
                        throw new InvalidOperationException($"The backend rejected processor '{processor.id}'.");
                }
            }
            return candidate;
        }
        catch (Exception failure)
        {
            try { m_retire(() => DestroyBusSet(device, candidate)); }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
            catch (Exception cleanup)
            {
                m_retirementFailed(cleanup);
                throw new AggregateException("Audio mixer preparation and rollback failed.", failure, cleanup);
            }
            throw;
        }
    }

    private static void DestroyBusSet(
        IAudioDevice device,
        Dictionary<AudioBusId, AudioBusHandle> buses)
    {
        List<Exception> failures = [];
        foreach ((AudioBusId id, AudioBusHandle bus) in buses.Reverse().ToArray())
        {
            try
            {
                if (!device.DestroyBus(bus))
                    throw new InvalidOperationException("The backend refused audio bus retirement.");
            }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
            catch (Exception exception) { failures.Add(exception); }
            buses.Remove(id);
        }
        if (failures.Count > 0)
            throw new AggregateException("Audio bus retirement failed after every bus was attempted.", failures);
    }

    private sealed class BusControlState(float volume, bool muted, bool paused)
    {
        internal float volume { get; set; } = volume;
        internal bool muted { get; set; } = muted;
        internal bool paused { get; set; } = paused;
    }
}
