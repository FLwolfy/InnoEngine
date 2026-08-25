using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Inno.Core.Logging;
using Inno.Core.Reflection;
using Inno.Editor.Core;

namespace Inno.Editor.Interactions;

internal sealed class EditorExtensionCatalog : TypeRegistry<EditorExtensionCatalog.Snapshot>
{
    private readonly EditorContext m_context;
    private readonly EditorExtensionDiagnosticPublisher m_diagnostics = new();
    private readonly EditorInteractions m_interactions;
    private readonly EditorExtensionStateStore m_state;
    private readonly Dictionary<string, PanelState> m_panelStates = new(StringComparer.Ordinal);
    private Snapshot? m_active;

    internal EditorExtensionCatalog(EditorContext context, EditorInteractions interactions)
    {
        m_context = context ?? throw new ArgumentNullException(nameof(context));
        m_interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
        m_state = new EditorExtensionStateStore(context);
    }

    internal Snapshot extensions
    {
        get
        {
            Snapshot snapshot = current;
            EnsureActive(snapshot);
            return snapshot;
        }
    }

    internal void UpdateModules()
    {
        Snapshot snapshot = extensions;
        for (int i = 0; i < snapshot.modules.Length; i++)
        {
            snapshot.modules[i].module.Update(m_context);
            if (snapshot.modules[i].module.blocksFollowingUpdates)
                break;
        }
        m_state.Update(m_context.frame.totalTime, snapshot.state, snapshot.panels);
    }

    internal void SaveState()
    {
        if (m_active is not null)
            m_state.Save(m_active.state, m_active.panels);
    }

    internal void PrepareShutdown()
    {
        if (m_active is not null)
            m_state.PrepareShutdown(m_active.state, m_active.panels);
    }

    internal void Shutdown(bool saveState = true)
    {
        if (m_active is not null)
        {
            if (saveState)
                m_state.PrepareShutdown(m_active.state, m_active.panels);
            Deactivate(m_active);
        }
        m_active = null;
        if (isInitialized)
            Clear();
        m_diagnostics.Dispose();
        m_state.ClearDiagnostics();
    }

    protected override Snapshot Build(TypeCacheSnapshot types)
    {
        CapturePanelStates();
        Type[] moduleTypes = types.GetTypesWithAttribute<EditorModuleAttribute>()
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToArray();
        var activator = new EditorExtensionActivator(
            m_context,
            m_interactions,
            moduleTypes,
            m_active?.instances);

        ModuleRegistration[] modules = moduleTypes
            .Select(type => new ModuleRegistration(
                type.GetCustomAttribute<EditorModuleAttribute>(false)!,
                type,
                activator.CreateModule(type)))
            .OrderBy(static value => value.attribute.order)
            .ThenBy(static value => value.type.FullName, StringComparer.Ordinal)
            .ToArray();

        ActionRegistration[] actions = types.GetTypesWithAttribute<EditorActionAttribute>()
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .Select(type => CreateActionRegistration(type, activator))
            .ToArray();
        ValidateActions(actions);

        MenuSourceRegistration[] menuSources = types.GetTypesWithAttribute<EditorMenuSourceAttribute>()
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .SelectMany(type => CreateMenuSourceRegistrations(type, activator))
            .OrderByDescending(static value => value.priority)
            .ThenBy(static value => value.type.FullName, StringComparer.Ordinal)
            .ToArray();

        DropRegistration[] drops = types.GetTypesWithAttribute<EditorDropAttribute>()
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .SelectMany(type => CreateDropRegistrations(type, activator))
            .ToArray();
        ValidateDrops(drops);

        PanelRegistration[] panels = types.GetTypesWithAttribute<EditorPanelAttribute>()
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .Select(type => CreatePanelRegistration(type, activator))
            .OrderBy(static value => value.attribute.order)
            .ThenBy(static value => value.type.FullName, StringComparer.Ordinal)
            .ToArray();
        ValidatePanels(panels);
        ValidateExtensionIds(modules, panels);

        ModalRegistration[] modals = types.GetTypesWithAttribute<EditorModalAttribute>()
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .Select(type => CreateModalRegistration(type, activator))
            .OrderBy(static value => value.attribute.order)
            .ThenBy(static value => value.type.FullName, StringComparer.Ordinal)
            .ToArray();
        ValidateModals(modals);

        HistoryHandlerRegistration[] historyHandlers = types
            .GetTypesWithAttribute<EditorHistoryHandlerAttribute>()
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .Select(type => CreateHistoryHandlerRegistration(type, activator))
            .OrderBy(static value => value.attribute.kind, StringComparer.Ordinal)
            .ThenBy(static value => value.type.FullName, StringComparer.Ordinal)
            .ToArray();
        ValidateHistoryHandlers(historyHandlers);

        StateRegistration[] state = CreateStateRegistrations(modules, panels);

        return new Snapshot(
            modules,
            actions,
            menuSources,
            drops,
            panels,
            modals,
            historyHandlers,
            state,
            activator.instances.ToArray());
    }

