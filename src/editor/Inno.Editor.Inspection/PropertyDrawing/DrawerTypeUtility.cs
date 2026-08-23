using System;
using System.Collections.Generic;

namespace Inno.Editor.Inspection;

internal static class DrawerTypeUtility
{
    internal static bool TryGetDistance(Type concreteType, Type registeredType, bool useForChildren, out int distance)
    {
        Type normalizedRegistered = registeredType;
        Type normalizedConcrete = concreteType;

        if (normalizedRegistered.IsGenericTypeDefinition)
        {
            if (normalizedConcrete.IsGenericType &&
                normalizedConcrete.GetGenericTypeDefinition() == normalizedRegistered)
            {
                distance = 1;
                return true;
            }

            if (!useForChildren)
            {
                distance = int.MaxValue;
                return false;
            }

            if (TryGetOpenGenericDistance(normalizedConcrete, normalizedRegistered, out int genericDistance))
            {
                distance = 100 + genericDistance;
                return true;
            }

            distance = int.MaxValue;
            return false;
        }

        if (normalizedConcrete == normalizedRegistered)
        {
            distance = 0;
            return true;
        }

        if (!useForChildren || !normalizedRegistered.IsAssignableFrom(normalizedConcrete))
        {
            distance = int.MaxValue;
            return false;
        }

        int assignableDistance = GetAssignableDistance(normalizedConcrete, normalizedRegistered);
        if (assignableDistance == int.MaxValue)
        {
            distance = int.MaxValue;
            return false;
        }

        distance = 100 + assignableDistance;
        return true;
    }

    private static bool TryGetOpenGenericDistance(Type concreteType, Type openGenericType, out int distance)
    {
        var queue = new Queue<(Type type, int distance)>();
        var visited = new HashSet<Type>();
        queue.Enqueue((concreteType, 0));
        while (queue.Count > 0)
        {
            (Type current, int currentDistance) = queue.Dequeue();
            if (!visited.Add(current))
            {
                continue;
            }

            if (current.IsGenericType && current.GetGenericTypeDefinition() == openGenericType)
            {
                distance = currentDistance;
                return true;
            }

            if (current.BaseType is not null)
            {
                queue.Enqueue((current.BaseType, currentDistance + 1));
            }

            Type[] interfaces = GetDirectInterfaces(current);
            for (int i = 0; i < interfaces.Length; i++)
            {
                queue.Enqueue((interfaces[i], currentDistance + 1));
            }
        }

        distance = int.MaxValue;
        return false;
    }

    private static int GetAssignableDistance(Type concreteType, Type targetType)
    {
        var queue = new Queue<(Type type, int distance)>();
        var visited = new HashSet<Type>();
        queue.Enqueue((concreteType, 0));
        while (queue.Count > 0)
        {
            (Type current, int distance) = queue.Dequeue();
            if (!visited.Add(current))
            {
                continue;
            }

            if (current == targetType)
            {
                return distance;
            }

            if (current.BaseType is not null)
            {
                queue.Enqueue((current.BaseType, distance + 1));
            }

            Type[] interfaces = GetDirectInterfaces(current);
            for (int i = 0; i < interfaces.Length; i++)
            {
                queue.Enqueue((interfaces[i], distance + 1));
            }
        }

        return int.MaxValue;
    }

    private static Type[] GetDirectInterfaces(Type type)
    {
        Type[] allInterfaces = type.GetInterfaces();
        if (allInterfaces.Length < 2 && type.BaseType is null)
        {
            return allInterfaces;
        }

        var inherited = new HashSet<Type>();
        if (type.BaseType is not null)
        {
            inherited.UnionWith(type.BaseType.GetInterfaces());
        }

        for (int i = 0; i < allInterfaces.Length; i++)
        {
            Type[] parentInterfaces = allInterfaces[i].GetInterfaces();
            for (int parentIndex = 0; parentIndex < parentInterfaces.Length; parentIndex++)
            {
                inherited.Add(parentInterfaces[parentIndex]);
            }
        }

        var direct = new List<Type>(allInterfaces.Length);
        for (int i = 0; i < allInterfaces.Length; i++)
        {
            if (!inherited.Contains(allInterfaces[i]))
            {
                direct.Add(allInterfaces[i]);
            }
        }

        return direct.ToArray();
    }
}
