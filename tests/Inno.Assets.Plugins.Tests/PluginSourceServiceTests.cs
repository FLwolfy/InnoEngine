using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Inno.Assets;
using Inno.Assets.Core;
using Inno.Assets.File;
using Inno.Assets.Plugins;
using Inno.Assets.Types;
using Inno.Core.Assemblies;
using Inno.Core.Identity;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Core.Settings;
using Xunit;

namespace Inno.Assets.Plugins.Tests;

[Collection("Plugin source serialization")]
public sealed class PluginSourceServiceTests : IDisposable
{
    private readonly string m_root;
    private readonly string m_plugins;
    private readonly string m_library;

    public PluginSourceServiceTests()
    {
        m_root = Path.Combine(Path.GetTempPath(), "InnoPluginArchiveTests", Guid.NewGuid().ToString("N"));
        m_plugins = Path.Combine(m_root, "Plugins");
        m_library = Path.Combine(m_root, "Library");
        Directory.CreateDirectory(m_plugins);
        IdentityManager.Initialize();
        _ = typeof(AssetManager);
        _ = typeof(TextAsset);
        _ = typeof(PluginSourceService);
        AssemblyManager.Initialize(new AssemblyManagerOptions
        {
            cacheDirectory = Path.Combine(m_root, "Assemblies")
        });
        TypeCacheManager.Initialize();
        SerializationManager.Initialize();
        ProjectSettingsManager.Initialize(Path.Combine(m_root, "ProjectSettings.inno"));
    }

