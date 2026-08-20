using Inno.Core.Scripting;
using Inno.Editor.Interactions;
using Inno.Editor.Interactions.Actions;
using Inno.Editor.Interactions.DragDrop;
using Inno.Editor.Interactions.Menus;
using Inno.Editor.Interactions.Selection;

[assembly: ScriptingApiNamespace(
    "InnoEditor.Interactions",
    "Inno.Editor.Interactions",
    ScriptingApiScope.Editor)]
[assembly: ScriptingApiNamespace(
    "InnoEditor.Interactions",
    "Inno.Editor.Interactions.Selection",
    ScriptingApiScope.Editor)]
[assembly: ScriptingApiNamespace(
    "InnoEditor.Actions",
    "Inno.Editor.Interactions.Actions",
    ScriptingApiScope.Editor)]
[assembly: ScriptingApiNamespace(
    "InnoEditor.Menus",
    "Inno.Editor.Interactions.Menus",
    ScriptingApiScope.Editor)]
[assembly: ScriptingApiNamespace(
    "InnoEditor.DragDrop",
    "Inno.Editor.Interactions.DragDrop",
    ScriptingApiScope.Editor)]

[assembly: ScriptingApiExport(typeof(EditorInteractions), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorInteraction), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorAreas), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorSelectionState), ScriptingApiScope.Editor)]

[assembly: ScriptingApiExport(typeof(EditorAction), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorAction<>), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorActionAttribute), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorActionContext), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorActionContext<>), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorActionState), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorActions), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorShortcutAttribute), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorValidationResult), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(HotKeyGesture), ScriptingApiScope.Editor)]

[assembly: ScriptingApiExport(typeof(EditorMenuAttribute), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorMenuBuilder), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorMenuContext), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorMenuItem), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorMenuModel), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorMenuSource), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorMenuSourceAttribute), ScriptingApiScope.Editor)]

[assembly: ScriptingApiExport(typeof(EditorDragData), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorDragContext), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorDrop), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorDrop<,>), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorDropAttribute), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorDropContext), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorDropContext<,>), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorDropPlacement), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorDropResult), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorDropStatus), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorDropVisual), ScriptingApiScope.Editor)]
