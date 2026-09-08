using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Core.Diagnostics;
using Inno.Core.Identity;
using Inno.Core.Logging;
using Inno.Core.Serialization;
using Inno.Core.Settings;
using Inno.Extensibility.Modules;
using Inno.Extensibility.Types;
using Inno.Extensibility.Reload;
using Xunit;

namespace Inno.Assets.Pipeline.Tests;

public sealed class SettingsReferenceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SettingsResolveTheCurrentAssetGenerationAndPreserveMissingIntent(bool composed)
    {
        using var fixture = new Fixture();
        string source = Path.Combine(fixture.assetRoot, "value.txt");
        File.WriteAllText(source, "original");
        TextAsset clip = fixture.assets.Load<TextAsset>(AssetPath.Project("value.txt"));
        byte[] metadata = File.ReadAllBytes(source + ".imeta");
        ProjectSettingId id = composed ? ComposedClipSettings.id : ClipSettings.id;
        ClipSettingsBase value = composed ? new ComposedClipSettings() : new ClipSettings();
        value.clip = clip;
        fixture.settings.SetProjectOverride(id, value, []);
        byte[] document = fixture.settings.CaptureDocument();
        Assert.Same(clip, fixture.settings.Get<ClipSettingsBase>(id).clip);

        File.Delete(source);
        fixture.assets.ReplaceSourceMounts(fixture.assets.sourceMounts);
        TextAsset missing = Assert.IsType<TextAsset>(fixture.settings.Get<ClipSettingsBase>(id).clip);
        Assert.True(missing.isMissing);
        Assert.Equal(clip.identity.persistentId, missing.identity.persistentId);
        Assert.Equal(document, fixture.settings.CaptureDocument());

        File.WriteAllText(source, "restored");
        File.WriteAllBytes(source + ".imeta", metadata);
        fixture.assets.ReplaceSourceMounts(fixture.assets.sourceMounts);
        TextAsset restored = Assert.IsType<TextAsset>(fixture.settings.Get<ClipSettingsBase>(id).clip);
        Assert.False(restored.isMissing);
        Assert.Equal(clip.identity.persistentId, restored.identity.persistentId);
        Assert.Equal("restored", restored.content);
        Assert.NotSame(clip, restored);
        Assert.Equal(document, fixture.settings.CaptureDocument());
        fixture.settings.RestoreDocument(document);
        Assert.Same(restored, fixture.settings.Get<ClipSettingsBase>(id).clip);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RuntimeSettingsLoadTheirColdReferencesThroughTheSessionAssetOwner(bool composed)
    {
        using var fixture = new Fixture();
        File.WriteAllText(Path.Combine(fixture.assetRoot, "value.txt"), "deployed");
        TextAsset clip = fixture.assets.Load<TextAsset>(AssetPath.Project("value.txt"));
        ProjectSettingId id = composed ? ComposedClipSettings.id : ClipSettings.id;
        ClipSettingsBase value = composed ? new ComposedClipSettings() : new ClipSettings();
        value.clip = clip;
        fixture.settings.SetProjectOverride(id, value, []);
        string content = Path.Combine(fixture.root, "Content");
        fixture.assets.ExportRuntimeArtifacts(content);
        string document = Path.Combine(content, "Settings.Project.inno");
        File.WriteAllBytes(document, fixture.settings.CaptureDocument());
        using SerializationGeneration serialization = fixture.serialization.CaptureGeneration();
        var identities = new IdentityAllocator();
        using var database = new AssetDatabase(content, serialization, fixture.types.current, identities);
        Assert.Null(identities.Get<AssetObject>(clip.identity.persistentId));

        using var settings = new ProjectSettingsStore(document, fixture.types, fixture.serialization,
            new ProjectId("tests.deployed.settings"), AssetSerializationContext.Create(database));

        TextAsset runtime = Assert.IsType<TextAsset>(settings.Get<ClipSettingsBase>(id).clip);
        Assert.Equal("deployed", runtime.content);
        Assert.Same(runtime, identities.Get<AssetObject>(clip.identity.persistentId));
        Assert.NotSame(clip, runtime);
    }

    [Fact]
    public void FivePhaseSettingsRollbackRestoresExactEffectiveStateWithoutWritingDocument()
    {
        using var fixture = new Fixture();
        byte[] first = fixture.serialization.CapturePropertiesData(new ClipSettings { labels = ["previous"] }, fixture.context);
        byte[] next = fixture.serialization.CapturePropertiesData(new ClipSettings { labels = ["candidate"] }, fixture.context);
        fixture.settings.Rebuild([new ProjectSettingsContributor("source", [], [], [new ProjectSettingRecord(ClipSettings.id, ClipSettings.typeId, first)])]);
        byte[] document = fixture.settings.CaptureDocument();
        long revision = fixture.settings.revision;
        IGenerationChange change = fixture.settings.CreateReloadChange();
        change.PrepareForActivation();
        fixture.settings.SetContributors([new ProjectSettingsContributor("source", [], [], [new ProjectSettingRecord(ClipSettings.id, ClipSettings.typeId, next)])]);
        change.Apply();
        Assert.Equal("candidate", Assert.Single(fixture.settings.Get<ClipSettings>(ClipSettings.id).labels));
        change.RollbackStructure();
        change.RestorePreviousState();
        Assert.Equal(revision, fixture.settings.revision);
        Assert.Equal(document, fixture.settings.CaptureDocument());
        Assert.Equal("previous", Assert.Single(fixture.settings.Get<ClipSettings>(ClipSettings.id).labels));
        fixture.settings.RebuildCurrent();
        Assert.Equal("previous", Assert.Single(fixture.settings.Get<ClipSettings>(ClipSettings.id).labels));
    }

    [Fact]
    public void ContributorPublicationFreezesNestedPayloadsAndKeepsLastGoodOnFailure()
    {
        using var fixture = new Fixture();
        var value = new ClipSettings { labels = ["stable"] };
        byte[] payload = fixture.serialization.CapturePropertiesData(value, fixture.context);
        var record = new ProjectSettingRecord(ClipSettings.id, ClipSettings.typeId, payload);
        string[] dependencies = ["base"];
        var contributor = new ProjectSettingsContributor("extension", dependencies, [], [record]);
        dependencies[0] = "changed";
        record.propertyData[0] ^= 1;
        contributor.settings[0].propertyData[0] ^= 1;
        Assert.Equal("base", Assert.Single(contributor.dependencies));
        Assert.Equal(payload, Assert.Single(contributor.settings).propertyData);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)contributor.dependencies).Clear());
        fixture.settings.Rebuild([new ProjectSettingsContributor("base", [], [], []), contributor]);
        Assert.Equal("stable", Assert.Single(fixture.settings.Get<ClipSettings>(ClipSettings.id).labels));

        long revision = fixture.settings.revision;
        Assert.ThrowsAny<Exception>(() => fixture.settings.Rebuild([
            new ProjectSettingsContributor("broken", [], [],
                [new ProjectSettingRecord(ClipSettings.id, ClipSettings.typeId, [1, 2, 3])])]));
        Assert.Equal(revision, fixture.settings.revision);
        Assert.Equal("stable", Assert.Single(fixture.settings.Get<ClipSettings>(ClipSettings.id).labels));
    }

    [Fact]
    public void EffectiveSnapshotsAreDetachedAndUnassignedRemainsDistinctFromMissing()
    {
        using var fixture = new Fixture();
        fixture.settings.SetProjectOverride(ClipSettings.id, new ClipSettings { labels = ["saved"] }, []);
        ClipSettings first = fixture.settings.Get<ClipSettings>(ClipSettings.id);
        first.labels[0] = "edited";
        ClipSettings second = fixture.settings.Get<ClipSettings>(ClipSettings.id);
        Assert.NotSame(first, second);
        Assert.Null(second.clip);
        Assert.Equal("saved", Assert.Single(second.labels));
    }

    [Fact]
    public void SettingsRequireAnExplicitOwnerSerializationContext()
    {
        using var fixture = new Fixture();
        Assert.Throws<ArgumentNullException>(() => new ProjectSettingsStore(
            Path.Combine(fixture.root, "Rejected.inno"), fixture.types, fixture.serialization,
            new ProjectId("tests.rejected"), null!));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ModuleHost m_modules;
        private readonly LogRouter m_logs = new();
        private readonly IdentityAllocator m_identities = new();
        private readonly DiagnosticHub m_diagnostics = new();
        private readonly IDisposable m_identityScope;

        internal Fixture()
        {
            root = Path.Combine(Path.GetTempPath(), "InnoSettingsReferences", Guid.NewGuid().ToString("N"));
            assetRoot = Path.Combine(root, "Assets");
            Directory.CreateDirectory(assetRoot);
            m_identityScope = m_identities.EnterScope();
            m_modules = new ModuleHost(new ModuleHostOptions { cacheDirectory = Path.Combine(root, "Modules") });
            types = new TypeCatalog(m_modules);
            serialization = new SerializationRegistry(types);
            assets = new AssetPipeline(m_modules, types, serialization, m_identities, m_diagnostics, m_logs,
                AssetPipelineOptions.Create(assetRoot, Path.Combine(root, "Library")) with { enableFileSystemWatcher = false });
            context = AssetSerializationContext.Create(assets);
            settings = new ProjectSettingsStore(Path.Combine(root, "Settings.Project.inno"), types,
                serialization, new ProjectId("tests.settings.references"), context);
        }

        internal string root { get; }
        internal string assetRoot { get; }
        internal TypeCatalog types { get; }
        internal SerializationRegistry serialization { get; }
        internal AssetPipeline assets { get; }
        internal SerializationContext context { get; }
        internal ProjectSettingsStore settings { get; }

        public void Dispose()
        {
            settings.Dispose();
            assets.Dispose();
            serialization.Dispose();
            types.Dispose();
            m_modules.Dispose();
            m_logs.Dispose();
            m_identityScope.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }
}

