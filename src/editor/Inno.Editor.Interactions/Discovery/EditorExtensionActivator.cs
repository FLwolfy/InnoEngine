using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Inno.Editor.Core;

namespace Inno.Editor.Interactions;

internal sealed class EditorExtensionActivator
{
    private readonly EditorContext m_context;
    private readonly EditorInteractions m_interactions;
    private readonly Type[] m_moduleTypes;
    private readonly Dictionary<Type, object> m_instances = [];
    private readonly HashSet<Type> m_constructing = [];

    internal EditorExtensionActivator(
        EditorContext context,
        EditorInteractions interactions,
        IEnumerable<Type> moduleTypes,
        IEnumerable<object>? retainedInstances = null)
    {
        m_context = context ?? throw new ArgumentNullException(nameof(context));
        m_interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
        m_moduleTypes = moduleTypes
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToArray();
        if (retainedInstances is null)
            return;
        foreach (object instance in retainedInstances)
            m_instances.TryAdd(instance.GetType(), instance);
    }

    internal IReadOnlyCollection<object> instances => m_instances.Values;

    internal EditorModule CreateModule(Type type)
        => (EditorModule)Create(type, typeof(EditorModule));

    internal TExtension CreateExtension<TExtension>(Type type)
        where TExtension : class
        => (TExtension)Create(type, typeof(TExtension));

    private object Create(Type type, Type contract)
    {
        if (m_instances.TryGetValue(type, out object? existing))
        {
            if (!contract.IsInstanceOfType(existing))
                throw CreateContractException(type, contract);
            return existing;
        }
        if (type.IsAbstract || !contract.IsAssignableFrom(type))
            throw CreateContractException(type, contract);
        if (!m_constructing.Add(type))
            throw new InvalidOperationException(
                $"Editor extension constructor dependency cycle contains '{type.FullName}'.");

        try
        {
            ConstructorInfo constructor = SelectConstructor(type);
            ParameterInfo[] parameters = constructor.GetParameters();
            var arguments = new object?[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
                arguments[i] = Resolve(parameters[i].ParameterType, type);
            object instance;
            try
            {
                instance = constructor.Invoke(arguments);
            }
            catch (TargetInvocationException exception)
            {
                throw new InvalidOperationException(
                    $"Editor extension '{type.FullName}' constructor failed.",
                    exception.InnerException ?? exception);
            }
            m_instances.Add(type, instance);
            return instance;
        }
        finally
        {
            _ = m_constructing.Remove(type);
        }
    }

    private object Resolve(Type parameterType, Type ownerType)
    {
        if (parameterType == typeof(EditorContext))
            return m_context;
        if (parameterType == typeof(EditorInteractions))
            return m_interactions;

        Type[] matches = m_moduleTypes
            .Where(parameterType.IsAssignableFrom)
            .ToArray();
        if (matches.Length == 1)
            return CreateModule(matches[0]);
        if (matches.Length == 0)
        {
            throw new InvalidOperationException(
                $"Editor extension '{ownerType.FullName}' requests unsupported constructor dependency " +
                $"'{parameterType.FullName}'. Only EditorContext, EditorInteractions, and discovered " +
                "EditorModule types are injectable.");
        }
        throw new InvalidOperationException(
            $"Editor extension '{ownerType.FullName}' has ambiguous module dependency " +
            $"'{parameterType.FullName}': {string.Join(", ", matches.Select(static type => type.FullName))}.");
    }

    private static ConstructorInfo SelectConstructor(Type type)
    {
        ConstructorInfo[] constructors = type.GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (constructors.Length == 0)
            throw new InvalidOperationException($"Editor extension '{type.FullName}' has no instance constructor.");
        if (constructors.Length > 1)
        {
            throw new InvalidOperationException(
                $"Editor extension '{type.FullName}' must declare exactly one constructor.");
        }
        return constructors[0];
    }

    private static InvalidOperationException CreateContractException(Type type, Type contract)
        => new($"Editor extension '{type.FullName}' must be a non-abstract '{contract.FullName}'.");
}
