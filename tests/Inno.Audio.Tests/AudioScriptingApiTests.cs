using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Inno.Assets.Pipeline;
using Inno.Audio.Runtime;
using Inno.Audio.Scene;
using Inno.Core.Identity;
using Inno.Core.Settings;
using Inno.Plugins;
using Inno.Plugins.Authoring;
using Inno.Runtime;
using Inno.Scripting.Compiler;
using Xunit;

namespace Inno.Audio.Tests;

public sealed class AudioScriptingApiTests
{
    [Fact]
    public async Task FacadeMixerProviderAndSceneComponentsCompileThroughLogicalNamespace()
    {
        string projectRoot = Path.Combine(
            Path.GetTempPath(),
            "InnoAudioScriptingTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "Assets"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "Plugins"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "Library"));
        try
        {
            _ = typeof(Inno.Audio.Audio);
            _ = typeof(AudioProjectSettings);
            _ = typeof(AudioSource);
            var identities = new IdentityAllocator();
            using IDisposable identityScope = identities.EnterScope();
            using EngineHost host = new EngineHostBuilder()
                .UseMetadataCache(Path.Combine(projectRoot, "Library", "Assemblies"))
                .Build();
            using var settings = new ProjectSettingsStore(
                Path.Combine(projectRoot, "Settings.Project.inno"),
                host.types,
                host.serialization,
                new ProjectId("tests.audio.scripting"));
            var pluginSources = new PluginSourceService(
                host.serialization,
                Path.Combine(projectRoot, "Plugins"),
                Path.Combine(projectRoot, "Library"));
            PluginScanResult scan = pluginSources.Scan();
            using var assets = new AssetPipeline(
                host.modules,
                host.types,
                host.serialization,
                identities,
                host.diagnostics,
                host.logs,
                AssetPipelineOptions.Create(
                    Path.Combine(projectRoot, "Assets"),
                    Path.Combine(projectRoot, "Library")) with
                {
                    enableFileSystemWatcher = false
                });
            using var plugins = new PluginEnvironment(
                assets,
                settings,
                host.serialization,
                Path.Combine(projectRoot, "Plugins"),
                Path.Combine(projectRoot, "Library"),
                scan);
            var compiler = new ScriptCompiler(
                new ScriptCompilerOptions { projectRootDirectory = projectRoot },
                assets,
                plugins);
            File.WriteAllText(Path.Combine(projectRoot, "Assets", "AudioProbe.cs"), """
                using InnoEngine.Audio;

                public sealed class ScriptAudioProbe
                {
                    public AudioSource? source { get; set; }

                    public AudioVoiceHandle Play(AudioClipAsset clip)
                        => Audio.Play(clip);
                }

                [AudioMixerFeatureExtension("tests.audio.feature")]
                public sealed class ScriptAudioFeature : AudioMixerFeature
                {
                    public override void Build(
                        AudioMixerBuilder builder,
                        SerializedAudioExtensionState state)
                    {
                        builder.AddBus(new AudioBusId("tests.audio.bus"), AudioBusId.master);
                    }
                }

                [AudioContentProviderExtension("tests.audio.provider")]
                public sealed class ScriptAudioProvider : AudioContentProvider
                {
                    public override void Submit(AudioContentProviderContext context)
                    {
                    }
                }
                """);
            assets.Rescan();

            ScriptCompilationResult result = await compiler.CompileAuthoringGenerationAsync();

            Assert.True(result.success, string.Join(
                Environment.NewLine,
                result.diagnostics.Select(static diagnostic => $"{diagnostic.id}: {diagnostic.message}")));
        }
        finally
        {
            if (Directory.Exists(projectRoot))
                Directory.Delete(projectRoot, recursive: true);
        }
    }
}
