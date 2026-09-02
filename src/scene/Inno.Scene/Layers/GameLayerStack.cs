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
    private string?[] m_ids;

    [SerializableProperty]
    private string?[] m_names;

    [SerializableProperty]
    private uint[] m_interactionMasks;

    /// <summary>
    /// Gets the stable project setting protocol for the project-wide layer catalog.
    /// </summary>
    public static ProjectSettingId settingId => new("inno.scene.layers");

    /// <summary>
    /// Creates a layer stack containing the immutable default layer and interactions between all slots.
    /// </summary>
    public GameLayerStack()
    {
        m_ids = new string?[GameLayer.C_MAX_COUNT];
        m_ids[GameLayer.defaultLayer.index] = GameLayerId.defaultLayer.value;
        m_names = new string?[GameLayer.C_MAX_COUNT];
        m_names[GameLayer.defaultLayer.index] = C_DEFAULT_LAYER_NAME;
        m_interactionMasks = Enumerable.Repeat(uint.MaxValue, GameLayer.C_MAX_COUNT).ToArray();
    }

    /// <summary>
    /// Gets the number of currently named layer slots.
    /// </summary>
    public int count => GetDefinitions().Count;

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
        for (int i = 0; i < m_names.Length; i++)
        {
            if (!string.IsNullOrEmpty(m_names[i]))
                result.Add(new GameLayerDefinition(new GameLayerId(m_ids[i]!), new GameLayer(i), m_names[i]!));
        }
        return result;
    }

    /// <summary>
    /// Determines whether a layer slot currently has a project name.
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
        return !string.IsNullOrEmpty(m_names[layer.index]);
    }

    /// <summary>
    /// Gets the project name assigned to a layer slot.
    /// </summary>
    /// <param name="layer">
    /// The layer slot to resolve.
    /// </param>
    /// <returns>
    /// The configured name, or <see langword="null"/> when the slot is undefined.
    /// </returns>
    public string? GetName(GameLayer layer)
    {
        ValidateState();
        return m_names[layer.index];
    }

    /// <summary>
    /// Gets the logical identity assigned to a layer slot.
    /// </summary>
    /// <param name="layer">
    /// The layer slot to resolve.
    /// </param>
    /// <returns>
    /// The stable logical ID, or <see langword="null"/> when the slot is undefined.
    /// </returns>
    public GameLayerId? GetId(GameLayer layer)
    {
        ValidateState();
        string? id = m_ids[layer.index];
        return id is null ? null : new GameLayerId(id);
    }

    /// <summary>
    /// Tries to resolve a stable logical layer identity to its compact runtime slot.
    /// </summary>
    /// <param name="id">
    /// The globally stable layer identity.
    /// </param>
    /// <param name="layer">
    /// The resolved runtime slot when found.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the identity is defined.
    /// </returns>
    public bool TryGetLayer(GameLayerId id, out GameLayer layer)
    {
        if (!id.isValid)
            throw new ArgumentException("A valid GameLayer ID is required.", nameof(id));
        ValidateState();
        for (int i = 0; i < m_ids.Length; i++)
        {
            if (!string.Equals(m_ids[i], id.value, StringComparison.Ordinal))
                continue;
            layer = new GameLayer(i);
            return true;
        }
        layer = default;
        return false;
    }

    /// <summary>
    /// Resolves a stable logical layer identity to its compact runtime slot.
    /// </summary>
    /// <param name="id">
    /// The globally stable layer identity.
    /// </param>
    /// <returns>
    /// The matching runtime layer slot.
    /// </returns>
    /// <exception cref="KeyNotFoundException">
    /// Thrown when the identity is not defined.
    /// </exception>
    public GameLayer GetLayer(GameLayerId id)
    {
        if (TryGetLayer(id, out GameLayer layer))
            return layer;
        throw new KeyNotFoundException($"GameLayer '{id}' is not defined.");
    }

    /// <summary>
    /// Tries to resolve an ordinal layer name to its stable slot identifier.
    /// </summary>
    /// <param name="name">
    /// The layer name to find.
    /// </param>
    /// <param name="layer">
    /// The resolved layer when the method succeeds.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a matching named layer exists.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="name"/> is empty.
    /// </exception>
    public bool TryGetLayer(string name, out GameLayer layer)
    {
        string normalized = NormalizeName(name);
        ValidateState();
        for (int i = 0; i < m_names.Length; i++)
        {
            if (!string.Equals(m_names[i], normalized, StringComparison.Ordinal))
                continue;
            layer = new GameLayer(i);
            return true;
        }
        layer = default;
        return false;
    }

    /// <summary>
    /// Resolves an ordinal layer name to its stable slot identifier.
    /// </summary>
    /// <param name="name">
    /// The configured layer name to resolve.
    /// </param>
    /// <returns>
    /// The matching layer slot.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="name"/> is empty.
    /// </exception>
    /// <exception cref="KeyNotFoundException">
    /// Thrown when no configured layer has the requested name.
    /// </exception>
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
    /// The configured names whose layer bits should be enabled.
    /// </param>
    /// <returns>
    /// A mask containing every resolved layer.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="names"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when a supplied name is empty.
    /// </exception>
    /// <exception cref="KeyNotFoundException">
    /// Thrown when a supplied name is not configured.
    /// </exception>
    public GameLayerMask GetMask(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        GameLayerMask result = GameLayerMask.none;
        foreach (string name in names)
            result = result.With(GetLayer(name));
        return result;
    }

    /// <summary>
    /// Creates a mask from stable logical layer identities.
    /// </summary>
    /// <param name="ids">
    /// The logical identities whose runtime bits should be enabled.
    /// </param>
    /// <returns>
    /// A mask containing every resolved layer.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="ids"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="KeyNotFoundException">
    /// Thrown when a supplied identity is not configured.
    /// </exception>
    public GameLayerMask GetMask(IEnumerable<GameLayerId> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        GameLayerMask result = GameLayerMask.none;
        foreach (GameLayerId id in ids)
            result = result.With(GetLayer(id));
        return result;
    }

    /// <summary>
    /// Defines a project-authored layer using a deterministic slot identity.
    /// </summary>
    /// <param name="layer">
    /// The layer slot to define.
    /// </param>
    /// <param name="name">
    /// The unique non-empty ordinal layer name.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="name"/> is invalid or already assigned to another slot.
    /// </exception>
    public void Define(GameLayer layer, string name)
        => Define(
            layer,
            new GameLayerId($"project.layer.{layer.index.ToString("00", CultureInfo.InvariantCulture)}"),
            name);

    /// <summary>
    /// Defines or updates one layer slot with an explicit globally stable identity.
    /// </summary>
    /// <param name="layer">
    /// The compact runtime slot to define.
    /// </param>
    /// <param name="id">
    /// The unique globally stable logical identity.
    /// </param>
    /// <param name="name">
    /// The unique non-empty display name.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when the identity or name is invalid, duplicated, or attempts to replace the built-in default definition.
    /// </exception>
    public void Define(GameLayer layer, GameLayerId id, string name)
    {
        if (!id.isValid)
            throw new ArgumentException("A valid GameLayer ID is required.", nameof(id));
        string normalized = NormalizeName(name);
        ValidateState();
        for (int i = 0; i < m_names.Length; i++)
        {
            if (i != layer.index && string.Equals(m_ids[i], id.value, StringComparison.Ordinal))
                throw new ArgumentException($"GameLayer ID '{id}' is already assigned to slot {i}.", nameof(id));
            if (i != layer.index && string.Equals(m_names[i], normalized, StringComparison.Ordinal))
                throw new ArgumentException($"GameLayer name '{normalized}' is already assigned to slot {i}.", nameof(name));
        }
        if (layer == GameLayer.defaultLayer
            && (id != GameLayerId.defaultLayer
                || !string.Equals(normalized, C_DEFAULT_LAYER_NAME, StringComparison.Ordinal)))
        {
            throw new ArgumentException("The built-in default layer cannot be replaced.", nameof(id));
        }
        m_ids[layer.index] = id.value;
        m_names[layer.index] = normalized;
    }

    /// <summary>
    /// Removes a custom layer definition while retaining its numeric slot and interaction settings.
    /// </summary>
    /// <param name="layer">
    /// The custom layer slot to undefine.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when an existing custom definition was removed.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when attempting to remove the built-in default layer.
    /// </exception>
    public bool Remove(GameLayer layer)
    {
        ValidateState();
        if (layer == GameLayer.defaultLayer)
            throw new InvalidOperationException("The built-in default layer cannot be removed.");
        if (m_names[layer.index] is null)
            return false;
        m_ids[layer.index] = null;
        m_names[layer.index] = null;
        return true;
    }

    /// <summary>
    /// Gets the interaction mask assigned to one layer.
    /// </summary>
    /// <param name="layer">
    /// The source layer whose interaction mask should be returned.
    /// </param>
    /// <returns>
    /// The mask of layers permitted to interact with the source layer.
    /// </returns>
    public GameLayerMask GetInteractionMask(GameLayer layer)
    {
        ValidateState();
        return new GameLayerMask(m_interactionMasks[layer.index]);
    }

    /// <summary>
    /// Determines whether two layer slots are permitted to interact.
    /// </summary>
    /// <param name="first">
    /// The first layer slot.
    /// </param>
    /// <param name="second">
    /// The second layer slot.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the symmetric interaction pair is enabled.
    /// </returns>
    public bool CanInteract(GameLayer first, GameLayer second)
    {
        ValidateState();
        return (m_interactionMasks[first.index] & (1u << second.index)) != 0u;
    }

    /// <summary>
    /// Enables or disables a symmetric interaction pair between two layer slots.
    /// </summary>
    /// <param name="first">
    /// The first layer slot.
    /// </param>
    /// <param name="second">
    /// The second layer slot.
    /// </param>
    /// <param name="canInteract">
    /// Whether the two layers should interact.
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
    /// Creates a detached copy that can be edited without mutating this stack.
    /// </summary>
    /// <returns>
    /// An independent stack containing the same definitions and interaction matrix.
    /// </returns>
    public GameLayerStack Clone()
    {
        ValidateState();
        return new GameLayerStack
        {
            m_ids = (string?[])m_ids.Clone(),
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

    internal string?[] CaptureNames()
    {
        ValidateState();
        return (string?[])m_names.Clone();
    }

    internal string?[] CaptureIds()
    {
        ValidateState();
        return (string?[])m_ids.Clone();
    }

    internal uint[] CaptureInteractionMasks()
    {
        ValidateState();
        return (uint[])m_interactionMasks.Clone();
    }

    internal static GameLayerStack Restore(string?[] ids, string?[] names, uint[] interactionMasks)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(interactionMasks);
        var stack = new GameLayerStack
        {
            m_ids = (string?[])ids.Clone(),
            m_names = (string?[])names.Clone(),
            m_interactionMasks = (uint[])interactionMasks.Clone()
        };
        stack.ValidateState();
        return stack;
    }

    private void ValidateState()
    {
        if (m_ids is null || m_ids.Length != GameLayer.C_MAX_COUNT)
            throw new InvalidOperationException("A layer stack must contain exactly thirty-two logical ID slots.");
        if (m_names is null || m_names.Length != GameLayer.C_MAX_COUNT)
            throw new InvalidOperationException("A layer stack must contain exactly thirty-two name slots.");
        if (m_interactionMasks is null || m_interactionMasks.Length != GameLayer.C_MAX_COUNT)
            throw new InvalidOperationException("A layer stack must contain exactly thirty-two interaction masks.");
        if (!string.Equals(m_ids[GameLayer.defaultLayer.index], GameLayerId.defaultLayer.value, StringComparison.Ordinal))
            throw new InvalidOperationException("GameLayer slot zero must contain the built-in logical ID.");
        if (!string.Equals(m_names[GameLayer.defaultLayer.index], C_DEFAULT_LAYER_NAME, StringComparison.Ordinal))
            throw new InvalidOperationException("GameLayer slot zero must contain the built-in Default layer.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < m_names.Length; i++)
        {
            string? id = m_ids[i];
            string? name = m_names[i];
            if ((id is null) != (name is null))
                throw new InvalidOperationException($"GameLayer slot {i} must define both an ID and a name.");
            if (id is null || name is null)
                continue;
            var normalizedId = new GameLayerId(id);
            if (!string.Equals(id, normalizedId.value, StringComparison.Ordinal))
                throw new InvalidOperationException($"GameLayer ID in slot {i} is not normalized.");
            if (!ids.Add(id))
                throw new InvalidOperationException($"GameLayer ID '{id}' is assigned to more than one slot.");
            string normalized = NormalizeName(name);
            if (!string.Equals(name, normalized, StringComparison.Ordinal))
                throw new InvalidOperationException($"GameLayer name in slot {i} is not normalized.");
            if (!names.Add(name))
                throw new InvalidOperationException($"GameLayer name '{name}' is assigned to more than one slot.");
        }
        for (int i = 0; i < m_interactionMasks.Length; i++)
        {
            for (int j = i; j < m_interactionMasks.Length; j++)
            {
                bool forward = (m_interactionMasks[i] & (1u << j)) != 0u;
                bool reverse = (m_interactionMasks[j] & (1u << i)) != 0u;
                if (forward != reverse)
                    throw new InvalidOperationException($"GameLayer interaction between slots {i} and {j} is not symmetric.");
            }
        }
    }
}
