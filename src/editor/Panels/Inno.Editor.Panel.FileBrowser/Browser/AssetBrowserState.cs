
using System;

using Inno.Assets.Core;
using Inno.Editor.Core;
using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.FileBrowser;

/// <summary>Identifies the authoring or installed-content root displayed by the Asset Browser.</summary>
public enum AssetBrowserRoot
{
    /// <summary>The writable project <c>Assets</c> authoring root.</summary>
    Assets,

    /// <summary>The read-only <c>Plugins</c> installation root.</summary>
    Plugins
}

/// <summary>Stores asset browser navigation independently from global object selection.</summary>
public sealed class AssetBrowserState
{
    private readonly EditorInteractions m_interactions;
    private string m_assetsDirectory = string.Empty;
    private string m_pluginsDirectory = string.Empty;

    /// <summary>Creates Asset Browser navigation state.</summary>
    /// <param name="interactions">The active editor interaction entry point.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="interactions"/> is <see langword="null"/>.</exception>
    public AssetBrowserState(EditorInteractions interactions)
    {
        m_interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
    }

    /// <summary>Gets the root currently displayed by the Asset Browser.</summary>
    public AssetBrowserRoot root { get; private set; }

    /// <summary>
    /// Gets the current directory inside <see cref="root"/>. An empty value identifies that root's overview.
    /// </summary>
    public string currentDirectory => root == AssetBrowserRoot.Assets
        ? m_assetsDirectory
        : m_pluginsDirectory;

    /// <summary>
    /// Gets the most recently visited writable project directory, independently of the displayed root.
    /// </summary>
    public string projectDirectory => m_assetsDirectory;

    /// <summary>
    /// Gets the selected asset path when the editor-wide target belongs to the Asset Browser.
    /// </summary>
    /// <param name="context">The shared editor context containing the global selection.</param>
    /// <returns>The normalized source-relative path, or <see langword="null"/> when another target type is selected.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is <see langword="null"/>.</exception>
    public string? GetSelectedPath(EditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return (m_interactions.selection.selectedTarget as Inno.Assets.File.AssetFileEntry)?.assetPath.ToString();
    }

    /// <summary>
    /// Switches the displayed root while preserving the last directory visited in each root.
    /// </summary>
    /// <param name="value">The authoring or installed-content root to display.</param>
    public void SetRoot(AssetBrowserRoot value)
        => root = value;

    /// <summary>
    /// Sets the current Asset Browser directory and infers its root from the isolated source identity.
    /// </summary>
    /// <param name="relativePath">
    /// The isolated source-relative directory path, or an empty value for the currently displayed root.
    /// </param>
    public void SetCurrentDirectory(string relativePath)
    {
        string normalized = Normalize(relativePath);
        if (string.IsNullOrEmpty(normalized))
        {
            SetDirectory(root, string.Empty);
            return;
        }

        AssetPath path = AssetPath.Parse(normalized);
        AssetBrowserRoot targetRoot = path.source == AssetSourceId.project
            ? AssetBrowserRoot.Assets
            : AssetBrowserRoot.Plugins;
        SetDirectory(targetRoot, normalized);
        root = targetRoot;
    }

    /// <summary>
    /// Selects an asset path through the editor-wide selection state, or clears Asset selection.
    /// </summary>
    /// <param name="context">The shared editor context whose selection should be updated.</param>
    /// <param name="relativePath">The source-relative path to select, or an empty value to clear selection.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is <see langword="null"/>.</exception>
    public void Select(EditorContext context, string? relativePath)
    {
        ArgumentNullException.ThrowIfNull(context);
        object? target = null;
        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            if (Inno.Assets.AssetManager.TryGetFileSystemEntry(
                    Inno.Assets.Core.AssetPath.Parse(Normalize(relativePath)),
                    out Inno.Assets.File.AssetFileEntry entry))
            {
                target = entry;
            }
        }
        _ = m_interactions.For(FileBrowserInteractionIds.C_AREA, target).Select();
    }

    internal string GetDirectory(AssetBrowserRoot targetRoot)
        => targetRoot == AssetBrowserRoot.Assets
            ? m_assetsDirectory
            : m_pluginsDirectory;

    internal void Restore(
        AssetBrowserRoot restoredRoot,
        string assetsDirectory,
        string pluginsDirectory)
    {
        m_assetsDirectory = Normalize(assetsDirectory);
        m_pluginsDirectory = Normalize(pluginsDirectory);
        root = restoredRoot;
    }

    internal void SetLocation(AssetBrowserRoot targetRoot, string directory)
    {
        string normalized = Normalize(directory);
        if (!string.IsNullOrEmpty(normalized))
        {
            AssetPath path = AssetPath.Parse(normalized);
            bool isProject = path.source == AssetSourceId.project;
            if ((targetRoot == AssetBrowserRoot.Assets) != isProject)
            {
                throw new ArgumentException(
                    $"Directory '{directory}' does not belong to the '{targetRoot}' root.",
                    nameof(directory));
            }
        }

        SetDirectory(targetRoot, normalized);
        root = targetRoot;
    }

    private void SetDirectory(AssetBrowserRoot targetRoot, string directory)
    {
        if (targetRoot == AssetBrowserRoot.Assets)
            m_assetsDirectory = directory;
        else
            m_pluginsDirectory = directory;
    }

    private static string Normalize(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return string.Empty;
        return AssetPath.Parse(relativePath.Replace('\\', '/').Trim()).ToString();
    }
}
