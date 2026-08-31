using Inno.Core.Serialization;
using Inno.Engine.Scene;

namespace Inno.Engine.Scene.Assets;

[RequiresSerializationConverter]
internal sealed class SceneSubtreeState(GameObject root) : ISerializable
{
    internal GameObject root { get; } = root;
}
