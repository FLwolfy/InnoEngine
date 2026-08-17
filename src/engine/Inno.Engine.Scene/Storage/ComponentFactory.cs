using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Inno.Engine.Scene;

/// <summary>Creates component instances without strongly rooting collectible component types.</summary>
internal static class ComponentFactory
{
    private static readonly ConditionalWeakTable<Type, FactoryBox> S_FACTORIES = new();

    internal static GameComponent Create(Type componentType)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        return S_FACTORIES.GetValue(componentType, static type => new FactoryBox(BuildFactory(type))).factory();
    }

    private static Func<GameComponent> BuildFactory(Type componentType)
    {
        if (!typeof(GameComponent).IsAssignableFrom(componentType) || componentType.IsAbstract || !componentType.IsClass)
            throw new ArgumentException($"Type '{componentType.FullName}' is not a concrete GameComponent.", nameof(componentType));
        ConstructorInfo? constructor = componentType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            Type.EmptyTypes,
            modifiers: null);
        if (constructor is null)
            throw new InvalidOperationException($"GameComponent '{componentType.FullName}' requires a parameterless constructor.");
        NewExpression create = Expression.New(constructor);
        return Expression.Lambda<Func<GameComponent>>(Expression.Convert(create, typeof(GameComponent))).Compile();
    }

    private sealed record FactoryBox(Func<GameComponent> factory);
}
