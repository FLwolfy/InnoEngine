using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Plugins.Authoring;
using Inno.Plugins;
using Inno.Extensibility.Modules;
using Inno.Core.Identity;
using Inno.Core.Diagnostics;
using Inno.Core.Logging;
using Inno.Extensibility.Types;
using Inno.Core.Serialization;
using Inno.Core.Settings;
using Xunit;

namespace Inno.Plugins.Authoring.Tests;

[Collection("Plugin source serialization")]
public sealed class PluginSourceServiceTests : IDisposable
{
    private readonly ModuleHost m_modules;
    private readonly TypeCatalog m_types;
    private readonly SerializationRegistry m_serialization;
    private readonly ProjectSettingsStore m_settings;
    private readonly LogRouter m_logs = new();
    private readonly DiagnosticHub m_diagnostics = new();
    private readonly IdentityAllocator m_identities = new();
    private readonly IDisposable m_identityScope;
    private readonly string m_root;
    private readonly string m_plugins;
    private readonly string m_library;
    private AssetPipeline? m_assets;
    private PluginEnvironment? m_environment;

    public PluginSourceServiceTests()
    {
        m_root = Path.Combine(Path.GetTempPath(), "InnoPluginArchiveTests", Guid.NewGuid().ToString("N"));
        m_plugins = Path.Combine(m_root, "Plugins");
        m_library = Path.Combine(m_root, "Library");
        Directory.CreateDirectory(m_plugins);
        m_identityScope = m_identities.EnterScope();
        _ = typeof(AssetPipeline);
        _ = typeof(TextAsset);
        _ = typeof(PluginSourceService);
        m_modules = new ModuleHost(new ModuleHostOptions
        {
            cacheDirectory = Path.Combine(m_root, "Assemblies")
        });
        m_types = new TypeCatalog(m_modules);
        m_serialization = new SerializationRegistry(m_types);
        m_settings = new ProjectSettingsStore(
            Path.Combine(m_root, "Settings.Project.inno"),
            m_types,
            m_serialization,
            new ProjectId("tests.plugins"));
    }

