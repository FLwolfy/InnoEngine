using System;

using Inno.Assets;
using Inno.Core.Serialization;

namespace Inno.Scene.Assets;

internal sealed class EngineResourceEnvelope : ISerializable
{
    internal const string C_SCENE_KIND = "scene";
    internal const string C_PREFAB_KIND = "prefab";

    [SerializableProperty]
    internal string resourceKind { get; set; } = string.Empty;

    [SerializableProperty]
    internal byte[] payload { get; set; } = [];

    [SerializableProperty]
    internal AssetDependency[] dependencies { get; set; } = [];

    internal void Validate(string expectedKind)
    {
        if (!string.Equals(resourceKind, expectedKind, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Engine resource kind '{resourceKind}' does not match expected kind '{expectedKind}'.");
        }
        if (payload.Length == 0)
            throw new InvalidOperationException($"Engine resource '{resourceKind}' has an empty graph payload.");
    }
}