    protected override void OnCommitted(Snapshot previous, Snapshot currentSnapshot)
    {
        Transition(previous, currentSnapshot);
    }

    protected override void DisposeSnapshot(Snapshot snapshot)
    {
        HashSet<object>? retained = m_active is null
            ? null
            : new HashSet<object>(m_active.instances, ReferenceEqualityComparer.Instance);
        foreach (object instance in snapshot.instances.Reverse())
        {
            if (retained?.Contains(instance) == true)
                continue;
            if (instance is IDisposable disposable)
                disposable.Dispose();
        }
    }

    private void EnsureActive(Snapshot snapshot)
    {
        if (ReferenceEquals(m_active, snapshot))
            return;
        if (m_active is not null)
            Deactivate(m_active);
        Activate(snapshot);
    }

    private void Activate(Snapshot snapshot)
    {
        // Publish before attaching extensions so a TypeCache query performed during startup can
        // retain these host instances instead of treating them as an abandoned candidate.
        m_active = snapshot;
        m_interactions.history.UpdateHandlers(CreateHistoryHandlerMap(snapshot.historyHandlers));
        m_diagnostics.RetainPanels(new HashSet<string>(StringComparer.Ordinal));
        for (int i = 0; i < snapshot.panels.Length; i++)
        {
            PanelRegistration registration = snapshot.panels[i];
            try
            {
                registration.panel.Attach(m_context);
                m_diagnostics.ResolvePanel(registration.attribute.id);
            }
            catch (Exception exception)
            {
                registration.panel.isOpen = false;
                m_diagnostics.ReportPanelFailure(registration.attribute.id, exception);
                Log.Error("Editor panel '{0}' failed to attach: {1}", registration.attribute.id, exception);
            }
        }
        m_diagnostics.Commit();
        for (int i = 0; i < snapshot.modules.Length; i++)
            snapshot.modules[i].module.Start(m_context);
        m_state.Restore(snapshot.state);
    }

    private void Deactivate(Snapshot snapshot)
    {
        CapturePanelStates(snapshot);
        for (int i = snapshot.modules.Length - 1; i >= 0; i--)
            snapshot.modules[i].module.Stop(m_context);
        for (int i = snapshot.panels.Length - 1; i >= 0; i--)
        {
            PanelRegistration registration = snapshot.panels[i];
            try
            {
                registration.panel.Detach(m_context);
            }
            catch (Exception exception)
            {
                Log.Error("Editor panel '{0}' failed to detach: {1}", registration.attribute.id, exception);
            }
        }
        if (ReferenceEquals(m_active, snapshot))
            m_active = null;
        m_diagnostics.RetainPanels(new HashSet<string>(StringComparer.Ordinal));
        m_diagnostics.Commit();
    }

