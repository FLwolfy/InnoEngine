using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.Logging;

internal static class LoggingInteractionIds
{
    internal const string C_ENTRY_AREA = "panel/diagnostics.console/entry";
    internal const string C_COPY_DETAILS = "console/copy-details";
    internal const string C_COPY_MESSAGE = "console/copy-message";

    internal static EditorAreaId entryArea { get; } = new(C_ENTRY_AREA);
    internal static EditorActionId copyDetails { get; } = new(C_COPY_DETAILS);
    internal static EditorActionId copyMessage { get; } = new(C_COPY_MESSAGE);
}
