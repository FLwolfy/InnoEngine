using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

using Inno.Core.Reflection;

namespace Inno.Core.Logging;

/// <summary>
/// Convenience facade for writing logs with automatic source/category resolution.
/// </summary>
public static class Log
{
    private const string C_DEFAULT_CATEGORY = "Unknown";

    private sealed class TypeInfo
    {
        public required AssemblyGroup source { get; init; }
        public required string category { get; init; }
    }

    private sealed class AssemblySource
    {
        public required AssemblyGroup source { get; init; }
    }

    private static readonly Lock TYPE_INFO_CACHE_SYNC = new();
    private static readonly Dictionary<int, TypeInfo> TYPE_INFO_CACHE = [];
    private static readonly Lock LOCAL_TYPE_KEY_SYNC = new();
    private static readonly Dictionary<Type, int> LOCAL_TYPE_KEYS = [];
    private static int s_nextLocalTypeKey = int.MinValue;
    private static readonly ConditionalWeakTable<Assembly, AssemblySource> ASSEMBLY_SOURCE_CACHE = new();

    /// <summary>
    /// Writes a debug-level message using the object's string representation.
    /// </summary>
    /// <param name="obj">The object to log.</param>
    [Conditional("DEBUG")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Debug(object? obj)
        => Write(LogLevel.Debug, $"{obj}", null);
    
    /// <summary>
    /// Writes a formatted debug-level message.
    /// </summary>
    /// <param name="message">The composite format string.</param>
    /// <param name="args">The format arguments.</param>
    [Conditional("DEBUG")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Debug(string message, params object[]? args)
        => Write(LogLevel.Debug, message, args);

    /// <summary>
    /// Writes a formatted info-level message.
    /// </summary>
    /// <param name="message">The composite format string.</param>
    /// <param name="args">The format arguments.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Info(object? obj)
        => Write(LogLevel.Info, $"{obj}", null);
    
    /// <summary>
    /// Writes a warning-level message using the object's string representation.
    /// </summary>
    /// <param name="obj">The object to log.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Info(string message, params object[]? args)
        => Write(LogLevel.Info, message, args);

    /// <summary>
    /// Writes a formatted warning-level message.
    /// </summary>
    /// <param name="message">The composite format string.</param>
    /// <param name="args">The format arguments.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Warn(object? obj)
        => Write(LogLevel.Warn, $"{obj}", null);
    
    /// <summary>
    /// Writes an error-level message using the object's string representation.
    /// </summary>
    /// <param name="obj">The object to log.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Warn(string message, params object[]? args)
        => Write(LogLevel.Warn, message, args);

    /// <summary>
    /// Writes a formatted error-level message.
    /// </summary>
    /// <param name="message">The composite format string.</param>
    /// <param name="args">The format arguments.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Error(object? obj)
        => Write(LogLevel.Error, $"{obj}", null);
    
    /// <summary>
    /// Writes a fatal-level message using the object's string representation.
    /// </summary>
    /// <param name="obj">The object to log.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Error(string message, params object[]? args)
        => Write(LogLevel.Error, message, args);

    /// <summary>
    /// Writes a formatted fatal-level message.
    /// </summary>
    /// <param name="message">The composite format string.</param>
    /// <param name="args">The format arguments.</param>
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

        var sf = new StackFrame(2, true);
        var method = sf.GetMethod();
        var callerType = method?.DeclaringType;

        AssemblyGroup source = AssemblyGroup.None;
        string category = C_DEFAULT_CATEGORY;

        if (callerType != null)
        {
            int runtimeTypeId = GetTypeCacheKey(callerType);
            TypeInfo info;
            lock (TYPE_INFO_CACHE_SYNC)
            {
                if (!TYPE_INFO_CACHE.TryGetValue(runtimeTypeId, out info!))
                {
                    var src = ASSEMBLY_SOURCE_CACHE.GetValue(callerType.Assembly, static assembly =>
                        new AssemblySource { source = assembly.GetInnoAssemblyGroup() });

                    info = new TypeInfo
                    {
                        source = src.source,
                        category = callerType.Name
                    };
                    TYPE_INFO_CACHE[runtimeTypeId] = info;
                }
            }

            source = info.source;
            category = info.category;
        }

        var msg = (args == null || args.Length == 0) ? message : string.Format(message, args);

        var file = C_DEFAULT_CATEGORY;
        var line = 0;
        var filePath = sf.GetFileName();
        file = string.IsNullOrWhiteSpace(filePath) ? C_DEFAULT_CATEGORY : filePath;
        line = sf.GetFileLineNumber();
        
        LogManager.Dispatch(new LogEntry(level, source, category, msg, file, line));
    }

    private static int GetTypeCacheKey(Type type)
    {
        if (TypeCache.TryGetRuntimeTypeId(type, out int runtimeTypeId))
        {
            return runtimeTypeId;
        }

        lock (LOCAL_TYPE_KEY_SYNC)
        {
            if (LOCAL_TYPE_KEYS.TryGetValue(type, out int existing))
            {
                return existing;
            }

            int created = s_nextLocalTypeKey++;
            LOCAL_TYPE_KEYS[type] = created;
            return created;
        }
    }
}
