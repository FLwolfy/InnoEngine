using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Inno.Extensibility.Types;
using Inno.Core.Serialization;
using Inno.Core.Settings;

namespace Inno.Scene.Layers;

/// <summary>
/// Stores the named layer catalog and symmetric layer-interaction matrix used by a project.
/// </summary>
[RequiresSerializationConverter]
[StableTypeId("bd64db72-60b2-4c79-af70-c6276202ad48")]
[ProjectSettingDefinition("inno.scene.layers")]
public sealed class GameLayerStack : ISerializable
{
    private const string C_DEFAULT_LAYER_NAME = "Default";

    [SerializableProperty]
    private string?[] m_localIds;

    [SerializableProperty]
    private string?[] m_names;

    [SerializableProperty]
    private uint[] m_interactionMasks;

    /// <summary>
    /// Gets the stable project setting protocol for the project-wide layer catalog.
    /// </summary>
    public static ProjectSettingId settingId => new("inno.scene.layers");

    /// <summary>
    /// Creates a layer stack containing the immutable default layer.
    /// </summary>
    public GameLayerStack()
    {
        m_localIds = new string?[GameLayer.C_MAX_COUNT];
        m_localIds[GameLayer.defaultLayer.index] = "default";
        m_names = new string?[GameLayer.C_MAX_COUNT];
        m_names[GameLayer.defaultLayer.index] = C_DEFAULT_LAYER_NAME;
        m_interactionMasks = Enumerable.Repeat(uint.MaxValue, GameLayer.C_MAX_COUNT).ToArray();
    }

    /// <summary>
    /// Gets the number of currently named layer slots.
    /// </summary>
    public int count
    {
        get
        {
            ValidateState();
            return m_names.Count(static name => name is not null);
        }
    }

    /// <summary>
    /// Gets an immutable snapshot of every named layer ordered by slot index.
    /// </summary>
    /// <returns>
    /// The ordered layer-definition snapshot.
    /// </returns>
    public IReadOnlyList<GameLayerDefinition> GetDefinitions()
    {
        ValidateState();
        var result = new List<GameLayerDefinition>();
        for (int index = 0; index < m_names.Length; index++)
        {
            if (m_names[index] is string name)
            {
                result.Add(new GameLayerDefinition(
                    new ProjectLocalId(m_localIds[index]!),
                    new GameLayer(index),
                    name));
            }
        }
        return result;
    }

    /// <summary>
    /// Determines whether a layer slot is defined.
    /// </summary>
    /// <param name="layer">
    /// The layer slot to test.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the slot is defined.
    /// </returns>
    public bool IsDefined(GameLayer layer)
    {
        ValidateState();
        return m_names[layer.index] is not null;
    }

    /// <summary>
    /// Gets the display name assigned to a layer slot.
    /// </summary>
    /// <param name="layer">
    /// The layer slot to resolve.
    /// </param>
    /// <returns>
    /// The configured name, or <see langword="null"/>.
    /// </returns>
    public string? GetName(GameLayer layer)
    {
        ValidateState();
        return m_names[layer.index];
    }

    /// <summary>
    /// Gets the stable project-independent identity assigned to a layer slot.
    /// </summary>
    /// <param name="layer">
    /// The layer slot to resolve.
    /// </param>
    /// <returns>
    /// The local identity, or <see langword="null"/>.
    /// </returns>
    public ProjectLocalId? GetLocalId(GameLayer layer)
    {
        ValidateState();
        string? value = m_localIds[layer.index];
        return value is null ? null : new ProjectLocalId(value);
    }

    /// <summary>
    /// Gets the complete identity assigned to a layer slot.
    /// </summary>
    /// <param name="projectId">
    /// The current project namespace.
    /// </param>
    /// <param name="layer">
    /// The layer slot to resolve.
    /// </param>
    /// <returns>
    /// The qualified identity, or <see langword="null"/>.
    /// </returns>
    public GameLayerId? GetId(ProjectId projectId, GameLayer layer)
    {
        ProjectLocalId? localId = GetLocalId(layer);
        return localId is ProjectLocalId value ? new GameLayerId(projectId, value) : null;
    }

    /// <summary>
    /// Tries to resolve a local identity to its compact runtime slot.
    /// </summary>
    /// <param name="localId">
    /// The stable project-local identity.
    /// </param>
    /// <param name="layer">
    /// The resolved runtime slot.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the identity is defined.
    /// </returns>
    public bool TryGetLayer(ProjectLocalId localId, out GameLayer layer)
    {
        if (string.IsNullOrEmpty(localId.value))
            throw new ArgumentException("A valid project-local layer ID is required.", nameof(localId));
        ValidateState();
        for (int index = 0; index < m_localIds.Length; index++)
        {
            if (!string.Equals(m_localIds[index], localId.value, StringComparison.Ordinal))
                continue;
            layer = new GameLayer(index);
            return true;
        }
        layer = default;
        return false;
    }

