using System;

using Inno.Assets;
using Inno.Assets.Core;
using Inno.Editor.Core;
using Inno.Engine.Scene.Assets;

namespace Inno.Editor.Panel.Inspector;

/// <summary>
/// Owns the canonical project layer-settings asset used by Inspector controls.
/// </summary>
[EditorModule(order: 205)]
internal sealed class GameLayerSettingsModule : EditorModule
{
    private GameLayerSettingsAsset? m_settings;

    /// <summary>
    /// Gets the canonical project layer-settings asset.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown before the module has started or when the canonical asset is unavailable.
    /// </exception>
    internal GameLayerSettingsAsset settings => m_settings
        ?? throw new InvalidOperationException("Project layer settings are not available.");

    /// <summary>
    /// Saves the current layer catalog and interaction matrix through the Asset database.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the canonical settings asset cannot be exported.
    /// </exception>
    internal void Save()
    {
        if (!AssetManager.Save(settings))
            throw new InvalidOperationException("The project layer settings could not be saved.");
    }

    /// <inheritdoc />
    protected override void OnStart(EditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!AssetManager.TryLoad(
                GameLayerSettingsAsset.defaultPath,
                out GameLayerSettingsAsset? existing) ||
            existing is null)
        {
            GameLayerSettingsAsset created = GameLayerSettingsAsset.CreateDefault();
            if (!AssetManager.Save(GameLayerSettingsAsset.defaultPath, created))
            {
                throw new InvalidOperationException(
                    $"Failed to create project layer settings at '{GameLayerSettingsAsset.defaultPath}'.");
            }
            existing = AssetManager.Load<GameLayerSettingsAsset>(GameLayerSettingsAsset.defaultPath);
        }
        m_settings = existing;
        AssetManager.AssetReloaded += OnAssetReloaded;
    }

    /// <inheritdoc />
    protected override void OnStop(EditorContext context)
    {
        AssetManager.AssetReloaded -= OnAssetReloaded;
        m_settings = null;
    }

    private void OnAssetReloaded(AssetObject asset)
    {
        if (asset is GameLayerSettingsAsset settings &&
            string.Equals(
                settings.sourcePath,
                GameLayerSettingsAsset.defaultPath,
                StringComparison.OrdinalIgnoreCase))
        {
            m_settings = settings;
        }
    }
}
