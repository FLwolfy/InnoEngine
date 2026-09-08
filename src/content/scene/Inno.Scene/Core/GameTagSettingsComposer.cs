using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.Serialization;
using Inno.Core.Settings;

namespace Inno.Scene;

/// <summary>
/// Composes sparse tag additions and removals from multiple setting contributors.
/// </summary>
[ProjectSettingComposer("inno.scene.tags")]
internal sealed class GameTagSettingsComposer
    : ProjectSettingComposer<GameTagCatalog, GameTagSettingContribution>
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
    /// The validated game tag setting contribution that represents the completed operation.
    /// </returns>
    protected override GameTagSettingContribution CaptureContribution(
        GameTagCatalog baseline,
        GameTagCatalog value)
    {
        IReadOnlySet<string> baselineTags = baseline.GetTags().ToHashSet(StringComparer.Ordinal);
        IReadOnlySet<string> valueTags = value.GetTags().ToHashSet(StringComparer.Ordinal);
        return new GameTagSettingContribution
        {
            additions = valueTags.Except(baselineTags, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            removals = baselineTags.Except(valueTags, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
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
    protected override bool IsEmpty(GameTagSettingContribution contribution)
    {
        contribution.Validate();
        return contribution.additions.Length == 0 && contribution.removals.Length == 0;
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
        GameTagCatalog target,
        IReadOnlyList<ProjectSettingContribution<GameTagSettingContribution>> contributions)
    {
        var owners = target.GetTags().ToDictionary(static tag => tag, static _ => "host", StringComparer.Ordinal);
        foreach (ProjectSettingContribution<GameTagSettingContribution> contribution in contributions)
        {
            contribution.value.Validate();
            foreach (string tag in contribution.value.removals)
            {
                if (!target.IsDefined(tag))
                    continue;
                string owner = owners[tag];
                if (!contribution.context.CanOverride(owner))
                {
                    throw new InvalidOperationException(
                        $"GameTag contribution '{contribution.context.contributorId}' cannot remove tag '{tag}' " +
                        $"owned by '{owner}'.");
                }
                if (!target.Remove(tag))
                {
                    throw new InvalidOperationException(
                        $"GameTag contribution '{contribution.context.contributorId}' cannot remove built-in tag '{tag}'.");
                }
                owners.Remove(tag);
            }
            foreach (string tag in contribution.value.additions)
            {
                if (target.IsDefined(tag))
                    continue;
                _ = target.Add(tag);
                owners.Add(tag, contribution.context.contributorId);
            }
        }
    }
}

/// <summary>
/// Stores sparse semantic operations for one GameTag setting contribution.
/// </summary>
internal sealed class GameTagSettingContribution : ISerializable
{
    [SerializableProperty]
    internal string[] additions { get; set; } = [];

    [SerializableProperty]
    internal string[] removals { get; set; } = [];

    internal void Validate()
    {
        if (additions is null || removals is null)
            throw new InvalidOperationException("GameTag contribution arrays cannot be null.");
        string[] normalizedAdditions = additions.Select(GameTagCatalog.Normalize).Order(StringComparer.Ordinal).ToArray();
        string[] normalizedRemovals = removals.Select(GameTagCatalog.Normalize).Order(StringComparer.Ordinal).ToArray();
        if (!normalizedAdditions.SequenceEqual(additions, StringComparer.Ordinal)
            || normalizedAdditions.Distinct(StringComparer.Ordinal).Count() != normalizedAdditions.Length)
        {
            throw new InvalidOperationException("GameTag additions must be unique, normalized, and ordered.");
        }
        if (!normalizedRemovals.SequenceEqual(removals, StringComparer.Ordinal)
            || normalizedRemovals.Distinct(StringComparer.Ordinal).Count() != normalizedRemovals.Length)
        {
            throw new InvalidOperationException("GameTag removals must be unique, normalized, and ordered.");
        }
        if (normalizedAdditions.Intersect(normalizedRemovals, StringComparer.Ordinal).Any())
            throw new InvalidOperationException("A GameTag contribution cannot add and remove the same tag.");
    }
}
