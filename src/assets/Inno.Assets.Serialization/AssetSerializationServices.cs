using System;
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
    public static void SetReferenceResolver(
        Func<Guid, Guid, string, Type, string, AssetObject>? referenceResolver)
        => Volatile.Write(ref s_referenceResolver, referenceResolver);

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
}
