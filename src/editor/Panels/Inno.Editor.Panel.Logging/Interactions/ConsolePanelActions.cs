namespace Inno.Editor.Panel.Logging;

/// <summary>
/// Defines stable actions owned by the Console panel.
/// </summary>
public static class ConsolePanelActions
{
    /// <summary>
    /// Copies only the rendered message of one console entry.
    /// </summary>
    public const string CopyMessage = "console/copy-message";

    /// <summary>
    /// Copies the complete formatted console entry and source location.
    /// </summary>
    public const string CopyDetails = "console/copy-details";
}
