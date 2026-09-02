using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

using Inno.Extensibility.Modules;

namespace Inno.Core.Logging;

/// <summary>
/// Convenience facade for writing logs with automatic source/category resolution.
/// </summary>
public static class Log
{
    private const string C_DEFAULT_CATEGORY = "Unknown";

    private sealed class TypeInfo
    {
        /// <summary>
        /// Gets the assembly domain that produced this script log call.
        /// </summary>
        public required AssemblyDomain domain { get; init; }
        /// <summary>
        /// Gets the assembly scope captured for this script log call.
        /// </summary>
        public required AssemblyScope scope { get; init; }
        /// <summary>
        /// Gets text used for stable identity, presentation, or diagnostics by this contract.
        /// </summary>
        public required string category { get; init; }
    }

    private sealed class AssemblySource
    {
        /// <summary>
        /// Gets the assembly domain that produced this script log call.
        /// </summary>
        public required AssemblyDomain domain { get; init; }
        /// <summary>
        /// Gets the assembly scope captured for this script log call.
        /// </summary>
        public required AssemblyScope scope { get; init; }
    }

    private static readonly ConditionalWeakTable<Type, TypeInfo> TYPE_INFO_CACHE = new();
    private static readonly ConditionalWeakTable<Assembly, AssemblySource> ASSEMBLY_SOURCE_CACHE = new();

    /// <summary>
    /// Writes a debug-level message using the object's string representation.
    /// </summary>
    /// <param name="obj">
    /// The object to log.
    /// </param>
    [Conditional("DEBUG")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Debug(object? obj)
        => Write(LogLevel.Debug, $"{obj}", null);
    
    /// <summary>
    /// Writes a formatted debug-level message.
    /// </summary>
    /// <param name="message">
    /// The composite format string.
    /// </param>
    /// <param name="args">
    /// The format arguments.
    /// </param>
    [Conditional("DEBUG")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Debug(string message, params object[]? args)
        => Write(LogLevel.Debug, message, args);

    /// <summary>
    /// Writes an info-level message using the object's string representation.
    /// </summary>
    /// <param name="obj">
    /// The object to log.
    /// </param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Info(object? obj)
        => Write(LogLevel.Info, $"{obj}", null);
    
    /// <summary>
    /// Writes a formatted info-level message.
    /// </summary>
    /// <param name="message">
    /// The composite format string.
    /// </param>
    /// <param name="args">
    /// The format arguments.
    /// </param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Info(string message, params object[]? args)
        => Write(LogLevel.Info, message, args);

    /// <summary>
    /// Writes a warning-level message using the object's string representation.
    /// </summary>
    /// <param name="obj">
    /// The object to log.
    /// </param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Warn(object? obj)
        => Write(LogLevel.Warn, $"{obj}", null);
    
    /// <summary>
    /// Writes a formatted warning-level message.
    /// </summary>
    /// <param name="message">
    /// The composite format string.
    /// </param>
    /// <param name="args">
    /// The format arguments.
    /// </param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Warn(string message, params object[]? args)
        => Write(LogLevel.Warn, message, args);

    /// <summary>
    /// Writes an error-level message using the object's string representation.
    /// </summary>
    /// <param name="obj">
    /// The object to log.
    /// </param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Error(object? obj)
        => Write(LogLevel.Error, $"{obj}", null);
    
    /// <summary>
    /// Writes a formatted error-level message.
    /// </summary>
    /// <param name="message">
    /// The composite format string.
    /// </param>
    /// <param name="args">
    /// The format arguments.
    /// </param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Error(string message, params object[]? args)
        => Write(LogLevel.Error, message, args);

    /// <summary>
    /// Writes a fatal-level message using the object's string representation.
    /// </summary>
    /// <param name="obj">
    /// The object to log.
    /// </param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Fatal(object? obj)
        => Write(LogLevel.Fatal, $"{obj}", null);

    /// <summary>
    /// Writes a formatted fatal-level message.
    /// </summary>
    /// <param name="message">
    /// The composite format string.
    /// </param>
    /// <param name="args">
    /// The format arguments.
    /// </param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Fatal(string message, params object[]? args)
        => Write(LogLevel.Fatal, message, args);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Write(LogLevel level, string message, params object[]? args)
    {
        LogRouter router = LogRouter.current;
        if (!router.IsEnabled(level))
            return;

        var stackTrace = new StackTrace(2, true);
        StackFrame? sf = stackTrace.GetFrame(0);
        var method = sf?.GetMethod();
        var callerType = method?.DeclaringType;

        AssemblyDomain domain = AssemblyDomain.InnoInternal;
        AssemblyScope scope = AssemblyScope.Runtime;
        string category = C_DEFAULT_CATEGORY;

        if (callerType != null)
        {
            TypeInfo info = TYPE_INFO_CACHE.GetValue(callerType, static type =>
            {
                var src = ASSEMBLY_SOURCE_CACHE.GetValue(type.Assembly, static assembly =>
                    new AssemblySource
                    {
                        domain = assembly.GetInnoAssemblyDomain(),
                        scope = assembly.GetInnoAssemblyScope()
                    });
                return new TypeInfo
                {
                    domain = src.domain,
                    scope = src.scope,
                    category = type.Name
                };
            });

            domain = info.domain;
            scope = info.scope;
            category = info.category;
        }

        var msg = (args == null || args.Length == 0) ? message : string.Format(message, args);

        var file = C_DEFAULT_CATEGORY;
        var line = 0;
        var filePath = sf?.GetFileName();
        file = string.IsNullOrWhiteSpace(filePath) ? C_DEFAULT_CATEGORY : filePath;
        line = sf?.GetFileLineNumber() ?? 0;
        
        router.Dispatch(new LogEntry(
            level,
            domain,
            scope,
            category,
            msg,
            file,
            line,
            stackTrace.ToString(),
            LogSessionContext.current));
    }

}
