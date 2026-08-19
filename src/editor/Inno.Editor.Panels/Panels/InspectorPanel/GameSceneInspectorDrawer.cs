using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;

using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Editor.ImGui;
using Inno.Editor.Inspection;
using Inno.Engine.Scene;
using Inno.Native.ImGui;
using Inno.Platform.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panels.Inspectors;

[InspectorDrawer(typeof(GameScene))]
internal sealed class GameSceneInspectorDrawer : IInspectorDrawer
{
    private const nuint C_NAME_BUFFER_SIZE = 512;
    private const nuint C_SEARCH_BUFFER_SIZE = 256;

    private readonly InspectorCardControls m_cardControls = new();
    private string m_systemSearch = string.Empty;

    /// <inheritdoc />
    public void Draw(InspectorDrawContext context)
    {
        var scene = (GameScene)context.target;
        if (!scene.isLoaded || scene.isDestroyed)
        {
            context.editorContext.selection.Clear();
            NativeImGui.TextUnformatted("Selected scene is no longer loaded.");
            return;
        }

        NativeImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4f, 2f));
        NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4f, 1f));
        try
        {
            string name = scene.name;
            NativeImGui.SetNextItemWidth(-1f);
            if (NativeImGui.InputText(
                    $"##scene_name_{scene.identity.persistentId:N}",
                    ref name,
                    C_NAME_BUFFER_SIZE,
                    ImGuiInputTextFlags.None))
                scene.name = name;
            NativeImGui.Spacing();
            DrawSystems(context, scene);
            DrawAddSystem(scene);
        }
        finally
        {
            NativeImGui.PopStyleVar(2);
        }
    }

    private void DrawSystems(InspectorDrawContext context, GameScene scene)
    {
        GameSystem? systemToRemove = null;
        IReadOnlyList<GameSystem> systems = scene.GetSystems();
        for (int i = 0; i < systems.Count; i++)
        {
            GameSystem system = systems[i];
            string systemId = system.identity.persistentId.ToString("N");
            bool open = ImGuiWidget.CollapsingCard(
                systemId,
                system.GetType().Name,
                () =>
                {
                    bool enabled = system.enabled;
                    if (ImGuiWidget.CompactCheckbox($"enabled_{systemId}", ref enabled))
                        system.enabled = enabled;
                },
                () => m_cardControls.DrawSystem(
                    scene,
                    system,
                    i,
                    systems.Count,
                    () => systemToRemove = system),
                dimmed: !system.enabled,
                trailingControlWidth: m_cardControls.width);
            if (ImGuiWidget.BeginContextMenu($"##system_menu_{systemId}"))
            {
                if (NativeImGui.MenuItem("Reset System"))
                    scene.ResetSystem(system);
                if (NativeImGui.MenuItem("Remove System"))
                    systemToRemove = system;
                ImGuiWidget.EndContextMenu();
            }
            if (!open)
            {
                NativeImGui.Dummy(new Vector2(0f, 3f));
                continue;
            }

            NativeImGui.Unindent();
            ImGuiWidget.CardBody(
                systemId,
                () =>
                {
                    foreach (SerializedProperty property in SerializationManager.GetProperties(system))
                    {
                        context.properties.Draw(
                            context.editorContext,
                            $"scene.{scene.identity.persistentId:N}.{systemId}",
                            property);
                    }
                },
                dimmed: !system.enabled);
            NativeImGui.Indent();
            NativeImGui.TreePop();
            NativeImGui.Dummy(new Vector2(0f, 3f));
        }

        if (systemToRemove is not null)
            _ = scene.RemoveSystem(systemToRemove);
    }

    private void DrawAddSystem(GameScene scene)
    {
        if (ImGuiWidget.CenteredButton("Add System", 7f))
        {
            m_systemSearch = string.Empty;
            NativeImGui.OpenPopup("##add_system_popup");
        }
        if (!ImGuiWidget.BeginSearchPopup(
                "##add_system_popup",
                ref m_systemSearch,
                "Search systems...",
                C_SEARCH_BUFFER_SIZE))
            return;

        foreach (Type systemType in TypeCacheManager.GetSubTypesOf<GameSystem>())
        {
            if (!IsAddable(systemType, scene) ||
                !string.IsNullOrWhiteSpace(m_systemSearch) &&
                systemType.Name.IndexOf(m_systemSearch, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            if (NativeImGui.Selectable(systemType.Name))
            {
                _ = scene.AddSystem(systemType);
                NativeImGui.CloseCurrentPopup();
                break;
            }
        }
        ImGuiWidget.EndSearchPopup();
    }

    private static bool IsAddable(Type systemType, GameScene scene)
    {
        if (systemType.IsAbstract || systemType.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                Type.EmptyTypes,
                modifiers: null) is null)
            return false;
        return systemType.IsDefined(typeof(AllowMultipleSystemAttribute), inherit: false) ||
            !scene.GetSystems().Any(system => system.GetType() == systemType);
    }
}
