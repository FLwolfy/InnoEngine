using System;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Inno.Scene;

/// <summary>
/// Creates component instances without retaining runtime type or constructor caches.
/// </summary>
internal static class ComponentFactory
{
    internal static GameComponent Create(Type componentType)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        if (!typeof(GameComponent).IsAssignableFrom(componentType) || componentType.IsAbstract || !componentType.IsClass)
            throw new ArgumentException($"Type '{componentType.FullName}' is not a concrete GameComponent.", nameof(componentType));
        ConstructorInfo? constructor = componentType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            Type.EmptyTypes,
            modifiers: null);
        if (constructor is null)
            throw new InvalidOperationException($"GameComponent '{componentType.FullName}' requires a parameterless constructor.");
        try
        {
            return (GameComponent)(constructor.Invoke(null)
                ?? throw new InvalidOperationException($"Could not create GameComponent '{componentType.FullName}'."));
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }
}
