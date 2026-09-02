using System;

namespace Inno.Editor.Interactions;

/// <summary>
/// Registers a stateless editor history handler for one stable change protocol.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EditorHistoryHandlerAttribute : Attribute
{
    /// <summary>
    /// Creates a history handler registration.
    /// </summary>
    /// <param name="kind">
    /// The stable globally unique change protocol identifier.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="kind"/> is empty.
    /// </exception>
    public EditorHistoryHandlerAttribute(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        this.kind = kind;
    }

    /// <summary>
    /// Gets the stable globally unique change protocol identifier.
    /// </summary>
    public string kind { get; }

}
