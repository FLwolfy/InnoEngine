using System;

namespace Inno.Editor.Inspection;

/// <summary>
/// Applies one root serialized-property mutation through the history mechanism owned by a feature.
/// </summary>
public interface IInspectionPropertyEditService
{
    /// <summary>
    /// Applies a property mutation and records it when the owning feature observes a value change.
    /// </summary>
    /// <param name="owner">
    /// The domain object that owns the root serialized property.
    /// </param>
    /// <param name="propertyName">
    /// The exact root serialized member name.
    /// </param>
    /// <param name="mutation">
    /// The callback that assigns the requested value.
    /// </param>
    /// <param name="historyName">
    /// The user-facing name for the resulting history entry.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the value changed and the owning feature recorded the edit;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="owner"/> or <paramref name="mutation"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="propertyName"/> or <paramref name="historyName"/> is empty.
    /// </exception>
    bool ChangeProperty(
        object owner,
        string propertyName,
        Action mutation,
        string historyName);
}