    public void Dispose()
    {
        m_environment?.Dispose();
        m_assets?.Dispose();
        m_settings.Dispose();
        m_serialization.Dispose();
        m_types.Dispose();
        m_modules.Dispose();
        m_logs.Dispose();
        m_identityScope.Dispose();
        if (Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }

    [Fact]
    public void CodePluginActivatesImmediatelyAfterValidation()
    {
        WritePlugin("code.iplugin", Manifest("tests.code"), new Dictionary<string, byte[]>
        {
            ["Assets/Plugin.cs"] = "public sealed class PluginEntry { }"u8.ToArray(),
            ["Assets/Plugin.cs.imeta"] = CreateTextSourceMeta()
        });
        var service = new PluginSourceService(m_serialization, m_plugins, m_library);

        PluginScanResult scan = service.Scan();
        PluginCandidate candidate = Assert.Single(scan.candidates);
        Assert.True(candidate.containsCode);
        Assert.Empty(scan.diagnostics);
        PluginCandidate active = Assert.Single(PluginSourceService.GetActivatableCandidates(scan));
        Assert.True(active.sourceMount.isReadOnly);
        Assert.Equal("tests.code", active.sourceMount.id.value);
        Assert.True(System.IO.File.Exists(Path.Combine(active.sourceMount.rootPath, "Plugin.cs")));
    }

    [Fact]
    public void FolderPluginIsRejectedAsANonPackageInstallation()
    {
        WriteDirectoryPlugin(
            "InstalledPlugin",
            Manifest("tests.directory"),
            TextContent("content"));
        var service = new PluginSourceService(m_serialization, m_plugins, m_library);

        PluginScanResult scan = service.Scan();

        Assert.Empty(scan.candidates);
        PluginDiagnostic diagnostic = Assert.Single(scan.diagnostics);
        Assert.Contains(
            ".iplugin package",
            diagnostic.message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RemovingAnActiveCodePackageKeepsTheLastGoodSnapshotUntilAtomicRemovalCommits()
    {
        const string c_fileName = "active-code.iplugin";
        string sourcePath = Path.Combine(m_plugins, c_fileName);
        WritePlugin(
            c_fileName,
            Manifest("tests.removed-package"),
            new Dictionary<string, byte[]>
            {
                ["Assets/Plugin.cs"] = "public sealed class RemovedPackageEntry { }"u8.ToArray(),
                ["Assets/Plugin.cs.imeta"] = CreateTextSourceMeta()
            });
        var service = new PluginSourceService(m_serialization, m_plugins, m_library);
        PluginScanResult initial = service.Scan();
        InitializeProjectAssets();
        StartEnvironment(initial);
        PluginCandidate active = Assert.Single(m_environment!.activePlugins);
        string activeSnapshotRoot = active.sourceMount.rootPath;

        System.IO.File.Delete(sourcePath);

        Assert.True(m_environment.Refresh());
        Assert.True(m_environment.hasPendingActivation);
        Assert.Empty(m_environment.compilationPlugins);
        Assert.Equal(activeSnapshotRoot, Assert.Single(m_environment.activePlugins).sourceMount.rootPath);
        Assert.True(Directory.Exists(activeSnapshotRoot));
        m_assets!.Update();

        m_environment.RollbackPending();
        m_assets.Update();
        Assert.False(m_environment.hasPendingActivation);
        Assert.Equal(activeSnapshotRoot, Assert.Single(m_environment.activePlugins).sourceMount.rootPath);

        Assert.True(m_environment.Refresh());
        m_environment.ActivatePending();
        m_assets.Update();
        m_settings.RebuildCurrent();
        m_environment.CommitPending();

        Assert.Empty(m_environment.activePlugins);
        Assert.Collection(
            m_assets.sourceMounts,
            mount => Assert.Equal(AssetSourceId.project, mount.id));
    }

    [Fact]
    public void StructurallyInvalidActivePluginStagesAnUnavailableGeneration()
    {
        const string c_fileName = "invalidated-code.iplugin";
        string sourcePath = Path.Combine(m_plugins, c_fileName);
        WritePlugin(
            c_fileName,
            Manifest("tests.invalidated-package"),
            new Dictionary<string, byte[]>
            {
                ["Assets/Plugin.cs"] = "public sealed class InvalidatedPackageEntry { }"u8.ToArray(),
                ["Assets/Plugin.cs.imeta"] = CreateTextSourceMeta()
            });
        var service = new PluginSourceService(m_serialization, m_plugins, m_library);
        InitializeProjectAssets();
        StartEnvironment(service.Scan());
        Assert.Single(m_environment!.activePlugins);

        System.IO.File.WriteAllBytes(sourcePath, [0x49, 0x4E, 0x4E, 0x4F]);

        Assert.True(m_environment.Refresh());
        Assert.True(m_environment.hasPendingActivation);
        Assert.Empty(m_environment.compilationPlugins);
        Assert.Single(m_environment.activePlugins);
        Assert.Contains(m_environment.discovery.diagnostics, diagnostic =>
            diagnostic.message.Contains("corrupt", StringComparison.OrdinalIgnoreCase));

        m_environment.ActivatePending();
        m_assets!.Update();
        m_settings.RebuildCurrent();
        m_environment.CommitPending();

        Assert.Empty(m_environment.activePlugins);
        Assert.Collection(
            m_assets.sourceMounts,
            mount => Assert.Equal(AssetSourceId.project, mount.id));
    }

    [Fact]
    public void InvalidAssetMetadataUpdateStagesAnUnavailableGenerationWithoutThrowing()
    {
        const string c_fileName = "invalid-asset-metadata.iplugin";
        WritePlugin(
            c_fileName,
            Manifest("tests.invalid-asset-metadata"),
            new Dictionary<string, byte[]>
            {
                ["Assets/Plugin.cs"] = "public sealed class InvalidAssetMetadataEntry { }"u8.ToArray(),
                ["Assets/Plugin.cs.imeta"] = CreateTextSourceMeta()
            });
        var service = new PluginSourceService(m_serialization, m_plugins, m_library);
        InitializeProjectAssets();
        StartEnvironment(service.Scan());
        Assert.Single(m_environment!.activePlugins);

        WritePlugin(
            c_fileName,
            Manifest("tests.invalid-asset-metadata"),
            new Dictionary<string, byte[]>
            {
                ["Assets/Plugin.cs"] = "public sealed class InvalidAssetMetadataEntry { }"u8.ToArray(),
                ["Assets/Plugin.cs.imeta"] = [1]
            });

        Assert.True(m_environment.Refresh());
        Assert.True(m_environment.hasPendingActivation);
        Assert.Empty(m_environment.compilationPlugins);
        Assert.Single(m_environment.activePlugins);
        Assert.Contains(m_environment.discovery.diagnostics, diagnostic =>
            diagnostic.message.Contains("Asset candidate validation failed", StringComparison.OrdinalIgnoreCase));

        m_environment.ActivatePending();
        m_assets!.Update();
        m_settings.RebuildCurrent();
        m_environment.CommitPending();

        Assert.Empty(m_environment.activePlugins);
        Assert.Collection(
            m_assets.sourceMounts,
            mount => Assert.Equal(AssetSourceId.project, mount.id));
    }

    [Fact]
    public void OperatingSystemMetadataBesidePackageDoesNotAffectIdentity()
    {
        WritePlugin("metadata.iplugin", Manifest("tests.package-metadata"), TextContent("content"));
        System.IO.File.WriteAllBytes(Path.Combine(m_plugins, ".DS_Store"), [1, 2, 3]);
        System.IO.File.WriteAllBytes(Path.Combine(m_plugins, "Thumbs.db"), [4, 5, 6]);
        var service = new PluginSourceService(m_serialization, m_plugins, m_library);

        PluginScanResult withMetadata = service.Scan();

        Assert.Empty(withMetadata.diagnostics);
        string contentHash = Assert.Single(withMetadata.candidates).contentHash;
        System.IO.File.Delete(Path.Combine(m_plugins, ".DS_Store"));
        System.IO.File.Delete(Path.Combine(m_plugins, "Thumbs.db"));
        PluginScanResult withoutMetadata = service.Scan();
        Assert.Empty(withoutMetadata.diagnostics);
        Assert.Equal(contentHash, Assert.Single(withoutMetadata.candidates).contentHash);
    }

    [Fact]
    public void OperatingSystemMetadataInsidePackageDoesNotInvalidateOrChangeIdentity()
    {
        PluginManifest manifest = Manifest("tests.package-entry-metadata");
        Dictionary<string, byte[]> content = TextContent("content");
        content[".DS_Store"] = [1, 2, 3];
        content["Assets/desktop.ini"] = [4, 5, 6];
        WritePlugin("metadata.iplugin", manifest, content);
        var service = new PluginSourceService(m_serialization, m_plugins, m_library);

        PluginScanResult withMetadata = service.Scan();

        Assert.Empty(withMetadata.diagnostics);
        string contentHash = Assert.Single(withMetadata.candidates).contentHash;
        System.IO.File.Delete(Path.Combine(m_plugins, "metadata.iplugin"));
        WritePlugin("metadata.iplugin", manifest, TextContent("content"));
        PluginScanResult withoutMetadata = service.Scan();
        Assert.Empty(withoutMetadata.diagnostics);
        Assert.Equal(contentHash, Assert.Single(withoutMetadata.candidates).contentHash);
    }

    [Fact]
    public void FolderManifestCannotConflictWithAValidPackageIdentity()
    {
        WritePlugin("duplicate.iplugin", Manifest("tests.same-source-id"), TextContent("package"));
        WriteDirectoryPlugin("DuplicateFolder", Manifest("tests.same-source-id"), TextContent("folder"));
        var service = new PluginSourceService(m_serialization, m_plugins, m_library);

        PluginScanResult result = service.Scan();

        Assert.Equal("tests.same-source-id", Assert.Single(result.candidates).manifest.pluginId);
        PluginDiagnostic diagnostic = Assert.Single(result.diagnostics);
        Assert.Contains(
            ".iplugin package",
            diagnostic.message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DependencyGraphUsesDeterministicTopologicalOrderAndRejectsCycles()
    {
        WritePlugin("z-dependent.iplugin", Manifest("tests.beta", "tests.alpha"), TextContent("beta"));
        WritePlugin("a-base.iplugin", Manifest("tests.alpha"), TextContent("alpha"));
        var service = new PluginSourceService(m_serialization, m_plugins, m_library);

        PluginScanResult ordered = service.Scan();
        Assert.Equal(["tests.alpha", "tests.beta"], ordered.candidates.Select(candidate => candidate.manifest.pluginId));

        Directory.Delete(m_plugins, recursive: true);
        Directory.CreateDirectory(m_plugins);
        WritePlugin("a.iplugin", Manifest("cycle.a", "cycle.b"), TextContent("a"));
        WritePlugin("b.iplugin", Manifest("cycle.b", "cycle.a"), TextContent("b"));
        PluginScanResult cycle = service.Scan();
        Assert.Empty(cycle.candidates);
        Assert.Equal(2, cycle.diagnostics.Count(diagnostic => diagnostic.message.Contains("dependency cycle")));
    }

    [Theory]
    [InlineData("../escape.txt", "non-portable segment")]
    [InlineData("Assets/missing.txt", "missing its required .imeta sidecar")]
    [InlineData("Assets/native.dll", "forbidden prebuilt binary")]
    public void UnsafeOrIncompleteArchivesAreIsolatedAsDiagnostics(string entryPath, string expectedMessage)
    {
        Dictionary<string, byte[]> entries = TextContent("safe");
        entries.Remove("Assets/content.txt");
        entries.Remove("Assets/content.txt.imeta");
        entries[entryPath] = [1, 2, 3];
        WritePlugin("invalid.iplugin", Manifest("tests.invalid"), entries);
        var service = new PluginSourceService(m_serialization, m_plugins, m_library);

        PluginScanResult result = service.Scan();

        Assert.Empty(result.candidates);
        PluginDiagnostic diagnostic = Assert.Single(result.diagnostics);
        Assert.Contains(expectedMessage, diagnostic.message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PortableCaseCollisionsAreRejectedBeforeExtraction()
    {
        WritePlugin("collision.iplugin", Manifest("tests.collision"), new Dictionary<string, byte[]>
        {
            ["Assets/Data.txt"] = [1],
            ["Assets/Data.txt.imeta"] = [1],
            ["Assets/data.txt"] = [2],
            ["Assets/data.txt.imeta"] = [2]
        });
        var service = new PluginSourceService(m_serialization, m_plugins, m_library);

        PluginScanResult result = service.Scan();

        Assert.Empty(result.candidates);
        Assert.Contains("duplicate or non-portable path", Assert.Single(result.diagnostics).message);
    }

    [Theory]
    [InlineData("Assets/CON.txt", "non-portable segment")]
    [InlineData("Assets/trailing-dot.", "non-portable segment")]
    [InlineData("Assets/e\u0301.txt", "Unicode-normalized")]
    public void PlatformSpecificAndNonNormalizedPathsAreRejected(
        string entryPath,
        string expectedMessage)
    {
        WritePlugin("portable.iplugin", Manifest("tests.portable"), new Dictionary<string, byte[]>
        {
            [entryPath] = [1],
            [entryPath + ".imeta"] = [1]
        });
        var service = new PluginSourceService(m_serialization, m_plugins, m_library);

        PluginScanResult result = service.Scan();

        Assert.Empty(result.candidates);
        Assert.Contains(expectedMessage, Assert.Single(result.diagnostics).message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SymbolicLinksAndExcessiveEntryCountsAreRejectedBeforeExtraction()
    {
        string symbolicLinkPath = Path.Combine(m_plugins, "symbolic-link.iplugin");
        using (FileStream stream = System.IO.File.Create(symbolicLinkPath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "Plugin.inno", m_serialization.Serialize(Manifest("tests.symbolic-link")));
            ZipArchiveEntry link = archive.CreateEntry("Assets/link.txt");
            link.ExternalAttributes = unchecked((int)0xA1FF0000);
            WriteEntry(archive, "Assets/link.txt.imeta", [1]);
        }

        var symbolicLinkService = new PluginSourceService(m_serialization, m_plugins, m_library);
        PluginScanResult symbolicLink = symbolicLinkService.Scan();

        Assert.Empty(symbolicLink.candidates);
        Assert.Contains("symbolic link", Assert.Single(symbolicLink.diagnostics).message, StringComparison.OrdinalIgnoreCase);

        System.IO.File.Delete(symbolicLinkPath);
        WritePlugin("entry-count.iplugin", Manifest("tests.entry-count"), TextContent("bounded"));
        var entryCountService = new PluginSourceService(
            m_serialization,
            m_plugins,
            m_library,
            new PluginSourceLimits { maximumEntryCount = 2 });

        PluginScanResult entryCount = entryCountService.Scan();

        Assert.Empty(entryCount.candidates);
        Assert.Contains("entry-count limit", Assert.Single(entryCount.diagnostics).message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("file", "file-size limit")]
    [InlineData("total", "total uncompressed-size limit")]
    [InlineData("ratio", "compression-ratio limit")]
    public void ArchiveLimitsRejectOversizedOrSuspiciousContent(string scenario, string expectedMessage)
    {
        byte[] first = scenario == "ratio" ? new byte[4096] : CreateNoise(1024, seed: 17);
        Dictionary<string, byte[]> entries = new()
        {
            ["Assets/first.data"] = first,
            ["Assets/first.data.imeta"] = [1]
        };
        if (scenario == "total")
        {
            entries["Assets/second.data"] = CreateNoise(1024, seed: 29);
            entries["Assets/second.data.imeta"] = [1];
        }
        WritePlugin("bounded.iplugin", Manifest("tests.bounded"), entries);
        PluginSourceLimits limits = scenario switch
        {
            "file" => new PluginSourceLimits
            {
                maximumFileBytes = 512,
                maximumTotalBytes = 16_384,
                maximumCompressionRatio = 500
            },
            "total" => new PluginSourceLimits
            {
                maximumFileBytes = 4096,
                maximumTotalBytes = 1800,
                maximumCompressionRatio = 500
            },
            "ratio" => new PluginSourceLimits
            {
                maximumFileBytes = 8192,
                maximumTotalBytes = 16_384,
                maximumCompressionRatio = 2
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var service = new PluginSourceService(m_serialization, m_plugins, m_library, limits);

        PluginScanResult result = service.Scan();

        Assert.Empty(result.candidates);
        Assert.Contains(expectedMessage, Assert.Single(result.diagnostics).message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CorruptManifestAndDuplicateEntriesAreIsolated()
    {
        WritePluginWithManifestBytes(
            "corrupt.iplugin",
            [0x49, 0x4E, 0x4E, 0x4F],
            TextContent("corrupt"));
        WriteArchiveWithDuplicateEntry("duplicate.iplugin");
        var service = new PluginSourceService(m_serialization, m_plugins, m_library);

        PluginScanResult result = service.Scan();

        Assert.Empty(result.candidates);
        Assert.Equal(2, result.diagnostics.Count);
        Assert.Contains(result.diagnostics, diagnostic =>
            diagnostic.message.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MissingDependenciesDuplicateIdsAndInvalidOverridesRejectTheCandidateGeneration()
    {
        WritePlugin("missing.iplugin", Manifest("tests.missing", "tests.absent"), TextContent("missing"));
        WritePlugin("duplicate-a.iplugin", Manifest("tests.duplicate"), TextContent("first"));
        WritePlugin("duplicate-b.iplugin", Manifest("tests.duplicate"), TextContent("second"));
        PluginManifest invalidOverride = Manifest("tests.override");
        invalidOverride.overrides = ["tests.base"];
        WritePlugin("override.iplugin", invalidOverride, TextContent("override"));
        var service = new PluginSourceService(m_serialization, m_plugins, m_library);

        PluginScanResult result = service.Scan();

        Assert.Empty(result.candidates);
        Assert.Contains(result.diagnostics, diagnostic =>
            diagnostic.message.Contains("not installed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.diagnostics, diagnostic =>
            diagnostic.message.Contains("installed more than once", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.diagnostics, diagnostic =>
            diagnostic.message.Contains("explicit override", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SettingsCompositionRejectsPeersAllowsExplicitDependencyOverrideAndKeepsProjectHighest()
    {
        ProjectSettingRecord first = SettingRecord(10);
        ProjectSettingRecord second = SettingRecord(20);
        m_settings.Rebuild(
        [
            new ProjectSettingsContributor("tests.first", [], [], [first])
        ]);
        Assert.Equal(10, m_settings.Get<PluginDefaultTestSetting>(PluginDefaultTestSetting.id).value);

        Assert.Throws<InvalidOperationException>(() => m_settings.Rebuild(
        [
            new ProjectSettingsContributor("tests.first", [], [], [first]),
            new ProjectSettingsContributor("tests.peer", [], [], [second])
        ]));
        Assert.Equal(10, m_settings.Get<PluginDefaultTestSetting>(PluginDefaultTestSetting.id).value);

        ProjectSettingsContributor[] overridden =
        [
            new ProjectSettingsContributor("tests.first", [], [], [first]),
            new ProjectSettingsContributor("tests.second", ["tests.first"], ["tests.first"], [second])
        ];
        m_settings.Rebuild(overridden);
        Assert.Equal(20, m_settings.Get<PluginDefaultTestSetting>(PluginDefaultTestSetting.id).value);

        m_settings.SetProjectOverride(
            PluginDefaultTestSetting.id,
            new PluginDefaultTestSetting { value = 30 },
            overridden);
        m_settings.Rebuild(
        [
            new ProjectSettingsContributor("tests.first", [], [], [SettingRecord(40)])
        ]);
        Assert.Equal(30, m_settings.Get<PluginDefaultTestSetting>(PluginDefaultTestSetting.id).value);
    }

    [Fact]
    public void InitialActivationFailureKeepsTheHostOnlyMountAndPublishesDiscoveryDiagnostics()
    {
        WritePlugin("invalid-meta.iplugin", Manifest("tests.invalid-meta"), new Dictionary<string, byte[]>
        {
            ["Assets/value.txt"] = "value"u8.ToArray(),
            ["Assets/value.txt.imeta"] = [1]
        });
        InitializeProjectAssets();
        var service = new PluginSourceService(m_serialization, m_plugins, m_library);
        PluginScanResult scan = service.Scan();

        StartEnvironment(scan);

        Assert.Empty(m_environment!.activePlugins);
        Assert.Single(m_environment!.discovery.candidates);
        Assert.Contains(m_environment!.discovery.diagnostics, diagnostic =>
            diagnostic.message.Contains("candidate activation failed", StringComparison.OrdinalIgnoreCase));
        Assert.Collection(
            m_assets!.sourceMounts,
            mount => Assert.Equal(AssetSourceId.project, mount.id));
    }

    [Fact]
    public void InitialActivationPublishesPluginSettingDefaultsWithoutAnEditorFrontend()
    {
        var contributed = new PluginDefaultTestSetting { value = 42 };
        PluginManifest manifest = Manifest("tests.settings");
        manifest.settingContributions =
        [
            new ProjectSettingRecord(
                PluginDefaultTestSetting.id,
                m_types.GetTypeRef(typeof(PluginDefaultTestSetting)).stableId,
                m_serialization.CapturePropertiesData(contributed))
        ];
        WritePlugin("settings.iplugin", manifest, new Dictionary<string, byte[]>
        {
            ["Assets/value.txt"] = "value"u8.ToArray(),
            ["Assets/value.txt.imeta"] = CreateTextSourceMeta()
        });
        InitializeProjectAssets();
        var service = new PluginSourceService(m_serialization, m_plugins, m_library);
        PluginScanResult scan = service.Scan();

        StartEnvironment(scan);

        Assert.Equal("tests.settings", Assert.Single(m_environment!.activePlugins).manifest.pluginId);
        Assert.Equal(
            [AssetSourceId.project, new AssetSourceId("tests.settings")],
            m_assets!.sourceMounts.Select(static mount => mount.id));
        Assert.Equal(42, m_settings.Get<PluginDefaultTestSetting>(PluginDefaultTestSetting.id).value);
    }

    [Fact]
    public void CodePluginUpdate_RemainsInvisibleWhileItsCompilationCandidateIsPending()
    {
        InitializeProjectAssets();
        var service = new PluginSourceService(m_serialization, m_plugins, m_library);
        StartEnvironment(service.Scan());
        WritePlugin("staged-code.iplugin", Manifest("tests.staged-code"), new Dictionary<string, byte[]>
        {
            ["Assets/Plugin.cs"] = "public sealed class PluginEntry { }"u8.ToArray(),
            ["Assets/Plugin.cs.imeta"] = CreateTextSourceMeta()
        });

        Assert.True(m_environment!.Refresh());

        Assert.True(m_environment!.hasPendingActivation);
        Assert.Empty(m_environment!.activePlugins);
        Assert.Collection(
            m_assets!.sourceMounts,
            mount => Assert.Equal(AssetSourceId.project, mount.id));
        Assert.Equal(
            "tests.staged-code",
            Assert.Single(m_environment!.compilationPlugins).manifest.pluginId);
        Assert.Contains(
            m_environment!.compilationAssets!.GetFileSystemEntries(includeDirectories: false),
            entry => entry.assetPath == new AssetPath(new AssetSourceId("tests.staged-code"), "Plugin.cs"));

        m_environment!.RollbackPending();

        Assert.False(m_environment!.hasPendingActivation);
        Assert.Empty(m_environment!.activePlugins);
        Assert.Collection(
            m_assets!.sourceMounts,
            mount => Assert.Equal(AssetSourceId.project, mount.id));
    }

    [Fact]
    public void SourceWatcherDebouncesChangesAndActivatesOnTheOwnerThread()
    {
        InitializeProjectAssets();
        var service = new PluginSourceService(m_serialization, m_plugins, m_library);
        StartEnvironment(service.Scan());
        WritePlugin("watched.iplugin", Manifest("tests.watched"), new Dictionary<string, byte[]>
        {
            ["Assets/value.txt"] = "watched"u8.ToArray(),
            ["Assets/value.txt.imeta"] = CreateTextSourceMeta()
        });

        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (m_environment!.activePlugins.Count == 0 && DateTime.UtcNow < deadline)
        {
            m_environment.Update();
            Thread.Sleep(20);
        }

        Assert.Equal("tests.watched", Assert.Single(m_environment.activePlugins).manifest.pluginId);
        Assert.Equal(
            [AssetSourceId.project, new AssetSourceId("tests.watched")],
            m_assets!.sourceMounts.Select(static mount => mount.id));
    }

    private static PluginManifest Manifest(string id, params string[] dependencies)
        => new()
        {
            pluginId = id,
            displayName = id,
            dependencies = dependencies
        };

    private static Dictionary<string, byte[]> TextContent(string value)
        => new()
        {
            ["Assets/content.txt"] = System.Text.Encoding.UTF8.GetBytes(value),
            ["Assets/content.txt.imeta"] = [1]
        };

    private ProjectSettingRecord SettingRecord(int value)
        => new(
            PluginDefaultTestSetting.id,
            m_types.GetTypeRef(typeof(PluginDefaultTestSetting)).stableId,
            m_serialization.CapturePropertiesData(new PluginDefaultTestSetting { value = value }));

    private PluginManifest ReadManifest(ZipArchive archive)
    {
        using Stream input = archive.GetEntry("Plugin.inno")!.Open();
        using var bytes = new MemoryStream();
        input.CopyTo(bytes);
        return m_serialization.Deserialize<PluginManifest>(bytes.ToArray());
    }

    private void WritePlugin(
        string fileName,
        PluginManifest manifest,
        IReadOnlyDictionary<string, byte[]> entries)
        => WritePluginWithManifestBytes(fileName, m_serialization.Serialize(manifest), entries);

    private void WritePluginWithManifestBytes(
        string fileName,
        byte[] manifestBytes,
        IReadOnlyDictionary<string, byte[]> entries)
    {
        string path = Path.Combine(m_plugins, fileName);
        using FileStream stream = System.IO.File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        WriteEntry(archive, "Plugin.inno", manifestBytes);
        foreach ((string entryPath, byte[] bytes) in entries)
            WriteEntry(archive, entryPath, bytes);
    }

    private void WriteDirectoryPlugin(
        string directoryName,
        PluginManifest manifest,
        IReadOnlyDictionary<string, byte[]> entries)
    {
        string root = Path.Combine(m_plugins, directoryName);
        Directory.CreateDirectory(root);
        System.IO.File.WriteAllBytes(
            Path.Combine(root, "Plugin.inno"),
            m_serialization.Serialize(manifest));
        foreach ((string entryPath, byte[] bytes) in entries)
        {
            string physicalPath = Path.Combine(
                root,
                entryPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);
            System.IO.File.WriteAllBytes(physicalPath, bytes);
        }
    }

    private void WriteArchiveWithDuplicateEntry(string fileName)
    {
        string path = Path.Combine(m_plugins, fileName);
        using FileStream stream = System.IO.File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        WriteEntry(archive, "Plugin.inno", m_serialization.Serialize(Manifest("tests.duplicate-entry")));
        WriteEntry(archive, "Assets/value.txt", [1]);
        WriteEntry(archive, "Assets/value.txt", [2]);
        WriteEntry(archive, "Assets/value.txt.imeta", [1]);
    }

    private static byte[] CreateNoise(int length, int seed)
    {
        var result = new byte[length];
        new Random(seed).NextBytes(result);
        return result;
    }

    private void InitializeProjectAssets()
    {
        string assets = Path.Combine(m_root, "RuntimeProject", "Assets");
        string library = Path.Combine(m_root, "RuntimeProject", "Library");
        Directory.CreateDirectory(assets);
        AssetPipelineOptions defaults = AssetPipelineOptions.Create(assets, library);
        m_assets = new AssetPipeline(
            m_modules,
            m_types,
            m_serialization,
            m_identities,
            m_diagnostics,
            m_logs,
            defaults with
            {
                enableFileSystemWatcher = false
            });
    }

    private void StartEnvironment(PluginScanResult scan)
    {
        m_environment = new PluginEnvironment(
            m_assets ?? throw new InvalidOperationException("Project assets must be initialized first."),
            m_settings,
            m_serialization,
            m_plugins,
            m_library,
            scan);
    }

    private byte[] CreateTextSourceMeta()
        => m_serialization.Serialize(new TestAssetSourceMeta
        {
            persistentId = Guid.NewGuid(),
            sourceKind = (int)AssetSourceKind.File,
            importerId = "Inno.Assets.Pipeline.Importers.TextAssetImporter"
        });

    private static void WriteEntry(ZipArchive archive, string path, byte[] bytes)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using Stream output = entry.Open();
        output.Write(bytes);
    }
}

[CollectionDefinition("Plugin source serialization", DisableParallelization = true)]
public sealed class PluginSourceSerializationCollection
{
}

[StableTypeId("706c1248-478b-43f1-a44d-98a1a8f7b919")]
[ProjectSettingDefinition("tests.plugins.default")]
internal sealed class PluginDefaultTestSetting : ISerializable
{
    internal static ProjectSettingId id => new("tests.plugins.default");

    [SerializableProperty]
    public int value { get; set; } = 1;
}

internal sealed class TestAssetSourceMeta : ISerializable
{
    [SerializableProperty]
    public Guid persistentId { get; set; }

    [SerializableProperty]
    public int sourceKind { get; set; }

    [SerializableProperty]
    public string importerId { get; set; } = string.Empty;

    [SerializableProperty]
    public byte[] importerSettingsBytes { get; set; } = [];
}