internal abstract class ClipSettingsBase : ISerializable
{
    [SerializableProperty] public TextAsset? clip { get; set; }
    [SerializableProperty] public string[] labels { get; set; } = [];
}

[StableTypeId("2c7288db-eb9e-43a5-9894-bf9d10b22a2d")]
[ProjectSettingDefinition("tests.settings.clip")]
internal sealed class ClipSettings : ClipSettingsBase
{
    internal static readonly Guid typeId = new("2c7288db-eb9e-43a5-9894-bf9d10b22a2d");
    internal static ProjectSettingId id => new("tests.settings.clip");
}

[StableTypeId("cbeec86a-29da-4894-915f-b6239086ee05")]
[ProjectSettingDefinition("tests.settings.composed-clip")]
internal sealed class ComposedClipSettings : ClipSettingsBase
{
    internal static ProjectSettingId id => new("tests.settings.composed-clip");
}

internal sealed class ClipContribution : ISerializable
{
    [SerializableProperty] public TextAsset? clip { get; set; }
}

[ProjectSettingComposer("tests.settings.composed-clip")]
internal sealed class ClipSettingsComposer : ProjectSettingComposer<ComposedClipSettings, ClipContribution>
{
    protected override ClipContribution CaptureContribution(ComposedClipSettings baseline, ComposedClipSettings value)
        => new() { clip = value.clip };

    protected override bool IsEmpty(ClipContribution contribution) => contribution.clip is null;

    protected override void Compose(ComposedClipSettings target, IReadOnlyList<ProjectSettingContribution<ClipContribution>> contributions)
    {
        foreach (ProjectSettingContribution<ClipContribution> contribution in contributions)
            target.clip = contribution.value.clip;
    }
}
