

using System;
using System.Collections.Generic;

using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Editor.Core;
using Inno.Editor.Interactions;
using static Inno.Editor.Panel.FileBrowser.FileBrowserUtility;

namespace Inno.Editor.Panel.FileBrowser;

internal sealed class FileBrowserNavigation(AssetEditorModule assets)
{
    private readonly Stack<BrowserLocation> m_backHistory = [];
    private readonly Stack<BrowserLocation> m_forwardHistory = [];

    private BrowserLocation m_historyCurrent = BrowserLocation.AssetsRoot;

    internal bool canGoBack => m_backHistory.Count > 0;

    internal bool canGoForward => m_forwardHistory.Count > 0;

    internal void NavigateTo(
        EditorContext context,
        string directory,
        string? selectedPathAfterNavigation = null)
    {
        directory = NormalizePath(directory);
        AssetBrowserRoot root = string.IsNullOrEmpty(directory)
            ? assets.browser.root
            : AssetPath.Parse(directory).source == AssetSourceId.project
                ? AssetBrowserRoot.Assets
                : AssetBrowserRoot.Plugins;
        var target = new BrowserLocation(root, directory);
        if (CurrentLocation == target)
            return;

        m_backHistory.Push(CurrentLocation);
        m_forwardHistory.Clear();
        ApplyLocation(context, target);
        if (selectedPathAfterNavigation is not null)
            assets.browser.Select(context, selectedPathAfterNavigation);
    }

    internal void NavigateToRoot(EditorContext context, AssetBrowserRoot root)
    {
        var target = new BrowserLocation(root, string.Empty);
        if (CurrentLocation == target)
            return;
        m_backHistory.Push(CurrentLocation);
        m_forwardHistory.Clear();
        ApplyLocation(context, target);
    }

    internal void SwitchRoot(EditorContext context, AssetBrowserRoot root)
    {
        if (assets.browser.root == root)
            return;
        m_backHistory.Clear();
        m_forwardHistory.Clear();
        ApplyLocation(context, new BrowserLocation(root, assets.browser.GetDirectory(root)));
    }

    internal void GoBack(EditorContext context)
    {
        if (m_backHistory.Count == 0)
            return;
        m_forwardHistory.Push(CurrentLocation);
        ApplyLocation(context, m_backHistory.Pop());
    }

    internal void GoForward(EditorContext context)
    {
        if (m_forwardHistory.Count == 0)
            return;
        m_backHistory.Push(CurrentLocation);
        ApplyLocation(context, m_forwardHistory.Pop());
    }

    private void ApplyLocation(EditorContext context, BrowserLocation location)
    {
        m_historyCurrent = location;
        assets.browser.SetLocation(location.root, location.directory);
        assets.browser.Select(context, string.Empty);
    }

    internal void OpenEntry(
        EditorContext context,
        AssetFileEntry entry,
        FileBrowserTree tree)
    {
        if (entry.isDirectory)
        {
            tree.RequestOpenTreeToPath(entry.assetPath.ToString());
            NavigateTo(context, entry.assetPath.ToString(), entry.assetPath.ToString());
            return;
        }
        _ = assets.interactions
            .For(FileBrowserInteractionIds.C_AREA, entry)
            .Execute(FileBrowserInteractionIds.C_OPEN);
    }

    internal void SyncExternalDirectoryChange(AssetBrowserRoot root, string directory)
    {
        var location = new BrowserLocation(root, NormalizePath(directory));
        if (m_historyCurrent == location)
            return;
        m_historyCurrent = location;
        m_backHistory.Clear();
        m_forwardHistory.Clear();
    }

    private BrowserLocation CurrentLocation
        => new(assets.browser.root, NormalizePath(assets.browser.currentDirectory));

    private readonly record struct BrowserLocation(
        AssetBrowserRoot root,
        string directory)
    {
        internal static BrowserLocation AssetsRoot { get; } = new(
            AssetBrowserRoot.Assets,
            string.Empty);
    }
}
