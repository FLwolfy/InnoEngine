namespace Inno.Core.Logging;

/// <summary>
/// Defines a log sink that consumes log entries from <see cref="LogManager"/>.
/// </summary>
public interface ILogSink
{
    /// <summary>
    /// Receives a log entry for processing.
    /// </summary>
    /// <param name="entry">The log entry to consume.</param>
    void Receive(LogEntry entry);
}
