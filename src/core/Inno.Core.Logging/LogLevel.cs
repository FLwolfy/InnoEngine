namespace Inno.Core.Logging;

/// <summary>
/// Represents log severity levels in ascending order.
/// </summary>
public enum LogLevel
{
    /// <summary>
    /// Diagnostic logs intended for local debugging.
    /// </summary>
    Debug,

    /// <summary>
    /// Informational logs for normal runtime flow.
    /// </summary>
    Info,

    /// <summary>
    /// Warning logs for recoverable problems.
    /// </summary>
    Warn,

    /// <summary>
    /// Error logs for failures that should be investigated.
    /// </summary>
    Error,

    /// <summary>
    /// Fatal logs for unrecoverable failures.
    /// </summary>
    Fatal
}
