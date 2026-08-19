using Inno.Core.Scripting;
using Inno.Editor.Core;
using Inno.Editor.Core.Commands;
using Inno.Editor.Core.DragDrop;
using Inno.Editor.Core.Menus;
using Inno.Editor.Core.Panels;

[assembly: ScriptingApiNamespace("InnoEditor.Core", "Inno.Editor.Core", ScriptingApiScope.Editor)]
[assembly: ScriptingApiNamespace("InnoEditor.Commands", "Inno.Editor.Core.Commands", ScriptingApiScope.Editor)]
[assembly: ScriptingApiNamespace("InnoEditor.Menus", "Inno.Editor.Core.Menus", ScriptingApiScope.Editor)]
[assembly: ScriptingApiNamespace("InnoEditor.DragDrop", "Inno.Editor.Core.DragDrop", ScriptingApiScope.Editor)]
[assembly: ScriptingApiNamespace("InnoEditor.Panels", "Inno.Editor.Core.Panels", ScriptingApiScope.Editor)]

[assembly: ScriptingApiExport(typeof(EditorContext), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorSelectionState), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorValidationResult), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorSurface), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorModule), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorModuleAttribute), ScriptingApiScope.Editor)]

[assembly: ScriptingApiExport(typeof(EditorAction), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorAction<>), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorActionAttribute), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorActionContext), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorActionContext<>), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorActionIds), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorActionState), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorShortcutAttribute), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(HotKeyGesture), ScriptingApiScope.Editor)]

[assembly: ScriptingApiExport(typeof(EditorMenuAttribute), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorMenuSource), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorMenuSourceAttribute), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorMenuContext), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorMenuBuilder), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorMenuPlacement), ScriptingApiScope.Editor)]

[assembly: ScriptingApiExport(typeof(EditorDragData), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorDragContext), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorDrop), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorDrop<,>), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorDropContext), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorDropContext<,>), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorDropAttribute), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorDropPlacement), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorDropStatus), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorDropResult), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorDropVisual), ScriptingApiScope.Editor)]

[assembly: ScriptingApiExport(typeof(EditorPanel), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorPanelAttribute), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorModal), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(EditorModalAttribute), ScriptingApiScope.Editor)]
[assembly: ScriptingApiExport(typeof(IEditorPanelReloadState), ScriptingApiScope.Editor)]
