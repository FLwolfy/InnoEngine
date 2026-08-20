using System;
using System.Collections.Generic;

namespace Inno.Editor.Interactions;

internal static class EditorTypeDistance
{
    internal static bool TryGet(Type actualType, Type registeredType, out int distance)
    {
        if (actualType == registeredType)
        {
            distance = 0;
            return true;
        }
        if (!registeredType.IsAssignableFrom(actualType))
        {
            distance = int.MaxValue;
            return false;
        }

        var visited = new HashSet<Type> { actualType };
        var queue = new Queue<(Type Type, int Distance)>();
        queue.Enqueue((actualType, 0));
        while (queue.Count > 0)
        {
            (Type type, int currentDistance) = queue.Dequeue();
            Type? baseType = type.BaseType;
            if (baseType is not null && visited.Add(baseType))
            {
                if (baseType == registeredType)
                {
                    distance = currentDistance + 1;
                    return true;
                }
                queue.Enqueue((baseType, currentDistance + 1));
            }
            foreach (Type interfaceType in type.GetInterfaces())
            {
                if (!visited.Add(interfaceType))
                    continue;
                if (interfaceType == registeredType)
                {
                    distance = currentDistance + 1;
                    return true;
                }
                queue.Enqueue((interfaceType, currentDistance + 1));
            }
        }

        distance = int.MaxValue;
        return false;
    }
}
