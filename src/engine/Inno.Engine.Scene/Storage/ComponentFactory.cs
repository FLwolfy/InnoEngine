using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace Inno.Engine.Scene;

/// <summary>Creates component instances through cached constructor delegates.</summary>
internal static class ComponentFactory
{
    private static readonly object s_sync = new();
    private static readonly Dictionary<Type, Func<GameComponent>> s_factories = new();

    internal static GameComponent Create(Type componentType)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        Func<GameComponent> factory;
        lock (s_sync)
        {
            if (!s_factories.TryGetValue(componentType, out factory!))
            {
                factory = BuildFactory(componentType);
                s_factories.Add(componentType, factory);
            }
        }

        return factory();
    }

    private static Func<GameComponent> BuildFactory(Type componentType)
    {
        if (!typeof(GameComponent).IsAssignableFrom(componentType) || componentType.IsAbstract || !componentType.IsClass)
            throw new ArgumentException($"Type '{componentType.FullName}' is not a concrete {nameof(GameComponent)}.", nameof(componentType));

        ConstructorInfo? constructor = componentType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            Type.EmptyTypes,
            modifiers: null);
        if (constructor is null)
            throw new InvalidOperationException($"GameComponent '{componentType.FullName}' must declare a parameterless constructor.");

        NewExpression create = Expression.New(constructor);
        UnaryExpression cast = Expression.Convert(create, typeof(GameComponent));
        return Expression.Lambda<Func<GameComponent>>(cast).Compile();
    }
}
