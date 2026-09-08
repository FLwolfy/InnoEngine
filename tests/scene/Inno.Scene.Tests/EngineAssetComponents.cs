using Inno.Assets;
using Inno.Extensibility.Types;
using Inno.Core.Serialization;
using Inno.Scene;

namespace Inno.Scene.Tests;

[StableTypeId("cc867fe9-866c-4a19-ac69-5e561b20a874")]
internal sealed class EngineAssetReferenceComponent : GameComponent
{
    [SerializableProperty]
    public TextAsset? asset { get; set; }
}

[StableTypeId("b4ada657-2b58-4929-b1c0-616b54bb0939")]
internal sealed class EngineObjectReferenceComponent : GameComponent
{
    [SerializableProperty]
    public GameObject? targetObject { get; set; }

    [SerializableProperty]
    public int value { get; set; }
}
