using System;
using System.Runtime.Loader;
using System.Threading;

using Inno.Assets.Core;

namespace Inno.Assets.Serialization;

/// <summary>
/// Configures runtime services required while deserializing asset references.
/// </summary>
public static class AssetSerializationServices
{
    private static Func<Guid, Guid, string, Type, string, AssetObject>? s_referenceResolver;

    /// <summary>
    /// Sets the resolver used to restore serialized asset references.
    /// </summary>
    /// <param name="referenceResolver">The resolver, or <see langword="null"/> to clear it.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when the resolver method or target belongs to a collectible assembly load context.
    /// </exception>
    public static void SetReferenceResolver(
        Func<Guid, Guid, string, Type, string, AssetObject>? referenceResolver)
    {
        if (referenceResolver is not null)
        {
            foreach (Delegate handler in referenceResolver.GetInvocationList())
            {
                if (IsCollectible(handler.Method.DeclaringType) ||
                    IsCollectible(handler.Target?.GetType()))
                {
                    throw new ArgumentException(
                        "The process-wide asset reference resolver cannot retain a collectible target or method.",
                        nameof(referenceResolver));
                }
            }
        }
        Volatile.Write(ref s_referenceResolver, referenceResolver);
    }

    internal static AssetObject ResolveReference(
        Guid persistentId,
        Guid stableTypeId,
        string lastKnownPath,
        Type expectedType,
        string propertyPath)
    {
        Func<Guid, Guid, string, Type, string, AssetObject>? referenceResolver =
            Volatile.Read(ref s_referenceResolver);
        if (referenceResolver is null)
        {
            throw new InvalidOperationException(
                "Asset reference deserialization requires an initialized AssetManager.");
        }

        return referenceResolver(
            persistentId,
            stableTypeId,
            lastKnownPath,
            expectedType,
            propertyPath);
    }

    private static bool IsCollectible(Type? type)
        => type is not null &&
           AssemblyLoadContext.GetLoadContext(type.Assembly) is { IsCollectible: true };
}
