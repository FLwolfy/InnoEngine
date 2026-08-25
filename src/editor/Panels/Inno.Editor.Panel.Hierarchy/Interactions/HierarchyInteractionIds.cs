using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.Hierarchy;

internal static class HierarchyInteractionIds
{
    internal const string C_AREA = "panel/scene.hierarchy";
    internal const string C_FILE_BROWSER_AREA = "panel/asset.file-browser";
    internal const string C_MAIN_MENU_AREA = "editor/main-menu";
    internal const string C_CREATE_CHILD = "hierarchy/create-child-game-object";
    internal const string C_CREATE_GAME_OBJECT = "hierarchy/create-game-object";
    internal const string C_CREATE_SCENE = "hierarchy/create-scene";
    internal const string C_DELETE_GAME_OBJECT = "hierarchy/delete-game-object";
    internal const string C_DELETE_SCENE = "hierarchy/delete-scene";
    internal const string C_OPEN = "editor/open";
    internal const string C_RENAME = "hierarchy/rename";
    internal const string C_SAVE = "editor/save";
    internal const string C_SET_ACTIVE_SCENE = "hierarchy/set-active-scene";

    internal static EditorAreaId area { get; } = new(C_AREA);
    internal static EditorActionId createChild { get; } = new(C_CREATE_CHILD);
    internal static EditorActionId createGameObject { get; } = new(C_CREATE_GAME_OBJECT);
    internal static EditorActionId createScene { get; } = new(C_CREATE_SCENE);
    internal static EditorActionId deleteGameObject { get; } = new(C_DELETE_GAME_OBJECT);
    internal static EditorActionId deleteScene { get; } = new(C_DELETE_SCENE);
    internal static EditorActionId open { get; } = new(C_OPEN);
    internal static EditorActionId rename { get; } = new(C_RENAME);
    internal static EditorActionId save { get; } = new(C_SAVE);
    internal static EditorActionId setActiveScene { get; } = new(C_SET_ACTIVE_SCENE);
}
