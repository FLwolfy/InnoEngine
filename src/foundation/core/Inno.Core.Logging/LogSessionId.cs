using System;

namespace Inno.Core.Logging;

/// <summary>
/// Identifies the isolated runtime session that produced a log entry.
/// </summary>
public readonly record struct LogSessionId
{
    private readonly Guid m_value;

    private LogSessionId(Guid value)
    {
        m_value = value;
    }

    /// <summary>
    /// Gets an identifier that represents process-level work outside an isolated runtime session.
    /// </summary>
    public static LogSessionId none => default;

    /// <summary>
    /// Gets whether this identifier names an isolated runtime session.
    /// </summary>
    public bool isAssigned => m_value != Guid.Empty;

    /// <summary>
    /// Creates a unique identifier for a new isolated runtime session.
    /// </summary>
    /// <returns>
    /// A newly allocated non-empty session identifier.
    /// </returns>
    public static LogSessionId Create()
        => new(Guid.NewGuid());

    /// <summary>
    /// Returns the stable textual representation used by diagnostics and persisted logs.
    /// </summary>
    /// <returns>
    /// The lowercase hexadecimal identifier, or an empty string for process-level work.
    /// </returns>
    public override string ToString()
        => m_value == Guid.Empty ? string.Empty : m_value.ToString("N");
}
