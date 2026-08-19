using System;

namespace Inno.Editor.Assets.AssetEditors;

internal static class AssetTypeDistance
{
    internal static bool TryGet(Type concreteType, Type candidateType, out int distance)
    {
        if (!candidateType.IsAssignableFrom(concreteType))
        {
            distance = int.MaxValue;
            return false;
        }

        if (concreteType == candidateType)
        {
            distance = 0;
            return true;
        }

        distance = 1;
        Type? current = concreteType.BaseType;
        while (current is not null)
        {
            if (current == candidateType)
                return true;
            distance++;
            current = current.BaseType;
        }

        distance = candidateType.IsInterface ? 1 : distance;
        return true;
    }
}