    /// <summary>
    /// Tries to resolve a qualified identity to its compact runtime slot.
    /// </summary>
    /// <param name="projectId">
    /// The current project namespace.
    /// </param>
    /// <param name="id">
    /// The complete layer identity.
    /// </param>
    /// <param name="layer">
    /// The resolved runtime slot.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the identity belongs to this project and is defined.
    /// </returns>
    public bool TryGetLayer(ProjectId projectId, GameLayerId id, out GameLayer layer)
    {
        if (!id.isValid)
            throw new ArgumentException("A valid GameLayer ID is required.", nameof(id));
        ValidateState();
        for (int index = 0; index < m_localIds.Length; index++)
        {
            if (m_localIds[index] is not string local)
                continue;
            var candidate = new GameLayerId(projectId, new ProjectLocalId(local));
            if (candidate != id)
                continue;
            layer = new GameLayer(index);
            return true;
        }
        layer = default;
        return false;
    }

    /// <summary>
    /// Tries to resolve an ordinal layer name to its compact slot.
    /// </summary>
    /// <param name="name">
    /// The layer name to find.
    /// </param>
    /// <param name="layer">
    /// The resolved layer.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a matching layer exists.
    /// </returns>
    public bool TryGetLayer(string name, out GameLayer layer)
    {
        string normalized = NormalizeName(name);
        ValidateState();
        for (int index = 0; index < m_names.Length; index++)
        {
            if (!string.Equals(m_names[index], normalized, StringComparison.Ordinal))
                continue;
            layer = new GameLayer(index);
            return true;
        }
        layer = default;
        return false;
    }

    /// <summary>
    /// Resolves an ordinal layer name to its compact slot.
    /// </summary>
    /// <param name="name">
    /// The configured layer name.
    /// </param>
    /// <returns>
    /// The matching runtime layer.
    /// </returns>
    public GameLayer GetLayer(string name)
    {
        if (TryGetLayer(name, out GameLayer layer))
            return layer;
        throw new KeyNotFoundException($"GameLayer '{name}' is not defined.");
    }

    /// <summary>
    /// Creates a mask from configured ordinal layer names.
    /// </summary>
    /// <param name="names">
    /// The configured names to include.
    /// </param>
    /// <returns>
    /// A mask containing every resolved layer.
    /// </returns>
    public GameLayerMask GetMask(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        GameLayerMask result = GameLayerMask.none;
        foreach (string name in names)
            result = result.With(GetLayer(name));
        return result;
    }

    /// <summary>
    /// Defines or renames a project layer without accepting an authored ID.
    /// </summary>
    /// <param name="layer">
    /// The compact runtime slot.
    /// </param>
    /// <param name="name">
    /// The unique display name.
    /// </param>
    public void Define(GameLayer layer, string name)
    {
        ProjectLocalId localId = GetLocalId(layer)
            ?? new ProjectLocalId($"layer.{layer.index.ToString("00", CultureInfo.InvariantCulture)}");
        DefineLocal(layer, localId, name);
    }

    internal void DefineLocal(GameLayer layer, ProjectLocalId localId, string name)
    {
        if (string.IsNullOrEmpty(localId.value))
            throw new ArgumentException("A valid project-local layer ID is required.", nameof(localId));
        string normalized = NormalizeName(name);
        ValidateState();
        for (int index = 0; index < m_names.Length; index++)
        {
            if (index != layer.index
                && string.Equals(m_localIds[index], localId.value, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"GameLayer local ID '{localId}' is already assigned to slot {index}.",
                    nameof(localId));
            }
            if (index != layer.index
                && string.Equals(m_names[index], normalized, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"GameLayer name '{normalized}' is already assigned to slot {index}.",
                    nameof(name));
            }
        }
        if (layer == GameLayer.defaultLayer
            && (localId != new ProjectLocalId("default")
                || !string.Equals(normalized, C_DEFAULT_LAYER_NAME, StringComparison.Ordinal)))
        {
            throw new ArgumentException("The built-in default layer cannot be replaced.", nameof(layer));
        }
        m_localIds[layer.index] = localId.value;
        m_names[layer.index] = normalized;
    }

    /// <summary>
    /// Removes a custom layer definition while retaining its numeric slot and interactions.
    /// </summary>
    /// <param name="layer">
    /// The custom layer slot to undefine.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a definition was removed.
    /// </returns>
    public bool Remove(GameLayer layer)
    {
        ValidateState();
        if (layer == GameLayer.defaultLayer)
            throw new InvalidOperationException("The built-in default layer cannot be removed.");
        if (m_names[layer.index] is null)
            return false;
        m_localIds[layer.index] = null;
        m_names[layer.index] = null;
        return true;
    }

    /// <summary>
    /// Gets the interaction mask assigned to one layer.
    /// </summary>
    /// <param name="layer">
    /// The source layer.
    /// </param>
    /// <returns>
    /// The configured interaction mask.
    /// </returns>
    public GameLayerMask GetInteractionMask(GameLayer layer)
    {
        ValidateState();
        return new GameLayerMask(m_interactionMasks[layer.index]);
    }

