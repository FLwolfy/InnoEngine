using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.Serialization;

namespace Inno.Engine.Scene.Layers;

/// <summary>
/// Stores the named layer catalog and symmetric layer-interaction matrix used by a project.
/// </summary>
[RequiresSerializationConverter]
public sealed class LayerStack : ISerializable
{
    private const string C_DEFAULT_LAYER_NAME = "Default";

    [SerializableProperty]
    private string?[] m_names;

    [SerializableProperty]
    private uint[] m_interactionMasks;

    /// <summary>
    /// Creates a layer stack containing the immutable default layer and interactions between all slots.
    /// </summary>
    public LayerStack()
    {
        m_names = new string?[Layer.C_MAX_COUNT];
        m_names[Layer.defaultLayer.index] = C_DEFAULT_LAYER_NAME;
        m_interactionMasks = Enumerable.Repeat(uint.MaxValue, Layer.C_MAX_COUNT).ToArray();
    }

    /// <summary>
    /// Gets the number of currently named layer slots.
    /// </summary>
    public int count => GetDefinitions().Count;

    /// <summary>
    /// Gets an immutable snapshot of every named layer ordered by slot index.
    /// </summary>
    /// <returns>The ordered layer-definition snapshot.</returns>
    public IReadOnlyList<LayerDefinition> GetDefinitions()
    {
        ValidateState();
        var result = new List<LayerDefinition>();
        for (int i = 0; i < m_names.Length; i++)
        {
            if (!string.IsNullOrEmpty(m_names[i]))
                result.Add(new LayerDefinition(new Layer(i), m_names[i]!));
        }
        return result;
    }

    /// <summary>
    /// Determines whether a layer slot currently has a project name.
    /// </summary>
    /// <param name="layer">The layer slot to test.</param>
    /// <returns><see langword="true"/> when the slot is defined.</returns>
    public bool IsDefined(Layer layer)
    {
        ValidateState();
        return !string.IsNullOrEmpty(m_names[layer.index]);
    }

    /// <summary>
    /// Gets the project name assigned to a layer slot.
    /// </summary>
    /// <param name="layer">The layer slot to resolve.</param>
    /// <returns>The configured name, or <see langword="null"/> when the slot is undefined.</returns>
    public string? GetName(Layer layer)
    {
        ValidateState();
        return m_names[layer.index];
    }

    /// <summary>
    /// Tries to resolve an ordinal layer name to its stable slot identifier.
    /// </summary>
    /// <param name="name">The layer name to find.</param>
    /// <param name="layer">The resolved layer when the method succeeds.</param>
    /// <returns><see langword="true"/> when a matching named layer exists.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="name"/> is empty.
    /// </exception>
    public bool TryGetLayer(string name, out Layer layer)
    {
        string normalized = NormalizeName(name);
        ValidateState();
        for (int i = 0; i < m_names.Length; i++)
        {
            if (!string.Equals(m_names[i], normalized, StringComparison.Ordinal))
                continue;
            layer = new Layer(i);
            return true;
        }
        layer = default;
        return false;
    }

    /// <summary>
    /// Resolves an ordinal layer name to its stable slot identifier.
    /// </summary>
    /// <param name="name">The configured layer name to resolve.</param>
    /// <returns>The matching layer slot.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="name"/> is empty.
    /// </exception>
    /// <exception cref="KeyNotFoundException">
    /// Thrown when no configured layer has the requested name.
    /// </exception>
    public Layer GetLayer(string name)
    {
        if (TryGetLayer(name, out Layer layer))
            return layer;
        throw new KeyNotFoundException($"Layer '{name}' is not defined.");
    }

    /// <summary>
    /// Creates a mask from configured ordinal layer names.
    /// </summary>
    /// <param name="names">The configured names whose layer bits should be enabled.</param>
    /// <returns>A mask containing every resolved layer.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="names"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when a supplied name is empty.
    /// </exception>
    /// <exception cref="KeyNotFoundException">
    /// Thrown when a supplied name is not configured.
    /// </exception>
    public LayerMask GetMask(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        LayerMask result = LayerMask.none;
        foreach (string name in names)
            result = result.With(GetLayer(name));
        return result;
    }

