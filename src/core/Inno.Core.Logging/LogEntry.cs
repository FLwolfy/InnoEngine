using System;

using Inno.Core.Reflection;

namespace Inno.Core.Logging;

public readonly struct LogEntry(
    LogLevel level,
    AssemblyGroup source,
    string category,
    string message,
    string file,
    int line
) {
    public readonly LogLevel level = level;
    public readonly AssemblyGroup source = source;
    public readonly string category = category;
    public readonly string message = message;
    public readonly DateTime time = DateTime.Now;
    public readonly string file = file;
    public readonly int line = line;
}