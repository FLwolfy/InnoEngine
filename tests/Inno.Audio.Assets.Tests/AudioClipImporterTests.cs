using System;
using System.Buffers.Binary;
using System.IO;
using System.Reflection;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Core.Diagnostics;
using Inno.Core.Identity;
using Inno.Core.Logging;
using Inno.Core.Serialization;
using Inno.Extensibility.Modules;
using Inno.Extensibility.Types;
using Xunit;

namespace Inno.Audio.Assets.Tests;

public sealed class AudioClipImporterTests : IDisposable
{
    private readonly DiagnosticHub m_diagnostics = new();
    private readonly IdentityAllocator m_identities = new();
    private readonly IDisposable m_identityScope;
    private readonly string m_root;
    private readonly string m_assets;
    private readonly string m_library;
    private readonly LogRouter m_logs = new();
    private readonly ModuleHost m_modules;
    private readonly SerializationRegistry m_serialization;
    private readonly TypeCatalog m_types;

    public AudioClipImporterTests()
    {
        m_root = Path.Combine(Path.GetTempPath(), "InnoAudioImporterTests", Guid.NewGuid().ToString("N"));
        m_assets = Path.Combine(m_root, "Assets");
        m_library = Path.Combine(m_root, "Library");
        Directory.CreateDirectory(m_assets);
        m_identityScope = m_identities.EnterScope();
        m_modules = new ModuleHost(new ModuleHostOptions
        {
            cacheDirectory = Path.Combine(m_root, "Assemblies")
        });
        _ = Assembly.Load("Inno.Audio.Assets");
        m_types = new TypeCatalog(m_modules);
        m_serialization = new SerializationRegistry(m_types);
        m_types.Rebuild();
    }

    public void Dispose()
    {
        m_serialization.Dispose();
        m_types.Dispose();
        m_modules.Dispose();
        m_logs.Dispose();
        m_identityScope.Dispose();
        if (Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }

    [Fact]
    public void ImportsWavFlacAndMp3IntoMetadataAndSeparateEncodedArtifacts()
    {
        Write("tone.wav", CreateWav());
        Write("tone.flac", CreateFlac());
        Write("tone.mp3", CreateMp3());
        using AssetLoader loader = CreateLoader();

        AudioClipAsset wav = Load(loader, "tone.wav");
        AudioClipAsset flac = Load(loader, "tone.flac");
        AudioClipAsset mp3 = Load(loader, "tone.mp3");

        Assert.Equal(AudioCodecId.wav, wav.metadata!.Value.codec);
        Assert.Equal(4, wav.metadata.Value.frameCount);
        Assert.Equal(AudioCodecId.flac, flac.metadata!.Value.codec);
        Assert.Equal(48000, flac.metadata.Value.frameCount);
        Assert.Equal(AudioCodecId.mp3, mp3.metadata!.Value.codec);
        Assert.Equal(1152, mp3.metadata.Value.frameCount);
        AssertSeparateArtifact(loader, wav, CreateWav());
        AssertSeparateArtifact(loader, flac, CreateFlac());
        AssertSeparateArtifact(loader, mp3, CreateMp3());
    }

    [Fact]
    public void CorruptEncodedSourceFailsWithoutCommittingAnAsset()
    {
        Write("broken.wav", [1, 2, 3, 4]);
        using AssetLoader loader = CreateLoader();

        Assert.False(loader.Import(AssetPath.Project("broken.wav")));
        Assert.True(loader.TryGetPersistentId(AssetPath.Project("broken.wav"), out Guid persistentId));
        Assert.False(loader.TryGetArtifact(persistentId, "audio-data", out _));
    }

    private AssetLoader CreateLoader()
        => new(m_types, m_serialization, m_identities, m_diagnostics, m_logs, m_assets, m_library);

    private static AudioClipAsset Load(AssetLoader loader, string path)
        => Assert.IsType<AudioClipAsset>(loader.Load(AssetPath.Project(path), typeof(AudioClipAsset)));

    private static void AssertSeparateArtifact(AssetLoader loader, AudioClipAsset asset, byte[] expected)
    {
        Assert.True(loader.TryGetArtifact(asset.identity.persistentId, "runtime", out AssetArtifactInfo? runtime));
        Assert.True(loader.TryGetArtifact(asset.identity.persistentId, "audio-data", out AssetArtifactInfo? data));
        Assert.NotNull(runtime);
        Assert.NotNull(data);
        Assert.InRange(runtime.length, 1, 127);
        Assert.NotEqual(runtime.absolutePath, data.absolutePath);
        Assert.Equal(expected, File.ReadAllBytes(data.absolutePath));
    }

    private void Write(string path, byte[] bytes) => File.WriteAllBytes(Path.Combine(m_assets, path), bytes);

    private static byte[] CreateWav()
    {
        byte[] bytes = new byte[60];
        "RIFF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), 52);
        "WAVEfmt "u8.CopyTo(bytes.AsSpan(8));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(22, 2), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24, 4), 48000);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28, 4), 192000);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(32, 2), 4);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(34, 2), 16);
        "data"u8.CopyTo(bytes.AsSpan(36));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40, 4), 16);
        return bytes;
    }

    private static byte[] CreateFlac()
    {
        byte[] bytes = new byte[42];
        "fLaC"u8.CopyTo(bytes);
        bytes[4] = 0x80;
        bytes[7] = 34;
        ulong packed = ((ulong)48000 << 44) | (1UL << 41) | (15UL << 36) | 48000UL;
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(18, 8), packed);
        return bytes;
    }

    private static byte[] CreateMp3()
    {
        byte[] bytes = new byte[417];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, 0xfffb9000);
        return bytes;
    }
}
