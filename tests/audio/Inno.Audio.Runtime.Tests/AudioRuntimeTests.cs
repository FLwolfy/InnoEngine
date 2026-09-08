using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Inno.Core.Diagnostics;
using System.Threading.Tasks;
using System.Threading;
using Inno.Assets;
using Inno.Audio.Runtime;
using Inno.Core.Events;
using Inno.Core.Execution;
using Inno.Core.Identity;
using Inno.References;
using Inno.Extensibility.Modules;
using Inno.Extensibility.Types;
using Xunit;

namespace Inno.Audio.Runtime.Tests;

public sealed class AudioRuntimeTests : IDisposable
{
    [Fact]
    public void InvalidPlaybackOptionsDoNotStealOrAcquireAnArtifact()
    {
        using var runtime = CreateRuntime(maxVoices: 1);
        AudioVoiceHandle previous = runtime.Play(CreateClip(48000), new AudioPlayOptions(loop: true));
        runtime.Update(0);
        AudioClipAsset next = CreateClip(48000);
        int acquisitions = m_artifacts.acquisitions;
        Assert.Throws<ArgumentException>(() => runtime.Play(next, default));
        Assert.Equal(acquisitions, m_artifacts.acquisitions);
        Assert.Equal(0, runtime.statistics.stolenVoiceCount);
        Assert.True(runtime.TryGetVoiceState(previous, out AudioPlaybackState state));
        Assert.Equal(AudioPlaybackState.Playing, state);
        Assert.False(runtime.SetVoiceParameters(previous, default));
        Assert.True(runtime.SetVoiceParameters(previous, new AudioVoiceParameters(0.5f, 1, 0)));
    }

    [Fact]
    public void ArtifactAcquisitionRetirementBarrierDoesNotStealOrBecomeDecodeFailure()
    {
        using var runtime = CreateRuntime(maxVoices: 1);
        AudioVoiceHandle previous = runtime.Play(CreateClip(48000), new AudioPlayOptions(loop: true));
        runtime.Update(0);
        AudioClipAsset next = CreateClip(48000);
        m_artifacts.acquisitionFailure = new RetirementPendingException("Expected artifact retirement barrier.");
        Assert.Throws<RetirementPendingException>(() => runtime.Play(next));
        Assert.Equal(0, runtime.statistics.stolenVoiceCount);
        Assert.Equal(1, runtime.statistics.activeVoices);
        Assert.True(runtime.TryGetVoiceState(previous, out AudioPlaybackState state));
        Assert.Equal(AudioPlaybackState.Playing, state);
        m_artifacts.acquisitionFailure = null;
    }

    [Fact]
    public void AdmissionRetryAfterCompletionBackpressureReleasesCandidateLeaseAndStealsOnce()
    {
        var events = new EventDispatcher(queueCapacity: 1);
        var device = new ReadyAudioDevice();
        using var reporter = new DiagnosticHub().CreateReporter(new DiagnosticSource("test.admission", "Admission"));
        using var runtime = new AudioRuntime(m_types, device, m_artifacts, events, reporter,
            new AudioRuntimeOptions { maxVoices = 1 });
        using EventHub hub = events.CreateHub();
        var completions = new List<AudioVoiceCompletedEvent>();
        hub.Listen<AudioVoiceCompletedEvent>(completions.Add);
        AudioVoiceHandle previous = runtime.Play(CreateClip(48000), new AudioPlayOptions(loop: true));
        runtime.Update(0);
        AudioClipAsset next = CreateClip(48000);
        events.Enqueue(new AudioVoiceCompletedEvent(new AudioVoiceAllocator().Allocate(), AudioCompletionReason.Stopped));
        m_artifacts.pendingReleaseId = next.identity.persistentId;
        m_artifacts.pendingReleases = 1;
        Assert.Throws<InvalidOperationException>(() => runtime.Play(next));
        Assert.Empty(m_artifacts.retainedKeys);
        Assert.Equal(3, m_artifacts.releaseAttempts);
        Assert.Equal(1, runtime.statistics.stolenVoiceCount);
        Assert.Equal(1, device.stops);
        events.Flush();
        completions.Clear();

        AudioVoiceHandle accepted = runtime.Play(next);
        Assert.True(accepted.isValid);
        Assert.Equal(1, runtime.statistics.activeVoices);
        Assert.Single(m_artifacts.retainedKeys);
        Assert.Equal(1, device.stops);
        Assert.Equal(1, runtime.statistics.stolenVoiceCount);
        events.Flush();
        AudioVoiceCompletedEvent completion = Assert.Single(completions);
        Assert.Equal(previous, completion.voice);
        Assert.Equal(AudioCompletionReason.Stolen, completion.reason);
    }

    [Fact]
    public void OrdinaryMissingMetadataStillCompletesThroughTheAsynchronousVoiceProtocol()
    {
        using var runtime = CreateRuntime();
        using EventHub hub = m_events.CreateHub();
        var completed = new List<AudioVoiceCompletedEvent>();
        hub.Listen<AudioVoiceCompletedEvent>(completed.Add);
        AudioVoiceHandle voice = runtime.Play(new AudioClipAsset());
        Assert.True(runtime.TryGetVoiceState(voice, out AudioPlaybackState preparing));
        Assert.Equal(AudioPlaybackState.Preparing, preparing);
        runtime.Update(0);
        m_events.Flush();
        Assert.Equal(voice, Assert.Single(completed).voice);
        Assert.Equal(AudioCompletionReason.DecodeFailed, completed[0].reason);
    }

    [Fact]
    public void UndefinedPreloadModeIsRejectedBeforeArtifactAcquisition()
    {
        using var runtime = CreateRuntime();
        AudioClipAsset clip = CreateClip(48000);
        Assert.Throws<ArgumentOutOfRangeException>(() => runtime.PreloadAsync(clip, (AudioClipLoadMode)999));
        Assert.Equal(0, m_artifacts.acquisitions);
        Assert.Equal(0, runtime.statistics.loadedClips);
    }

