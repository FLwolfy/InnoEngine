using Inno.Core.Input;

namespace Inno.Editor.HotKeys;

/// <summary>
/// Creates the default editor shortcut map in one centralized location.
/// </summary>
public static class EditorHotKeyDefaults
{
    /// <summary>
    /// Creates the standard editor shortcut bindings.
    /// </summary>
    /// <returns>A new shortcut map.</returns>
    public static EditorHotKeyMap Create()
    {
        var hotKeys = new EditorHotKeyMap();
        hotKeys.Bind(EditorHotKeyCommands.Save, HotKeyGesture.Primary(KeyCode.S));
        hotKeys.Bind(EditorHotKeyCommands.Rename, new HotKeyGesture(KeyCode.F2));
        hotKeys.Bind(EditorHotKeyCommands.Delete, new HotKeyGesture(KeyCode.Delete));
        return hotKeys;
    }
}
