using System;
using System.Collections.Generic;
using System.Linq;

namespace Inno.Editor.Core;

/// <summary>
/// Identifies one editor statistic across panels, modules, and reloadable extensions.
/// </summary>
public readonly record struct EditorStatisticId
{
    /// <summary>
    /// Creates a globally stable statistic identifier.
    /// </summary>
    /// <param name="value">
    /// Non-empty globally stable identity.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="value"/> is empty.
    /// </exception>
    public EditorStatisticId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        this.value = value.Trim();
    }

    /// <summary>
    /// Gets the globally stable identity.
    /// </summary>
    public string value { get; }

    /// <summary>
    /// Gets whether this identifier contains a usable value.
    /// </summary>
    public bool isValid => !string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// Formats this value as a human-readable representation.
    /// </summary>
    /// <returns>
    /// The human-readable representation of this value.
    /// </returns>
    public override string ToString() => value ?? string.Empty;
}

/// <summary>
/// Identifies one visual group in the editor statistics presentation.
/// </summary>
public readonly record struct EditorStatisticGroupId
{
    /// <summary>
    /// Creates a globally stable statistic-group identifier.
    /// </summary>
    /// <param name="value">
    /// Non-empty globally stable identity.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="value"/> is empty.
    /// </exception>
    public EditorStatisticGroupId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        this.value = value.Trim();
    }

    /// <summary>
    /// Gets the globally stable identity.
    /// </summary>
    public string value { get; }

    /// <summary>
    /// Gets whether this identifier contains a usable value.
    /// </summary>
    public bool isValid => !string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// Formats this value as a human-readable representation.
    /// </summary>
    /// <returns>
    /// The human-readable representation of this value.
    /// </returns>
    public override string ToString() => value ?? string.Empty;
}

/// <summary>
/// Describes one immutable, presentation-ready statistic without retaining provider instances or types.
/// </summary>
public sealed class EditorStatistic
{
    /// <summary>
    /// Creates one statistic contribution.
    /// </summary>
    /// <param name="id">
    /// Globally stable statistic identity used for replacement within a frame.
    /// </param>
    /// <param name="groupId">
    /// Stable visual group identity.
    /// </param>
    /// <param name="groupName">
    /// User-facing group heading.
    /// </param>
    /// <param name="label">
    /// User-facing metric label.
    /// </param>
    /// <param name="value">
    /// Already formatted user-facing value.
    /// </param>
    /// <param name="groupOrder">
    /// Ascending group presentation order.
    /// </param>
    /// <param name="order">
    /// Ascending metric order within the group.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when an identifier, group name, label, or value is empty.
    /// </exception>
    public EditorStatistic(
        EditorStatisticId id,
        EditorStatisticGroupId groupId,
        string groupName,
        string label,
        string value,
        int groupOrder = 0,
        int order = 0)
    {
        if (!id.isValid)
            throw new ArgumentException("A statistic ID is required.", nameof(id));
        if (!groupId.isValid)
            throw new ArgumentException("A statistic group ID is required.", nameof(groupId));
        ArgumentException.ThrowIfNullOrWhiteSpace(groupName);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        this.id = id;
        this.groupId = groupId;
        this.groupName = groupName.Trim();
        this.label = label.Trim();
        this.value = value.Trim();
        this.groupOrder = groupOrder;
        this.order = order;
    }

    /// <summary>
    /// Gets the globally stable statistic identity.
    /// </summary>
    public EditorStatisticId id { get; }

    /// <summary>
    /// Gets the stable visual group identity.
    /// </summary>
    public EditorStatisticGroupId groupId { get; }

    /// <summary>
    /// Gets the user-facing group heading.
    /// </summary>
    public string groupName { get; }

    /// <summary>
    /// Gets the user-facing metric label.
    /// </summary>
    public string label { get; }

    /// <summary>
    /// Gets the presentation-ready value.
    /// </summary>
    public string value { get; }

    /// <summary>
    /// Gets ascending group presentation order.
    /// </summary>
    public int groupOrder { get; }

    /// <summary>
    /// Gets ascending metric order within the group.
    /// </summary>
    public int order { get; }
}

/// <summary>
/// Exchanges frame-scoped, reload-safe statistics between independent editor features and viewers.
/// </summary>
/// <remarks>
/// Contributions contain only stable identifiers and strings. A contribution is retained for the
/// current frame and one completed-frame handoff so panel draw order cannot lose data.
/// </remarks>
public sealed class EditorStatistics
{
    private readonly object m_sync = new();
    private readonly Dictionary<EditorStatisticId, EditorStatistic> m_current = [];
    private Dictionary<EditorStatisticId, EditorStatistic> m_completed = [];

    /// <summary>
    /// Publishes or replaces one statistic for the current editor frame.
    /// </summary>
    /// <param name="statistic">
    /// Immutable contribution containing no runtime provider references.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="statistic"/> is <see langword="null"/>.
    /// </exception>
    public void Publish(EditorStatistic statistic)
    {
        ArgumentNullException.ThrowIfNull(statistic);
        lock (m_sync)
            m_current[statistic.id] = statistic;
    }

    /// <summary>
    /// Publishes or replaces several statistics for the current editor frame.
    /// </summary>
    /// <param name="statistics">
    /// Contributions to publish in enumeration order.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="statistics"/> or one of its values is null.
    /// </exception>
    public void Publish(IEnumerable<EditorStatistic> statistics)
    {
        ArgumentNullException.ThrowIfNull(statistics);
        foreach (EditorStatistic statistic in statistics)
            Publish(statistic);
    }

    /// <summary>
    /// Gets a deterministic snapshot combining current contributions with the previous completed
    /// frame for providers that draw after their consumer.
    /// </summary>
    /// <returns>
    /// A detached, sorted snapshot containing at most one value for each stable statistic ID.
    /// </returns>
    public IReadOnlyList<EditorStatistic> GetSnapshot()
    {
        lock (m_sync)
        {
            var merged = new Dictionary<EditorStatisticId, EditorStatistic>(m_completed);
            foreach ((EditorStatisticId id, EditorStatistic statistic) in m_current)
                merged[id] = statistic;
            return merged.Values
                .OrderBy(static value => value.groupOrder)
                .ThenBy(static value => value.groupName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static value => value.order)
                .ThenBy(static value => value.label, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static value => value.id.value, StringComparer.Ordinal)
                .ToArray();
        }
    }

    internal void AdvanceFrame()
    {
        lock (m_sync)
        {
            m_completed = new Dictionary<EditorStatisticId, EditorStatistic>(m_current);
            m_current.Clear();
        }
    }
}
