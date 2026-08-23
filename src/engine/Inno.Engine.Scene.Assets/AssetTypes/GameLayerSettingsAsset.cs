using System;
using System.Text.Json;

using Inno.Assets.Core;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Engine.Scene.Layers;

namespace Inno.Engine.Scene.Assets;

/// <summary>
/// Stores the project layer catalog and symmetric layer-interaction matrix.
/// </summary>
[StableTypeId("cb67dfdd-a692-4b24-adf7-9feb50fd34ee")]
public sealed class GameLayerSettingsAsset : AssetObject
{
    /// <summary>
    /// Defines the canonical source-relative path used by Editor and runtime project tooling.
    /// </summary>
    public const string defaultPath = "Settings/GameLayers.ilayers";

    private static readonly JsonSerializerOptions S_JSON_OPTIONS = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Creates a settings asset containing the built-in default layer.
    /// </summary>
    public GameLayerSettingsAsset()
    {
        layerStack = new LayerStack();
    }

    private GameLayerSettingsAsset(LayerStack layerStack)
    {
        this.layerStack = layerStack ?? throw new ArgumentNullException(nameof(layerStack));
    }

    /// <summary>
    /// Gets the editable layer catalog and interaction matrix owned by this asset.
    /// </summary>
    [SerializableProperty]
    public LayerStack layerStack { get; private set; }

    /// <summary>
    /// Creates a detached settings asset containing only the built-in default configuration.
    /// </summary>
    /// <returns>A new unsaved project layer settings asset.</returns>
    public static GameLayerSettingsAsset CreateDefault() => new();

    internal byte[] ExportSource()
    {
        var document = new SourceDocument
        {
            layers = layerStack.CaptureNames(),
            interactionMasks = layerStack.CaptureInteractionMasks()
        };
        return JsonSerializer.SerializeToUtf8Bytes(document, S_JSON_OPTIONS);
    }

    internal static GameLayerSettingsAsset Import(ReadOnlySpan<byte> sourceBytes)
    {
        SourceDocument document = JsonSerializer.Deserialize<SourceDocument>(sourceBytes, S_JSON_OPTIONS)
            ?? throw new InvalidOperationException("Game layer settings source is empty.");
        if (document.layers is null || document.interactionMasks is null)
            throw new InvalidOperationException("Game layer settings must declare layers and interaction masks.");
        return new GameLayerSettingsAsset(LayerStack.Restore(document.layers, document.interactionMasks));
    }

    private sealed class SourceDocument
    {
        public string?[]? layers { get; set; }
        public uint[]? interactionMasks { get; set; }
    }
}
