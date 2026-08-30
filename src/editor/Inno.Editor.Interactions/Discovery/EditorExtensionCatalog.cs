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
    private readonly Action m_extensionsChanged;
    private readonly EditorExtensionDiagnosticPublisher m_diagnostics = new();
    private readonly EditorInteractions m_interactions;
    private readonly object[] m_hostServices;
    private readonly EditorExtensionStateStore m_state;
    private readonly Dictionary<string, PanelState> m_panelStates = new(StringComparer.Ordinal);
    private Snapshot? m_active;
    private ActivationState? m_activation;
    private Snapshot? m_staging;

    internal EditorExtensionCatalog(
        EditorContext context,
        EditorInteractions interactions,
        IEnumerable<object> hostServices,
        Action extensionsChanged)
    {
        m_context = context ?? throw new ArgumentNullException(nameof(context));
        m_interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
        ArgumentNullException.ThrowIfNull(hostServices);
        m_hostServices = hostServices.Select(static service =>
            service ?? throw new ArgumentException("Host services cannot contain null.", nameof(hostServices)))
            .ToArray();
        m_extensionsChanged = extensionsChanged ?? throw new ArgumentNullException(nameof(extensionsChanged));
        m_state = new EditorExtensionStateStore(context);
    }

    internal Snapshot extensions
    {
        get
        {
            Snapshot snapshot = current;
            if (!ReferenceEquals(snapshot, m_active) && !ReferenceEquals(snapshot, m_staging))
                throw new InvalidOperationException("The extension registry returned an unpublished snapshot.");
            return snapshot;
        }
    }

    internal void UpdateModules()
    {
        Snapshot snapshot = extensions;
        for (int i = 0; i < snapshot.modules.Length; i++)
        {
            ModuleRegistration registration = snapshot.modules[i];
            if (snapshot.quarantinedModules.Contains(registration.module))
                continue;
            try
            {
                registration.module.Update(m_context);
                if (registration.module.blocksFollowingUpdates)
                    break;
            }
            catch (Exception exception)
            {
                snapshot.quarantinedModules.Add(registration.module);
                Log.Error("Editor module '{0}' failed to update and was quarantined: {1}",
                    registration.attribute.id,
                    exception);
                continue;
            }
        }
        m_state.Update(m_context.frame.totalTime, GetAvailableState(snapshot), GetAvailablePanels(snapshot));
    }

    internal void SaveState()
    {
        if (m_active is not null)
            m_state.Save(GetAvailableState(m_active), GetAvailablePanels(m_active));
    }

    internal bool TryTogglePanel(string panelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(panelId);
        Snapshot snapshot = extensions;
        for (int i = 0; i < snapshot.panels.Length; i++)
        {
            PanelRegistration registration = snapshot.panels[i];
            if (!string.Equals(registration.attribute.id, panelId, StringComparison.Ordinal) ||
                snapshot.quarantinedPanels.Contains(registration.panel))
            {
                continue;
            }
            registration.panel.isOpen = !registration.panel.isOpen;
            return true;
        }
        return false;
    }

    internal void PrepareShutdown()
    {
        if (m_active is not null)
            m_state.PrepareShutdown(GetAvailableState(m_active), GetAvailablePanels(m_active));
    }

    internal ActionRegistration[] GetActionsForShutdown()
        => m_active?.actions ?? [];

    internal void QuarantinePanel(Snapshot snapshot, PanelRegistration registration, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(exception);
        if (!ReferenceEquals(snapshot, m_active) || !snapshot.quarantinedPanels.Add(registration.panel))
            return;
        registration.panel.isOpen = false;
        m_diagnostics.ReportPanelFailure(registration.attribute.id, exception);
        m_diagnostics.Commit();
        Log.Error("Editor panel '{0}' failed to draw and was quarantined: {1}",
            registration.attribute.id,
            exception);
    }

    internal void QuarantineModal(Snapshot snapshot, ModalRegistration registration, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(exception);
        if (!ReferenceEquals(snapshot, m_active) || !snapshot.quarantinedModals.Add(registration.modal))
            return;
        Log.Error("Editor modal '{0}' failed and was quarantined: {1}", registration.attribute.id, exception);
    }

    internal void Shutdown(bool saveState = true)
    {
        if (m_active is not null)
        {
            Snapshot active = m_active;
            try
            {
                if (saveState)
                    m_state.PrepareShutdown(GetAvailableState(active), GetAvailablePanels(active));
            }
            finally
            {
                Deactivate(active, captureReloadState: false);
            }
        }
        m_active = null;
        NotifyExtensionsChanged();
        m_diagnostics.Dispose();
        m_state.ClearDiagnostics();
    }

    protected override Snapshot Build(TypeCacheSnapshot types)
    {
        CapturePanelStates();
        Type[] moduleTypes = types.GetTypesWithAttribute<EditorModuleAttribute>()
            .Select(typeRef => typeRef.Resolve(types))
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToArray();
        var activator = new EditorExtensionActivator(
            m_context,
            m_interactions,
            moduleTypes,
            types.types.Select(typeRef => typeRef.Resolve(types)).ToArray(),
            m_hostServices,
            m_active?.instances);

        ModuleRegistration[] modules = moduleTypes
            .Where(activator.CanCreate)
            .Select(type => new ModuleRegistration(
                type.GetCustomAttribute<EditorModuleAttribute>(false)!,
                type,
                activator.CreateModule(type)))
            .OrderBy(static value => value.attribute.order)
            .ThenBy(static value => value.type.FullName, StringComparer.Ordinal)
            .ToArray();

        ActionRegistration[] actions = types.GetTypesWithAttribute<EditorActionAttribute>()
            .Select(typeRef => typeRef.Resolve(types))
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .Where(activator.CanCreate)
            .Select(type => CreateActionRegistration(type, activator))
            .ToArray();
        ValidateActions(actions);
        ValidateShortcuts(actions);
        ValidateToolbars(actions);

        MenuSourceRegistration[] menuSources = types.GetTypesWithAttribute<EditorMenuSourceAttribute>()
            .Select(typeRef => typeRef.Resolve(types))
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .Where(activator.CanCreate)
            .SelectMany(type => CreateMenuSourceRegistrations(type, activator))
            .OrderByDescending(static value => value.priority)
            .ThenBy(static value => value.type.FullName, StringComparer.Ordinal)
            .ToArray();

        DropRegistration[] drops = types.GetTypesWithAttribute<EditorDropAttribute>()
            .Select(typeRef => typeRef.Resolve(types))
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .Where(activator.CanCreate)
            .SelectMany(type => CreateDropRegistrations(type, activator))
            .ToArray();
        ValidateDrops(drops);

        PanelRegistration[] panels = types.GetTypesWithAttribute<EditorPanelAttribute>()
            .Select(typeRef => typeRef.Resolve(types))
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .Where(activator.CanCreate)
            .Select(type => CreatePanelRegistration(type, activator))
            .OrderBy(static value => value.attribute.order)
            .ThenBy(static value => value.type.FullName, StringComparer.Ordinal)
            .ToArray();
        ValidatePanels(panels);
        ValidateExtensionIds(modules, panels);

        ModalRegistration[] modals = types.GetTypesWithAttribute<EditorModalAttribute>()
            .Select(typeRef => typeRef.Resolve(types))
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .Where(activator.CanCreate)
            .Select(type => CreateModalRegistration(type, activator))
            .OrderBy(static value => value.attribute.order)
            .ThenBy(static value => value.type.FullName, StringComparer.Ordinal)
            .ToArray();
        ValidateModals(modals);

        HistoryHandlerRegistration[] historyHandlers = types
            .GetTypesWithAttribute<EditorHistoryHandlerAttribute>()
            .Select(typeRef => typeRef.Resolve(types))
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .Where(activator.CanCreate)
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

    protected override void OnActivating(Snapshot? previous, Snapshot candidate)
    {
        if (m_activation is not null)
            throw new InvalidOperationException("An extension generation transition is already active.");

        if (previous is not null)
            CapturePanelStates(previous);
        if (previous is not null)
            m_state.Save(GetAvailableState(previous), GetAvailablePanels(previous));

        var existing = previous is null
            ? new HashSet<object>(ReferenceEqualityComparer.Instance)
            : new HashSet<object>(previous.instances, ReferenceEqualityComparer.Instance);
        EditorHistory.HandlerUpdate handlers = m_interactions.historyHost.PrepareHandlerUpdate(
            CreateHistoryHandlerMap(candidate.historyHandlers));
        var activation = new ActivationState(previous, candidate, existing, handlers);
        m_activation = activation;
        m_staging = candidate;
        m_interactions.PrepareGenerationTransition();
        handlers.Activate();

        if (previous is not null)
        {
            foreach (ModuleRegistration registration in candidate.modules)
            {
                if (existing.Contains(registration.module) && previous.startedModules.Contains(registration.module))
                    candidate.startedModules.Add(registration.module);
            }
            foreach (PanelRegistration registration in candidate.panels)
            {
                if (existing.Contains(registration.panel) && previous.attachedPanels.Contains(registration.panel))
                    candidate.attachedPanels.Add(registration.panel);
            }
        }

        if (previous is not null)
            RetirePrevious(previous, candidate, activation);

        for (int i = 0; i < candidate.modules.Length; i++)
        {
            ModuleRegistration registration = candidate.modules[i];
            if (existing.Contains(registration.module))
                continue;
            registration.module.Start(m_context);
            activation.startedModules.Add(registration);
            candidate.startedModules.Add(registration.module);
        }

        var retainedPanelIds = new HashSet<string>(
            candidate.panels
                .Where(registration => existing.Contains(registration.panel))
                .Select(static registration => registration.attribute.id),
            StringComparer.Ordinal);
        m_diagnostics.RetainPanels(retainedPanelIds);
        for (int i = 0; i < candidate.panels.Length; i++)
        {
            PanelRegistration registration = candidate.panels[i];
            if (existing.Contains(registration.panel))
                continue;
            try
            {
                registration.panel.Attach(m_context);
                activation.attachedPanels.Add(registration);
                candidate.attachedPanels.Add(registration.panel);
                m_diagnostics.ResolvePanel(registration.attribute.id);
            }
            catch (Exception exception)
            {
                candidate.quarantinedPanels.Add(registration.panel);
                registration.panel.isOpen = false;
                TryDetach(registration, "failed activation cleanup");
                m_diagnostics.ReportPanelFailure(registration.attribute.id, exception);
                Log.Error("Editor panel '{0}' failed to attach: {1}", registration.attribute.id, exception);
            }
        }
        m_diagnostics.Commit();
        m_state.Restore(GetAvailableState(candidate));
        m_active = candidate;
    }

    protected override void OnActivationRolledBack(Snapshot? previous, Snapshot candidate)
    {
        ActivationState? activation = m_activation;
        m_active = previous;
        m_staging = null;
        if (activation is null || !ReferenceEquals(activation.candidate, candidate))
            return;

        activation.handlers.Rollback();
        m_interactions.RollbackGenerationTransition();
        for (int i = activation.attachedPanels.Count - 1; i >= 0; i--)
            TryDetach(activation.attachedPanels[i], "activation rollback");
        for (int i = activation.startedModules.Count - 1; i >= 0; i--)
            TryStop(activation.startedModules[i], "activation rollback");
        RestorePrevious(activation);
        m_activation = null;

        var previousPanelIds = previous is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(
                GetAvailablePanels(previous).Select(static registration => registration.attribute.id),
                StringComparer.Ordinal);
        m_diagnostics.RetainPanels(previousPanelIds);
        m_diagnostics.Commit();
    }

    protected override void OnActivationCompleted(Snapshot? previous, Snapshot currentSnapshot)
    {
        ActivationState? activation = m_activation;
        if (activation is null || !ReferenceEquals(activation.candidate, currentSnapshot))
            return;

        activation.handlers.Complete();
        m_interactions.CompleteGenerationTransition();
        NotifyExtensionsChanged();
        m_staging = null;
        m_activation = null;
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
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception exception)
                {
                    Log.Error(
                        "Editor extension '{0}' failed while being disposed: {1}",
                        instance.GetType().FullName ?? instance.GetType().Name,
                        exception);
                }
            }
        }
    }

    private void Deactivate(Snapshot snapshot, bool captureReloadState = true)
    {
        if (captureReloadState)
            CapturePanelStates(snapshot);
        for (int i = snapshot.panels.Length - 1; i >= 0; i--)
        {
            if (snapshot.attachedPanels.Contains(snapshot.panels[i].panel))
                TryDetach(snapshot.panels[i], "shutdown");
        }
        for (int i = snapshot.modules.Length - 1; i >= 0; i--)
        {
            if (snapshot.startedModules.Contains(snapshot.modules[i].module))
                TryStop(snapshot.modules[i], "shutdown");
        }
        if (ReferenceEquals(m_active, snapshot))
            m_active = null;
        m_diagnostics.RetainPanels(new HashSet<string>(StringComparer.Ordinal));
        m_diagnostics.Commit();
    }

    private void RetirePrevious(
        Snapshot previous,
        Snapshot next,
        ActivationState activation)
    {
        var retained = new HashSet<object>(next.instances, ReferenceEqualityComparer.Instance);
        for (int i = previous.panels.Length - 1; i >= 0; i--)
        {
            PanelRegistration registration = previous.panels[i];
            if (retained.Contains(registration.panel) || !previous.attachedPanels.Contains(registration.panel))
                continue;
            if (TryDetach(registration, "generation retirement"))
                activation.detachedPreviousPanels.Add(registration);
        }
        for (int i = previous.modules.Length - 1; i >= 0; i--)
        {
            if (!retained.Contains(previous.modules[i].module) &&
                previous.startedModules.Contains(previous.modules[i].module))
            {
                if (TryStop(previous.modules[i], "generation retirement"))
                    activation.stoppedPreviousModules.Add(previous.modules[i]);
            }
        }
    }

    private void RestorePrevious(ActivationState activation)
    {
        Snapshot? previous = activation.previous;
        if (previous is null)
            return;
        for (int i = activation.stoppedPreviousModules.Count - 1; i >= 0; i--)
        {
            ModuleRegistration registration = activation.stoppedPreviousModules[i];
            try
            {
                registration.module.Start(m_context);
            }
            catch (Exception exception)
            {
                previous.quarantinedModules.Add(registration.module);
                Log.Error(
                    "Editor module '{0}' failed while restoring the previous generation: {1}",
                    registration.attribute.id,
                    exception);
            }
        }
        for (int i = activation.detachedPreviousPanels.Count - 1; i >= 0; i--)
        {
            PanelRegistration registration = activation.detachedPreviousPanels[i];
            try
            {
                registration.panel.Attach(m_context);
            }
            catch (Exception exception)
            {
                previous.quarantinedPanels.Add(registration.panel);
                registration.panel.isOpen = false;
                m_diagnostics.ReportPanelFailure(registration.attribute.id, exception);
                Log.Error(
                    "Editor panel '{0}' failed while restoring the previous generation: {1}",
                    registration.attribute.id,
                    exception);
            }
        }
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
                ? reloadable.CaptureReloadState().ToArray()
                : ReadOnlyMemory<byte>.Empty;
            m_panelStates[registration.attribute.id] = new PanelState(
                registration.panel.isOpen,
                payload);
        }
    }

    private static StateRegistration[] GetAvailableState(Snapshot snapshot)
        => snapshot.state
            .Where(registration => registration.kind != StateOwnerKind.Panel ||
                                   !snapshot.quarantinedPanels.Contains((EditorPanel)registration.owner))
            .Where(registration => registration.kind != StateOwnerKind.Module ||
                                   !snapshot.quarantinedModules.Contains((EditorModule)registration.owner))
            .ToArray();

    private static PanelRegistration[] GetAvailablePanels(Snapshot snapshot)
        => snapshot.panels
            .Where(registration => !snapshot.quarantinedPanels.Contains(registration.panel))
            .ToArray();

    private bool TryStop(ModuleRegistration registration, string phase)
    {
        try
        {
            registration.module.Stop(m_context);
            return true;
        }
        catch (Exception exception)
        {
            Log.Error("Editor module '{0}' failed during {1}: {2}", registration.attribute.id, phase, exception);
            return false;
        }
    }

    private bool TryDetach(PanelRegistration registration, string phase)
    {
        try
        {
            registration.panel.Detach(m_context);
            return true;
        }
        catch (Exception exception)
        {
            Log.Error("Editor panel '{0}' failed during {1}: {2}", registration.attribute.id, phase, exception);
            return false;
        }
    }

    private void NotifyExtensionsChanged()
    {
        try
        {
            m_extensionsChanged();
        }
        catch (Exception exception)
        {
            Log.Error("Editor extension description invalidation failed: {0}", exception);
        }
    }

    private ActionRegistration CreateActionRegistration(Type type, EditorExtensionActivator activator)
    {
        EditorAction action = activator.CreateExtension<EditorAction>(type);
        EditorActionAttribute attribute = type.GetCustomAttribute<EditorActionAttribute>(false)!;
        EditorMenuAttribute[] menus = type.GetCustomAttributes<EditorMenuAttribute>(false).ToArray();
        EditorToolbarItemAttribute[] toolbars = type.GetCustomAttributes<EditorToolbarItemAttribute>(false).ToArray();
        EditorShortcutAttribute[] shortcuts = type.GetCustomAttributes<EditorShortcutAttribute>(false).ToArray();
        return new ActionRegistration(
            attribute,
            type,
            action,
            action.targetType,
            action.argumentType,
            menus,
            toolbars,
            shortcuts);
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
        Type sourceType = drop.sourceType;
        Type targetType = drop.targetType;
        return type.GetCustomAttributes<EditorDropAttribute>(false)
            .Select(attribute => new DropRegistration(
                sourceType,
                targetType,
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
                     value.targetType,
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

    private static void ValidateShortcuts(ActionRegistration[] actions)
    {
        var shortcuts = new List<ShortcutValidationEntry>();
        for (int actionIndex = 0; actionIndex < actions.Length; actionIndex++)
        {
            ActionRegistration registration = actions[actionIndex];
            for (int shortcutIndex = 0; shortcutIndex < registration.shortcuts.Length; shortcutIndex++)
            {
                EditorShortcutAttribute shortcut = registration.shortcuts[shortcutIndex];
                if (!string.IsNullOrEmpty(registration.attribute.area) &&
                    !string.IsNullOrEmpty(shortcut.area) &&
                    !string.Equals(registration.attribute.area, shortcut.area, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Editor shortcut on '{registration.id}' targets area '{shortcut.area}', " +
                        $"outside its action area '{registration.attribute.area}'.");
                }
                string effectiveArea = string.IsNullOrEmpty(shortcut.area)
                    ? registration.attribute.area
                    : shortcut.area;
                shortcuts.Add(new ShortcutValidationEntry(
                    registration,
                    effectiveArea,
                    CreateShortcutGesture(shortcut)));
            }
        }

        for (int leftIndex = 0; leftIndex < shortcuts.Count; leftIndex++)
        {
            ShortcutValidationEntry left = shortcuts[leftIndex];
            for (int rightIndex = leftIndex + 1; rightIndex < shortcuts.Count; rightIndex++)
            {
                ShortcutValidationEntry right = shortcuts[rightIndex];
                bool sameAction = string.Equals(
                    left.registration.id,
                    right.registration.id,
                    StringComparison.Ordinal);
                if (left.gesture != right.gesture ||
                    left.registration.attribute.priority != right.registration.attribute.priority ||
                    !string.Equals(left.area, right.area, StringComparison.Ordinal) ||
                    !MayHaveEqualTargetSpecificity(
                        left.registration.targetType,
                        right.registration.targetType))
                {
                    continue;
                }

                if (sameAction)
                {
                    throw new InvalidOperationException(
                        $"Editor action '{left.registration.id}' declares duplicate shortcut " +
                        $"'{left.gesture}' for area '{(string.IsNullOrEmpty(left.area) ? "*" : left.area)}'.");
                }

                throw new InvalidOperationException(
                    $"Editor shortcut '{left.gesture}' is ambiguous between actions " +
                    $"'{left.registration.id}' and '{right.registration.id}' for area " +
                    $"'{(string.IsNullOrEmpty(left.area) ? "*" : left.area)}'.");
            }
        }
    }

    private static bool MayHaveEqualTargetSpecificity(Type? left, Type? right)
    {
        if (left == right)
            return true;
        if (left is null || right is null)
            return false;
        return left.IsInterface || right.IsInterface;
    }

    private static void ValidateToolbars(IReadOnlyList<ActionRegistration> actions)
    {
        for (int actionIndex = 0; actionIndex < actions.Count; actionIndex++)
        {
            ActionRegistration registration = actions[actionIndex];
            if (registration.toolbars.Length == 0)
                continue;
            if (registration.targetType is not null || registration.argumentType is not null)
            {
                throw new InvalidOperationException(
                    $"Editor toolbar action '{registration.id}' must not require a target or argument.");
            }
            for (int toolbarIndex = 0; toolbarIndex < registration.toolbars.Length; toolbarIndex++)
            {
                EditorToolbarItemAttribute toolbar = registration.toolbars[toolbarIndex];
                if (!string.IsNullOrEmpty(registration.attribute.area) &&
                    !string.Equals(registration.attribute.area, toolbar.area, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Editor toolbar placement on '{registration.id}' targets area '{toolbar.area}', " +
                        $"outside its action area '{registration.attribute.area}'.");
                }
            }
        }

        IGrouping<(string id, string area), (string id, string area)>? duplicate = actions
            .SelectMany(static registration => registration.toolbars.Select(toolbar => (
                registration.id,
                toolbar.area)))
            .GroupBy(static placement => placement, EqualityComparer<(string id, string area)>.Default)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException(
                $"Editor action '{duplicate.Key.id}' has duplicate toolbar placements.");
    }

    private static HotKeyGesture CreateShortcutGesture(EditorShortcutAttribute shortcut)
        => shortcut.primary
            ? HotKeyGesture.Primary(shortcut.key, shortcut.modifiers)
            : new HotKeyGesture(shortcut.key, shortcut.modifiers);

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
        object[] instances)
    {
        internal HashSet<EditorModule> quarantinedModules { get; } =
            new(ReferenceEqualityComparer.Instance);

        internal HashSet<EditorPanel> quarantinedPanels { get; } =
            new(ReferenceEqualityComparer.Instance);

        internal HashSet<EditorModal> quarantinedModals { get; } =
            new(ReferenceEqualityComparer.Instance);

        internal HashSet<EditorModule> startedModules { get; } =
            new(ReferenceEqualityComparer.Instance);

        internal HashSet<EditorPanel> attachedPanels { get; } =
            new(ReferenceEqualityComparer.Instance);
    }

    internal sealed record ModuleRegistration(
        EditorModuleAttribute attribute,
        Type type,
        EditorModule module);

    internal sealed record ActionRegistration(
        EditorActionAttribute attribute,
        Type type,
        EditorAction action,
        Type? targetType,
        Type? argumentType,
        EditorMenuAttribute[] menus,
        EditorToolbarItemAttribute[] toolbars,
        EditorShortcutAttribute[] shortcuts)
    {
        internal string id { get; } = attribute.action;

        internal string? area { get; } = string.IsNullOrEmpty(attribute.area)
            ? null
            : attribute.area;
    }

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

    private readonly record struct ShortcutValidationEntry(
        ActionRegistration registration,
        string area,
        HotKeyGesture gesture);

    private sealed class ActivationState(
        Snapshot? previous,
        Snapshot candidate,
        HashSet<object> existing,
        EditorHistory.HandlerUpdate handlers)
    {
        internal Snapshot? previous { get; } = previous;
        internal Snapshot candidate { get; } = candidate;
        internal HashSet<object> existing { get; } = existing;
        internal EditorHistory.HandlerUpdate handlers { get; } = handlers;
        internal List<ModuleRegistration> startedModules { get; } = [];
        internal List<PanelRegistration> attachedPanels { get; } = [];
        internal List<ModuleRegistration> stoppedPreviousModules { get; } = [];
        internal List<PanelRegistration> detachedPreviousPanels { get; } = [];
    }
}
