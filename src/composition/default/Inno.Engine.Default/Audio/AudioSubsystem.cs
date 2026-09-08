using System;
using Inno.Audio;
using Inno.Audio.Runtime;
using Inno.Core.Diagnostics;
using Inno.Runtime.Contracts;
using Inno.Scene;

namespace Inno.Engine.Default;

internal static class AudioSubsystem
{
    [RuntimeSubsystemRegistration("inno.runtime.audio")]
    internal static IRuntimeSubsystemFactory Create(EngineSessionComposition context)
        => context.audioOverride ?? new AudioRuntimeFactory(owner => CreateRuntime(context, owner));

    private static AudioRuntime CreateRuntime(EngineSessionComposition composition, RuntimeSubsystemContext owner)
    {
        DiagnosticReporter diagnostics = owner.resources.Own(owner.diagnostics.CreateReporter(
            new DiagnosticSource($"inno.audio.{composition.session.sessionId}", "Audio")));
        IAudioDevice device;
        try
        {
            device = composition.adapters.audio.CreateDevice(composition.selection.audio);
        }
        catch (Exception exception)
        {
            diagnostics.Publish(new Diagnostic("AUDIO_DEVICE_INIT_FAILED",
                $"Audio output could not start; this session is explicitly muted: {exception.Message}",
                DiagnosticSeverity.Warning, composition.selection.audio.ToString()));
            device = new MutedAudioDevice();
        }
        AudioRuntime? runtime = null;
        try
        {
            AudioProjectSettings settings = composition.audioSettings()
                ?? throw new InvalidOperationException("The audio settings source returned null.");
            runtime = new AudioRuntime(owner.types, device, composition.artifacts, owner.events, diagnostics,
                new AudioRuntimeOptions
                {
                    maxVoices = settings.maxVoices,
                    decodedCacheBudgetBytes = settings.decodedCacheBudgetBytes,
                    automaticStreamingThresholdBytes = settings.automaticStreamingThresholdBytes
                },
                contentScopeProvider: () => SceneContentSource.CreateScope(composition.session.scenes),
                deviceRecoveryFactory: () => composition.adapters.audio.CreateDevice(composition.selection.audio));
            if (settings.defaultMixer is not null && !runtime.ApplyMixer(settings.defaultMixer))
                throw new InvalidOperationException("The configured default mixer could not be activated.");
            if (!runtime.SetBusVolume(AudioBusId.master, settings.masterVolume))
                throw new InvalidOperationException("The configured master volume could not be applied.");
            return runtime;
        }
        catch (Exception failure)
        {
            try { if (runtime is null) device.Dispose(); else runtime.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException("Audio composition and rollback failed.", failure, cleanup); }
            throw;
        }
    }
}
