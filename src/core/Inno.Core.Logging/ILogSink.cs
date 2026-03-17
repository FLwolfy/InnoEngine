namespace Inno.Core.Logging;

public interface ILogSink
{
    void Receive(LogEntry entry);
}