    [Fact]
    public void MutedBackendRejectsInvalidValuesWithTheSameAdmissionPolicy()
    {
        using var device = new MutedAudioDevice();
        IAudioDevice backend = device;
        AudioClipAsset asset = CreateClip(48000);
        using ArtifactLease artifact = m_artifacts.AcquireArtifact(asset.identity.persistentId, "audio-data");
        AudioBusHandle master = backend.CreateBus(AudioBusId.master);
        AudioClipHandle clip = backend.CreateClip(new AudioClipDescriptor(artifact.info.absolutePath,
            AudioCodecId.wav, AudioClipLoadMode.Decode, 2, 48000, 48000, artifact.info.length));
        Assert.False(backend.Play(clip, master, default).isValid);
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d })
            Assert.False(backend.Play(clip, master, AudioPlayOptions.defaultValue, invalid).isValid);
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            Assert.False(backend.SetBusVolume(master, invalid));
        Assert.Equal(0, backend.statistics.activeVoices);
        AudioDeviceVoiceHandle voice = backend.Play(clip, master, AudioPlayOptions.defaultValue);
        Assert.False(backend.SetVoiceParameters(voice, default));
        Assert.True(backend.SetVoiceParameters(voice, new AudioVoiceParameters(0.5f, 1, 0)));
        Assert.True(backend.Stop(voice));
        Assert.True(backend.DestroyClip(clip));
        Assert.True(backend.DestroyBus(master));
    }

    [Fact]
    public void ProviderFailureDiscardsItsEntireContributionAndReleasesItsIdentities()
    {
        var content = new TestAudioContent();
        var identities = new IdentityAllocator();
        identities.Register(content);
        AudioClipAsset clip = CreateClip(48000);
        var emitter = new AudioEmitterSnapshot(Guid.NewGuid(), clip, new AudioPlayOptions(loop: true), true);
        var listener = new AudioListenerSnapshot(Guid.NewGuid(), 0, default, true);
        content.first = context =>
        {
            context.Submit(emitter);
            context.Submit(listener);
            throw new InvalidOperationException("Expected partial provider failure.");
        };
        content.second = context => { context.Submit(emitter); context.Submit(listener); };
        var diagnostics = new DiagnosticHub();
        var sink = new RecordingSink();
        diagnostics.RegisterSink(sink);
        using var reporter = diagnostics.CreateReporter(new DiagnosticSource("test.content", "Content"));
        var device = new ReadyAudioDevice();
        using var runtime = new AudioRuntime(m_types, device, m_artifacts, m_events, reporter,
            contentScopeProvider: () => new ContentReadScope([content.identity]));

        runtime.Update(0);
        Assert.Equal(new AudioContentStatistics(1024, 2, 1), runtime.contentStatistics);
        Assert.Equal(1, runtime.statistics.activeVoices);
        Assert.Equal(1, device.listenerCreations);
        Assert.Single(sink.issues.Where(issue => issue.code == "AUDIO_CONTENT_PROVIDER_FAILED"));
        content.first = null;
        runtime.Update(0);
        Assert.Equal(0, runtime.contentStatistics.rejectedProviders);
        Assert.DoesNotContain(sink.issues, issue => issue.code == "AUDIO_CONTENT_PROVIDER_FAILED");
    }

    [Fact]
    public void CrossProviderDuplicateRejectsTheWholeBatchWithoutClaimingOtherIdentities()
    {
        var content = new TestAudioContent();
        var identities = new IdentityAllocator();
        identities.Register(content);
        AudioClipAsset clip = CreateClip(48000);
        var first = new AudioEmitterSnapshot(Guid.NewGuid(), clip, new AudioPlayOptions(loop: true), true);
        var second = new AudioEmitterSnapshot(Guid.NewGuid(), clip, new AudioPlayOptions(loop: true), true);
        content.first = context => context.Submit(first);
        content.second = context => { context.Submit(second); context.Submit(first); };
        content.third = context => context.Submit(second);
        using var reporter = new DiagnosticHub().CreateReporter(new DiagnosticSource("test.duplicate", "Duplicate"));
        using var runtime = new AudioRuntime(m_types, new ReadyAudioDevice(), m_artifacts, m_events, reporter,
            contentScopeProvider: () => new ContentReadScope([content.identity]));

        runtime.Update(0);
        Assert.Equal(new AudioContentStatistics(1024, 2, 1), runtime.contentStatistics);
        Assert.Equal(2, runtime.statistics.activeVoices);
    }

    [Fact]
    public void RepeatedOverflowIsBoundedAndCannotBeHiddenByAProviderCatch()
    {
        var content = new TestAudioContent();
        var identities = new IdentityAllocator();
        identities.Register(content);
        AudioClipAsset clip = CreateClip(48000);
        var first = new AudioEmitterSnapshot(Guid.NewGuid(), clip, new AudioPlayOptions(loop: true), true);
        var overflow = new AudioEmitterSnapshot(Guid.NewGuid(), clip, new AudioPlayOptions(loop: true), true);
        var last = new AudioEmitterSnapshot(Guid.NewGuid(), clip, new AudioPlayOptions(loop: true), true);
        content.first = context => context.Submit(first);
        content.second = context =>
        {
            context.Submit(overflow);
            try { context.Submit(last); }
            catch (InvalidOperationException) { }
        };
        content.third = context => context.Submit(last);
        using var reporter = new DiagnosticHub().CreateReporter(new DiagnosticSource("test.capacity", "Capacity"));
        var options = new AudioRuntimeOptions { maxContentSnapshots = 2 };
        using var runtime = new AudioRuntime(m_types, new ReadyAudioDevice(), m_artifacts, m_events, reporter,
            options, () => new ContentReadScope([content.identity]));
        options.maxContentSnapshots = 100;

        for (int frame = 0; frame < 256; frame++)
        {
            runtime.Update(0);
            Assert.Equal(new AudioContentStatistics(2, 2, 1), runtime.contentStatistics);
            Assert.Equal(2, runtime.statistics.activeVoices);
            Assert.Equal(1, runtime.statistics.loadedClips);
        }
        content.first = content.second = content.third = null;
        runtime.Update(0);
        Assert.Equal(new AudioContentStatistics(2, 0, 0), runtime.contentStatistics);
        Assert.Equal(0, runtime.statistics.activeVoices);
    }

    [Fact]
    public void ProviderContextAndContentLookupAreRevokedAfterTheUpdate()
    {
        var content = new TestAudioContent();
        var identities = new IdentityAllocator();
        identities.Register(content);
        AudioContentProviderContext? captured = null;
        ContentReadScope? capturedScope = null;
        content.first = context => { captured = context; capturedScope = context.content; };
        using var reporter = new DiagnosticHub().CreateReporter(new DiagnosticSource("test.scope", "Scope"));
        using var runtime = new AudioRuntime(m_types, new ReadyAudioDevice(), m_artifacts, m_events, reporter,
            contentScopeProvider: () => new ContentReadScope([content.identity]));
        runtime.Update(0);
        Assert.NotNull(captured);
        Assert.Throws<ObjectDisposedException>(() => captured!.Submit(default(AudioListenerSnapshot)));
        Assert.Throws<ObjectDisposedException>(() => _ = captured!.emitters);
        Assert.Throws<ObjectDisposedException>(() => capturedScope!.GetValues<TestAudioContent>());
    }

    [Fact]
    public void ProviderRetirementPendingIsNotConvertedToARejectedContribution()
    {
        var content = new TestAudioContent();
        var identities = new IdentityAllocator();
        identities.Register(content);
        bool nextInvoked = false;
        AudioContentProviderContext? captured = null;
        content.first = context =>
        {
            captured = context;
            throw new RetirementPendingException("Expected provider work drain.");
        };
        content.second = _ => nextInvoked = true;
        using var reporter = new DiagnosticHub().CreateReporter(new DiagnosticSource("test.pending-content", "Pending"));
        using var runtime = new AudioRuntime(m_types, new ReadyAudioDevice(), m_artifacts, m_events, reporter,
            contentScopeProvider: () => new ContentReadScope([content.identity]));
        Assert.Throws<RetirementPendingException>(() => runtime.Update(0));
        Assert.False(nextInvoked);
        Assert.Equal(0, runtime.contentStatistics.rejectedProviders);
        Assert.Throws<ObjectDisposedException>(() => _ = captured!.listeners);
    }

    [Theory]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void InvalidUpdateTimeIsRejectedBeforeProviderExecution(float deltaTime)
    {
        using var runtime = CreateRuntime();
        Assert.Throws<ArgumentOutOfRangeException>(() => runtime.Update(deltaTime));
    }

    private sealed class TestAudioContent : IdentityObject
    {
        internal Action<AudioContentProviderContext>? first;
        internal Action<AudioContentProviderContext>? second;
        internal Action<AudioContentProviderContext>? third;
    }

    [AudioContentProviderExtension("tests.content.first", -30)]
    private sealed class FirstContentProvider : AudioContentProvider
    {
        public override void Submit(AudioContentProviderContext context)
        {
            foreach (TestAudioContent content in context.content.GetValues<TestAudioContent>())
                content.first?.Invoke(context);
        }
    }

    [AudioContentProviderExtension("tests.content.second", -20)]
    private sealed class SecondContentProvider : AudioContentProvider
    {
        public override void Submit(AudioContentProviderContext context)
        {
            foreach (TestAudioContent content in context.content.GetValues<TestAudioContent>())
                content.second?.Invoke(context);
        }
    }

    [AudioContentProviderExtension("tests.content.third", -10)]
    private sealed class ThirdContentProvider : AudioContentProvider
    {
        public override void Submit(AudioContentProviderContext context)
        {
            foreach (TestAudioContent content in context.content.GetValues<TestAudioContent>())
                content.third?.Invoke(context);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PendingClipRetirementPreservesDeviceAndRetriesExactlyOnce(bool playing)
    {
        var device = new ReadyAudioDevice();
        using var reporter = new DiagnosticHub().CreateReporter(new DiagnosticSource("test.pending", "Pending"));
        var runtime = new AudioRuntime(m_types, device, m_artifacts, m_events, reporter);
        AudioClipAsset clip = CreateClip(48000);
        if (playing)
        {
            runtime.Play(clip);
            runtime.Update(0);
        }
        else
            await runtime.PreloadAsync(clip);
        device.pendingClipRetirements = 1;

        Assert.Throws<RetirementPendingException>(runtime.Dispose);
        Assert.Equal(0, device.disposals);
        Assert.Equal(0, device.busRetirements);
        Assert.Throws<InvalidOperationException>(() => runtime.Play(clip));

        runtime.Dispose();
        Assert.Equal(2, device.clipRetirements);
        Assert.Equal(1, device.busRetirements);
        Assert.Equal(1, device.disposals);
        runtime.Dispose();
        Assert.Equal(1, device.disposals);
    }

    [Fact]
    public void PendingBusRetirementDoesNotDestroyTheDeviceBeforeRetry()
    {
        var device = new ReadyAudioDevice { pendingBusRetirements = 1 };
        using var reporter = new DiagnosticHub().CreateReporter(new DiagnosticSource("test.bus", "Bus"));
        var runtime = new AudioRuntime(m_types, device, m_artifacts, m_events, reporter);
        Assert.Throws<RetirementPendingException>(runtime.Dispose);
        Assert.Equal(0, device.disposals);
        runtime.Dispose();
        Assert.Equal(2, device.busRetirements);
        Assert.Equal(1, device.disposals);
    }

    [Fact]
    public void PendingReplacementRetirementDrainsBeforePublishingTheNewDevice()
    {
        var previous = new ReadyAudioDevice { pendingBusRetirements = 1 };
        var replacement = new ReadyAudioDevice();
        using var reporter = new DiagnosticHub().CreateReporter(new DiagnosticSource("test.replacement", "Replacement"));
        using var runtime = new AudioRuntime(m_types, previous, m_artifacts, m_events, reporter);
        AudioVoiceHandle oldVoice = runtime.Play(CreateClip(48000));
        runtime.ReplaceDevice(replacement);
        Assert.Equal(2, previous.busRetirements);
        Assert.Equal(1, previous.disposals);
        Assert.Equal(0, replacement.disposals);
        Assert.False(runtime.Stop(oldVoice));
        Assert.True(runtime.TryGetVoiceState(oldVoice, out AudioPlaybackState completed));
        Assert.Equal(AudioPlaybackState.Completed, completed);
        Assert.NotEqual("Faulted", m_modules.generations.state.ToString());
    }

    [Fact]
    public void PendingProviderIsRetriedBeforeItsGenerationIsReleased()
    {
        using var runtime = CreateRuntime();
        runtime.Update(0);
        RetirementProviderFirst.disposals = 0;
        RetirementProviderFirst.pendingRetirements = 1;
        try
        {
            m_types.Rebuild();
            Assert.Equal(2, RetirementProviderFirst.disposals);
            Assert.NotEqual("Faulted", m_modules.generations.state.ToString());
        }
        finally { RetirementProviderFirst.pendingRetirements = 0; }
    }

    [Fact]
    public void CompletionBackpressureRetainsTheVoiceUntilItsEventIsAccepted()
    {
        var events = new EventDispatcher(queueCapacity: 1);
        using var reporter = new DiagnosticHub().CreateReporter(new DiagnosticSource("test.completion", "Completion"));
        using var runtime = new AudioRuntime(m_types, new ReadyAudioDevice(), m_artifacts, events, reporter);
        using EventHub hub = events.CreateHub();
        var completed = new List<AudioVoiceCompletedEvent>();
        hub.Listen<AudioVoiceCompletedEvent>(completed.Add);
        AudioVoiceHandle voice = runtime.Play(CreateClip(4800));
        events.Enqueue(new AudioVoiceCompletedEvent(voice, AudioCompletionReason.Stopped));

        Assert.Throws<InvalidOperationException>(() => runtime.Update(1));
        Assert.Equal(1, runtime.statistics.activeVoices);
        events.Flush();
        completed.Clear();
        runtime.Update(0);
        events.Flush();
        Assert.Equal(voice, Assert.Single(completed).voice);
        Assert.Equal(0, runtime.statistics.activeVoices);
        runtime.Update(0);
        events.Flush();
        Assert.Single(completed);
    }

    [Fact]
    public void CandidateCleanupFailureFaultsInsteadOfReportingRecoverableMixerFailure()
    {
        using var runtime = CreateRuntime();
        runtime.Update(0);
        RetirementProviderFirst.fail = true;
        RetirementProviderSecond.failConstruction = true;
        try
        {
            Assert.ThrowsAny<Exception>(m_types.Rebuild);
            Assert.Equal("Faulted", m_modules.generations.state.ToString());
            Assert.Throws<InvalidOperationException>(() => runtime.ApplyMixer(new AudioMixerAsset()));
        }
        finally
        {
            RetirementProviderFirst.fail = false;
            RetirementProviderSecond.failConstruction = false;
        }
    }

    [Fact]
    public async Task CacheRetirementAttemptsEveryClipBeforeDeviceDisposal()
    {
        var device = new ReadyAudioDevice();
        using var reporter = new DiagnosticHub().CreateReporter(new DiagnosticSource("test.retire", "Retire"));
        var runtime = new AudioRuntime(m_types, device, m_artifacts, m_events, reporter);
        await runtime.PreloadAsync(CreateClip(4800));
        await runtime.PreloadAsync(CreateClip(9600));
        device.failClipRetirement = true;
        Assert.ThrowsAny<Exception>(runtime.Dispose);
        Assert.Equal(2, device.clipRetirements);
        Assert.Equal(1, device.disposals);
        runtime.Dispose();
    }

    [Fact]
    public void FailedOldDeviceRetirementConsumesCandidateAndFaultsWithoutFalseRecovery()
    {
        var previous = new ReadyAudioDevice { failDisposal = true };
        var candidate = new ReadyAudioDevice();
        using var reporter = new DiagnosticHub().CreateReporter(new DiagnosticSource("test.replace", "Replace"));
        var runtime = new AudioRuntime(m_types, previous, m_artifacts, m_events, reporter);
        Assert.Throws<AggregateException>(() => runtime.ReplaceDevice(candidate));
        Assert.Equal(1, previous.disposals);
        Assert.Equal(1, candidate.disposals);
        Assert.Equal("Faulted", m_modules.generations.state.ToString());
        Assert.Throws<InvalidOperationException>(() => runtime.Update(0));
        previous.failDisposal = false;
        runtime.Dispose();
    }

    [Fact]
    public void RecoveryClearsMutedAndRecoveryFailureDiagnostics()
    {
        var diagnostics = new DiagnosticHub();
        var sink = new RecordingSink();
        diagnostics.RegisterSink(sink);
        using var reporter = diagnostics.CreateReporter(new DiagnosticSource("test.device", "Device"));
        using var runtime = new AudioRuntime(m_types, new MutedAudioDevice(), m_artifacts, m_events, reporter);
        Assert.Contains(sink.issues, issue => issue.code == "AUDIO_NO_DEVICE");
        Assert.False(runtime.TryRecoverDevice(static () => throw new InvalidOperationException("Device unavailable.")));
        Assert.Contains(sink.issues, issue => issue.code == "AUDIO_DEVICE_RECOVERY_FAILED");
        Assert.True(runtime.TryRecoverDevice(static () => new ReadyAudioDevice()));
        Assert.DoesNotContain(sink.issues, issue => issue.code is "AUDIO_NO_DEVICE" or "AUDIO_DEVICE_RECOVERY_FAILED");
    }

    [Fact]
    public void ProviderRetirementAttemptsEveryInstanceAndFaultsSharedGeneration()
    {
        using var runtime = CreateRuntime();
        runtime.Update(0);
        RetirementProviderFirst.disposals = 0;
        RetirementProviderSecond.disposals = 0;
        RetirementProviderFirst.fail = true;
        try
        {
            Assert.ThrowsAny<Exception>(m_types.Rebuild);
            Assert.Equal(1, RetirementProviderFirst.disposals);
            Assert.Equal(1, RetirementProviderSecond.disposals);
            Assert.Equal("Faulted", m_modules.generations.state.ToString());
        }
        finally { RetirementProviderFirst.fail = false; }
    }

    [AudioContentProviderExtension("tests.retirement.first", -100)]
    private sealed class RetirementProviderFirst : AudioContentProvider
    {
        internal static bool fail;
        internal static int disposals;
        internal static int pendingRetirements;
        public override void Submit(AudioContentProviderContext context) { }
        protected override void Dispose(bool disposing)
        {
            disposals++;
            if (pendingRetirements-- > 0)
                throw new RetirementPendingException("Expected provider drain.");
            if (fail)
                throw new InvalidOperationException("Expected provider retirement failure.");
        }
    }

    [AudioContentProviderExtension("tests.retirement.second", 100)]
    private sealed class RetirementProviderSecond : AudioContentProvider
    {
        internal static bool failConstruction;
        internal static int disposals;
        public RetirementProviderSecond()
        {
            if (failConstruction)
                throw new InvalidOperationException("Expected provider candidate failure.");
        }
        public override void Submit(AudioContentProviderContext context) { }
        protected override void Dispose(bool disposing) => disposals++;
    }

    [Fact]
    public void RecoveredMixerResolvesItsPreviousMissingDiagnostic()
    {
        var diagnostics = new DiagnosticHub();
        var sink = new RecordingSink();
        diagnostics.RegisterSink(sink);
        using var reporter = diagnostics.CreateReporter(new DiagnosticSource("test.mixer", "Mixer"));
        using var runtime = new AudioRuntime(m_types, new MutedAudioDevice(), m_artifacts, m_events, reporter);
        var mixer = new AudioMixerAsset { mixerTypeId = "tests.unavailable" };
        Assert.False(runtime.ApplyMixer(mixer));
        Assert.Contains(sink.issues, issue => issue.code == "AUDIO_MIXER_EXTENSION_MISSING");
        mixer.mixerTypeId = string.Empty;
        Assert.True(runtime.ApplyMixer(mixer));
        Assert.DoesNotContain(sink.issues, issue => issue.code == "AUDIO_MIXER_EXTENSION_MISSING");
    }

    private sealed class RecordingSink : IDiagnosticSink
    {
        internal IReadOnlyList<Diagnostic> issues { get; private set; } = [];
        public void Replace(DiagnosticReport report) => issues = report.diagnostics;
        public void Clear(DiagnosticSource source) => issues = [];
    }

    private readonly FakeArtifactLookup m_artifacts = new();
    private readonly EventDispatcher m_events = new();
    private readonly string m_root;
    private readonly ModuleHost m_modules;
    private readonly TypeCatalog m_types;

    public AudioRuntimeTests()
    {
        m_root = Path.Combine(Path.GetTempPath(), "InnoAudioRuntimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(m_root);
        m_modules = new ModuleHost(new ModuleHostOptions
        {
            cacheDirectory = Path.Combine(m_root, "Assemblies")
        });
        _ = typeof(TestMixerExtension);
        m_types = new TypeCatalog(m_modules);
        m_types.Rebuild();
    }

    public void Dispose()
    {
        m_types.Dispose();
        m_modules.Dispose();
        if (Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }

    [Fact]
    public void PlayTransitionsFromPreparingToNaturalCompletionOnMainThread()
    {
        AudioClipAsset clip = CreateClip(frameCount: 4800);
        using var runtime = CreateRuntime();
        using EventHub hub = m_events.CreateHub();
        var completions = new List<AudioVoiceCompletedEvent>();
        hub.Listen<AudioVoiceCompletedEvent>(completions.Add);

        AudioVoiceHandle voice = runtime.Play(clip);
        Assert.True(runtime.TryGetVoiceState(voice, out AudioPlaybackState preparing));
        Assert.Equal(AudioPlaybackState.Preparing, preparing);

        runtime.Update(0.01f);
        Assert.True(runtime.TryGetVoiceState(voice, out AudioPlaybackState playing));
        Assert.Equal(AudioPlaybackState.Playing, playing);
        runtime.Update(0.2f);
        m_events.Flush();

        Assert.True(runtime.TryGetVoiceState(voice, out AudioPlaybackState completed));
        Assert.Equal(AudioPlaybackState.Completed, completed);
        Assert.Single(completions);
        Assert.Equal(AudioCompletionReason.NaturalEnd, completions[0].reason);
    }

    [Fact]
    public void ScheduledLoopingVoiceSupportsPauseResumeAndSeek()
    {
        AudioClipAsset clip = CreateClip(frameCount: 4800);
        using var runtime = CreateRuntime();
        AudioVoiceHandle voice = runtime.PlayScheduled(
            clip,
            1d,
            new AudioPlayOptions(loop: true));

        runtime.Update(0.1f);
        Assert.True(runtime.TryGetVoiceState(voice, out AudioPlaybackState scheduled));
        Assert.Equal(AudioPlaybackState.Scheduled, scheduled);
        Assert.True(runtime.Pause(voice));
        Assert.True(runtime.Seek(voice, TimeSpan.FromMilliseconds(50)));
        Assert.True(runtime.Resume(voice));
        runtime.Update(1f);
        runtime.Update(1f);

        Assert.True(runtime.TryGetVoiceState(voice, out AudioPlaybackState playing));
        Assert.Equal(AudioPlaybackState.Playing, playing);
    }

    [Fact]
    public void VoiceBudgetStealsLowestPriorityThenOldestVoice()
    {
        AudioClipAsset clip = CreateClip(frameCount: 480000);
        using var runtime = CreateRuntime(maxVoices: 2);
        using EventHub hub = m_events.CreateHub();
        var completions = new List<AudioVoiceCompletedEvent>();
        hub.Listen<AudioVoiceCompletedEvent>(completions.Add);
        AudioVoiceHandle first = runtime.Play(clip, new AudioPlayOptions(priority: 1));
        AudioVoiceHandle second = runtime.Play(clip, new AudioPlayOptions(priority: 10));

        AudioVoiceHandle third = runtime.Play(clip, new AudioPlayOptions(priority: 0));
        m_events.Flush();

        Assert.Equal(2, runtime.statistics.activeVoices);
        Assert.True(runtime.TryGetVoiceState(first, out AudioPlaybackState firstState));
        Assert.Equal(AudioPlaybackState.Completed, firstState);
        Assert.True(runtime.TryGetVoiceState(second, out AudioPlaybackState secondState));
        Assert.Equal(AudioPlaybackState.Preparing, secondState);
        Assert.True(runtime.TryGetVoiceState(third, out AudioPlaybackState thirdState));
        Assert.Equal(AudioPlaybackState.Preparing, thirdState);
        Assert.Single(completions);
        Assert.Equal(AudioCompletionReason.Stolen, completions[0].reason);
    }

    [Fact]
    public async Task PreloadRetainsDistinctDecodeAndStreamEntriesUntilReleased()
    {
        AudioClipAsset clip = CreateClip(frameCount: 4800);
        using var runtime = CreateRuntime();

        await runtime.PreloadAsync(clip, AudioClipLoadMode.Decode);
        await runtime.PreloadAsync(clip, AudioClipLoadMode.Stream);
        Assert.Equal(2, runtime.statistics.loadedClips);

        runtime.ReleasePreload(clip);
        runtime.ReleasePreload(clip);
        Assert.Equal(0, runtime.statistics.loadedClips);
    }

    [Fact]
    public async Task AutomaticPreparationStreamsWhenDecodedFootprintExceedsBudget()
    {
        AudioClipAsset clip = CreateClip(frameCount: 4800);
        using var runtime = CreateRuntime(
            decodedCacheBudgetBytes: 1024,
            automaticStreamingThresholdBytes: long.MaxValue);

        await runtime.PreloadAsync(clip);

        Assert.Equal(1, runtime.statistics.loadedClips);
        Assert.Equal(0, runtime.statistics.decodedBytes);
        Assert.Throws<InvalidOperationException>(() => runtime.PreloadAsync(clip, AudioClipLoadMode.Decode));
    }

    [Fact]
    public void MixerExtensionBuildsOpenBusGraphAndMissingCandidateKeepsLastGood()
    {
        using var runtime = CreateRuntime();
        var mixer = new AudioMixerAsset { mixerTypeId = "tests.audio.mixer" };

        Assert.True(runtime.ApplyMixer(mixer));
        Assert.True(runtime.SetBusVolume(new AudioBusId("tests.audio.bus.music"), 0.5f));

        mixer.mixerTypeId = "tests.audio.missing";
        Assert.False(runtime.ApplyMixer(mixer));
        Assert.True(runtime.SetBusMuted(new AudioBusId("tests.audio.bus.music"), true));
    }

    [Fact]
    public void DeviceReplacementCompletesOldHandlesAndUsesANewGeneration()
    {
        AudioClipAsset clip = CreateClip(frameCount: 480000);
        using var runtime = CreateRuntime();
        AudioVoiceHandle oldVoice = runtime.Play(clip);
        runtime.Update(0f);

        runtime.ReplaceDevice(new MutedAudioDevice());
        AudioVoiceHandle newVoice = runtime.Play(clip);

        Assert.NotEqual(oldVoice.ownerGeneration, newVoice.ownerGeneration);
        Assert.True(runtime.TryGetVoiceState(oldVoice, out AudioPlaybackState state));
        Assert.Equal(AudioPlaybackState.Completed, state);
        Assert.False(runtime.Stop(oldVoice));
    }

    [Fact]
    public void RecoveryCandidatePreservesTheLastGoodMixerGraph()
    {
        using var runtime = CreateRuntime();
        var mixer = new AudioMixerAsset { mixerTypeId = "tests.audio.mixer" };
        Assert.True(runtime.ApplyMixer(mixer));
        AudioBusId music = new("tests.audio.bus.music");
        Assert.True(runtime.SetBusVolume(music, 0.25f));
        Assert.True(runtime.SetBusPaused(music, true));
        var replacement = new ReadyAudioDevice();

        Assert.True(runtime.TryRecoverDevice(() => replacement));

        Assert.Equal(AudioDeviceState.Ready, runtime.deviceState);
        Assert.Equal(0.25f, replacement.GetBusVolume(music));
        Assert.True(replacement.GetBusPaused(music));
    }

    [Fact]
    public async Task PreloadCompletesOnlyAfterPreparationAndCancellationReleasesReservation()
    {
        var device = new ReadyAudioDevice { clipState = AudioClipState.Preparing };
        using var runtime = new AudioRuntime(m_types, device, m_artifacts, m_events,
            new Inno.Core.Diagnostics.DiagnosticHub().CreateReporter(new Inno.Core.Diagnostics.DiagnosticSource("test.preload", "Preload")));
        AudioClipAsset clip = CreateClip(4800);
        ValueTask ready = runtime.PreloadAsync(clip);
        Assert.False(ready.IsCompleted);
        device.clipState = AudioClipState.Ready;
        runtime.Update(0f);
        await ready;
        runtime.ReleasePreload(clip);
        Assert.Equal(0, runtime.statistics.loadedClips);
        device.clipState = AudioClipState.Preparing;
        using var cancellation = new CancellationTokenSource();
        ValueTask canceled = runtime.PreloadAsync(clip, cancellationToken: cancellation.Token);
        cancellation.Cancel();
        runtime.Update(0f);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled.AsTask());
        Assert.Equal(0, runtime.statistics.loadedClips);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreloadedArtifactRetirementResumesWithoutDestroyingTheNativeClipTwice(bool shutdown)
    {
        var device = new ReadyAudioDevice();
        using var reporter = new DiagnosticHub().CreateReporter(new DiagnosticSource("test.artifact", "Artifact"));
        using var runtime = new AudioRuntime(m_types, device, m_artifacts, m_events, reporter);
        AudioClipAsset clip = CreateClip(4800);
        await runtime.PreloadAsync(clip);
        m_artifacts.pendingReleaseId = clip.identity.persistentId;
        m_artifacts.pendingReleases = 1;
        Action release = shutdown ? runtime.Dispose : () => runtime.ReleasePreload(clip);
        Assert.Throws<RetirementPendingException>(release);
        Assert.Equal(1, device.clipRetirements);
        Assert.Single(m_artifacts.retainedKeys);
        Assert.Equal(0, device.disposals);
        if (shutdown)
            runtime.Dispose();
        else
            runtime.Update(0);
        Assert.Equal(1, device.clipRetirements);
        Assert.Empty(m_artifacts.retainedKeys);
        if (!shutdown)
            Assert.Equal(0, runtime.statistics.loadedClips);
    }

    [Fact]
    public void CanceledPreloadDoesNotCompleteBeforeItsPendingArtifactRetires()
    {
        var device = new ReadyAudioDevice { clipState = AudioClipState.Preparing };
        using var reporter = new DiagnosticHub().CreateReporter(new DiagnosticSource("test.cancel", "Cancel"));
        using var runtime = new AudioRuntime(m_types, device, m_artifacts, m_events, reporter);
        AudioClipAsset clip = CreateClip(4800);
        using var cancellation = new CancellationTokenSource();
        Task pending = runtime.PreloadAsync(clip, cancellationToken: cancellation.Token).AsTask();
        m_artifacts.pendingReleaseId = clip.identity.persistentId;
        m_artifacts.pendingReleases = 1;
        cancellation.Cancel();
        Assert.Throws<RetirementPendingException>(() => runtime.Update(0));
        Assert.False(pending.IsCompleted);
        Assert.Single(m_artifacts.retainedKeys);
        Assert.Equal(1, device.clipRetirements);
        runtime.Update(0);
        Assert.True(pending.IsCanceled);
        Assert.Equal(1, device.clipRetirements);
        Assert.Empty(m_artifacts.retainedKeys);
        Assert.Equal(0, runtime.statistics.loadedClips);
    }

    [Fact]
    public async Task APartiallyRetiredClipCannotBeReusedByANewVoice()
    {
        var device = new ReadyAudioDevice();
        using var reporter = new DiagnosticHub().CreateReporter(new DiagnosticSource("test.reuse", "Reuse"));
        using var runtime = new AudioRuntime(m_types, device, m_artifacts, m_events, reporter);
        AudioClipAsset clip = CreateClip(4800);
        await runtime.PreloadAsync(clip);
        m_artifacts.pendingReleaseId = clip.identity.persistentId;
        m_artifacts.pendingReleases = 1;
        Assert.Throws<RetirementPendingException>(() => runtime.ReleasePreload(clip));
        AudioVoiceHandle voice = runtime.Play(clip, new AudioPlayOptions(loop: true));
        runtime.Update(0);
        Assert.True(runtime.TryGetVoiceState(voice, out AudioPlaybackState state));
        Assert.Equal(AudioPlaybackState.Playing, state);
        Assert.Equal(1, device.clipRetirements);
        Assert.Single(m_artifacts.retainedKeys);
        runtime.Stop(voice);
        runtime.Update(0);
        Assert.Equal(2, device.clipRetirements);
        Assert.Empty(m_artifacts.retainedKeys);
    }

    [Fact]
    public async Task CacheHitRequestRetiresItsExtraArtifactThroughTheSharedGenerationBarrier()
    {
        using var runtime = CreateRuntime();
        AudioClipAsset clip = CreateClip(4800);
        await runtime.PreloadAsync(clip);
        m_artifacts.pendingReleaseId = clip.identity.persistentId;
        m_artifacts.pendingReleases = 1;
        await runtime.PreloadAsync(clip);
        Assert.Equal(2, m_artifacts.releaseAttempts);
        runtime.ReleasePreload(clip);
        Assert.Single(m_artifacts.retainedKeys);
        runtime.ReleasePreload(clip);
        Assert.Empty(m_artifacts.retainedKeys);
    }

    [Fact]
    public async Task RetryingPreloadReleaseDoesNotConsumeAnotherLoadModeReservation()
    {
        using var runtime = CreateRuntime();
        AudioClipAsset clip = CreateClip(4800);
        await runtime.PreloadAsync(clip, AudioClipLoadMode.Decode);
        await runtime.PreloadAsync(clip, AudioClipLoadMode.Stream);
        m_artifacts.pendingReleaseId = clip.identity.persistentId;
        m_artifacts.pendingReleases = 1;
        Assert.Throws<RetirementPendingException>(() => runtime.ReleasePreload(clip));
        runtime.ReleasePreload(clip);
        Assert.Equal(1, runtime.statistics.loadedClips);
        Assert.Single(m_artifacts.retainedKeys);
        runtime.ReleasePreload(clip);
        Assert.Equal(0, runtime.statistics.loadedClips);
        Assert.Empty(m_artifacts.retainedKeys);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FailedPreloadAdmissionRetiresOnlyItsOwnReservation(bool cached, bool queryThrows)
    {
        var device = new ReadyAudioDevice();
        using var reporter = new DiagnosticHub().CreateReporter(new DiagnosticSource("test.query", "Query"));
        using var runtime = new AudioRuntime(m_types, device, m_artifacts, m_events, reporter);
        AudioClipAsset clip = CreateClip(4800);
        if (cached)
            await runtime.PreloadAsync(clip);
        if (queryThrows)
            device.clipQueryFailure = new InvalidOperationException("Expected clip query failure.");
        else
            device.clipState = AudioClipState.Failed;
        m_artifacts.pendingReleaseId = clip.identity.persistentId;
        m_artifacts.pendingReleases = 1;
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => runtime.PreloadAsync(clip));
        Assert.Contains(queryThrows ? "query failure" : "preparation failed", failure.Message);
        Assert.Equal(cached ? 1 : 0, runtime.statistics.loadedClips);
        Assert.Equal(cached ? 0 : 1, device.clipRetirements);
        Assert.Equal(2, m_artifacts.releaseAttempts);
        device.clipQueryFailure = null;
        device.clipState = AudioClipState.Ready;
        if (cached)
        {
            Assert.Single(m_artifacts.retainedKeys);
            runtime.ReleasePreload(clip);
        }
        Assert.Empty(m_artifacts.retainedKeys);
        Assert.Equal(0, runtime.statistics.loadedClips);
        await runtime.PreloadAsync(clip);
        runtime.ReleasePreload(clip);
        Assert.Empty(m_artifacts.retainedKeys);
    }

    [Fact]
    public void PreloadQueryAndRollbackFailureRetainBothDiagnosticsAndFaultTheGeneration()
    {
        var device = new ReadyAudioDevice
        {
            clipQueryFailure = new InvalidOperationException("Expected clip query failure."),
            failClipRetirement = true
        };
        using var reporter = new DiagnosticHub().CreateReporter(new DiagnosticSource("test.rollback", "Rollback"));
        using var runtime = new AudioRuntime(m_types, device, m_artifacts, m_events, reporter);
        AudioClipAsset clip = CreateClip(4800);
        AggregateException failure = Assert.Throws<AggregateException>(() => runtime.PreloadAsync(clip));
        Assert.Contains(failure.Flatten().InnerExceptions, error => error.Message == "Expected clip query failure.");
        Assert.Contains(failure.Flatten().InnerExceptions, error => error.Message == "Expected clip retirement failure.");
        Assert.Empty(m_artifacts.retainedKeys);
        Assert.Equal("Faulted", m_modules.generations.state.ToString());
        Assert.Throws<InvalidOperationException>(() => runtime.Update(0));
        runtime.Dispose();
        Assert.Equal(1, device.clipRetirements);
        Assert.Equal(1, device.disposals);
    }

    private AudioRuntime CreateRuntime(
        int maxVoices = 128,
        long decodedCacheBudgetBytes = 128L * 1024 * 1024,
        long automaticStreamingThresholdBytes = 1024)
        => new(
            m_types,
            new MutedAudioDevice(),
            m_artifacts,
            m_events,
            new Inno.Core.Diagnostics.DiagnosticHub().CreateReporter(new Inno.Core.Diagnostics.DiagnosticSource("tests.audio", "Audio")),
            options: new AudioRuntimeOptions
            {
                maxVoices = maxVoices,
                decodedCacheBudgetBytes = decodedCacheBudgetBytes,
                automaticStreamingThresholdBytes = automaticStreamingThresholdBytes
            });

    private AudioClipAsset CreateClip(long frameCount)
    {
        var clip = new AudioClipAsset();
        byte[] payload = AudioClipMetadataCodec.Encode(new AudioClipMetadata(
            AudioCodecId.wav,
            2,
            48000,
            frameCount,
            256));
        new AssetRuntimeOwner().Initialize(
            clip,
            AssetPath.Project($"Audio/{clip.identity.persistentId:N}.wav"),
            "TEST",
            payload,
            isMissing: false,
            version: 1);
        m_artifacts.Add(clip.identity.persistentId, Path.Combine(m_root, $"{clip.identity.persistentId:N}.wav"));
        return clip;
    }

    [AudioMixerExtension("tests.audio.mixer")]
    private sealed class TestMixerExtension : AudioMixerExtension
    {
        public override void Build(AudioMixerBuilder builder, SerializedAudioExtensionState state)
        {
            builder.AddBus(new AudioBusId("tests.audio.bus.music"), AudioBusId.master);
            builder.AddProcessor(
                new AudioBusId("tests.audio.bus.music"),
                new AudioProcessorConfiguration(AudioProcessorId.lowPass));
        }
    }

    private sealed class FakeArtifactLookup : AssetResidencyProvider, IAssetArtifactLookup
    {
        private readonly ArtifactRetention m_retention = new();
        internal int acquisitions;
        internal Exception? acquisitionFailure;
        internal Guid pendingReleaseId;
        internal int pendingReleases;
        internal int releaseAttempts;
        internal IReadOnlyList<AssetArtifactKey> retainedKeys => m_retention.GetRetainedKeys();
        public ArtifactLease AcquireArtifact(Guid persistentId, string outputName)
        {
            acquisitions++;
            if (acquisitionFailure is not null)
                throw acquisitionFailure;
            ArtifactLease retained = m_retention.Retain(m_artifacts[persistentId]);
            return CreateArtifactLease(retained.info, () =>
            {
                releaseAttempts++;
                if (persistentId == pendingReleaseId && pendingReleases-- > 0)
                    throw new RetirementPendingException("Expected admitted artifact drain.");
                retained.Dispose();
            });
        }
        private readonly Dictionary<Guid, AssetArtifactInfo> m_artifacts = [];

        internal void Add(Guid id, string path)
        {
            File.WriteAllBytes(path, new byte[256]);
            m_artifacts[id] = new AssetArtifactInfo(
                new AssetArtifactKey("AABB"),
                "audio-data",
                path,
                "TEST",
                256);
        }

        public bool TryGetArtifact(Guid persistentId, string outputName, out AssetArtifactInfo? artifact)
        {
            if (outputName == "audio-data" && m_artifacts.TryGetValue(persistentId, out AssetArtifactInfo? found))
            {
                artifact = found;
                return true;
            }
            artifact = null;
            return false;
        }
    }

    private sealed class ReadyAudioDevice : IAudioDevice
    {
        private readonly Dictionary<AudioBusHandle, AudioBusId> m_busIds = [];
        private readonly Dictionary<AudioBusId, bool> m_busPaused = [];
        private readonly Dictionary<AudioBusId, float> m_busVolumes = [];
        private readonly IAudioDevice m_inner = new MutedAudioDevice();
        internal bool failClipRetirement;
        internal bool failDisposal;
        internal int clipRetirements;
        internal int disposals;
        internal int pendingClipRetirements;
        internal int pendingBusRetirements;
        internal int busRetirements;
        internal int listenerCreations;
        internal int stops;
        internal Exception? clipQueryFailure;

        public AudioCapabilities capabilities => m_inner.capabilities;

        public uint generation => m_inner.generation;

        public AudioDeviceState state => AudioDeviceState.Ready;

        public double dspTime => m_inner.dspTime;

        public AudioStatistics statistics => m_inner.statistics;

        public AudioClipHandle CreateClip(AudioClipDescriptor descriptor) => m_inner.CreateClip(descriptor);
        internal AudioClipState clipState = AudioClipState.Ready;
        public AudioClipState GetClipState(AudioClipHandle clip)
        {
            if (clipQueryFailure is not null)
                throw clipQueryFailure;
            return clipState;
        }

        public bool DestroyClip(AudioClipHandle clip)
        {
            clipRetirements++;
            if (pendingClipRetirements-- > 0)
                throw new RetirementPendingException("Expected clip drain.");
            if (failClipRetirement)
                throw new InvalidOperationException("Expected clip retirement failure.");
            return m_inner.DestroyClip(clip);
        }

        public AudioDeviceVoiceHandle Play(
            AudioClipHandle clip,
            AudioBusHandle bus,
            AudioPlayOptions options,
            double? scheduledDspTime = null)
            => m_inner.Play(clip, bus, options, scheduledDspTime);

        public bool Stop(AudioDeviceVoiceHandle voice)
        {
            stops++;
            return m_inner.Stop(voice);
        }

        public bool Pause(AudioDeviceVoiceHandle voice) => m_inner.Pause(voice);

        public bool Resume(AudioDeviceVoiceHandle voice) => m_inner.Resume(voice);

        public bool Seek(AudioDeviceVoiceHandle voice, TimeSpan position) => m_inner.Seek(voice, position);

        public bool SetVoiceParameters(AudioDeviceVoiceHandle voice, AudioVoiceParameters parameters)
            => m_inner.SetVoiceParameters(voice, parameters);

        public bool TryGetVoiceState(AudioDeviceVoiceHandle voice, out AudioPlaybackState playbackState)
            => m_inner.TryGetVoiceState(voice, out playbackState);

        public AudioBusHandle CreateBus(AudioBusId id, AudioBusHandle parent = default)
        {
            AudioBusHandle handle = m_inner.CreateBus(id, parent);
            if (handle.isValid)
                m_busIds.Add(handle, id);
            return handle;
        }

        public bool DestroyBus(AudioBusHandle bus)
        {
            busRetirements++;
            if (pendingBusRetirements-- > 0)
                throw new RetirementPendingException("Expected bus drain.");
            return m_inner.DestroyBus(bus);
        }

        public bool SetBusVolume(AudioBusHandle bus, float volume)
        {
            if (!m_inner.SetBusVolume(bus, volume))
                return false;
            m_busVolumes[m_busIds[bus]] = volume;
            return true;
        }

        public bool SetBusMuted(AudioBusHandle bus, bool muted) => m_inner.SetBusMuted(bus, muted);

        public bool SetBusPaused(AudioBusHandle bus, bool paused)
        {
            if (!m_inner.SetBusPaused(bus, paused))
                return false;
            m_busPaused[m_busIds[bus]] = paused;
            return true;
        }

        public bool AddBusProcessor(AudioBusHandle bus, AudioProcessorConfiguration processor)
            => m_inner.AddBusProcessor(bus, processor);

        public AudioListenerHandle CreateListener(AudioListenerState state)
        {
            listenerCreations++;
            return m_inner.CreateListener(state);
        }

        public bool SetListener(AudioListenerHandle listener, AudioListenerState state)
            => m_inner.SetListener(listener, state);

        public bool DestroyListener(AudioListenerHandle listener) => m_inner.DestroyListener(listener);

        public void Update(float deltaTime) => m_inner.Update(deltaTime);

        public bool TryDequeueCompletion(out AudioDeviceCompletion completion)
            => m_inner.TryDequeueCompletion(out completion);

        public void Dispose()
        {
            disposals++;
            m_inner.Dispose();
            if (failDisposal)
                throw new InvalidOperationException("Expected device retirement failure.");
        }

        internal float GetBusVolume(AudioBusId id) => m_busVolumes[id];

        internal bool GetBusPaused(AudioBusId id) => m_busPaused[id];
    }
}
