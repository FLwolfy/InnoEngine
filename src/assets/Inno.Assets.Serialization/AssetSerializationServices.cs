using System;
using System.Runtime.Loader;
using System.Threading;

using Inno.Assets.Core;
using Inno.Core.Scripting;

namespace Inno.Assets.Serialization;

/// <summary>
/// Configures runtime services required while deserializing asset references.
/// </summary>
public static class AssetSerializationServices
{
    private static readonly AsyncLocal<Func<Guid, Guid, string, Type, string, AssetObject>?> S_SCOPED_RESOLVER = new();
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
        ValidateResolver(referenceResolver, nameof(referenceResolver));
        Volatile.Write(ref s_referenceResolver, referenceResolver);
    }

    /// <summary>
    /// Temporarily selects an asset-reference resolver for the current asynchronous operation.
    /// </summary>
    /// <param name="referenceResolver">Resolver that owns the isolated asset generation being processed.</param>
    /// <returns>A scope that restores the previous resolver when disposed.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="referenceResolver"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the resolver retains a collectible assembly generation.
    /// </exception>
    [ScriptingApiIgnore]
    public static IDisposable PushReferenceResolver(
        Func<Guid, Guid, string, Type, string, AssetObject> referenceResolver)
    {
        ArgumentNullException.ThrowIfNull(referenceResolver);
        ValidateResolver(referenceResolver, nameof(referenceResolver));
        Func<Guid, Guid, string, Type, string, AssetObject>? previous = S_SCOPED_RESOLVER.Value;
        S_SCOPED_RESOLVER.Value = referenceResolver;
        return new ReferenceResolverScope(referenceResolver, previous);
    }

    internal static AssetObject ResolveReference(
        Guid persistentId,
        Guid stableTypeId,
        string lastKnownPath,
        Type expectedType,
        string propertyPath)
    {
        Func<Guid, Guid, string, Type, string, AssetObject>? referenceResolver =
            S_SCOPED_RESOLVER.Value ?? Volatile.Read(ref s_referenceResolver);
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

    private static void ValidateResolver(
        Func<Guid, Guid, string, Type, string, AssetObject>? referenceResolver,
        string parameterName)
    {
        if (referenceResolver is null)
            return;
        foreach (Delegate handler in referenceResolver.GetInvocationList())
        {
            if (!IsCollectible(handler.Method.DeclaringType) && !IsCollectible(handler.Target?.GetType()))
                continue;
            throw new ArgumentException(
                "An asset reference resolver cannot retain a collectible target or method.",
                parameterName);
        }
    }

    private sealed class ReferenceResolverScope(
        Func<Guid, Guid, string, Type, string, AssetObject> current,
        Func<Guid, Guid, string, Type, string, AssetObject>? previous) : IDisposable
    {
        private int m_disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
                return;
            if (!ReferenceEquals(S_SCOPED_RESOLVER.Value, current))
            {
                throw new InvalidOperationException(
                    "Asset reference resolver scopes must be disposed in reverse creation order.");
            }
            S_SCOPED_RESOLVER.Value = previous;
        }
    }
}
