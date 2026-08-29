using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Assets.Core;
using Inno.Core.Reflection;
using Inno.Core.Serialization;

namespace Inno.Rendering;

/// <summary>
/// Stores reload-safe configuration for one pipeline or feature extension generation.
/// </summary>
public struct SerializedRenderExtensionState
{
    /// <summary>Creates empty extension state for deserialization.</summary>
    public SerializedRenderExtensionState()
    {
        propertyData = [];
    }

    /// <summary>Creates immutable extension state from stable identity and property bytes.</summary>
    /// <param name="stableTypeId">Stable type identity, or empty when the extension has no typed settings.</param>
    /// <param name="propertyData">Neutral bytes produced by <see cref="SerializationManager.CapturePropertiesData"/>.</param>
    public SerializedRenderExtensionState(Guid stableTypeId, ReadOnlySpan<byte> propertyData)
    {
        this.stableTypeId = stableTypeId;
        this.propertyData = propertyData.ToArray();
    }

    /// <summary>Gets or sets the stable settings type identity.</summary>
    [SerializableProperty]
    public Guid stableTypeId { get; set; }

    /// <summary>Gets or sets neutral serialized property bytes.</summary>
    [SerializableProperty]
    public byte[] propertyData { get; set; }

    /// <summary>Restores settings into a generation-local instance.</summary>
    /// <typeparam name="TSettings">Current settings contract.</typeparam>
    /// <param name="target">Current generation instance to restore.</param>
    /// <exception cref="ArgumentException">Thrown when the active type does not match the stored stable identity.</exception>
    public void Restore<TSettings>(TSettings target) where TSettings : class, ISerializable
    {
        ArgumentNullException.ThrowIfNull(target);
        TypeRef activeType = TypeCacheManager.GetTypeRef(target.GetType());
        if (stableTypeId != Guid.Empty && activeType.stableId != stableTypeId)
        {
            throw new ArgumentException(
                $"Settings type '{activeType.stableId:D}' does not match '{stableTypeId:D}'.",
                nameof(target));
        }

        if (propertyData is { Length: > 0 })
            _ = SerializationManager.RestorePropertiesData(target, propertyData);
    }
}

/// <summary>Stores one ordered feature extension selection using only stable data.</summary>
public struct RenderFeatureConfiguration
{
    /// <summary>Creates an empty feature configuration for deserialization.</summary>
    public RenderFeatureConfiguration()
    {
    }

    /// <summary>Creates a feature configuration.</summary>
    /// <param name="featureTypeId">Globally stable feature extension identifier.</param>
    /// <param name="state">Optional reload-safe settings state.</param>
    /// <param name="enabled">Whether the feature participates in graph building.</param>
    public RenderFeatureConfiguration(
        string featureTypeId,
        SerializedRenderExtensionState? state = null,
        bool enabled = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureTypeId);
        this.featureTypeId = featureTypeId;
        this.state = state ?? new SerializedRenderExtensionState();
        this.enabled = enabled;
    }

    /// <summary>Gets or sets the stable feature extension identifier.</summary>
    [SerializableProperty]
    public string featureTypeId { get; set; } = string.Empty;

    /// <summary>Gets or sets reload-safe settings state.</summary>
    [SerializableProperty]
    public SerializedRenderExtensionState state { get; set; }

    /// <summary>Gets or sets whether the feature participates in graph building.</summary>
    [SerializableProperty]
    public bool enabled { get; set; } = true;
}

/// <summary>
/// Selects a pipeline extension and ordered feature configuration without defining a render path.
/// </summary>
[StableTypeId("b17d289e-62de-4299-9e85-497143911798")]
public sealed class RenderPipelineAsset : AssetObject
{
    private RenderFeatureConfiguration[] m_features = [];

    /// <summary>Gets or sets the globally stable pipeline extension identifier.</summary>
    [SerializableProperty]
    public string pipelineTypeId { get; set; } = string.Empty;

    /// <summary>Gets or sets reload-safe pipeline settings.</summary>
    [SerializableProperty]
    public SerializedRenderExtensionState pipelineState { get; set; } = new();

    /// <summary>Gets ordered feature configurations.</summary>
    [SerializableProperty]
    public RenderFeatureConfiguration[] features
    {
        get => m_features;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            m_features = value.ToArray();
        }
    }

    /// <summary>Replaces ordered feature configurations.</summary>
    /// <param name="features">Complete ordered feature configuration set.</param>
    public void SetFeatures(IEnumerable<RenderFeatureConfiguration> features)
    {
        ArgumentNullException.ThrowIfNull(features);
        this.features = features.ToArray();
    }
}
