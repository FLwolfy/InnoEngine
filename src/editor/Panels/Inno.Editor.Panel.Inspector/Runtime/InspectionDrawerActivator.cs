using System;
using System.Linq;
using System.Reflection;

using Inno.Editor.Inspection;

namespace Inno.Editor.Panel.Inspector;

/// <summary>
/// Constructs discovered inspection drawers from the dependencies owned by the Inspector composition root.
/// </summary>
internal sealed class InspectionDrawerActivator
{
    private readonly object[] m_dependencies;

    /// <summary>
    /// Creates an activator over the dependencies available to built-in and extension drawers.
    /// </summary>
    /// <param name="dependencies">The non-null dependency instances available for constructor injection.</param>
    internal InspectionDrawerActivator(params object[] dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        if (dependencies.Any(static dependency => dependency is null))
            throw new ArgumentException("Inspection drawer dependencies cannot contain null.", nameof(dependencies));
        m_dependencies = [.. dependencies];
    }

    /// <summary>
    /// Creates one drawer by resolving its single constructor from the available dependencies.
    /// </summary>
    /// <param name="drawerType">The concrete inspection drawer type to construct.</param>
    /// <returns>The constructed inspection drawer.</returns>
    internal IInspectionDrawer Create(Type drawerType)
    {
        ArgumentNullException.ThrowIfNull(drawerType);
        ConstructorInfo[] constructors = drawerType.GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (constructors.Length != 1)
        {
            throw new InvalidOperationException(
                $"Inspection drawer '{drawerType.FullName}' must declare exactly one constructor.");
        }

        ParameterInfo[] parameters = constructors[0].GetParameters();
        var arguments = new object?[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
            arguments[i] = Resolve(drawerType, parameters[i].ParameterType);
        return constructors[0].Invoke(arguments) as IInspectionDrawer
               ?? throw new InvalidOperationException(
                   $"Inspection drawer '{drawerType.FullName}' does not implement " +
                   $"'{typeof(IInspectionDrawer).FullName}'.");
    }

    private object Resolve(Type drawerType, Type dependencyType)
    {
        object[] matches = m_dependencies
            .Where(dependencyType.IsInstanceOfType)
            .ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                $"Inspection drawer '{drawerType.FullName}' requests unavailable dependency " +
                $"'{dependencyType.FullName}'. Drawers should normally use InspectionDrawContext, " +
                "or request one dependency supplied by the Inspector composition root."),
            _ => throw new InvalidOperationException(
                $"Inspection drawer '{drawerType.FullName}' has ambiguous dependency " +
                $"'{dependencyType.FullName}'.")
        };
    }
}
