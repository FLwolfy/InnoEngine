using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.Logging;
using Inno.Editor.Core;

namespace Inno.Editor.Interactions;

internal sealed class EditorMenuCatalog(
    EditorExtensionCatalog catalog,
    EditorActionRouter actions)
{
    private static readonly EditorAreaId S_MAIN_MENU_AREA = new("editor/main-menu");
    private static readonly EditorActionId S_TOGGLE_PANEL_ACTION = new("editor/toggle-panel");

    private readonly HashSet<string> m_sourceFailures = new(StringComparer.Ordinal);

    internal EditorMenuModel Build(EditorMenuContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var placements = new List<Placement>();
        foreach (EditorExtensionCatalog.ActionRegistration registration in catalog.extensions.actions)
        {
            foreach (EditorMenuAttribute menu in registration.menus)
            {
                if (!string.Equals(menu.area, context.area.value, StringComparison.Ordinal))
                    continue;
                placements.Add(new Placement(
                    NormalizePath(menu.path),
                    registration.id,
                    menu.order,
                    menu.separatorBefore,
                    argument: null));
            }
        }

        foreach (EditorExtensionCatalog.MenuSourceRegistration registration in catalog.extensions.menuSources)
        {
            if (!string.Equals(registration.area, context.area.value, StringComparison.Ordinal))
                continue;
            try
            {
                var builder = new EditorMenuBuilder();
                registration.source.Build(context, builder);
                string sourceName = registration.type.FullName ?? registration.type.Name;
                m_sourceFailures.Remove(sourceName);
                placements.AddRange(builder.items.Select(static item => new Placement(
                    NormalizePath(item.path),
                    item.actionId,
                    item.order,
                    item.separatorBefore,
                    item.argument)));
            }
            catch (Exception exception)
            {
                string sourceName = registration.type.FullName ?? registration.type.Name;
                if (m_sourceFailures.Add(sourceName))
                {
                    Log.Error(
                        "Editor menu source '{0}' failed: {1}",
                        sourceName,
                        exception);
                }
            }
        }

        if (context.area == S_MAIN_MENU_AREA)
        {
            foreach (EditorExtensionCatalog.PanelRegistration panel in catalog.extensions.panels)
            {
                placements.Add(new Placement(
                    $"Panel/{panel.attribute.title}",
                    S_TOGGLE_PANEL_ACTION,
                    panel.attribute.order,
                    separatorBefore: false,
                    new EditorPanelId(panel.attribute.id)));
            }
        }

        var root = new MutableNode(string.Empty, 0, false);
        foreach (Placement placement in placements
                     .OrderBy(static value => value.order)
                     .ThenBy(static value => value.path, StringComparer.Ordinal))
        {
            AddPlacement(root, placement, context);
        }
        return new EditorMenuModel(Freeze(root.children.Values));
    }

    private void AddPlacement(MutableNode root, Placement placement, EditorMenuContext context)
    {
        EditorActionContext actionContext = context.CreateActionContext(placement.argument);
        EditorActionState state = actions.Query(placement.actionId.value, actionContext);
        if (!state.isVisible)
            return;

        string[] segments = placement.path.Split('/');
        MutableNode current = root;
        for (int i = 0; i < segments.Length; i++)
        {
            string segment = segments[i];
            if (!current.children.TryGetValue(segment, out MutableNode? child))
            {
                child = new MutableNode(
                    segment,
                    placement.order,
                    i == segments.Length - 1 && placement.separatorBefore);
                current.children.Add(segment, child);
            }
            current = child;
        }

        current.order = placement.order;
        current.separatorBefore = placement.separatorBefore;
        current.actionId = placement.actionId;
        current.argument = placement.argument;
        current.state = state;
        if (!string.IsNullOrWhiteSpace(state.displayName))
            current.label = state.displayName!;
    }

    private static IReadOnlyList<EditorMenuItem> Freeze(IEnumerable<MutableNode> nodes)
    {
        var result = new List<EditorMenuItem>();
        foreach (MutableNode node in nodes
                     .OrderBy(static value => value.order)
                     .ThenBy(static value => value.label, StringComparer.Ordinal))
        {
            IReadOnlyList<EditorMenuItem> children = Freeze(node.children.Values);
            EditorActionState state = node.actionId is null
                ? new EditorActionState(children.Count > 0, children.Count > 0)
                : node.state;
            if (!state.isVisible && children.Count == 0)
                continue;
            result.Add(new EditorMenuItem(
                node.label,
                node.actionId?.value ?? string.Empty,
                node.order,
                node.separatorBefore,
                state,
                children,
                node.argument));
        }
        return result;
    }

    private static string NormalizePath(string path)
    {
        string normalized = string.Join(
            '/',
            path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (string.IsNullOrEmpty(normalized))
            throw new InvalidOperationException("An editor menu path must contain at least one segment.");
        return normalized;
    }

    private sealed record Placement(
        string path,
        EditorActionId actionId,
        int order,
        bool separatorBefore,
        object? argument);

    private sealed class MutableNode(string label, int order, bool separatorBefore)
    {
        internal string label = label;
        internal int order = order;
        internal bool separatorBefore = separatorBefore;
        internal readonly Dictionary<string, MutableNode> children = new(StringComparer.Ordinal);
        internal EditorActionId? actionId;
        internal object? argument;
        internal EditorActionState state = EditorActionState.hidden;
    }
}
