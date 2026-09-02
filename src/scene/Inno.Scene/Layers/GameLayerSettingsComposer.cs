using System;
using System.Collections.Generic;

using Inno.Core.Serialization;
using Inno.Core.Settings;

namespace Inno.Scene.Layers;

/// <summary>
/// Composes sparse logical layer and interaction contributions into one compact runtime stack.
/// </summary>
[ProjectSettingComposer("inno.scene.layers")]
internal sealed class GameLayerSettingsComposer
    : ProjectSettingComposer<GameLayerStack, GameLayerSettingContribution>
{
    /// <summary>
    /// Captures the contribution introduced by the supplied project setting value.
    /// </summary>
    /// <param name="baseline">
    /// The baseline consumed by capture contribution; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="value">
    /// The concrete value read or transformed by this operation.
    /// </param>
    /// <returns>
    /// The validated game layer setting contribution that represents the completed operation.
    /// </returns>
    protected override GameLayerSettingContribution CaptureContribution(
        GameLayerStack baseline,
        GameLayerStack value)
    {
        var removedSlots = new List<int>();
        var removedIds = new List<string>();
        var upsertSlots = new List<int>();
        var upsertIds = new List<string>();
        var upsertNames = new List<string>();
        for (int index = 0; index < GameLayer.C_MAX_COUNT; index++)
        {
            var layer = new GameLayer(index);
            GameLayerId? baselineId = baseline.GetId(layer);
            GameLayerId? valueId = value.GetId(layer);
            string? baselineName = baseline.GetName(layer);
            string? valueName = value.GetName(layer);
            if (baselineId is GameLayerId oldId && oldId != valueId)
            {
                removedSlots.Add(index);
                removedIds.Add(oldId.value);
            }
            if (valueId is GameLayerId newId
                && (baselineId != valueId
                    || !string.Equals(baselineName, valueName, StringComparison.Ordinal)))
            {
                upsertSlots.Add(index);
                upsertIds.Add(newId.value);
                upsertNames.Add(valueName!);
            }
        }

        var interactionFirstSlots = new List<int>();
        var interactionSecondSlots = new List<int>();
        var interactionValues = new List<bool>();
        for (int first = 0; first < GameLayer.C_MAX_COUNT; first++)
        {
            for (int second = first; second < GameLayer.C_MAX_COUNT; second++)
            {
                var firstLayer = new GameLayer(first);
                var secondLayer = new GameLayer(second);
                bool baselineValue = baseline.CanInteract(firstLayer, secondLayer);
                bool authoredValue = value.CanInteract(firstLayer, secondLayer);
                if (baselineValue == authoredValue)
                    continue;
                interactionFirstSlots.Add(first);
                interactionSecondSlots.Add(second);
                interactionValues.Add(authoredValue);
            }
        }

        return new GameLayerSettingContribution
        {
            removedSlots = removedSlots.ToArray(),
            removedIds = removedIds.ToArray(),
            upsertSlots = upsertSlots.ToArray(),
            upsertIds = upsertIds.ToArray(),
            upsertNames = upsertNames.ToArray(),
            interactionFirstSlots = interactionFirstSlots.ToArray(),
            interactionSecondSlots = interactionSecondSlots.ToArray(),
            interactionValues = interactionValues.ToArray()
        };
    }

    /// <summary>
    /// Determines whether the contribution contains no changes from its baseline.
    /// </summary>
    /// <param name="contribution">
    /// The contribution consumed by is empty; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the operation succeeds or its condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    protected override bool IsEmpty(GameLayerSettingContribution contribution)
    {
        contribution.Validate();
        return contribution.removedSlots.Length == 0
               && contribution.upsertSlots.Length == 0
               && contribution.interactionFirstSlots.Length == 0;
    }

    /// <summary>
    /// Composes validated setting contributions into the final runtime setting.
    /// </summary>
    /// <param name="target">
    /// The existing target that receives the validated result.
    /// </param>
    /// <param name="contributions">
    /// The contributions consumed by compose; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    protected override void Compose(
        GameLayerStack target,
        IReadOnlyList<ProjectSettingContribution<GameLayerSettingContribution>> contributions)
    {
        var slotOwners = new string?[GameLayer.C_MAX_COUNT];
        var idOwners = new Dictionary<GameLayerId, string>();
        var interactionOwners = new string[GameLayer.C_MAX_COUNT, GameLayer.C_MAX_COUNT];
        for (int index = 0; index < GameLayer.C_MAX_COUNT; index++)
        {
            var layer = new GameLayer(index);
            if (target.GetId(layer) is GameLayerId id)
            {
                slotOwners[index] = "host";
                idOwners.Add(id, "host");
            }
            for (int second = 0; second < GameLayer.C_MAX_COUNT; second++)
                interactionOwners[index, second] = "host";
        }

        foreach (ProjectSettingContribution<GameLayerSettingContribution> contribution in contributions)
        {
            contribution.value.Validate();
            ApplyRemovals(target, contribution, slotOwners, idOwners);
            ApplyUpserts(target, contribution, slotOwners, idOwners);
            ApplyInteractions(target, contribution, interactionOwners);
        }
    }

    private static void ApplyRemovals(
        GameLayerStack target,
        ProjectSettingContribution<GameLayerSettingContribution> contribution,
        string?[] slotOwners,
        Dictionary<GameLayerId, string> idOwners)
    {
        for (int i = 0; i < contribution.value.removedSlots.Length; i++)
        {
            var layer = new GameLayer(contribution.value.removedSlots[i]);
            var expectedId = new GameLayerId(contribution.value.removedIds[i]);
            GameLayerId? currentId = target.GetId(layer);
            if (currentId is null)
                continue;
            if (currentId != expectedId)
            {
                throw Conflict(
                    contribution.context,
                    $"cannot remove layer '{expectedId}' from slot {layer.index} because it contains '{currentId}'.");
            }
            string owner = slotOwners[layer.index] ?? "host";
            if (!contribution.context.CanOverride(owner))
            {
                throw Conflict(
                    contribution.context,
                    $"cannot remove layer '{expectedId}' owned by '{owner}'.");
            }
            _ = target.Remove(layer);
            slotOwners[layer.index] = null;
            idOwners.Remove(expectedId);
        }
    }

    private static void ApplyUpserts(
        GameLayerStack target,
        ProjectSettingContribution<GameLayerSettingContribution> contribution,
        string?[] slotOwners,
        Dictionary<GameLayerId, string> idOwners)
    {
        for (int i = 0; i < contribution.value.upsertSlots.Length; i++)
        {
            var layer = new GameLayer(contribution.value.upsertSlots[i]);
            var id = new GameLayerId(contribution.value.upsertIds[i]);
            string name = contribution.value.upsertNames[i];
            GameLayerId? currentId = target.GetId(layer);
            string? currentName = target.GetName(layer);
            if (currentId == id && string.Equals(currentName, name, StringComparison.Ordinal))
                continue;

            if (target.TryGetLayer(id, out GameLayer existingLayer) && existingLayer != layer)
            {
                string existingOwner = slotOwners[existingLayer.index] ?? idOwners[id];
                if (!contribution.context.CanOverride(existingOwner))
                {
                    throw Conflict(
                        contribution.context,
                        $"cannot move layer '{id}' from slot {existingLayer.index}; it is owned by '{existingOwner}'.");
                }
                _ = target.Remove(existingLayer);
                slotOwners[existingLayer.index] = null;
                idOwners.Remove(id);
            }

            if (currentId is GameLayerId replacedId)
            {
                string owner = slotOwners[layer.index] ?? idOwners[replacedId];
                if (!contribution.context.CanOverride(owner))
                {
                    throw Conflict(
                        contribution.context,
                        $"cannot replace slot {layer.index} layer '{replacedId}' owned by '{owner}'.");
                }
                idOwners.Remove(replacedId);
            }
            target.Define(layer, id, name);
            slotOwners[layer.index] = contribution.context.contributorId;
            idOwners[id] = contribution.context.contributorId;
        }
    }

    private static void ApplyInteractions(
        GameLayerStack target,
        ProjectSettingContribution<GameLayerSettingContribution> contribution,
        string[,] interactionOwners)
    {
        for (int i = 0; i < contribution.value.interactionFirstSlots.Length; i++)
        {
            var first = new GameLayer(contribution.value.interactionFirstSlots[i]);
            var second = new GameLayer(contribution.value.interactionSecondSlots[i]);
            bool value = contribution.value.interactionValues[i];
            if (target.CanInteract(first, second) == value)
                continue;
            string owner = interactionOwners[first.index, second.index];
            if (!contribution.context.CanOverride(owner))
            {
                throw Conflict(
                    contribution.context,
                    $"cannot replace interaction ({first.index}, {second.index}) owned by '{owner}'.");
            }
            target.SetInteraction(first, second, value);
            interactionOwners[first.index, second.index] = contribution.context.contributorId;
            interactionOwners[second.index, first.index] = contribution.context.contributorId;
        }
    }

    private static InvalidOperationException Conflict(
        ProjectSettingContributionContext context,
        string message)
        => new($"GameLayer contribution '{context.contributorId}' {message}");
}

