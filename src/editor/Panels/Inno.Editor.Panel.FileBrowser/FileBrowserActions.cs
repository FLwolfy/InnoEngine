namespace Inno.Editor.Panel.FileBrowser;

/// <summary>Defines stable action identifiers owned by the File Browser panel.</summary>
public static class FileBrowserActions
{
    /// <summary>Creates a tracked asset folder.</summary>
    public const string CreateFolder = "file-browser/create-folder";

    /// <summary>Begins inline renaming for the selected source entry.</summary>
    public const string Rename = "file-browser/rename";

    /// <summary>Deletes the selected source entry through the Asset database.</summary>
    public const string Delete = "file-browser/delete";
}
