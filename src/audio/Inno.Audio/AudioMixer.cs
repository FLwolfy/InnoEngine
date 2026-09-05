using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Assets;
using Inno.Core.Serialization;
using Inno.Extensibility.Types;

namespace Inno.Audio;

/// <summary>
/// Stores one neutral numeric parameter assigned to an audio processor.
/// </summary>
public readonly record struct AudioProcessorParameter
{
    /// <summary>
    /// Creates a processor parameter value.
    /// </summary>
    /// <param name="id">
    /// Open parameter identifier understood by the processor protocol.
    /// </param>
    /// <param name="value">
    /// Neutral numeric value interpreted by the processor.
    /// </param>
    public AudioProcessorParameter(AudioParameterId id, float value)
    {
        if (!id.isValid)
            throw new ArgumentException("A valid parameter identifier is required.", nameof(id));
        this.id = id;
        this.value = value;
    }

    /// <summary>
    /// Gets the open parameter identifier.
    /// </summary>
    public AudioParameterId id { get; }

    /// <summary>
    /// Gets the neutral numeric value.
    /// </summary>
    public float value { get; }
}

/// <summary>
/// Describes one ordered processor instance attached to a mixer bus.
/// </summary>
public sealed class AudioProcessorConfiguration
{
    private readonly AudioProcessorParameter[] m_parameters;

    /// <summary>
    /// Creates an immutable processor configuration.
    /// </summary>
    /// <param name="id">
    /// Open processor protocol identifier.
    /// </param>
    /// <param name="parameters">
    /// Complete processor parameter set.
    /// </param>
    public AudioProcessorConfiguration(
        AudioProcessorId id,
        IEnumerable<AudioProcessorParameter>? parameters = null)
    {
        if (!id.isValid)
            throw new ArgumentException("A valid processor identifier is required.", nameof(id));
        this.id = id;
        m_parameters = parameters?.ToArray() ?? [];
        if (m_parameters.Select(static parameter => parameter.id).Distinct().Count() != m_parameters.Length)
            throw new ArgumentException("Processor parameter identifiers must be unique.", nameof(parameters));
    }

    /// <summary>
    /// Gets the open processor protocol identifier.
    /// </summary>
    public AudioProcessorId id { get; }

    /// <summary>
    /// Gets the immutable processor parameter set.
    /// </summary>
    public IReadOnlyList<AudioProcessorParameter> parameters => m_parameters;
}

/// <summary>
/// Describes one bus and its ordered processor chain in a compiled mixer graph.
/// </summary>
public sealed class AudioBusDefinition
{
    private readonly AudioProcessorConfiguration[] m_processors;

    /// <summary>
    /// Creates an immutable mixer bus definition.
    /// </summary>
    /// <param name="id">
    /// Stable bus identifier.
    /// </param>
    /// <param name="parent">
    /// Parent bus identifier, or <see langword="null"/> only for the master bus.
    /// </param>
    /// <param name="volume">
    /// Non-negative linear bus gain.
    /// </param>
    /// <param name="muted">
    /// Whether output from the bus is initially silenced.
    /// </param>
    /// <param name="processors">
    /// Ordered processor chain.
    /// </param>
    public AudioBusDefinition(
        AudioBusId id,
        AudioBusId? parent,
        float volume,
        bool muted,
        IEnumerable<AudioProcessorConfiguration>? processors = null)
    {
        if (!id.isValid)
            throw new ArgumentException("A valid bus identifier is required.", nameof(id));
        if (volume < 0f)
            throw new ArgumentOutOfRangeException(nameof(volume));
        if (id == AudioBusId.master && parent is not null)
            throw new ArgumentException("The master bus cannot have a parent.", nameof(parent));
        if (id != AudioBusId.master && (parent is null || !parent.Value.isValid))
            throw new ArgumentException("A non-master bus requires a valid parent.", nameof(parent));
        this.id = id;
        this.parent = parent;
        this.volume = volume;
        this.muted = muted;
        m_processors = processors?.ToArray() ?? [];
    }

