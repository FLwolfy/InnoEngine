using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.FileBrowser;

internal static class FileBrowserInteractionIds
{
    internal const string C_AREA = "panel/asset.file-browser";
    internal const string C_CREATE_FOLDER = "file-browser/create-folder";
    internal const string C_DELETE = "file-browser/delete";
    internal const string C_OPEN = "editor/open";
    internal const string C_RENAME = "file-browser/rename";

    internal static EditorAreaId area { get; } = new(C_AREA);
    internal static EditorActionId createFolder { get; } = new(C_CREATE_FOLDER);
    internal static EditorActionId delete { get; } = new(C_DELETE);
    internal static EditorActionId open { get; } = new(C_OPEN);
    internal static EditorActionId rename { get; } = new(C_RENAME);
}
