using System;

using Inno.Core.Identity;
using Inno.Core.Serialization;

namespace Inno.Assets.Core;

internal sealed class AssetRefCodec<TAsset> : SerializationCodec<AssetRef<TAsset>>
    where TAsset : AssetObject
{
    public override bool CanHandleType(Type declaredType)
    {
        return (Nullable.GetUnderlyingType(declaredType) ?? declaredType) == typeof(AssetRef<TAsset>);
    }

    public override object? OnSerialize(in SerializeContext context, AssetRef<TAsset> value)
    {
        return value.identity.persistentId;
    }

    public override AssetRef<TAsset> OnDeserialize(in DeserializeContext context, object? node)
    {
        Guid persistentId = node switch
        {
            Guid guid => guid,
            string text when Guid.TryParse(text, out Guid parsed) => parsed,
            null => Guid.Empty,
            _ => throw new InvalidOperationException("Asset reference node must contain a Guid.")
        };

        return persistentId == Guid.Empty
            ? default
            : new AssetRef<TAsset>(new Identity(persistentId));
    }
}
