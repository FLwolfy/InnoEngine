using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets;
using Inno.Core.Diagnostics;
using Inno.Core.Identity;
using Inno.Core.Logging;
using Inno.Core.Serialization;
using Inno.Extensibility.Modules;
using Inno.Extensibility.Types;

using Xunit;

namespace Inno.Assets.Pipeline.Tests;

public sealed class ImportSettingsTests
{
    [Fact]
    public void SettingsRoundTripReimportAndResetWithoutChangingSourceOrIdentity()
    {
        using var fixture = new Fixture();
        using AssetLoader loader = fixture.CreateLoader();
        var asset = Assert.IsType<ConfiguredAsset>(loader.Load(Fixture.path, typeof(ConfiguredAsset)));
        Guid identity = asset.identity.persistentId;
        AssetImportSettingsSnapshot snapshot = loader.GetImportSettings(Fixture.path);
        var settings = Assert.IsType<ConfiguredSettings>(snapshot.value);
        Assert.Equal(1, settings.multiplier);
        settings.multiplier = 7;
        Assert.Equal(1, Assert.IsType<ConfiguredSettings>(loader.GetImportSettings(Fixture.path).value).multiplier);
        Assert.True(loader.SaveImportSettings(Fixture.path, settings, snapshot.fingerprint));
        Assert.Equal("7:source", asset.value);
        Assert.Equal(identity, asset.identity.persistentId);
        Assert.Equal("source", File.ReadAllText(fixture.sourcePath));
        AssetImportSettingsSnapshot updated = loader.GetImportSettings(Fixture.path);
        Assert.NotEqual(snapshot.fingerprint, updated.fingerprint);
        Assert.True(loader.SaveImportSettings(Fixture.path, null, updated.fingerprint));
        Assert.Equal("1:source", asset.value);
    }

    [Fact]
    public void SettingsSurviveColdLoaderAndLibraryRecreation()
    {
        using var fixture = new Fixture();
        Guid identity;
        using (AssetLoader loader = fixture.CreateLoader())
        {
            Assert.True(loader.Import(Fixture.path));
            AssetImportSettingsSnapshot snapshot = loader.GetImportSettings(Fixture.path);
            Assert.True(loader.SaveImportSettings(Fixture.path, new ConfiguredSettings { multiplier = 12 }, snapshot.fingerprint));
            identity = loader.Load(Fixture.path, typeof(ConfiguredAsset))!.identity.persistentId;
        }
        using AssetLoader cold = fixture.CreateLoader("OtherLibrary");
        var asset = Assert.IsType<ConfiguredAsset>(cold.Load(Fixture.path, typeof(ConfiguredAsset)));
        Assert.Equal("12:source", asset.value);
        Assert.Equal(identity, asset.identity.persistentId);
    }

    [Fact]
    public void StaleSettingsSnapshotCannotOverwriteNewerSidecar()
    {
        using var fixture = new Fixture();
        using AssetLoader loader = fixture.CreateLoader();
        Assert.True(loader.Import(Fixture.path));
        AssetImportSettingsSnapshot original = loader.GetImportSettings(Fixture.path);
        Assert.True(loader.SaveImportSettings(Fixture.path, new ConfiguredSettings { multiplier = 2 }, original.fingerprint));
        byte[] current = File.ReadAllBytes(fixture.sourcePath + ".imeta");
        Assert.Throws<IOException>(() => loader.SaveImportSettings(Fixture.path,
            new ConfiguredSettings { multiplier = 3 }, original.fingerprint));
        Assert.Equal(current, File.ReadAllBytes(fixture.sourcePath + ".imeta"));
    }