    private void Transition(Snapshot previous, Snapshot next)
    {
        CapturePanelStates(previous);
        m_state.Save(previous.state, previous.panels);
        var retained = new HashSet<object>(next.instances, ReferenceEqualityComparer.Instance);
        for (int i = previous.modules.Length - 1; i >= 0; i--)
        {
            if (!retained.Contains(previous.modules[i].module))
                previous.modules[i].module.Stop(m_context);
        }
        for (int i = previous.panels.Length - 1; i >= 0; i--)
        {
            PanelRegistration registration = previous.panels[i];
            if (retained.Contains(registration.panel))
                continue;
            try
            {
                registration.panel.Detach(m_context);
            }
            catch (Exception exception)
            {
                Log.Error("Editor panel '{0}' failed to detach: {1}", registration.attribute.id, exception);
            }
        }

        var existing = new HashSet<object>(previous.instances, ReferenceEqualityComparer.Instance);
        // Publish the candidate before attaching new extensions so refreshes triggered by startup
        // retain the newly active generation instead of reactivating the stopped snapshot.
        m_active = next;
        m_interactions.history.UpdateHandlers(CreateHistoryHandlerMap(next.historyHandlers));
        var retainedPanelIds = new HashSet<string>(
            next.panels
                .Where(registration => existing.Contains(registration.panel))
                .Select(static registration => registration.attribute.id),
            StringComparer.Ordinal);
        m_diagnostics.RetainPanels(retainedPanelIds);
        for (int i = 0; i < next.panels.Length; i++)
        {
            PanelRegistration registration = next.panels[i];
            if (existing.Contains(registration.panel))
                continue;
            try
            {
                registration.panel.Attach(m_context);
                m_diagnostics.ResolvePanel(registration.attribute.id);
            }
            catch (Exception exception)
            {
                registration.panel.isOpen = false;
                m_diagnostics.ReportPanelFailure(registration.attribute.id, exception);
                Log.Error("Editor panel '{0}' failed to attach: {1}", registration.attribute.id, exception);
            }
        }
        m_diagnostics.Commit();
        for (int i = 0; i < next.modules.Length; i++)
        {
            if (!existing.Contains(next.modules[i].module))
                next.modules[i].module.Start(m_context);
        }
        m_state.Restore(next.state);
    }

    private void CapturePanelStates()
    {
        if (m_active is not null)
            CapturePanelStates(m_active);
    }

    private void CapturePanelStates(Snapshot snapshot)
    {
        foreach (PanelRegistration registration in snapshot.panels)
        {
            ReadOnlyMemory<byte> payload = registration.panel is IEditorPanelReloadState reloadable
                ? reloadable.CaptureReloadState()
                : ReadOnlyMemory<byte>.Empty;
            m_panelStates[registration.attribute.id] = new PanelState(
                registration.panel.isOpen,
                payload);
        }
    }

    private ActionRegistration CreateActionRegistration(Type type, EditorExtensionActivator activator)
    {
        EditorAction action = activator.CreateExtension<EditorAction>(type);
        EditorActionAttribute attribute = type.GetCustomAttribute<EditorActionAttribute>(false)!;
        EditorMenuAttribute[] menus = type.GetCustomAttributes<EditorMenuAttribute>(false).ToArray();
        EditorShortcutAttribute[] shortcuts = type.GetCustomAttributes<EditorShortcutAttribute>(false).ToArray();
        return new ActionRegistration(attribute, type, action, menus, shortcuts);
    }

    private static IEnumerable<MenuSourceRegistration> CreateMenuSourceRegistrations(
        Type type,
        EditorExtensionActivator activator)
    {
        EditorMenuSource source = activator.CreateExtension<EditorMenuSource>(type);
        return type.GetCustomAttributes<EditorMenuSourceAttribute>(false)
            .Select(attribute => new MenuSourceRegistration(
                attribute.area,
                attribute.priority,
                type,
                source));
    }

    private static IEnumerable<DropRegistration> CreateDropRegistrations(
        Type type,
        EditorExtensionActivator activator)
    {
        EditorDrop drop = activator.CreateExtension<EditorDrop>(type);
        return type.GetCustomAttributes<EditorDropAttribute>(false)
            .Select(attribute => new DropRegistration(
                drop.sourceType,
                drop.targetType,
                attribute.area,
                attribute.priority,
                type,
                drop));
    }