    /// <summary>
    /// Gets the stable bus identifier.
    /// </summary>
    public AudioBusId id { get; }

    /// <summary>
    /// Gets the parent bus, or <see langword="null"/> for the master bus.
    /// </summary>
    public AudioBusId? parent { get; }

    /// <summary>
    /// Gets the initial linear bus gain.
    /// </summary>
    public float volume { get; }

    /// <summary>
    /// Gets whether output from the bus is initially silenced.
    /// </summary>
    public bool muted { get; }

    /// <summary>
    /// Gets the ordered immutable processor chain.
    /// </summary>
    public IReadOnlyList<AudioProcessorConfiguration> processors => m_processors;
}

/// <summary>
/// Contains one validated backend-neutral mixer graph.
/// </summary>
public sealed class AudioMixer
{
    private readonly AudioBusDefinition[] m_buses;

    internal AudioMixer(IEnumerable<AudioBusDefinition> buses)
    {
        m_buses = buses.ToArray();
    }

    /// <summary>
    /// Gets buses in parent-before-child creation order.
    /// </summary>
    public IReadOnlyList<AudioBusDefinition> buses => m_buses;
}

/// <summary>
/// Builds and validates an open backend-neutral mixer graph.
/// </summary>
public sealed class AudioMixerBuilder
{
    private readonly Dictionary<AudioBusId, AudioBusDefinition> m_buses = new()
    {
        [AudioBusId.master] = new(AudioBusId.master, null, 1f, false)
    };

    /// <summary>
    /// Adds one semantic bus to the graph.
    /// </summary>
    /// <param name="id">
    /// Stable bus identifier.
    /// </param>
    /// <param name="parent">
    /// Parent bus identifier.
    /// </param>
    /// <param name="volume">
    /// Initial non-negative linear gain.
    /// </param>
    /// <param name="muted">
    /// Whether output is initially silenced.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when the bus identifier is invalid or duplicated.
    /// </exception>
    public void AddBus(AudioBusId id, AudioBusId parent, float volume = 1f, bool muted = false)
    {
        if (!id.isValid || id == AudioBusId.master)
            throw new ArgumentException("A valid non-master bus identifier is required.", nameof(id));
        if (m_buses.ContainsKey(id))
            throw new ArgumentException($"Audio bus '{id}' is already defined.", nameof(id));
        m_buses.Add(id, new AudioBusDefinition(id, parent, volume, muted));
    }

    /// <summary>
    /// Appends one processor to an existing bus chain.
    /// </summary>
    /// <param name="bus">
    /// Existing bus identifier.
    /// </param>
    /// <param name="processor">
    /// Immutable processor configuration to append.
    /// </param>
    /// <exception cref="KeyNotFoundException">
    /// Thrown when the bus is not defined.
    /// </exception>
    public void AddProcessor(AudioBusId bus, AudioProcessorConfiguration processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        if (!m_buses.TryGetValue(bus, out AudioBusDefinition? definition))
            throw new KeyNotFoundException($"Audio bus '{bus}' is not defined.");
        m_buses[bus] = new AudioBusDefinition(
            definition.id,
            definition.parent,
            definition.volume,
            definition.muted,
            definition.processors.Append(processor));
    }

    /// <summary>
    /// Validates the graph and returns an immutable parent-before-child snapshot.
    /// </summary>
    /// <returns>
    /// A validated mixer graph containing the mandatory master bus.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a parent is missing or bus routing contains a cycle.
    /// </exception>
    public AudioMixer Build()
    {
        var order = new List<AudioBusDefinition>(m_buses.Count);
        var visiting = new HashSet<AudioBusId>();
        var visited = new HashSet<AudioBusId>();
        Visit(AudioBusId.master, order, visiting, visited);
        foreach (AudioBusId id in m_buses.Keys.OrderBy(static id => id.value, StringComparer.Ordinal))
            Visit(id, order, visiting, visited);
        return new AudioMixer(order);
    }