    [Fact]
    public void InvalidSettingsPersistWhilePreviousArtifactRemainsLastGood()
    {
        using var fixture = new Fixture();
        using AssetLoader loader = fixture.CreateLoader();
        var asset = Assert.IsType<ConfiguredAsset>(loader.Load(Fixture.path, typeof(ConfiguredAsset)));
        Assert.True(loader.TryGetInfo(Fixture.path, out AssetInfo? original));
        AssetImportSettingsSnapshot snapshot = loader.GetImportSettings(Fixture.path);
        Assert.False(loader.SaveImportSettings(Fixture.path, new ConfiguredSettings { multiplier = -1 }, snapshot.fingerprint));
        Assert.Equal(-1, Assert.IsType<ConfiguredSettings>(loader.GetImportSettings(Fixture.path).value).multiplier);
        Assert.Equal("1:source", asset.value);
        Assert.True(loader.TryGetInfo(Fixture.path, out AssetInfo? failed));
        Assert.Equal(AssetImportStatus.Failed, failed!.status);
        Assert.Equal(original!.artifactKey, failed.lastSuccessfulArtifactKey);
        Assert.Contains(failed.diagnostics, item => item.Contains("negative multiplier", StringComparison.Ordinal));
    }

    [Fact]
    public void SidecarChangeInvalidatesArtifactWithoutSourceChangeAndSelfNotificationsDoNotReimport()
    {
        using var fixture = new Fixture();
        using AssetLoader loader = fixture.CreateLoader();
        var asset = Assert.IsType<ConfiguredAsset>(loader.Load(Fixture.path, typeof(ConfiguredAsset)));
        byte[] first = File.ReadAllBytes(fixture.sourcePath + ".imeta");
        AssetImportSettingsSnapshot snapshot = loader.GetImportSettings(Fixture.path);
        Assert.True(loader.SaveImportSettings(Fixture.path, new ConfiguredSettings { multiplier = 2 }, snapshot.fingerprint));
        long revision = asset.contentVersion;
        loader.ApplySourceChanges([new AssetChangedEvent("value.configured.imeta", WatcherChangeTypes.Changed)]);
        Assert.Equal(revision, asset.contentVersion);
        File.WriteAllBytes(fixture.sourcePath + ".imeta", first);
        loader.ApplySourceChanges([new AssetChangedEvent("value.configured.imeta", WatcherChangeTypes.Changed)]);
        Assert.Equal("1:source", asset.value);
        Assert.True(asset.contentVersion > revision);
    }

    [Fact]
    public void SettingsReferencesAreImportDependenciesNotRuntimeDependencies()
    {
        using var fixture = new Fixture();
        File.WriteAllText(Path.Combine(fixture.assetRoot, "lookup.txt"), "first");
        using AssetLoader loader = fixture.CreateLoader();
        var dependency = Assert.IsType<TextAsset>(loader.Load(AssetPath.Project("lookup.txt"), typeof(TextAsset)));
        var asset = Assert.IsType<ConfiguredAsset>(loader.Load(Fixture.path, typeof(ConfiguredAsset)));
        AssetImportSettingsSnapshot snapshot = loader.GetImportSettings(Fixture.path);
        Assert.True(loader.SaveImportSettings(Fixture.path, new ConfiguredSettings { lookup = dependency }, snapshot.fingerprint));
        Assert.Contains(AssetPath.Project("lookup.txt"), loader.GetImportDependencies(asset));
        Assert.Empty(loader.GetDependencies(asset));
        Assert.Equal("1:source:first", asset.value);
        File.WriteAllText(Path.Combine(fixture.assetRoot, "lookup.txt"), "second");
        Assert.True(loader.Import(AssetPath.Project("lookup.txt")));
        var reloaded = Assert.IsType<ConfiguredAsset>(loader.Load(Fixture.path, typeof(ConfiguredAsset)));
        Assert.Equal("1:source:second", reloaded.value);
    }

    [Fact]
    public void ReadOnlySourcesAllowViewingButRejectSettingsWrites()
    {
        using var fixture = new Fixture();
        using (AssetLoader writer = fixture.CreateLoader()) Assert.True(writer.Import(Fixture.path));
        byte[] metadata = File.ReadAllBytes(fixture.sourcePath + ".imeta");
        var source = new AssetSourceId("tests.readonly.settings");
        using AssetLoader reader = fixture.CreateReadOnlyLoader(source);
        var path = new AssetPath(source, "value.configured");
        AssetImportSettingsSnapshot snapshot = reader.GetImportSettings(path);
        Assert.IsType<ConfiguredSettings>(snapshot.value);
        Assert.Throws<InvalidOperationException>(() => reader.SaveImportSettings(path, snapshot.value, snapshot.fingerprint));
        Assert.Equal(metadata, File.ReadAllBytes(fixture.sourcePath + ".imeta"));
    }

