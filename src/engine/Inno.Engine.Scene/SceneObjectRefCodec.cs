using System;

using Inno.Core.Identity;
using Inno.Core.Serialization;

namespace Inno.Engine.Scene;

internal sealed class SceneObjectRefCodec<TObject> : SerializationCodec<SceneObjectRef<TObject>>
    where TObject : class, IIdentityObject
{
    public override bool CanHandleType(Type declaredType)
    {
        return (Nullable.GetUnderlyingType(declaredType) ?? declaredType) == typeof(SceneObjectRef<TObject>);
    }

    public override object? OnSerialize(in SerializeContext context, SceneObjectRef<TObject> value)
    {
        return value.persistentId;
    }

    public override SceneObjectRef<TObject> OnDeserialize(in DeserializeContext context, object? node)
    {
        Guid persistentId = node switch
        {
            Guid guid => guid,
            string text when Guid.TryParse(text, out Guid parsed) => parsed,
            null => Guid.Empty,
            _ => throw new InvalidOperationException("Scene reference node must contain a Guid.")
        };

        return new SceneObjectRef<TObject>(persistentId);
    }
}
