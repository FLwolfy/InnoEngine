using Inno.Core.Serialization;
using Inno.Core.Settings;
using Inno.Extensibility.Types;

namespace Inno.Audio.Runtime;

/// <summary>
/// Stores portable project-wide defaults for audio playback and caching.
/// </summary>
[StableTypeId("29f99b3b-a9f3-45f4-888a-f4b13cb3504c")]
[ProjectSettingDefinition("inno.audio.runtime")]
public sealed class AudioProjectSettings : ISerializable
{
    /// <summary>
    /// Gets the stable project-setting identity for audio runtime defaults.
    /// </summary>
    public static ProjectSettingId settingId => new("inno.audio.runtime");

    /// <summary>
    /// Gets or sets the default mixer asset, or <see langword="null"/> for the master-only graph.
    /// </summary>
    [SerializableProperty]
    public AudioMixerAsset? defaultMixer { get; set; }

    /// <summary>
    /// Gets or sets the initial non-negative master bus gain.
    /// </summary>
    [SerializableProperty]
    public float masterVolume { get; set; } = 1f;

    /// <summary>
    /// Gets or sets the positive maximum active voice count.
    /// </summary>
    [SerializableProperty]
    public int maxVoices { get; set; } = 128;

    /// <summary>
    /// Gets or sets the decoded clip cache budget in bytes.
    /// </summary>
    [SerializableProperty]
    public long decodedCacheBudgetBytes { get; set; } = 128L * 1024 * 1024;

    /// <summary>
    /// Gets or sets the encoded byte threshold used by automatic streaming selection.
    /// </summary>
    [SerializableProperty]
    public long automaticStreamingThresholdBytes { get; set; } = 2L * 1024 * 1024;
}
