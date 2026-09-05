using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Mathematics;
using Inno.Scene;

namespace Inno.Audio.Scene;

[AudioContentProviderExtension("inno.audio.content.scene")]
internal sealed class SceneAudioContentProvider : AudioContentProvider
{
    private readonly Dictionary<Guid, Vector3> m_previousPositions = [];

    /// <summary>
    /// Converts active Scene audio components into immutable runtime snapshots.
    /// </summary>
    /// <param name="context">
    /// Current Scene content and destination snapshot collector.
    /// </param>
    public override void Submit(AudioContentProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var seen = new HashSet<Guid>();
        foreach (GameScene scene in context.content.GetValues<GameScene>())
        {
            foreach (GameObject gameObject in scene.GetObjects())
            {
                foreach (AudioSource source in gameObject.GetComponents<AudioSource>())
                    SubmitSource(context, source, seen);
                foreach (AudioListener listener in gameObject.GetComponents<AudioListener>())
                    SubmitListener(context, listener, seen);
            }
        }
        foreach (Guid stale in m_previousPositions.Keys.Where(id => !seen.Contains(id)).ToArray())
            m_previousPositions.Remove(stale);
    }

    private void SubmitSource(
        AudioContentProviderContext context,
        AudioSource source,
        HashSet<Guid> seen)
    {
        Guid id = source.identity.persistentId;
        seen.Add(id);
        Vector3 position = source.transform.worldPosition;
        Vector3 velocity = CalculateVelocity(id, position, context.deltaTime);
        AudioClipAsset? clip = source.clip;
        if (clip is null)
            return;
        AudioSpatialOptions? spatial = source.spatialize
            ? new AudioSpatialOptions(
                position,
                Vector3.Transform(Vector3.FORWARD, source.transform.worldRotation).normalized,
                velocity,
                source.distanceModel,
                source.minDistance,
                source.maxDistance,
                source.rolloff,
                source.coneInnerAngle,
                source.coneOuterAngle,
                source.coneOuterGain,
                source.dopplerFactor)
            : null;
        context.Submit(new AudioEmitterSnapshot(
            id,
            clip,
            new AudioPlayOptions(
                source.volume,
                source.pitch,
                source.pan,
                source.loop,
                source.priority,
                source.bus,
                source.loadMode,
                spatial),
            source.isActiveAndEnabled && source.isPlaybackRequested,
            source.playbackRevision));
    }

    private void SubmitListener(
        AudioContentProviderContext context,
        AudioListener listener,
        HashSet<Guid> seen)
    {
        Guid id = listener.identity.persistentId;
        seen.Add(id);
        Vector3 position = listener.transform.worldPosition;
        Vector3 velocity = CalculateVelocity(id, position, context.deltaTime);
        context.Submit(new AudioListenerSnapshot(
            id,
            listener.priority,
            new AudioListenerState(
                position,
                Vector3.Transform(Vector3.FORWARD, listener.transform.worldRotation).normalized,
                Vector3.Transform(Vector3.UP, listener.transform.worldRotation).normalized,
                velocity),
            listener.isActiveAndEnabled));
    }

    private Vector3 CalculateVelocity(Guid id, Vector3 position, float deltaTime)
    {
        Vector3 velocity = deltaTime > 0f && m_previousPositions.TryGetValue(id, out Vector3 previous)
            ? (position - previous) / deltaTime
            : Vector3.ZERO;
        m_previousPositions[id] = position;
        return velocity;
    }
}