    /// <summary>
    /// Determines whether two layer slots may interact.
    /// </summary>
    /// <param name="first">
    /// The first layer.
    /// </param>
    /// <param name="second">
    /// The second layer.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when interaction is enabled.
    /// </returns>
    public bool CanInteract(GameLayer first, GameLayer second)
    {
        ValidateState();
        return (m_interactionMasks[first.index] & (1u << second.index)) != 0u;
    }

    /// <summary>
    /// Sets a symmetric interaction pair.
    /// </summary>
    /// <param name="first">
    /// The first layer.
    /// </param>
    /// <param name="second">
    /// The second layer.
    /// </param>
    /// <param name="canInteract">
    /// Whether interaction is enabled.
    /// </param>
    public void SetInteraction(GameLayer first, GameLayer second, bool canInteract)
    {
        ValidateState();
        uint firstBit = 1u << first.index;
        uint secondBit = 1u << second.index;
        if (canInteract)
        {
            m_interactionMasks[first.index] |= secondBit;
            m_interactionMasks[second.index] |= firstBit;
        }
        else
        {
            m_interactionMasks[first.index] &= ~secondBit;
            m_interactionMasks[second.index] &= ~firstBit;
        }
    }

    /// <summary>
    /// Creates a detached copy of this stack.
    /// </summary>
    /// <returns>
    /// An independent layer stack.
    /// </returns>
    public GameLayerStack Clone()
    {
        ValidateState();
        return new GameLayerStack
        {
            m_localIds = (string?[])m_localIds.Clone(),
            m_names = (string?[])m_names.Clone(),
            m_interactionMasks = (uint[])m_interactionMasks.Clone()
        };
    }

    internal static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string normalized = name.Trim();
        if (normalized.Contains('\r') || normalized.Contains('\n'))
            throw new ArgumentException("GameLayer names cannot contain line breaks.", nameof(name));
        return normalized;
    }

    internal string?[] CaptureLocalIds()
    {
        ValidateState();
        return (string?[])m_localIds.Clone();
    }

    internal string?[] CaptureNames()
    {
        ValidateState();
        return (string?[])m_names.Clone();
    }

    internal uint[] CaptureInteractionMasks()
    {
        ValidateState();
        return (uint[])m_interactionMasks.Clone();
    }

    internal static GameLayerStack Restore(
        string?[] localIds,
        string?[] names,
        uint[] interactionMasks)
    {
        ArgumentNullException.ThrowIfNull(localIds);
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(interactionMasks);
        var stack = new GameLayerStack
        {
            m_localIds = (string?[])localIds.Clone(),
            m_names = (string?[])names.Clone(),
            m_interactionMasks = (uint[])interactionMasks.Clone()
        };
        stack.ValidateState();
        return stack;
    }

    private void ValidateState()
    {
        if (m_localIds is null || m_localIds.Length != GameLayer.C_MAX_COUNT)
            throw new InvalidOperationException("A layer stack must contain exactly thirty-two local ID slots.");
        if (m_names is null || m_names.Length != GameLayer.C_MAX_COUNT)
            throw new InvalidOperationException("A layer stack must contain exactly thirty-two name slots.");
        if (m_interactionMasks is null || m_interactionMasks.Length != GameLayer.C_MAX_COUNT)
            throw new InvalidOperationException("A layer stack must contain exactly thirty-two interaction masks.");
        if (!string.Equals(m_localIds[0], "default", StringComparison.Ordinal)
            || !string.Equals(m_names[0], C_DEFAULT_LAYER_NAME, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("GameLayer slot zero must contain the built-in Default layer.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < m_names.Length; index++)
        {
            string? localId = m_localIds[index];
            string? name = m_names[index];
            if ((localId is null) != (name is null))
                throw new InvalidOperationException($"GameLayer slot {index} must define both a local ID and a name.");
            if (localId is null || name is null)
                continue;
            var normalizedId = new ProjectLocalId(localId);
            if (!ids.Add(normalizedId.value))
                throw new InvalidOperationException($"GameLayer local ID '{localId}' is assigned more than once.");
            string normalizedName = NormalizeName(name);
            if (!string.Equals(name, normalizedName, StringComparison.Ordinal))
                throw new InvalidOperationException($"GameLayer name in slot {index} is not normalized.");
            if (!names.Add(name))
                throw new InvalidOperationException($"GameLayer name '{name}' is assigned more than once.");
        }
        for (int first = 0; first < m_interactionMasks.Length; first++)
        {
            for (int second = first; second < m_interactionMasks.Length; second++)
            {
                bool forward = (m_interactionMasks[first] & (1u << second)) != 0u;
                bool reverse = (m_interactionMasks[second] & (1u << first)) != 0u;
                if (forward != reverse)
                {
                    throw new InvalidOperationException(
                        $"GameLayer interaction between slots {first} and {second} is not symmetric.");
                }
            }
        }
    }
}