    private PanelRegistration CreatePanelRegistration(Type type, EditorExtensionActivator activator)
    {
        EditorPanel panel = activator.CreateExtension<EditorPanel>(type);
        EditorPanelAttribute attribute = type.GetCustomAttribute<EditorPanelAttribute>(false)!;
        if (m_panelStates.TryGetValue(attribute.id, out PanelState state))
        {
            panel.isOpen = state.isOpen;
            if (panel is IEditorPanelReloadState reloadable && !state.payload.IsEmpty)
                reloadable.RestoreReloadState(state.payload);
        }
        else
        {
            panel.isOpen = m_state.TryGetPanelOpen(attribute.id, out bool isOpen)
                ? isOpen
                : attribute.defaultOpen;
        }
        return new PanelRegistration(attribute, type, panel);
    }

    private static ModalRegistration CreateModalRegistration(Type type, EditorExtensionActivator activator)
        => new(
            type.GetCustomAttribute<EditorModalAttribute>(false)!,
            type,
            activator.CreateExtension<EditorModal>(type));

    private static HistoryHandlerRegistration CreateHistoryHandlerRegistration(
        Type type,
        EditorExtensionActivator activator)
        => new(
            type.GetCustomAttribute<EditorHistoryHandlerAttribute>(false)!,
            type,
            activator.CreateExtension<EditorHistoryHandler>(type));

    private static void ValidateActions(ActionRegistration[] actions)
    {
        foreach (IGrouping<(string Action, string Area, Type? Target, int Priority), ActionRegistration> group in
                 actions.GroupBy(static value => (
                     value.attribute.action,
                     value.attribute.area,
                     value.action.targetType,
                     value.attribute.priority)))
        {
            if (group.Count() > 1)
            {
                throw new InvalidOperationException(
                    $"Editor action '{group.Key.Action}' has conflicting registrations for area " +
                    $"'{(string.IsNullOrEmpty(group.Key.Area) ? "*" : group.Key.Area)}' and target " +
                    $"'{group.Key.Target?.FullName ?? "*"}'.");
            }
        }
    }

    private static void ValidateDrops(DropRegistration[] drops)
    {
        foreach (IGrouping<(Type Source, Type Target, string Area, int Priority), DropRegistration> group in
                 drops.GroupBy(static value => (
                     value.sourceType,
                     value.targetType,
                     value.area,
                     value.priority)))
        {
            if (group.Count() > 1)
                throw new InvalidOperationException("Editor drop registrations contain an ambiguous match.");
        }
    }

    private static void ValidatePanels(PanelRegistration[] panels)
    {
        string? duplicate = panels
            .GroupBy(static value => value.attribute.id, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1)?.Key;
        if (duplicate is not null)
            throw new InvalidOperationException($"Editor panel id '{duplicate}' is registered more than once.");
    }

    private static void ValidateModals(ModalRegistration[] modals)
    {
        string? duplicate = modals
            .GroupBy(static value => value.attribute.id, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1)?.Key;
        if (duplicate is not null)
            throw new InvalidOperationException($"Editor modal id '{duplicate}' is registered more than once.");
    }

