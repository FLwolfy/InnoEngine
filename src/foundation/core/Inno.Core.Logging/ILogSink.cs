namespace Inno.Core.Logging;

/// <summary>
/// Defines a log sink that consumes log entries from a <see cref="LogRouter"/>.
/// </summary>
public interface ILogSink
{
    /// <summary>
    /// Receives a log entry for processing.
    /// </summary>
    /// <param name="entry">
    /// The log entry to consume.
    /// </param>
    void Receive(LogEntry entry);
}
