using System;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Inno.Editor.Interactions.Tests;

internal static class EditorTestReflection
{
    internal static object Create(
        Assembly assembly,
        string typeName,
        params object?[] arguments)
    {
        Type type = assembly.GetType(typeName, throwOnError: true)!;
        try
        {
            return Activator.CreateInstance(
                type,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: arguments,
                culture: null)!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            Rethrow(exception.InnerException);
            throw;
        }
    }

    internal static T Get<T>(object target, string propertyName)
        => (T)target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(target)!;

    internal static T Invoke<T>(object target, string methodName, params object?[] arguments)
    {
        MethodInfo method = target.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(candidate =>
                candidate.Name == methodName &&
                candidate.GetParameters().Length == arguments.Length);
        try
        {
            return (T)method.Invoke(target, arguments)!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            Rethrow(exception.InnerException);
            throw;
        }
    }

    internal static void Invoke(object target, string methodName, params object?[] arguments)
        => _ = Invoke<object?>(target, methodName, arguments);

    private static void Rethrow(Exception exception)
        => ExceptionDispatchInfo.Capture(exception).Throw();
}