    private static void ValidateHistoryHandlers(HistoryHandlerRegistration[] handlers)
    {
        string? duplicate = handlers
            .GroupBy(static value => value.attribute.kind, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1)?.Key;
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Editor history handler kind '{duplicate}' is registered more than once.");
        }
    }

    private static IReadOnlyDictionary<string, EditorHistoryHandler> CreateHistoryHandlerMap(
        IReadOnlyList<HistoryHandlerRegistration> handlers)
    {
        var result = new Dictionary<string, EditorHistoryHandler>(handlers.Count, StringComparer.Ordinal);
        for (int i = 0; i < handlers.Count; i++)
            result.Add(handlers[i].attribute.kind, handlers[i].handler);
        return result;
    }

    private static void ValidateExtensionIds(
        IReadOnlyList<ModuleRegistration> modules,
        IReadOnlyList<PanelRegistration> panels)
    {
        string? duplicate = modules
            .Select(static value => value.attribute.id)
            .Concat(panels.Select(static value => value.attribute.id))
            .GroupBy(static id => id, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1)?.Key;
        if (duplicate is not null)
            throw new InvalidOperationException(
                $"Editor extension id '{duplicate}' is registered more than once.");
    }

    private static StateRegistration[] CreateStateRegistrations(
        IReadOnlyList<ModuleRegistration> modules,
        IReadOnlyList<PanelRegistration> panels)
    {
        var result = new List<StateRegistration>();
        for (int i = 0; i < modules.Count; i++)
        {
            ModuleRegistration module = modules[i];
            StateRegistration? registration = CreateStateRegistration(
                module.attribute.id,
                StateOwnerKind.Module,
                module.type,
                module.module,
                typeof(EditorModule));
            if (registration is not null)
                result.Add(registration);
        }
        for (int i = 0; i < panels.Count; i++)
        {
            PanelRegistration panel = panels[i];
            StateRegistration? registration = CreateStateRegistration(
                panel.attribute.id,
                StateOwnerKind.Panel,
                panel.type,
                panel.panel,
                typeof(EditorPanel));
            if (registration is not null)
                result.Add(registration);
        }
        return result
            .OrderBy(static value => value.id, StringComparer.Ordinal)
            .ToArray();
    }

    private static StateRegistration? CreateStateRegistration(
        string id,
        StateOwnerKind kind,
        Type type,
        object owner,
        Type baseType)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        MethodInfo capture = type.GetMethod(
            "Capture",
            flags,
            binder: null,
            [typeof(EditorState)],
            modifiers: null) ?? throw new InvalidOperationException(
                $"Editor extension '{type.FullName}' has no state capture hook.");
        if (capture.DeclaringType == baseType)
            return null;
        MethodInfo restore = type.GetMethod(
            "Restore",
            flags,
            binder: null,
            [typeof(EditorState)],
            modifiers: null) ?? throw new InvalidOperationException(
                $"Editor extension '{type.FullName}' has no state restore hook.");
        return new StateRegistration(
            id,
            kind,
            owner,
            capture.CreateDelegate<Action<EditorState>>(owner),
            restore.CreateDelegate<Action<EditorState>>(owner));
    }

    internal sealed record Snapshot(
        ModuleRegistration[] modules,
        ActionRegistration[] actions,
        MenuSourceRegistration[] menuSources,
        DropRegistration[] drops,
        PanelRegistration[] panels,
        ModalRegistration[] modals,
        HistoryHandlerRegistration[] historyHandlers,
        StateRegistration[] state,
        object[] instances);

    internal sealed record ModuleRegistration(
        EditorModuleAttribute attribute,
        Type type,
        EditorModule module);

    internal sealed record ActionRegistration(
        EditorActionAttribute attribute,
        Type type,
        EditorAction action,
        EditorMenuAttribute[] menus,
        EditorShortcutAttribute[] shortcuts);

    internal sealed record MenuSourceRegistration(
        string area,
        int priority,
        Type type,
        EditorMenuSource source);

    internal sealed record DropRegistration(
        Type sourceType,
        Type targetType,
        string area,
        int priority,
        Type type,
        EditorDrop drop);

    internal sealed record PanelRegistration(
        EditorPanelAttribute attribute,
        Type type,
        EditorPanel panel);

    internal sealed record ModalRegistration(
        EditorModalAttribute attribute,
        Type type,
        EditorModal modal);

    internal sealed record HistoryHandlerRegistration(
        EditorHistoryHandlerAttribute attribute,
        Type type,
        EditorHistoryHandler handler);

    internal sealed record StateRegistration(
        string id,
        StateOwnerKind kind,
        object owner,
        Action<EditorState> capture,
        Action<EditorState> restore);

    internal enum StateOwnerKind
    {
        Module,
        Panel
    }

    private readonly record struct PanelState(bool isOpen, ReadOnlyMemory<byte> payload);
}