    private void Visit(
        AudioBusId id,
        ICollection<AudioBusDefinition> order,
        ISet<AudioBusId> visiting,
        ISet<AudioBusId> visited)
    {
        if (visited.Contains(id))
            return;
        if (!m_buses.TryGetValue(id, out AudioBusDefinition? definition))
            throw new InvalidOperationException($"Audio mixer references missing bus '{id}'.");
        if (!visiting.Add(id))
            throw new InvalidOperationException($"Audio mixer bus routing contains a cycle at '{id}'.");
        if (definition.parent is AudioBusId parent)
            Visit(parent, order, visiting, visited);
        visiting.Remove(id);
        visited.Add(id);
        order.Add(definition);
    }
}

/// <summary>
/// Stores reload-safe configuration for one mixer or feature extension generation.
/// </summary>
public struct SerializedAudioExtensionState
{
    /// <summary>
    /// Creates empty extension state for deserialization.
    /// </summary>
    public SerializedAudioExtensionState()
    {
        propertyData = [];
    }

    /// <summary>
    /// Creates neutral extension state.
    /// </summary>
    /// <param name="stableTypeId">
    /// Stable settings type identity, or empty when no typed settings exist.
    /// </param>
    /// <param name="propertyData">
    /// Neutral serialized property bytes.
    /// </param>
    public SerializedAudioExtensionState(Guid stableTypeId, ReadOnlySpan<byte> propertyData)
    {
        this.stableTypeId = stableTypeId;
        this.propertyData = propertyData.ToArray();
    }

    /// <summary>
    /// Gets or sets the stable settings type identity.
    /// </summary>
    [SerializableProperty]
    public Guid stableTypeId { get; set; }

    /// <summary>
    /// Gets or sets neutral serialized property bytes.
    /// </summary>
    [SerializableProperty]
    public byte[] propertyData { get; set; }
}

/// <summary>
/// Stores one ordered mixer feature selection using only stable data.
/// </summary>
public struct AudioMixerFeatureConfiguration
{
    /// <summary>
    /// Creates an empty feature configuration for deserialization.
    /// </summary>
    public AudioMixerFeatureConfiguration()
    {
    }

    /// <summary>
    /// Creates a mixer feature configuration.
    /// </summary>
    /// <param name="featureTypeId">
    /// Globally stable feature extension identifier.
    /// </param>
    /// <param name="state">
    /// Optional reload-safe feature state.
    /// </param>
    /// <param name="enabled">
    /// Whether the feature participates in graph construction.
    /// </param>
    public AudioMixerFeatureConfiguration(
        string featureTypeId,
        SerializedAudioExtensionState? state = null,
        bool enabled = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureTypeId);
        this.featureTypeId = featureTypeId;
        this.state = state ?? new SerializedAudioExtensionState();
        this.enabled = enabled;
    }

    /// <summary>
    /// Gets or sets the stable feature extension identifier.
    /// </summary>
    [SerializableProperty]
    public string featureTypeId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets reload-safe feature settings.
    /// </summary>
    [SerializableProperty]
    public SerializedAudioExtensionState state { get; set; }

    /// <summary>
    /// Gets or sets whether the feature participates in graph construction.
    /// </summary>
    [SerializableProperty]
    public bool enabled { get; set; } = true;
}

/// <summary>
/// Selects a mixer extension and ordered features without defining a game-specific mixing model.
/// </summary>
[StableTypeId("6e167326-d078-45e9-ad33-993300ee5fed")]
public sealed class AudioMixerAsset : AssetObject
{
    private AudioMixerFeatureConfiguration[] m_features = [];

    /// <summary>
    /// Gets or sets the globally stable mixer extension identifier.
    /// </summary>
    [SerializableProperty]
    public string mixerTypeId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets reload-safe mixer settings.
    /// </summary>
    [SerializableProperty]
    public SerializedAudioExtensionState mixerState { get; set; } = new();

    /// <summary>
    /// Gets or sets ordered feature configurations.
    /// </summary>
    [SerializableProperty]
    public AudioMixerFeatureConfiguration[] features
    {
        get => m_features;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            m_features = value.ToArray();
        }
    }
}