    /// <summary>
    /// Defines or renames a layer slot.
    /// </summary>
    /// <param name="layer">The layer slot to define.</param>
    /// <param name="name">The unique non-empty ordinal layer name.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="name"/> is invalid or already assigned to another slot.
    /// </exception>
    public void Define(Layer layer, string name)
    {
        string normalized = NormalizeName(name);
        ValidateState();
        for (int i = 0; i < m_names.Length; i++)
        {
            if (i != layer.index && string.Equals(m_names[i], normalized, StringComparison.Ordinal))
                throw new ArgumentException($"Layer name '{normalized}' is already assigned to slot {i}.", nameof(name));
        }
        if (layer == Layer.defaultLayer && !string.Equals(normalized, C_DEFAULT_LAYER_NAME, StringComparison.Ordinal))
            throw new ArgumentException("The built-in default layer cannot be renamed.", nameof(name));
        m_names[layer.index] = normalized;
    }

    /// <summary>
    /// Removes a custom layer definition while retaining its numeric slot and interaction settings.
    /// </summary>
    /// <param name="layer">The custom layer slot to undefine.</param>
    /// <returns><see langword="true"/> when an existing custom definition was removed.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when attempting to remove the built-in default layer.
    /// </exception>
    public bool Remove(Layer layer)
    {
        ValidateState();
        if (layer == Layer.defaultLayer)
            throw new InvalidOperationException("The built-in default layer cannot be removed.");
        if (m_names[layer.index] is null)
            return false;
        m_names[layer.index] = null;
        return true;
    }

    /// <summary>
    /// Gets the interaction mask assigned to one layer.
    /// </summary>
    /// <param name="layer">The source layer whose interaction mask should be returned.</param>
    /// <returns>The mask of layers permitted to interact with the source layer.</returns>
    public LayerMask GetInteractionMask(Layer layer)
    {
        ValidateState();
        return new LayerMask(m_interactionMasks[layer.index]);
    }

    /// <summary>
    /// Determines whether two layer slots are permitted to interact.
    /// </summary>
    /// <param name="first">The first layer slot.</param>
    /// <param name="second">The second layer slot.</param>
    /// <returns><see langword="true"/> when the symmetric interaction pair is enabled.</returns>
    public bool CanInteract(Layer first, Layer second)
    {
        ValidateState();
        return (m_interactionMasks[first.index] & (1u << second.index)) != 0u;
    }

    /// <summary>
    /// Enables or disables a symmetric interaction pair between two layer slots.
    /// </summary>
    /// <param name="first">The first layer slot.</param>
    /// <param name="second">The second layer slot.</param>
    /// <param name="canInteract">Whether the two layers should interact.</param>
    public void SetInteraction(Layer first, Layer second, bool canInteract)
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
    /// <returns>An independent stack containing the same definitions and interaction matrix.</returns>
    public LayerStack Clone()
    {
        ValidateState();
        return new LayerStack
        {
            m_names = (string?[])m_names.Clone(),
            m_interactionMasks = (uint[])m_interactionMasks.Clone()
        };
    }

    internal static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string normalized = name.Trim();
        if (normalized.Contains('\r') || normalized.Contains('\n'))
            throw new ArgumentException("Layer names cannot contain line breaks.", nameof(name));
        return normalized;
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

    internal static LayerStack Restore(string?[] names, uint[] interactionMasks)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(interactionMasks);
        var stack = new LayerStack
        {
            m_names = (string?[])names.Clone(),
            m_interactionMasks = (uint[])interactionMasks.Clone()
        };
        stack.ValidateState();
        return stack;
    }

    private void ValidateState()
    {
        if (m_names is null || m_names.Length != Layer.C_MAX_COUNT)
            throw new InvalidOperationException("A layer stack must contain exactly thirty-two name slots.");
        if (m_interactionMasks is null || m_interactionMasks.Length != Layer.C_MAX_COUNT)
            throw new InvalidOperationException("A layer stack must contain exactly thirty-two interaction masks.");
        if (!string.Equals(m_names[Layer.defaultLayer.index], C_DEFAULT_LAYER_NAME, StringComparison.Ordinal))
            throw new InvalidOperationException("Layer slot zero must contain the built-in Default layer.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < m_names.Length; i++)
        {
            string? name = m_names[i];
            if (name is null)
                continue;
            string normalized = NormalizeName(name);
            if (!string.Equals(name, normalized, StringComparison.Ordinal))
                throw new InvalidOperationException($"Layer name in slot {i} is not normalized.");
            if (!names.Add(name))
                throw new InvalidOperationException($"Layer name '{name}' is assigned to more than one slot.");
        }
        for (int i = 0; i < m_interactionMasks.Length; i++)
        {
            for (int j = i; j < m_interactionMasks.Length; j++)
            {
                bool forward = (m_interactionMasks[i] & (1u << j)) != 0u;
                bool reverse = (m_interactionMasks[j] & (1u << i)) != 0u;
                if (forward != reverse)
                    throw new InvalidOperationException($"Layer interaction between slots {i} and {j} is not symmetric.");
            }
        }
    }
}
