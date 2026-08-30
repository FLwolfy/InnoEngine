namespace Inno.Editor.Panel.Logging;

internal readonly record struct EditorConsoleEntryId(
    EditorConsoleEntryKind kind,
    long value);
