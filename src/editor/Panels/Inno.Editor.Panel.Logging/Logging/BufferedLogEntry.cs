using Inno.Core.Logging;

namespace Inno.Editor.Panel.Logging;

internal readonly record struct BufferedLogEntry(long id, LogEntry entry);
