using System;
using System.Collections.Generic;

using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.ImGui;

public static partial class ImGuiWidget
{
    /// <summary>
    /// Draws a simple "View" menu with panel visibility toggles.
    /// </summary>
    /// <param name="items">Panel title + visible state pairs.</param>
    public static void ViewMenu(IReadOnlyList<(string title, bool isOpen)> items, Action<int, bool> onChanged)
    {
        if (!NativeImGui.BeginMainMenuBar())
            return;

        if (NativeImGui.BeginMenu("View"))
        {
            for (int i = 0; i < items.Count; i++)
            {
                bool open = items[i].isOpen;
                if (NativeImGui.MenuItem(items[i].title, string.Empty, ref open))
                {
                    onChanged(i, open);
                }
            }

            NativeImGui.EndMenu();
        }

        NativeImGui.EndMainMenuBar();
    }
}
