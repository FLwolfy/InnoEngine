using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;

using Inno.Core.Reflection;

namespace Inno.Core.Logging;

public static class Log
{
    private const string C_DEFAULT_CATEGORY = "Unknown";

    private static readonly ConcurrentDictionary<Type, (AssemblyGroup Source, string Category)> TYPE_INFO_CACHE = new();
    private static readonly ConcurrentDictionary<Assembly, AssemblyGroup> ASSEMBLY_SOURCE_CACHE = new();

    [Conditional("DEBUG")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Debug(object? obj)
        => Write(LogLevel.Debug, $"{obj}", null);
    
    [Conditional("DEBUG")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Debug(string message, params object[]? args)
        => Write(LogLevel.Debug, message, args);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Info(object? obj)
        => Write(LogLevel.Info, $"{obj}", null);
    
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Info(string message, params object[]? args)
        => Write(LogLevel.Info, message, args);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Warn(object? obj)
        => Write(LogLevel.Warn, $"{obj}", null);
    
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Warn(string message, params object[]? args)
        => Write(LogLevel.Warn, message, args);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Error(object? obj)
        => Write(LogLevel.Error, $"{obj}", null);
    
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Error(string message, params object[]? args)
        => Write(LogLevel.Error, message, args);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Fatal(object? obj)
        => Write(LogLevel.Fatal, $"{obj}", null);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Fatal(string message, params object[]? args)
        => Write(LogLevel.Fatal, message, args);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Write(LogLevel level, string message, params object[]? args)
    {
        if (!LogManager.IsEnabled(level)) return;

        var sf = new StackTrace(skipFrames: 2, fNeedFileInfo: true).GetFrame(0);
        if (sf == null) return;
        
        var method = sf.GetMethod();
        var callerType = method?.DeclaringType;

        AssemblyGroup source = AssemblyGroup.None;
        string category = C_DEFAULT_CATEGORY;

        if (callerType != null)
        {
            var info = TYPE_INFO_CACHE.GetOrAdd(callerType, static t =>
            {
                var src = ASSEMBLY_SOURCE_CACHE.GetOrAdd(t.Assembly, static assembly => assembly.GetInnoAssemblyGroup());
                return (src, t.Name);
            });

            source = info.Source;
            category = info.Category;
        }

        var msg = (args == null || args.Length == 0) ? message : string.Format(message, args);
        var file = sf.GetFileName() ?? C_DEFAULT_CATEGORY;
        var line = sf.GetFileLineNumber();
        
        LogManager.Dispatch(new LogEntry(level, source, category, msg, file, line));
    }
}