/// <summary>
/// Stores sparse semantic operations for one GameLayer setting contribution.
/// </summary>
internal sealed class GameLayerSettingContribution : ISerializable
{
    [SerializableProperty]
    internal int[] removedSlots { get; set; } = [];

    [SerializableProperty]
    internal string[] removedIds { get; set; } = [];

    [SerializableProperty]
    internal int[] upsertSlots { get; set; } = [];

    [SerializableProperty]
    internal string[] upsertIds { get; set; } = [];

    [SerializableProperty]
    internal string[] upsertNames { get; set; } = [];

    [SerializableProperty]
    internal int[] interactionFirstSlots { get; set; } = [];

    [SerializableProperty]
    internal int[] interactionSecondSlots { get; set; } = [];

    [SerializableProperty]
    internal bool[] interactionValues { get; set; } = [];

    internal void Validate()
    {
        if (removedSlots is null
            || removedIds is null
            || upsertSlots is null
            || upsertIds is null
            || upsertNames is null
            || interactionFirstSlots is null
            || interactionSecondSlots is null
            || interactionValues is null)
        {
            throw new InvalidOperationException("GameLayer contribution arrays cannot be null.");
        }
        if (removedSlots.Length != removedIds.Length)
            throw new InvalidOperationException("GameLayer removal contribution arrays must have equal lengths.");
        if (upsertSlots.Length != upsertIds.Length || upsertSlots.Length != upsertNames.Length)
            throw new InvalidOperationException("GameLayer upsert contribution arrays must have equal lengths.");
        if (interactionFirstSlots.Length != interactionSecondSlots.Length
            || interactionFirstSlots.Length != interactionValues.Length)
        {
            throw new InvalidOperationException("GameLayer interaction contribution arrays must have equal lengths.");
        }
        var slots = new HashSet<int>();
        for (int i = 0; i < removedSlots.Length; i++)
        {
            _ = new GameLayer(removedSlots[i]);
            _ = new GameLayerId(removedIds[i]);
            if (!slots.Add(removedSlots[i]))
                throw new InvalidOperationException($"GameLayer slot {removedSlots[i]} is removed more than once.");
        }
        slots.Clear();
        for (int i = 0; i < upsertSlots.Length; i++)
        {
            _ = new GameLayer(upsertSlots[i]);
            _ = new GameLayerId(upsertIds[i]);
            _ = GameLayerStack.NormalizeName(upsertNames[i]);
            if (!slots.Add(upsertSlots[i]))
                throw new InvalidOperationException($"GameLayer slot {upsertSlots[i]} is upserted more than once.");
        }
        var pairs = new HashSet<(int first, int second)>();
        for (int i = 0; i < interactionFirstSlots.Length; i++)
        {
            int first = interactionFirstSlots[i];
            int second = interactionSecondSlots[i];
            _ = new GameLayer(first);
            _ = new GameLayer(second);
            if (first > second)
                throw new InvalidOperationException("GameLayer interaction pairs must use canonical slot order.");
            if (!pairs.Add((first, second)))
                throw new InvalidOperationException($"GameLayer interaction ({first}, {second}) is contributed more than once.");
        }
    }
}
