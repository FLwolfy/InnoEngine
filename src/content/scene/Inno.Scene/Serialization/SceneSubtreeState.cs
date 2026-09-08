using Inno.Core.Serialization;
using Inno.Scene;

namespace Inno.Scene;

[RequiresSerializationConverter]
internal sealed class SceneSubtreeState(GameObject root) : ISerializable
{
    internal GameObject root { get; } = root;
}
