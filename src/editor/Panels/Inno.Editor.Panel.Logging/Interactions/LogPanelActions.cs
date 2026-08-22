namespace Inno.Editor.Panel.Logging;

/// <summary>
/// Defines stable actions owned by the Log panel.
/// </summary>
public static class LogPanelActions
{
    /// <summary>
    /// Copies only the rendered message of one console entry.
    /// </summary>
    public const string CopyMessage = "log/copy-message";

    /// <summary>
    /// Copies the complete formatted console entry and source location.
    /// </summary>
    public const string CopyDetails = "log/copy-details";
}