    [Fact]
    public void CorruptSidecarIsNotErasedByFailedImport()
    {
        using var fixture = new Fixture();
        using AssetLoader loader = fixture.CreateLoader();
        Assert.True(loader.Import(Fixture.path));
        byte[] corrupt = [0xff, 0xee, 0xdd];
        File.WriteAllBytes(fixture.sourcePath + ".imeta", corrupt);
        Assert.False(loader.Import(Fixture.path));
        Assert.Equal(corrupt, File.ReadAllBytes(fixture.sourcePath + ".imeta"));
    }

    private sealed class Fixture : IDisposable
    {
        internal static AssetPath path => AssetPath.Project("value.configured");
        private readonly string m_root = Path.Combine(Path.GetTempPath(), "InnoImportSettingsTests", Guid.NewGuid().ToString("N"));
        private readonly ModuleHost m_modules;
        private readonly TypeCatalog m_types;
        private readonly SerializationRegistry m_serialization;
        private readonly LogRouter m_logs = new();
        private readonly DiagnosticHub m_diagnostics = new();
        private readonly IdentityAllocator m_identities = new();
        private readonly IDisposable m_scope;

        internal Fixture()
        {
            Directory.CreateDirectory(assetRoot);
            Directory.CreateDirectory(Path.Combine(m_root, "EmptyProject"));
            File.WriteAllText(sourcePath, "source");
            m_scope = m_identities.EnterScope();
            m_modules = new ModuleHost(new ModuleHostOptions { cacheDirectory = Path.Combine(m_root, "Assemblies") });
            m_types = new TypeCatalog(m_modules);
            m_serialization = new SerializationRegistry(m_types);
        }
        internal string assetRoot => Path.Combine(m_root, "Assets");
        internal string sourcePath => Path.Combine(assetRoot, "value.configured");
        internal AssetLoader CreateLoader(string library = "Library")
            => new(m_types, m_serialization, m_identities, m_diagnostics, m_logs, assetRoot, Path.Combine(m_root, library));
        internal AssetLoader CreateReadOnlyLoader(AssetSourceId id)
            => new(m_types, m_serialization, m_identities, m_diagnostics, m_logs,
                [new AssetSourceMount(AssetSourceId.project, Path.Combine(m_root, "EmptyProject"), false),
                 new AssetSourceMount(id, assetRoot, true)], Path.Combine(m_root, "ReadOnlyLibrary"));
        public void Dispose()
        {
            m_serialization.Dispose();
            m_types.Dispose();
            m_modules.Dispose();
            m_logs.Dispose();
            m_scope.Dispose();
            Directory.Delete(m_root, true);
        }
    }
}

[StableTypeId("f58eaeea-4bd5-4f3a-a7c0-c5a5414a6bb4")]
internal sealed class ConfiguredSettings : ISerializable
{
    [SerializableProperty] internal int multiplier { get; set; } = 1;
    [SerializableProperty] internal TextAsset? lookup { get; set; }
}

[StableTypeId("bdb3db71-e9cb-4d46-839b-3cbe302c5555")]
internal sealed class ConfiguredAsset : AssetObject
{
    [SerializableProperty] internal string value { get; set; } = string.Empty;
}

[AssetImporterExtension]
internal sealed class ConfiguredImporter : AssetImporter<ConfiguredAsset>
{
    public override string importerId => "tests.configured";
    public override IReadOnlyList<string> supportedExtensions { get; } = [".configured"];
    public override ISerializable CreateImportSettings() => new ConfiguredSettings();
    protected override ValueTask ImportAsync(AssetImportContext context, AssetImportWriter<ConfiguredAsset> output, CancellationToken cancellationToken)
    {
        var settings = (ConfiguredSettings)context.importSettings!;
        if (settings.multiplier < 0) throw new InvalidOperationException("A negative multiplier is invalid.");
        string value = $"{settings.multiplier}:{context.ReadUtf8Text()}";
        if (settings.lookup is not null) value += ":" + settings.lookup.content;
        output.SetAsset(new ConfiguredAsset { value = value });
        return output.WriteArtifactAsync("runtime", Encoding.UTF8.GetBytes(value), cancellationToken);
    }
}
