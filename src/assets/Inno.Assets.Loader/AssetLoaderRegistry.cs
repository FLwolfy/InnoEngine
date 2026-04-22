using System;
using System.Collections.Generic;

using Inno.Core.Reflection;

namespace Inno.Assets.Loader;

/// <summary>
/// Maintains a registry of IAssetLoader implementations for different asset types.
/// </summary>
internal static class AssetLoaderRegistry
{
    private static readonly Dictionary<Type, IAssetLoader> LOADERS = new();
    private static readonly Dictionary<string, IAssetLoader> EXTENSION_LOADERS = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes the registry by scanning for IAssetLoader implementations.
    /// </summary>
    [TypeCacheInitialize]
    private static void RegisterAll()
    {
        LOADERS.Clear();
        EXTENSION_LOADERS.Clear();
        
        foreach (var type in TypeCacheManager.GetTypesImplementing<IAssetLoader>())
        {
            Register(type);
        }
    }

    /// <summary>
    /// Registers a loader type.
    /// </summary>
    private static void Register(Type type)
    {
        if (type.IsAbstract || type.IsInterface)
            return;

        var baseType = type.BaseType;
        if (baseType!.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(InnoAssetLoader<>))
        {
            if (Activator.CreateInstance(type) is IAssetLoader instance)
            {
                var genericArg = baseType.GetGenericArguments()[0];
                LOADERS[genericArg] = instance;
                
                var prop = type.GetProperty("validExtensions");
                if (prop != null && prop.GetValue(instance) is string[] extensions)
                {
                    foreach (var ext in extensions)
                    {
                        EXTENSION_LOADERS[ext] = instance;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Tries to get a loader for the specified asset type.
    /// </summary>
    internal static bool TryGetLoader(Type assetType, out IAssetLoader? loader)
    {
        return LOADERS.TryGetValue(assetType, out loader);
    }
    
    /// <summary>
    /// Tries to get a loader for the specified file extensions.
    /// </summary>
    internal static bool TryGetLoader(string extension, out IAssetLoader? loader)
    {
        return EXTENSION_LOADERS.TryGetValue(extension, out loader);
    }
}