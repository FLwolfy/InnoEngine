using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Inno.Core.Reflection;

/// <summary>
/// Provides lock-file governance for <see cref="StableTypeIdAttribute"/> mappings.
/// </summary>
public static class StableTypeIdGovernance
{
    /// <summary>
    /// Parses lock-file JSON into a stable type map.
    /// </summary>
    /// <param name="json">Lock-file JSON content.</param>
    /// <returns>Parsed stable type map.</returns>
    /// <exception cref="InvalidOperationException">Thrown when any lock entry has an invalid Guid string.</exception>
    public static IReadOnlyDictionary<string, Guid> ParseLockJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                     ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var result = new SortedDictionary<string, Guid>(StringComparer.Ordinal);

        foreach ((string key, string value) in parsed)
        {
            if (!Guid.TryParse(value, out Guid id))
            {
                throw new InvalidOperationException($"Stable type lock contains invalid Guid for '{key}': '{value}'.");
            }

            result[key] = id;
        }

        return result;
    }

    /// <summary>
    /// Converts a stable type map to deterministic lock-file JSON.
    /// </summary>
    /// <param name="map">Stable type map.</param>
    /// <returns>Formatted lock-file JSON.</returns>
    public static string ToLockJson(IReadOnlyDictionary<string, Guid> map)
    {
        ArgumentNullException.ThrowIfNull(map);

        var ordered = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach ((string key, Guid value) in map)
        {
            ordered[key] = value.ToString("D");
        }

        return JsonSerializer.Serialize(ordered, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    /// <summary>
    /// Validates that the current map exactly matches the lock map.
    /// </summary>
    /// <param name="locked">Lock-file map.</param>
    /// <param name="current">Current runtime-discovered map.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when any type is added, removed, or changed compared to the lock.
    /// </exception>
    public static void ValidateLockOrThrow(
        IReadOnlyDictionary<string, Guid> locked,
        IReadOnlyDictionary<string, Guid> current)
    {
        ArgumentNullException.ThrowIfNull(locked);
        ArgumentNullException.ThrowIfNull(current);

        var added = new List<string>();
        var removed = new List<string>();
        var changed = new List<string>();

        foreach ((string key, Guid currentId) in current)
        {
            if (!locked.TryGetValue(key, out Guid lockedId))
            {
                added.Add(key);
                continue;
            }

            if (lockedId != currentId)
            {
                changed.Add($"{key} ({lockedId:D} -> {currentId:D})");
            }
        }

        foreach (string key in locked.Keys)
        {
            if (!current.ContainsKey(key))
            {
                removed.Add(key);
            }
        }

        if (added.Count == 0 && removed.Count == 0 && changed.Count == 0)
        {
            return;
        }

        string message = BuildDiffMessage(added, removed, changed);
        throw new InvalidOperationException(message);
    }

    /// <summary>
    /// Builds the current stable type map from <see cref="TypeIdentityRegistry"/>.
    /// </summary>
    /// <returns>Current stable type map keyed by lock-file type key.</returns>
    public static IReadOnlyDictionary<string, Guid> BuildCurrentStableMap()
        => TypeIdentityRegistry.GetStableTypeMapSnapshot();

    private static string BuildDiffMessage(
        IReadOnlyList<string> added,
        IReadOnlyList<string> removed,
        IReadOnlyList<string> changed)
    {
        var parts = new List<string>(4)
        {
            "StableTypeId lock mismatch."
        };

        if (added.Count > 0)
        {
            parts.Add("Added: " + string.Join(", ", added.OrderBy(static x => x, StringComparer.Ordinal)));
        }

        if (removed.Count > 0)
        {
            parts.Add("Removed: " + string.Join(", ", removed.OrderBy(static x => x, StringComparer.Ordinal)));
        }

        if (changed.Count > 0)
        {
            parts.Add("Changed: " + string.Join(", ", changed.OrderBy(static x => x, StringComparer.Ordinal)));
        }

        parts.Add("Update StableTypeId.lock.json intentionally if this change is expected.");
        return string.Join(" ", parts);
    }
}
