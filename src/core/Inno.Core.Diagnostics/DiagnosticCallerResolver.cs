using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Inno.Core.Diagnostics;

internal static class DiagnosticCallerResolver
{
    private sealed class CallerInfo
    {
        internal required string sourcePrefix { get; init; }
    }

    private static readonly ConditionalWeakTable<Type, CallerInfo> CALLERS = new();

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static DiagnosticSource Resolve(
        string group,
        Guid? targetId = null,
        string? displayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(group);
        if (targetId == Guid.Empty)
            throw new ArgumentException("A diagnostic target identifier cannot be empty.", nameof(targetId));

        Type callerType = ResolveCallerType();
        CallerInfo caller = CALLERS.GetValue(callerType, static type =>
        {
            string assemblyName = type.Assembly.GetName().Name
                ?? throw new InvalidOperationException("The diagnostic caller assembly has no simple name.");
            string typeName = type.FullName ?? type.Name;
            return new CallerInfo
            {
                sourcePrefix = $"{assemblyName}:{typeName}"
            };
        });

        string id = targetId.HasValue
            ? $"{caller.sourcePrefix}/{group}/{targetId.Value:N}"
            : $"{caller.sourcePrefix}/{group}";
        string resolvedDisplayName = string.IsNullOrWhiteSpace(displayName)
            ? group
            : displayName;
        return new DiagnosticSource(id, resolvedDisplayName);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Type ResolveCallerType()
    {
        var trace = new StackTrace(skipFrames: 1, fNeedFileInfo: false);
        StackFrame[] frames = trace.GetFrames();
        Assembly infrastructureAssembly = typeof(Diagnostics).Assembly;
        for (int i = 0; i < frames.Length; i++)
        {
            Type? declaringType = frames[i].GetMethod()?.DeclaringType;
            if (declaringType is null || declaringType.Assembly == infrastructureAssembly)
                continue;
            return NormalizeCompilerGeneratedType(declaringType);
        }

        throw new InvalidOperationException("The diagnostic caller type could not be resolved.");
    }

    private static Type NormalizeCompilerGeneratedType(Type type)
    {
        Type current = type;
        while (current.DeclaringType is not null &&
               current.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
        {
            current = current.DeclaringType;
        }
        return current;
    }
}
