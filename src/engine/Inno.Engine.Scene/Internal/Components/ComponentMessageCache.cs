using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Inno.Engine.Scene;

/// <summary>Caches component message delegates such as Reset.</summary>
internal static class ComponentMessageCache
{
    private static readonly object s_sync = new();
    private static readonly Dictionary<Type, Action<GameComponent>?> s_resetByType = new();

    internal static void InvokeReset(GameComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        Action<GameComponent>? reset;
        Type componentType = component.GetType();
        lock (s_sync)
        {
            if (!s_resetByType.TryGetValue(componentType, out reset))
            {
                reset = BuildReset(componentType);
                s_resetByType.Add(componentType, reset);
            }
        }

        reset?.Invoke(component);
    }

    private static Action<GameComponent>? BuildReset(Type componentType)
    {
        const BindingFlags c_flags = BindingFlags.Instance | BindingFlags.Public |
                                     BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        MethodInfo? reset = null;
        for (Type? current = componentType; current is not null && current != typeof(GameComponent); current = current.BaseType)
        {
            MethodInfo[] declaredResetMethods = current.GetMethods(c_flags)
                .Where(static method => string.Equals(method.Name, "Reset", StringComparison.Ordinal))
                .ToArray();
            if (declaredResetMethods.Length == 0)
                continue;

            reset = declaredResetMethods.FirstOrDefault(static method =>
                !method.IsStatic && method.ReturnType == typeof(void) && method.GetParameters().Length == 0);
            if (reset is null)
            {
                throw new InvalidOperationException(
                    $"GameComponent reset message on '{current.FullName}' must include a parameterless instance method returning void.");
            }

            if (reset is not null)
                break;
        }

        if (reset is null)
            return null;

        ParameterExpression component = Expression.Parameter(typeof(GameComponent), "component");
        MethodCallExpression call = Expression.Call(Expression.Convert(component, reset.DeclaringType!), reset);
        return Expression.Lambda<Action<GameComponent>>(call, component).Compile();
    }
}
