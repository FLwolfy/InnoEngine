using System;
using System.IO;

using Inno.Core.Serialization;
using Inno.Core.Serialization.Converters;
using Inno.Engine.Scene;

namespace Inno.Engine.Scene.Assets;

[SerializationExtension]
internal sealed class GameSystemReferenceConverter : SerializationConverter<GameSystem>
{
    public override void Write(SerializationWriter writer, GameSystem value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        EngineReferenceToken token = SceneGraphReferenceMap.current.Capture(value, writer.path);
        writer.Write("kind", (int)token.kind);
        writer.Write("sourceId", token.sourceId);
    }

    public override GameSystem Read(SerializationReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var token = new EngineReferenceToken(
            (EngineReferenceKind)reader.Read<int>("kind"),
            reader.Read<Guid>("sourceId"));
        if (token.kind != EngineReferenceKind.GameSystem)
            throw new InvalidDataException($"Reference token at '{reader.path}' is not a GameSystem reference.");
        return (GameSystem)SceneGraphReferenceMap.current.Resolve(token, reader.valueType, reader.path);
    }
}
