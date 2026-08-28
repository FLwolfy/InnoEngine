using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Inno.Assets.Core;
using Inno.Core.Reflection;
using Inno.Core.Serialization;

namespace Inno.Rendering;

/// <summary>
/// Stores one ordered, reload-safe pipeline feature configuration.
/// </summary>
public sealed class RenderFeatureConfiguration
{
    /// <summary>
    /// Creates a feature configuration.
    /// </summary>
    /// <param name="featureTypeId">Stable feature extension identifier.</param>
    /// <param name="settingsJson">Neutral JSON settings owned by the feature.</param>
    /// <param name="enabled">Whether the feature participates in graph building.</param>
    public RenderFeatureConfiguration(string featureTypeId, string settingsJson = "{}", bool enabled = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureTypeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsJson);
        using JsonDocument settings = JsonDocument.Parse(settingsJson);
        this.featureTypeId = featureTypeId;
        this.settingsJson = settings.RootElement.GetRawText();
        this.enabled = enabled;
    }

    /// <summary>Gets the stable feature extension identifier.</summary>
    public string featureTypeId { get; }

    /// <summary>Gets normalized neutral JSON settings.</summary>
    public string settingsJson { get; }

    /// <summary>Gets whether the feature participates in graph building.</summary>
    public bool enabled { get; }
}

/// <summary>
/// Stores quality controls shared by built-in Forward+ and Deferred paths.
/// </summary>
public sealed class RenderQualitySettings
{
    private readonly Action? m_changed;
    private int m_directionalShadowCascades = 4;
    private int m_shadowResolution = 2048;
    private bool m_hdr = true;
    private bool m_bloom = true;
    private float m_exposure;

    /// <summary>Creates default production quality settings.</summary>
    public RenderQualitySettings()
    {
    }

    internal RenderQualitySettings(Action changed)
    {
        m_changed = changed ?? throw new ArgumentNullException(nameof(changed));
    }

    /// <summary>Gets or sets whether the pipeline renders an HDR intermediate target.</summary>
    public bool hdr
    {
        get => m_hdr;
        set
        {
            m_hdr = value;
            m_changed?.Invoke();
        }
    }

    /// <summary>Gets or sets whether Bloom participates in post-processing.</summary>
    public bool bloom
    {
        get => m_bloom;
        set
        {
            m_bloom = value;
            m_changed?.Invoke();
        }
    }

    /// <summary>Gets or sets exposure in photographic stops.</summary>
    public float exposure
    {
        get => m_exposure;
        set
        {
            m_exposure = float.IsFinite(value)
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value));
            m_changed?.Invoke();
        }
    }

    /// <summary>Gets or sets directional shadow cascade count from one through four.</summary>
    public int directionalShadowCascades
    {
        get => m_directionalShadowCascades;
        set
        {
            m_directionalShadowCascades = Math.Clamp(value, 1, 4);
            m_changed?.Invoke();
        }
    }

    /// <summary>Gets or sets directional shadow-map resolution.</summary>
    public int shadowResolution
    {
        get => m_shadowResolution;
        set
        {
            m_shadowResolution = Math.Clamp(value, 256, 8192);
            m_changed?.Invoke();
        }
    }

    internal void Restore(
        bool hdr,
        bool bloom,
        float exposure,
        int directionalShadowCascades,
        int shadowResolution)
    {
        m_hdr = hdr;
        m_bloom = bloom;
        m_exposure = exposure;
        m_directionalShadowCascades = Math.Clamp(directionalShadowCascades, 1, 4);
        m_shadowResolution = Math.Clamp(shadowResolution, 256, 8192);
    }
}

/// <summary>
/// Selects a pipeline extension, default path, quality and ordered features as project data.
/// </summary>
[StableTypeId("b17d289e-62de-4299-9e85-497143911798")]
public sealed class RenderPipelineAsset : AssetObject
{
    private readonly List<RenderFeatureConfiguration> m_features = [];
    [SerializableProperty(PropertyVisibility.Hide)]
    private string m_qualityStateJson = "{}";
    [SerializableProperty(PropertyVisibility.Hide)]
    private string m_featureStateJson = "[]";

    /// <summary>Creates a pipeline asset with default production quality.</summary>
    public RenderPipelineAsset()
    {
        quality = new RenderQualitySettings(SynchronizeQuality);
        SynchronizeQuality();
    }

    /// <summary>Gets or sets the stable pipeline extension identifier.</summary>
    [SerializableProperty]
    public string pipelineTypeId { get; set; } = "inno.pipeline.universal";

    /// <summary>Gets or sets the default render path.</summary>
    [SerializableProperty]
    public RenderPath defaultRenderPath { get; set; } = RenderPath.ForwardPlus;

    /// <summary>Gets quality settings shared by pipeline paths.</summary>
    public RenderQualitySettings quality { get; }

    /// <summary>Gets ordered feature configurations.</summary>
    public IReadOnlyList<RenderFeatureConfiguration> features => m_features;

    /// <summary>
    /// Replaces ordered feature configurations.
    /// </summary>
    /// <param name="features">Ordered neutral feature configurations.</param>
    public void SetFeatures(IEnumerable<RenderFeatureConfiguration> features)
    {
        ArgumentNullException.ThrowIfNull(features);
        m_features.Clear();
        foreach (RenderFeatureConfiguration feature in features)
        {
            ArgumentNullException.ThrowIfNull(feature);
            m_features.Add(feature);
        }

        m_featureStateJson = JsonSerializer.Serialize(m_features.Select(static value => new FeatureData
        {
            featureTypeId = value.featureTypeId,
            settingsJson = value.settingsJson,
            enabled = value.enabled
        }));
    }

    [OnSerializableRestored]
    private void OnSerializableRestored()
    {
        QualityData qualityData = JsonSerializer.Deserialize<QualityData>(m_qualityStateJson) ?? new QualityData();
        quality.Restore(
            qualityData.hdr,
            qualityData.bloom,
            qualityData.exposure,
            qualityData.directionalShadowCascades,
            qualityData.shadowResolution);
        FeatureData[] features = JsonSerializer.Deserialize<FeatureData[]>(m_featureStateJson) ?? [];
        m_features.Clear();
        m_features.AddRange(features.Select(static value => new RenderFeatureConfiguration(
            value.featureTypeId,
            value.settingsJson,
            value.enabled)));
    }

    private void SynchronizeQuality()
    {
        m_qualityStateJson = JsonSerializer.Serialize(new QualityData
        {
            hdr = quality.hdr,
            bloom = quality.bloom,
            exposure = quality.exposure,
            directionalShadowCascades = quality.directionalShadowCascades,
            shadowResolution = quality.shadowResolution
        });
    }

    private sealed class QualityData
    {
        public bool hdr { get; set; } = true;
        public bool bloom { get; set; } = true;
        public float exposure { get; set; }
        public int directionalShadowCascades { get; set; } = 4;
        public int shadowResolution { get; set; } = 2048;
    }

    private sealed class FeatureData
    {
        public string featureTypeId { get; set; } = string.Empty;
        public string settingsJson { get; set; } = "{}";
        public bool enabled { get; set; } = true;
    }
}