    public void Dispose()
    {
        PluginManager.Shutdown();
        AssetManager.Shutdown();
        PluginCatalog.Shutdown();
        ProjectSettingsManager.Shutdown();
        SerializationManager.Shutdown();
        TypeCacheManager.Shutdown();
        AssemblyManager.Shutdown();
        IdentityManager.Shutdown();
        if (Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }

    [Fact]
    public void CodePluginActivatesImmediatelyAfterValidation()
    {
        WritePlugin("code.zip", Manifest("tests.code"), new Dictionary<string, byte[]>
        {
            ["Assets/Plugin.cs"] = "public sealed class PluginEntry { }"u8.ToArray(),
            ["Assets/Plugin.cs.imeta"] = CreateTextSourceMeta()
        });
        var service = new PluginSourceService(m_plugins, m_library);

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
    public void InstalledFolderPluginUsesTheSameReadOnlyMountContractAsZip()
    {
        string directory = Path.Combine(m_plugins, "InstalledPlugin");
        string assets = Path.Combine(directory, "Assets");
        Directory.CreateDirectory(assets);
        System.IO.File.WriteAllBytes(
            Path.Combine(directory, "Plugin.inno"),
            SerializationManager.Serialize(Manifest("tests.directory")));
        string scriptPath = Path.Combine(assets, "Plugin.cs");
        System.IO.File.WriteAllText(scriptPath, "public sealed class DirectoryPluginEntry { }");
        System.IO.File.WriteAllBytes(scriptPath + ".imeta", CreateTextSourceMeta());
        var service = new PluginSourceService(m_plugins, m_library);

        PluginScanResult scan = service.Scan();
        PluginCandidate discovered = Assert.Single(scan.candidates);
        Assert.Equal(PluginSourceKind.Directory, discovered.sourceKind);
        Assert.Equal(Path.GetFullPath(directory), discovered.sourcePath);
        PluginCandidate active = Assert.Single(PluginSourceService.GetActivatableCandidates(scan));
        Assert.Equal(Path.GetFullPath(assets), active.sourceMount.rootPath);
        Assert.True(active.sourceMount.isReadOnly);
        string previousHash = active.contentHash;

        System.IO.File.WriteAllText(scriptPath, "public sealed class UpdatedDirectoryPluginEntry { }");
        PluginCandidate updated = Assert.Single(PluginSourceService.GetActivatableCandidates(
            service.Scan()));
        Assert.NotEqual(previousHash, updated.contentHash);
    }

    [Fact]
    public void OperatingSystemMetadataDoesNotInvalidateOrChangeDirectoryPluginIdentity()
    {
        string directory = Path.Combine(m_plugins, "MetadataDirectory");
        WriteDirectoryPlugin("MetadataDirectory", Manifest("tests.directory-metadata"), TextContent("content"));
        string assets = Path.Combine(directory, "Assets");
        System.IO.File.WriteAllBytes(Path.Combine(directory, ".DS_Store"), [1, 2, 3]);
        System.IO.File.WriteAllBytes(Path.Combine(assets, "Thumbs.db"), [4, 5, 6]);
        var service = new PluginSourceService(m_plugins, m_library);

        PluginScanResult withMetadata = service.Scan();

        Assert.Empty(withMetadata.diagnostics);
        string contentHash = Assert.Single(withMetadata.candidates).contentHash;
        System.IO.File.Delete(Path.Combine(directory, ".DS_Store"));
        System.IO.File.Delete(Path.Combine(assets, "Thumbs.db"));
        PluginScanResult withoutMetadata = service.Scan();
        Assert.Empty(withoutMetadata.diagnostics);
        Assert.Equal(contentHash, Assert.Single(withoutMetadata.candidates).contentHash);
    }

    [Fact]
    public void OperatingSystemMetadataDoesNotInvalidateOrChangeZipPluginIdentity()
    {
        PluginManifest manifest = Manifest("tests.zip-metadata");
        Dictionary<string, byte[]> content = TextContent("content");
        content[".DS_Store"] = [1, 2, 3];
        content["Assets/desktop.ini"] = [4, 5, 6];
        WritePlugin("metadata.zip", manifest, content);
        var service = new PluginSourceService(m_plugins, m_library);

        PluginScanResult withMetadata = service.Scan();

        Assert.Empty(withMetadata.diagnostics);
        string contentHash = Assert.Single(withMetadata.candidates).contentHash;
        System.IO.File.Delete(Path.Combine(m_plugins, "metadata.zip"));
        WritePlugin("metadata.zip", manifest, TextContent("content"));
        PluginScanResult withoutMetadata = service.Scan();
        Assert.Empty(withoutMetadata.diagnostics);
        Assert.Equal(contentHash, Assert.Single(withoutMetadata.candidates).contentHash);
    }

    [Fact]
    public void DuplicateIdsAcrossZipAndDirectoryRejectBothSources()
    {
        WritePlugin("duplicate.zip", Manifest("tests.same-source-id"), TextContent("zip"));
        WriteDirectoryPlugin("DuplicateFolder", Manifest("tests.same-source-id"), TextContent("folder"));
        var service = new PluginSourceService(m_plugins, m_library);

        PluginScanResult result = service.Scan();

        Assert.Empty(result.candidates);
        Assert.Equal(2, result.diagnostics.Count(diagnostic =>
            diagnostic.message.Contains("installed more than once", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void DependencyGraphUsesDeterministicTopologicalOrderAndRejectsCycles()
    {
        WritePlugin("z-dependent.zip", Manifest("tests.beta", "tests.alpha"), TextContent("beta"));
        WritePlugin("a-base.zip", Manifest("tests.alpha"), TextContent("alpha"));
        var service = new PluginSourceService(m_plugins, m_library);

        PluginScanResult ordered = service.Scan();
        Assert.Equal(["tests.alpha", "tests.beta"], ordered.candidates.Select(candidate => candidate.manifest.pluginId));

        Directory.Delete(m_plugins, recursive: true);
        Directory.CreateDirectory(m_plugins);
        WritePlugin("a.zip", Manifest("cycle.a", "cycle.b"), TextContent("a"));
        WritePlugin("b.zip", Manifest("cycle.b", "cycle.a"), TextContent("b"));
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
        WritePlugin("invalid.zip", Manifest("tests.invalid"), entries);
        var service = new PluginSourceService(m_plugins, m_library);

        PluginScanResult result = service.Scan();

        Assert.Empty(result.candidates);
        PluginDiagnostic diagnostic = Assert.Single(result.diagnostics);
        Assert.Contains(expectedMessage, diagnostic.message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PortableCaseCollisionsAreRejectedBeforeExtraction()
    {
        WritePlugin("collision.zip", Manifest("tests.collision"), new Dictionary<string, byte[]>
        {
            ["Assets/Data.txt"] = [1],
            ["Assets/Data.txt.imeta"] = [1],
            ["Assets/data.txt"] = [2],
            ["Assets/data.txt.imeta"] = [2]
        });
        var service = new PluginSourceService(m_plugins, m_library);

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
        WritePlugin("portable.zip", Manifest("tests.portable"), new Dictionary<string, byte[]>
        {
            [entryPath] = [1],
            [entryPath + ".imeta"] = [1]
        });
        var service = new PluginSourceService(m_plugins, m_library);

        PluginScanResult result = service.Scan();

        Assert.Empty(result.candidates);
        Assert.Contains(expectedMessage, Assert.Single(result.diagnostics).message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SymbolicLinksAndExcessiveEntryCountsAreRejectedBeforeExtraction()
    {
        string symbolicLinkPath = Path.Combine(m_plugins, "symbolic-link.zip");
        using (FileStream stream = System.IO.File.Create(symbolicLinkPath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "Plugin.inno", SerializationManager.Serialize(Manifest("tests.symbolic-link")));
            ZipArchiveEntry link = archive.CreateEntry("Assets/link.txt");
            link.ExternalAttributes = unchecked((int)0xA1FF0000);
            WriteEntry(archive, "Assets/link.txt.imeta", [1]);
        }

        var symbolicLinkService = new PluginSourceService(m_plugins, m_library);
        PluginScanResult symbolicLink = symbolicLinkService.Scan();

        Assert.Empty(symbolicLink.candidates);
        Assert.Contains("symbolic link", Assert.Single(symbolicLink.diagnostics).message, StringComparison.OrdinalIgnoreCase);

        System.IO.File.Delete(symbolicLinkPath);
        WritePlugin("entry-count.zip", Manifest("tests.entry-count"), TextContent("bounded"));
        var entryCountService = new PluginSourceService(
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
        WritePlugin("bounded.zip", Manifest("tests.bounded"), entries);
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
        var service = new PluginSourceService(m_plugins, m_library, limits);

        PluginScanResult result = service.Scan();

        Assert.Empty(result.candidates);
        Assert.Contains(expectedMessage, Assert.Single(result.diagnostics).message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CorruptManifestAndDuplicateEntriesAreIsolated()
    {
        WritePluginWithManifestBytes(
            "corrupt.zip",
            [0x49, 0x4E, 0x4E, 0x4F],
            TextContent("corrupt"));
        WriteArchiveWithDuplicateEntry("duplicate.zip");
        string corruptDirectory = Path.Combine(m_plugins, "CorruptDirectory");
        Directory.CreateDirectory(Path.Combine(corruptDirectory, "Assets"));
        System.IO.File.WriteAllBytes(Path.Combine(corruptDirectory, "Plugin.inno"), [0x49, 0x4E, 0x4E, 0x4F]);
        var service = new PluginSourceService(m_plugins, m_library);

        PluginScanResult result = service.Scan();

        Assert.Empty(result.candidates);
        Assert.Equal(3, result.diagnostics.Count);
        Assert.Equal(2, result.diagnostics.Count(diagnostic =>
            diagnostic.message.Contains("malformed", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(result.diagnostics, diagnostic =>
            diagnostic.message.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MissingDependenciesDuplicateIdsAndInvalidOverridesRejectTheCandidateGeneration()
    {
        WritePlugin("missing.zip", Manifest("tests.missing", "tests.absent"), TextContent("missing"));
        WritePlugin("duplicate-a.zip", Manifest("tests.duplicate"), TextContent("first"));
        WritePlugin("duplicate-b.zip", Manifest("tests.duplicate"), TextContent("second"));
        PluginManifest invalidOverride = Manifest("tests.override");
        invalidOverride.overrides = ["tests.base"];
        WritePlugin("override.zip", invalidOverride, TextContent("override"));
        var service = new PluginSourceService(m_plugins, m_library);

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
    public void ExportProducesAValidatedDeterministicSourceOnlyArchive()
    {
        string assets = Path.Combine(m_root, "ProjectAssets");
        string assetLibrary = Path.Combine(m_root, "AssetLibrary");
        Directory.CreateDirectory(assets);
        AssetManagerOptions defaults = AssetManagerOptions.Create(assets, assetLibrary);
        AssetManager.Initialize(new AssetManagerOptions
        {
            assetRoot = defaults.assetRoot,
            libraryRoot = defaults.libraryRoot,
            enableFileSystemWatcher = false,
            fileWatcherFlushDelayMs = defaults.fileWatcherFlushDelayMs,
            sourcePolicy = defaults.sourcePolicy,
            sourceMounts = defaults.sourceMounts,
            cacheOptions = defaults.cacheOptions
        });
        AssetManager.CreateDirectory(AssetPath.Project("Content"));
        Assert.True(AssetManager.Save(AssetPath.Project("Content/value.txt"), new TextAsset("deterministic")));
        ProjectSettingsManager.SetProjectOverride(
            PluginDefaultTestSetting.id,
            new PluginDefaultTestSetting { value = 73 },
            []);
        var definition = new PluginDefinitionAsset
        {
            pluginId = "tests.export",
            displayName = "Export Test",
            assetRoots = ["Content"],
            settingIds = [PluginDefaultTestSetting.id]
        };
        string firstPath = Path.Combine(m_root, "first.zip");
        string secondPath = Path.Combine(m_root, "second.zip");

        string firstHash = PluginExportService.ExportZip(definition, firstPath);
        string secondHash = PluginExportService.ExportZip(definition, secondPath);

        Assert.Equal(firstHash, secondHash);
        Assert.Equal(System.IO.File.ReadAllBytes(firstPath), System.IO.File.ReadAllBytes(secondPath));
        using ZipArchive archive = ZipFile.OpenRead(firstPath);
        Assert.Equal(
            ["Assets/", "Assets/Content.imeta", "Assets/Content/", "Assets/Content/value.txt", "Assets/Content/value.txt.imeta", "Plugin.inno"],
            archive.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal));
        Assert.All(archive.Entries, entry => Assert.Equal(1980, entry.LastWriteTime.Year));
        Assert.DoesNotContain(archive.Entries, entry =>
            string.Equals(Path.GetExtension(entry.FullName), ".dll", StringComparison.OrdinalIgnoreCase));
        PluginManifest manifest = ReadManifest(archive);
        ProjectSettingRecord setting = Assert.Single(manifest.settingContributions);
        Assert.Equal(PluginDefaultTestSetting.id, setting.id);
        Assert.Equal(
            TypeCacheManager.GetTypeRef(typeof(PluginDefaultTestSetting)).stableId,
            setting.stableTypeId);
        var restoredSetting = new PluginDefaultTestSetting();
        _ = SerializationManager.RestorePropertiesData(restoredSetting, setting.propertyData);
        Assert.Equal(73, restoredSetting.value);
    }

    [Fact]
    public void ZipAndDirectoryExportsShareOneLogicalContentHashAndScanContract()
    {
        InitializeProjectAssets();
        AssetManager.CreateDirectory(AssetPath.Project("Content"));
        Assert.True(AssetManager.Save(AssetPath.Project("Content/value.txt"), new TextAsset("shared")));
        var definition = new PluginDefinitionAsset
        {
            pluginId = "tests.dual-container",
            displayName = "Dual Container",
            assetRoots = ["Content"]
        };
        string zipPath = Path.Combine(m_root, "dual-container.zip");
        string directoryPath = Path.Combine(m_plugins, "DualContainer");

        string zipHash = PluginExportService.ExportZip(definition, zipPath);
        string directoryHash = PluginExportService.ExportDirectory(definition, directoryPath);

        Assert.Equal(zipHash, directoryHash);
        Assert.True(System.IO.File.Exists(Path.Combine(directoryPath, "Plugin.inno")));
        Assert.True(System.IO.File.Exists(Path.Combine(directoryPath, "Assets", "Content", "value.txt")));
        PluginCandidate candidate = Assert.Single(new PluginSourceService(m_plugins, m_library)
            .Scan().candidates);
        Assert.Equal(PluginSourceKind.Directory, candidate.sourceKind);
        Assert.Equal(directoryHash, candidate.contentHash);
    }

    [Fact]
    public void ExportRejectsASecondPhysicalContainerForTheSameInstalledSourceName()
    {
        InitializeProjectAssets();
        AssetManager.CreateDirectory(AssetPath.Project("Content"));
        Assert.True(AssetManager.Save(AssetPath.Project("Content/value.txt"), new TextAsset("shared")));
        var definition = new PluginDefinitionAsset
        {
            pluginId = "tests.container-conflict",
            displayName = "Container Conflict",
            assetRoots = ["Content"]
        };
        string directoryPath = Path.Combine(m_plugins, definition.pluginId);
        _ = PluginExportService.ExportDirectory(definition, directoryPath);

        InvalidOperationException zipConflict = Assert.Throws<InvalidOperationException>(() =>
            PluginExportService.ExportZip(definition, directoryPath + ".zip"));

        Assert.Contains("already exists as a directory", zipConflict.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExportIncludesTransitiveProjectDependenciesAndExcludesDefinitionAssets()
    {
        InitializeProjectAssets();
        AssetManager.CreateDirectory(AssetPath.Project("Content"));
        AssetManager.CreateDirectory(AssetPath.Project("Definitions"));
        Assert.True(AssetManager.Save(AssetPath.Project("Content/value.txt"), new TextAsset("dependency")));
        TextAsset dependency = AssetManager.Load<TextAsset>(AssetPath.Project("Content/value.txt"));
        Assert.True(AssetManager.Save(AssetPath.Project("Definitions/nested.iplugin"), new PluginDefinitionAsset
        {
            pluginId = "tests.nested",
            displayName = "Nested",
            assets = [dependency]
        }));
        PluginDefinitionAsset nested = AssetManager.Load<PluginDefinitionAsset>(AssetPath.Project("Definitions/nested.iplugin"));
        var definition = new PluginDefinitionAsset
        {
            pluginId = "tests.transitive",
            displayName = "Transitive",
            assets = [nested]
        };
        string outputPath = Path.Combine(m_root, "transitive.zip");

        _ = PluginExportService.ExportZip(definition, outputPath);

        using ZipArchive archive = ZipFile.OpenRead(outputPath);
        Assert.Contains(archive.Entries, static entry => entry.FullName == "Assets/Content/value.txt");
        Assert.Contains(archive.Entries, static entry => entry.FullName == "Assets/Content/value.txt.imeta");
        Assert.DoesNotContain(archive.Entries, static entry =>
            entry.FullName.EndsWith(".iplugin", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExportRequiresDeclaredPluginDependenciesAndDoesNotCopyTheirAssets()
    {
        InitializeProjectAssets();
        AssetManager.CreateDirectory(AssetPath.Project("Local"));
        Assert.True(AssetManager.Save(AssetPath.Project("Local/marker.txt"), new TextAsset("local")));
        string pluginRoot = Path.Combine(m_root, "DependencyPlugin");
        Directory.CreateDirectory(pluginRoot);
        System.IO.File.WriteAllText(Path.Combine(pluginRoot, "external.txt"), "external");
        System.IO.File.WriteAllBytes(Path.Combine(pluginRoot, "external.txt.imeta"), CreateTextSourceMeta());
        AssetManager.ReplaceSourceMounts(
        [
            AssetManager.sourceMounts.Single(static mount => mount.id == AssetSourceId.project),
            new AssetSourceMount(new AssetSourceId("tests.dependency"), pluginRoot, isReadOnly: true)
        ]);
        TextAsset external = AssetManager.Load<TextAsset>(
            new AssetPath(new AssetSourceId("tests.dependency"), "external.txt"));
        var definition = new PluginDefinitionAsset
        {
            pluginId = "tests.consumer",
            displayName = "Consumer",
            assetRoots = ["Local"],
            assets = [external]
        };

        InvalidOperationException undeclared = Assert.Throws<InvalidOperationException>(() =>
            PluginExportService.ExportZip(definition, Path.Combine(m_root, "undeclared.zip")));
        Assert.Contains("undeclared Plugin dependency", undeclared.Message, StringComparison.OrdinalIgnoreCase);

        definition.dependencies = ["tests.dependency"];
        string outputPath = Path.Combine(m_root, "declared.zip");
        _ = PluginExportService.ExportZip(definition, outputPath);

        using ZipArchive archive = ZipFile.OpenRead(outputPath);
        PluginManifest manifest = ReadManifest(archive);
        Assert.Equal(new[] { "tests.dependency" }, manifest.dependencies);
        Assert.DoesNotContain(archive.Entries, static entry => entry.FullName.Contains("external.txt"));
    }

    [Fact]
    public void SettingsCompositionRejectsPeersAllowsExplicitDependencyOverrideAndKeepsProjectHighest()
    {
        ProjectSettingRecord first = SettingRecord(10);
        ProjectSettingRecord second = SettingRecord(20);
        ProjectSettingsManager.Rebuild(
        [
            new ProjectSettingsContributor("tests.first", [], [], [first])
        ]);
        Assert.Equal(10, ProjectSettingsManager.Get<PluginDefaultTestSetting>(PluginDefaultTestSetting.id).value);

        Assert.Throws<InvalidOperationException>(() => ProjectSettingsManager.Rebuild(
        [
            new ProjectSettingsContributor("tests.first", [], [], [first]),
            new ProjectSettingsContributor("tests.peer", [], [], [second])
        ]));
        Assert.Equal(10, ProjectSettingsManager.Get<PluginDefaultTestSetting>(PluginDefaultTestSetting.id).value);

        ProjectSettingsContributor[] overridden =
        [
            new ProjectSettingsContributor("tests.first", [], [], [first]),
            new ProjectSettingsContributor("tests.second", ["tests.first"], ["tests.first"], [second])
        ];
        ProjectSettingsManager.Rebuild(overridden);
        Assert.Equal(20, ProjectSettingsManager.Get<PluginDefaultTestSetting>(PluginDefaultTestSetting.id).value);

        ProjectSettingsManager.SetProjectOverride(
            PluginDefaultTestSetting.id,
            new PluginDefaultTestSetting { value = 30 },
            overridden);
        ProjectSettingsManager.Rebuild(
        [
            new ProjectSettingsContributor("tests.first", [], [], [SettingRecord(40)])
        ]);
        Assert.Equal(30, ProjectSettingsManager.Get<PluginDefaultTestSetting>(PluginDefaultTestSetting.id).value);
    }

    [Fact]
    public void InitialActivationFailureKeepsTheHostOnlyMountAndPublishesDiscoveryDiagnostics()
    {
        WritePlugin("invalid-meta.zip", Manifest("tests.invalid-meta"), new Dictionary<string, byte[]>
        {
            ["Assets/value.txt"] = "value"u8.ToArray(),
            ["Assets/value.txt.imeta"] = [1]
        });
        InitializeProjectAssets();
        var service = new PluginSourceService(m_plugins, m_library);
        PluginScanResult scan = service.Scan();

        PluginManager.Initialize(m_plugins, m_library, scan);

        Assert.Empty(PluginCatalog.activePlugins);
        Assert.Single(PluginCatalog.discovery.candidates);
        Assert.Contains(PluginCatalog.discovery.diagnostics, diagnostic =>
            diagnostic.message.Contains("candidate activation failed", StringComparison.OrdinalIgnoreCase));
        Assert.Collection(
            AssetManager.sourceMounts,
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
                TypeCacheManager.GetTypeRef(typeof(PluginDefaultTestSetting)).stableId,
                SerializationManager.CapturePropertiesData(contributed))
        ];
        WritePlugin("settings.zip", manifest, new Dictionary<string, byte[]>
        {
            ["Assets/value.txt"] = "value"u8.ToArray(),
            ["Assets/value.txt.imeta"] = CreateTextSourceMeta()
        });
        InitializeProjectAssets();
        var service = new PluginSourceService(m_plugins, m_library);
        PluginScanResult scan = service.Scan();

        PluginManager.Initialize(m_plugins, m_library, scan);

        Assert.Equal("tests.settings", Assert.Single(PluginCatalog.activePlugins).manifest.pluginId);
        Assert.Equal(
            [AssetSourceId.project, new AssetSourceId("tests.settings")],
            AssetManager.sourceMounts.Select(static mount => mount.id));
        Assert.Equal(42, ProjectSettingsManager.Get<PluginDefaultTestSetting>(PluginDefaultTestSetting.id).value);
    }

    [Fact]
    public void CodePluginUpdate_RemainsInvisibleWhileItsCompilationCandidateIsPending()
    {
        InitializeProjectAssets();
        var service = new PluginSourceService(m_plugins, m_library);
        PluginManager.Initialize(
            m_plugins,
            m_library,
            service.Scan());
        WritePlugin("staged-code.zip", Manifest("tests.staged-code"), new Dictionary<string, byte[]>
        {
            ["Assets/Plugin.cs"] = "public sealed class PluginEntry { }"u8.ToArray(),
            ["Assets/Plugin.cs.imeta"] = CreateTextSourceMeta()
        });

        Assert.True(PluginManager.Refresh());

        Assert.True(PluginManager.hasPendingActivation);
        Assert.Empty(PluginCatalog.activePlugins);
        Assert.Collection(
            AssetManager.sourceMounts,
            mount => Assert.Equal(AssetSourceId.project, mount.id));
        Assert.Equal(
            "tests.staged-code",
            Assert.Single(PluginManager.compilationPlugins).manifest.pluginId);
        Assert.Contains(
            PluginManager.compilationAssets!.GetFileSystemEntries(includeDirectories: false),
            entry => entry.assetPath == new AssetPath(new AssetSourceId("tests.staged-code"), "Plugin.cs"));

        PluginManager.RollbackPending();

        Assert.False(PluginManager.hasPendingActivation);
        Assert.Empty(PluginCatalog.activePlugins);
        Assert.Collection(
            AssetManager.sourceMounts,
            mount => Assert.Equal(AssetSourceId.project, mount.id));
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

    private static ProjectSettingRecord SettingRecord(int value)
        => new(
            PluginDefaultTestSetting.id,
            TypeCacheManager.GetTypeRef(typeof(PluginDefaultTestSetting)).stableId,
            SerializationManager.CapturePropertiesData(new PluginDefaultTestSetting { value = value }));

    private static PluginManifest ReadManifest(ZipArchive archive)
    {
        using Stream input = archive.GetEntry("Plugin.inno")!.Open();
        using var bytes = new MemoryStream();
        input.CopyTo(bytes);
        return SerializationManager.Deserialize<PluginManifest>(bytes.ToArray());
    }

    private void WritePlugin(
        string fileName,
        PluginManifest manifest,
        IReadOnlyDictionary<string, byte[]> entries)
        => WritePluginWithManifestBytes(fileName, SerializationManager.Serialize(manifest), entries);

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
            SerializationManager.Serialize(manifest));
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
        WriteEntry(archive, "Plugin.inno", SerializationManager.Serialize(Manifest("tests.duplicate-entry")));
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
        AssetManagerOptions defaults = AssetManagerOptions.Create(assets, library);
        AssetManager.Initialize(new AssetManagerOptions
        {
            assetRoot = defaults.assetRoot,
            libraryRoot = defaults.libraryRoot,
            enableFileSystemWatcher = false,
            fileWatcherFlushDelayMs = defaults.fileWatcherFlushDelayMs,
            sourcePolicy = defaults.sourcePolicy,
            sourceMounts = defaults.sourceMounts,
            cacheOptions = defaults.cacheOptions
        });
    }

    private static byte[] CreateTextSourceMeta()
        => SerializationManager.Serialize(new TestAssetSourceMeta
        {
            persistentId = Guid.NewGuid(),
            sourceKind = (int)AssetSourceKind.File,
            importerId = "Inno.Assets.Loader.Importers.TextAssetImporter"
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
