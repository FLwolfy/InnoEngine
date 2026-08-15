using Inno.Editor.Core;

namespace Inno.Editor.Panels;

/// <summary>
/// Factory for default editor panel set.
/// </summary>
public static class EditorDefaultPanels
{
    /// <summary>
    /// Creates default panel instances.
    /// </summary>
    public static EditorPanel[] Create()
    {
        return
        [
            new HierarchyPanel(),
            new InspectorPanel(),
            new FileBrowserPanel(),
            new LogPanel(),
            new StatsPanel()
        ];
    }
}
