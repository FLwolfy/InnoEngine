using System;
using System.IO;

using Inno.Core.Serialization;
using Inno.Core.Serialization.Converters;

namespace Inno.Engine.Scene;

[SerializationExtension]
internal sealed class GameComponentReferenceConverter : SerializationConverter<GameComponent>
{
    public override void Write(SerializationWriter writer, GameComponent value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        EngineReferenceToken token = SceneGraphReferenceMap.current.Capture(value, writer.path);
        writer.Write("kind", (int)token.kind);
        writer.Write("sourceId", token.sourceId);
    }

    public override GameComponent Read(SerializationReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var token = new EngineReferenceToken(
            (EngineReferenceKind)reader.Read<int>("kind"),
            reader.Read<Guid>("sourceId"));
        if (token.kind != EngineReferenceKind.GameComponent)
            throw new InvalidDataException($"Reference token at '{reader.path}' is not a GameComponent reference.");
        return (GameComponent)SceneGraphReferenceMap.current.Resolve(token, reader.valueType, reader.path);
    }
}
