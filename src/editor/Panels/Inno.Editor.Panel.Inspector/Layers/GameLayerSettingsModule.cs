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
    private readonly GameLayerDiagnosticPublisher m_diagnostics = new();
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
    /// Saves the current layer catalog through the Asset database.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the canonical settings asset cannot be exported.
    /// </exception>
    internal void Save()
    {
        GameLayerSettingsAsset current = settings;
        bool saved = AssetManager.Save(GameLayerSettingsAsset.defaultPath, current);
        if (!saved)
            throw new InvalidOperationException("The project layer settings could not be saved.");
        if (AssetManager.TryLoad(
                GameLayerSettingsAsset.defaultPath,
                out GameLayerSettingsAsset? persisted) &&
            persisted is not null)
        {
            SetSettings(persisted);
        }
    }

    /// <summary>
    /// Determines whether an asset occupies the only project path recognized by layer consumers.
    /// </summary>
    /// <param name="settings">The layer-settings asset to inspect.</param>
    /// <returns><see langword="true"/> when the asset is stored at the canonical Settings path.</returns>
    internal bool IsCanonical(GameLayerSettingsAsset settings)
        => settings is not null && IsDefaultPath(settings.sourcePath);

    /// <inheritdoc />
    protected override void OnStart(EditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ResolveInitialSettings();
        AssetManager.Changed += OnAssetsChanged;
        AssetManager.AssetReloaded += OnAssetReloaded;
    }

    /// <inheritdoc />
    protected override void OnUpdate(EditorContext context)
    {
        m_diagnostics.Refresh(settings.layerStack);
    }

    /// <inheritdoc />
    protected override void OnStop(EditorContext context)
    {
        AssetManager.Changed -= OnAssetsChanged;
        AssetManager.AssetReloaded -= OnAssetReloaded;
        m_diagnostics.Clear();
        m_settings = null;
    }

    private void OnAssetsChanged(AssetChangeSet changeSet)
    {
        for (int i = 0; i < changeSet.changes.Count; i++)
        {
            AssetChange change = changeSet.changes[i];
            if (!IsDefaultPath(change.relativePath) &&
                !IsDefaultPath(change.oldRelativePath))
                continue;

            ResolveCanonicalSettings();
            return;
        }
    }

    private void OnAssetReloaded(AssetObject asset)
    {
        if (asset is not GameLayerSettingsAsset settings)
            return;
        if (IsDefaultPath(settings.sourcePath))
        {
            SetSettings(settings);
        }
    }

    private void ResolveInitialSettings()
        => ResolveCanonicalSettings();

    private void ResolveCanonicalSettings()
    {
        if (AssetManager.TryLoad(
                GameLayerSettingsAsset.defaultPath,
                out GameLayerSettingsAsset? defaultSettings) &&
            defaultSettings is not null)
        {
            SetSettings(defaultSettings);
            return;
        }
        SetSettings(GameLayerSettingsAsset.CreateDefault());
    }

    private void SetSettings(GameLayerSettingsAsset settings)
    {
        m_settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    private static bool IsDefaultPath(string? path)
        => string.Equals(
            path,
            GameLayerSettingsAsset.defaultPath,
            StringComparison.OrdinalIgnoreCase);
}
