using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Inno.Extensibility.Modules;
using Inno.Extensibility.Reload;
using Inno.Core.Diagnostics;
using Inno.Core.Events;
using Inno.Core.Execution;
using Inno.Core.Identity;
using Inno.Core.Input;
using Inno.Core.Logging;
using Inno.Core.Serialization;
using Inno.Core.Settings;
using Inno.Extensibility.Types;
using Inno.Editor.Core;
using Inno.Editor.ImGui;
using Inno.Editor.Interactions;
using Inno.Editor.Settings;
using Xunit;

namespace Inno.Editor.Interactions.Tests;

public sealed class EditorRuntimeTests : IDisposable
{
    private readonly string m_projectRoot = Path.Combine(
        Path.GetTempPath(),
        "InnoEditorRuntimeTests",
        Guid.NewGuid().ToString("N"));
    private readonly DiagnosticHub m_diagnostics = new();
    private readonly LogRouter m_logs = new();
    private readonly IdentityAllocator m_identities = new();
    private readonly IDisposable m_diagnosticScope;
    private readonly ModuleHost m_modules;
    private readonly TypeCatalog m_types;
    private readonly EditorInteractionRuntime m_runtime;

    public EditorRuntimeTests()
    {
        Directory.CreateDirectory(Path.Combine(m_projectRoot, "Assets"));
        m_diagnosticScope = m_diagnostics.EnterScope();
        m_modules = new ModuleHost(new ModuleHostOptions
        {
            cacheDirectory = Path.Combine(m_projectRoot, "Library", "Assemblies")
        });
        m_types = new TypeCatalog(m_modules);

        TestModule.startCount = 0;
        TestModule.stopCount = 0;
        TestModule.stateValue = 0;
        TestModule.restoredStateValue = 0;
        TestModule.rebuildDuringRestore = false;
        TestModule.captureFailure = false;
        TestModule.throwOnStart = false;
        TestModule.throwOnStop = false;
        TestModule.pendingStops = 0;
        TestPanel.attachCount = 0;
        TestPanel.detachCount = 0;
        TestPanel.firstAttachPrecededModuleStart = false;
        DeferredAction.executeCount = 0;
        NeutralHistoryHandler.value = 0;
        UpdateBarrierModule.block = false;
        UpdateBarrierModule.throwWhenRead = false;
        UpdateBarrierModule.barrierReadCount = 0;
        FollowingUpdateModule.updateCount = 0;
        TestPanel.throwFromWindowPadding = false;
        TestPanel.throwOnAttach = false;
        TestPanel.throwOnDetach = false;
        TestPanel.pendingDetaches = 0;
        MultiShortcutAction.executeCount = 0;
        ToolbarAction.isChecked = false;
        ToolbarAction.executeCount = 0;
        MetadataAction.targetTypeReadCount = 0;
        MetadataAction.throwWhenRead = false;
        ThrowAfterActivateAction.cancelCount = 0;
        ADisposeObserverModule.historyAvailableDuringStop = false;
        ADisposeObserverModule.historyDisposedDuringDispose = false;
        ADisposeObserverModule.disposeCount = 0;
        ZThrowingDisposeModule.disposeCount = 0;
        ZThrowingDisposeModule.throwOnDispose = false;
        ShutdownOrder.events.Clear();

        m_runtime = CreateRuntime(new EditorContext(m_projectRoot));
        m_runtime.Start();
    }

    public void Dispose()
    {
        m_runtime.Dispose();
        m_types.Dispose();
        m_logs.Dispose();
        m_diagnosticScope.Dispose();
        m_modules.Dispose();
        if (Directory.Exists(m_projectRoot))
            Directory.Delete(m_projectRoot, recursive: true);
    }

    private EditorInteractionRuntime CreateRuntime(
        EditorContext context,
        params object[] hostServices)
        => new(context, m_types, m_logs, [m_types, m_identities, .. hostServices]);

    [Fact]
    public void HistoryAndExtensionStateContractsDoNotExposeStandaloneWorkspaceTypes()
    {
        Assembly core = typeof(EditorModule).Assembly;
        Assert.Null(core.GetType("Inno.Editor.Core.IEditorWorkspaceState"));
        Assert.Null(core.GetType("Inno.Editor.Core.EditorWorkspaceStateReader"));
        Assert.Null(core.GetType("Inno.Editor.Core.EditorWorkspaceStateWriter"));
        Assert.Null(typeof(EditorHistoryChange).GetProperty("version"));
        ConstructorInfo constructor = Assert.Single(typeof(EditorHistoryHandlerAttribute).GetConstructors());
        Assert.Collection(
            constructor.GetParameters(),
            static parameter => Assert.Equal(typeof(string), parameter.ParameterType));
    }

    [Fact]
    public void EditorDensityDefaultsToComfortableAndCanSwitchIdempotently()
    {
        var style = new EditorStyleMetrics();
        float comfortablePadding = style.framePadding.Y;

        Assert.False(style.isCompact);
        Assert.True(style.SetCompactMode(true));
        Assert.True(style.isCompact);
        Assert.True(style.framePadding.Y < comfortablePadding);
        Assert.False(style.SetCompactMode(true));
        Assert.True(style.SetCompactMode(false));
        Assert.False(style.isCompact);
        Assert.Equal(comfortablePadding, style.framePadding.Y);
    }

    [Fact]
    public void InteractionRuntimeInjectsOneStableAssignableHostService()
    {
        var service = new TestHostService();
        HostServicePanel.current = null;
        using EditorInteractionRuntime runtime = CreateRuntime(
            new EditorContext(m_projectRoot),
            service);

        runtime.Start();

        Assert.Contains(runtime.panels, static panel => panel.id == "tests.host-service");
        Assert.Same(service, HostServicePanel.current);
    }

    [Fact]
    public void InteractionRuntimeRejectsExtensionWhenHostServiceIsUnavailable()
    {
        HostServicePanel.current = null;
        using EditorInteractionRuntime runtime = CreateRuntime(new EditorContext(m_projectRoot));

        runtime.Start();

        Assert.DoesNotContain(runtime.panels, static panel => panel.id == "tests.host-service");
        Assert.Null(HostServicePanel.current);
    }

    [Fact]
    public void InteractionIdentifiersArePlainValidatedStringsWithoutWrapperTypes()
    {
        Assembly assembly = typeof(EditorInteractions).Assembly;
        Assert.Null(assembly.GetType("Inno.Editor.Interactions.EditorActionId"));
        Assert.Null(assembly.GetType("Inno.Editor.Interactions.EditorAreaId"));
        Assert.Null(assembly.GetType("Inno.Editor.Interactions.EditorPanelId"));
        Assert.Null(assembly.GetType("Inno.Editor.Interactions.EditorCommand"));
        Assert.Null(assembly.GetType("Inno.Editor.Interactions.EditorCommand`1"));
        Assert.Equal(typeof(string), typeof(EditorInteractions).GetProperty("focusedArea")!.PropertyType);
        Assert.Equal(typeof(string), typeof(EditorInteraction).GetProperty("area")!.PropertyType);
        Assert.Equal(typeof(string), typeof(EditorPanelExtension).GetProperty("id")!.PropertyType);
        MethodInfo forMethod = Assert.Single(typeof(EditorInteractions).GetMethods()
            .Where(static method => method.Name == "For"));
        Assert.Equal(typeof(string), forMethod.GetParameters()[0].ParameterType);
        Assert.Throws<ArgumentException>(() => m_runtime.interactions.For(string.Empty));
        Assert.Throws<ArgumentException>(() => m_runtime.interactions.TogglePanel(" "));
        Assert.Throws<ArgumentException>(() => m_runtime.interactions.OpenPanel(" "));
        Assert.Throws<ArgumentException>(() => m_runtime.interactions.ClosePanel(" "));
    }

    [Fact]
    public void PanelOpenAndCloseOperationsAreIdempotentAndRequestFocus()
    {
        EditorPanelExtension panel = Assert.Single(
            m_runtime.panels.Where(static candidate => candidate.id == "tests.panel"));
        Assert.False(panel.isOpen);

        Assert.True(m_runtime.interactions.OpenPanel("tests.panel"));
        Assert.True(m_runtime.interactions.OpenPanel("tests.panel"));
        Assert.True(panel.isOpen);
        Assert.True(panel.TakeFocusRequest());
        Assert.False(panel.TakeFocusRequest());

        Assert.True(m_runtime.interactions.ClosePanel("tests.panel"));
        Assert.True(m_runtime.interactions.ClosePanel("tests.panel"));
        Assert.False(panel.isOpen);
        Assert.False(panel.TakeFocusRequest());
        Assert.False(m_runtime.interactions.OpenPanel("tests.missing-panel"));
    }

    [Fact]
    public void DocumentHostOpensOneInstanceAndRequiresExplicitDirtyCloseDecision()
    {
        IEditorDocumentService documents = m_runtime.interactions.documents;
        var provider = new TestDocumentProvider();
        using IDisposable lease = documents.RegisterProvider(provider);
        Guid assetId = Guid.NewGuid();

        EditorDocumentContext opened = documents.Open("./Assets/Hero.ispriteatlas2d", assetId);
        EditorDocumentContext focused = documents.Open("Assets/OtherName.ispriteatlas2d", assetId);
        documents.MarkDirty(opened.documentId);

        Assert.Same(opened, focused);
        Assert.Same(opened, documents.activeDocument);
        Assert.True(opened.isDirty);
        Assert.False(documents.Close(opened.documentId, EditorDocumentCloseMode.Cancel));
        Assert.Single(documents.documents);
        Assert.True(documents.Close(opened.documentId, EditorDocumentCloseMode.Save));
        Assert.Empty(documents.documents);
        Assert.Equal(1, provider.saveCount);
        Assert.Equal(1, provider.closeCount);
    }

    [Fact]
    public void DocumentStateSurvivesProviderGenerationReplacement()
    {
        IEditorDocumentService documents = m_runtime.interactions.documents;
        var firstProvider = new TestDocumentProvider();
        IDisposable firstLease = documents.RegisterProvider(firstProvider);
        EditorDocumentContext opened = documents.Open("Assets/World.itilemap2d", Guid.NewGuid());
        opened.activeTool = "tilemap/brush";
        opened.SetViewParameter("zoom", "2.5");
        documents.MarkDirty(opened.documentId);
        IReadOnlyList<EditorDocumentState> state = documents.CaptureState();

        firstLease.Dispose();

        Assert.False(opened.isProviderAvailable);
        Assert.False(documents.DrawActive());
        Assert.False(documents.Save(opened.documentId));

        var replacement = new TestDocumentProvider();
        using IDisposable replacementLease = documents.RegisterProvider(replacement);

        Assert.True(opened.isProviderAvailable);
        Assert.Equal(1, replacement.openCount);
        Assert.True(documents.DrawActive());
        Assert.Equal(1, replacement.drawCount);
        Assert.True(documents.Close(opened.documentId, EditorDocumentCloseMode.Discard));

        documents.RestoreState(state, opened.documentId);

        EditorDocumentContext restored = Assert.Single(documents.documents);
        Assert.Equal(opened.documentId, restored.documentId);
        Assert.Equal("tilemap/brush", restored.activeTool);
        Assert.True(restored.TryGetViewParameter("zoom", out string zoom));
        Assert.Equal("2.5", zoom);
        Assert.Same(restored, documents.activeDocument);
    }

    [Fact]
    public void ViewportToolCapturesOnePointerAndCommitsOneGestureTransaction()
    {
        IEditorHistory history = m_runtime.interactions.history;
        var coordinates = new TestViewportCoordinates();
        var tool = new TestViewportTool(history);
        using var session = new EditorViewportToolSession(history, coordinates);
        session.SetTool(tool);
        var down = new EditorViewportPointerEvent(
            7,
            EditorViewportPointerPhase.Down,
            new Inno.Core.Mathematics.Vector2(2f, 3f),
            coordinates.ScreenToWorld(new Inno.Core.Mathematics.Vector2(2f, 3f)),
            0,
            KeyModifier.None);

        Assert.True(session.HandlePointer(down));
        Assert.False(session.HandlePointer(new EditorViewportPointerEvent(
            8,
            EditorViewportPointerPhase.Move,
            default,
            default,
            0,
            KeyModifier.None)));
        Assert.True(session.HandlePointer(new EditorViewportPointerEvent(
            7,
            EditorViewportPointerPhase.Up,
            down.screenPosition,
            down.worldPosition,
            0,
            KeyModifier.None)));

        Assert.Equal(1, tool.downCount);
        Assert.Equal(1, tool.upCount);
        Assert.True(history.canUndo);
        Assert.Equal("Paint Tile", history.undoName);
        Assert.True(history.Undo().succeeded);
        Assert.Equal(0, NeutralHistoryHandler.value);
    }

    [Fact]
    public void RuntimeDiscoversModulesAndPanelsWithoutRegistrationCalls()
    {
        Assert.True(TestModule.startCount > 0);
        Assert.True(TestPanel.attachCount > 0);
        Assert.False(TestPanel.firstAttachPrecededModuleStart);
        Assert.True(m_runtime.panelCount >= 1);
    }

    [Fact]
    public void StatisticsUseStableReplacementAndOneFrameOrderHandoff()
    {
        EditorStatistics statistics = m_runtime.context.statistics;
        var id = new EditorStatisticId("tests.statistics.frame-time");
        var groupId = new EditorStatisticGroupId("tests.statistics");
        statistics.Publish(new EditorStatistic(
            id,
            groupId,
            "Tests",
            "Frame Time",
            "10 ms"));
        statistics.Publish(new EditorStatistic(
            id,
            groupId,
            "Tests",
            "Frame Time",
            "8 ms"));

        Assert.Equal("8 ms", Assert.Single(statistics.GetSnapshot()).value);

        m_runtime.Update(new EditorFrame(0.016f, 0.016f, isFocused: true));
        Assert.Equal("8 ms", Assert.Single(statistics.GetSnapshot()).value);

        m_runtime.Update(new EditorFrame(0.016f, 0.032f, isFocused: true));
        Assert.Empty(statistics.GetSnapshot());
    }

    [Fact]
    public void BlockingModuleDefersOnlyModulesOrderedAfterIt()
    {
        UpdateBarrierModule.block = true;

        m_runtime.Update(new EditorFrame(0.016f, 0.016f, isFocused: true));

        Assert.Equal(0, FollowingUpdateModule.updateCount);
        UpdateBarrierModule.block = false;

        m_runtime.Update(new EditorFrame(0.016f, 0.032f, isFocused: true));

        Assert.Equal(1, FollowingUpdateModule.updateCount);
    }

    [Fact]
    public void ThrowingModuleBarrierIsQuarantinedWithoutStoppingFollowingModules()
    {
        UpdateBarrierModule.throwWhenRead = true;

        m_runtime.Update(new EditorFrame(0.016f, 0.016f, isFocused: true));
        m_runtime.Update(new EditorFrame(0.016f, 0.032f, isFocused: true));

        Assert.Equal(1, UpdateBarrierModule.barrierReadCount);
        Assert.Equal(2, FollowingUpdateModule.updateCount);
    }

    [Fact]
    public void ActionResolutionPrefersExactAreaAndTarget()
    {
        var target = new DerivedTarget();

        EditorActionState exact = m_runtime.interactions
            .For("tests/special", target)
            .Query("tests.resolve");
        EditorActionState fallback = m_runtime.interactions
            .For("tests/other", target)
            .Query("tests.resolve");

        Assert.Equal("area", exact.displayName);
        Assert.Equal("base", fallback.displayName);
    }

    [Fact]
    public void ActionTypeMetadataIsCapturedOnceAndNeverReadOnTheRoutingPath()
    {
        int readsAfterCatalogBuild = MetadataAction.targetTypeReadCount;
        MetadataAction.throwWhenRead = true;
        try
        {
            EditorInteraction interaction = m_runtime.interactions.For(
                "tests/action-metadata",
                new BaseTarget());

            Assert.True(interaction.Query("tests.action-metadata").isEnabled);
            Assert.True(interaction.Execute("tests.action-metadata"));
            Assert.True(interaction.Query("tests.action-metadata").isEnabled);
        }
        finally
        {
            MetadataAction.throwWhenRead = false;
        }

        Assert.Equal(readsAfterCatalogBuild, MetadataAction.targetTypeReadCount);
    }

    [Fact]
    public void QueuedActionExecutesAtRuntimeSafePoint()
    {
        m_runtime.interactions.For("tests/other").Enqueue("tests.deferred");
        Assert.Equal(0, DeferredAction.executeCount);

        m_runtime.Update(new EditorFrame(0.016f, 1f, isFocused: true));

        Assert.Equal(1, DeferredAction.executeCount);
    }

    [Fact]
    public void BuiltInUndoAndRedoActionsExposeHistoryThroughMenusAndShortcuts()
    {
        NeutralHistoryHandler.value = 1;
        m_runtime.interactions.history.RecordApplied(
            "Change Test Value",
            NeutralHistoryHandler.CreateChange(before: 0, after: 1));
        EditorInteraction global = m_runtime.interactions.For("editor/global");

        EditorActionState undo = global.Query("editor/undo");
        Assert.True(undo.isEnabled);
        Assert.Equal("Undo Change Test Value", undo.displayName);
        Assert.True(global.Execute("editor/undo"));
        Assert.Equal(0, NeutralHistoryHandler.value);

        EditorActionState redo = global.Query("editor/redo");
        Assert.True(redo.isEnabled);
        Assert.Equal("Redo Change Test Value", redo.displayName);
        Assert.True(global.Execute("editor/redo"));
        Assert.Equal(1, NeutralHistoryHandler.value);
    }

    [Fact]
    public void NeutralHistorySurvivesATypeCatalogGenerationChange()
    {
        NeutralHistoryHandler.value = 14;
        m_runtime.interactions.history.RecordApplied(
            "Change Neutral Value",
            NeutralHistoryHandler.CreateChange(before: 3, after: 14));

        m_types.Rebuild();
        _ = m_runtime.panelCount;

        Assert.True(m_runtime.interactions.history.canUndo);
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.Equal(3, NeutralHistoryHandler.value);
        Assert.True(m_runtime.interactions.history.Redo().succeeded);
        Assert.Equal(14, NeutralHistoryHandler.value);
    }

    [Fact]
    public void MissingHistoryHandlerCreatesAnExplicitBarrier()
    {
        m_runtime.interactions.history.RecordApplied(
            "Unavailable Change",
            new EditorHistoryChange(
                "tests/missing-history-handler",
                EditorHistoryPayload.FromBytes([1, 2, 3])));

        Assert.False(m_runtime.interactions.history.canUndo);
        Assert.Contains("tests/missing-history-handler", m_runtime.interactions.history.undoUnavailableReason);
        Assert.False(m_runtime.interactions.history.Undo().succeeded);
        Assert.Equal("Unavailable Change", m_runtime.interactions.history.undoName);
    }

    [Fact]
    public void LargeNeutralPayloadSpillsToTheBoundedSessionDiskStore()
    {
        NeutralHistoryHandler.value = 32;
        byte[] bytes = new byte[128 * 1024];
        BitConverter.GetBytes(9).CopyTo(bytes, 0);
        BitConverter.GetBytes(32).CopyTo(bytes, sizeof(int));
        m_runtime.interactions.history.RecordApplied(
            "Large Neutral Change",
            new EditorHistoryChange(
                NeutralHistoryHandler.KIND,
                EditorHistoryPayload.FromBytes(bytes)));

        Assert.Equal(0, m_runtime.interactions.history.residentBytes);
        Assert.Equal(bytes.LongLength, m_runtime.interactions.history.diskBytes);
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.Equal(9, NeutralHistoryHandler.value);
    }

    [Fact]
    public void AttributeMenusSupportArbitraryDepthAndDynamicEntries()
    {
        EditorMenuModel menu = m_runtime.interactions.For("tests/menu").BuildMenu();

        EditorMenuItem tools = Assert.Single(menu.items);
        Assert.Equal("Tools", tools.label);
        EditorMenuItem create = Assert.Single(tools.children);
        Assert.Equal("Create", create.label);
        Assert.Equal(
            ["Asset", "Generated"],
            create.children.Select(static item => item.label));
    }

    [Fact]
    public void ToolbarResolvesContextualIconTooltipStateAndDispatch()
    {
        EditorInteraction interaction = m_runtime.interactions.For("tests/toolbar");

        EditorToolbarItem initial = Assert.Single(interaction.BuildToolbar().items);
        Assert.Equal("tests.toolbar", initial.actionId);
        Assert.Equal(EditorToolbarIcon.Play, initial.icon);
        Assert.Equal("Start Runtime", initial.tooltip);
        Assert.False(initial.status.isChecked);

        ToolbarAction.isChecked = true;
        EditorToolbarItem active = Assert.Single(interaction.BuildToolbar().items);
        Assert.Equal(EditorToolbarIcon.Stop, active.icon);
        Assert.Equal("Stop Runtime", active.tooltip);
        Assert.True(active.status.isChecked);

        interaction.Enqueue(active.actionId);
        m_runtime.Update(new EditorFrame(0.016f, 1f, isFocused: true));
        Assert.Equal(1, ToolbarAction.executeCount);
    }

    [Fact]
    public void MainMenuPlacesGeneratedPanelTogglesUnderPanel()
    {
        EditorMenuModel menu = m_runtime.interactions
            .For("editor/main-menu")
            .BuildMenu();

        EditorMenuItem panel = Assert.Single(menu.items.Where(static item => item.label == "Panel"));
        EditorMenuItem testing = Assert.Single(panel.children.Where(static item => item.label == "Testing"));
        EditorMenuItem test = Assert.Single(testing.children.Where(static item => item.label == "Test"));
        Assert.False(test.status.isChecked);
        EditorMenuItem? view = menu.items.SingleOrDefault(static item => item.label == "View");
        Assert.True(view is null || view.children.All(static item => item.label != "Test"));

        Assert.True(m_runtime.interactions.TogglePanel("tests.panel"));
        EditorMenuModel openMenu = m_runtime.interactions
            .For("editor/main-menu")
            .BuildMenu();
        EditorMenuItem openPanel = Assert.Single(openMenu.items.Where(static item => item.label == "Panel"));
        EditorMenuItem openTesting = Assert.Single(
            openPanel.children.Where(static item => item.label == "Testing"));
        EditorMenuItem openTest = Assert.Single(
            openTesting.children.Where(static item => item.label == "Test"));
        Assert.True(openTest.status.isChecked);
    }

    [Fact]
    public void EveryShortcutDeclaredByTheSameActionCanDispatch()
    {
        m_runtime.interactions.For("tests/multiple-shortcuts").Focus();

        m_runtime.HandleKeyPressed(new KeyPressedEvent(0, KeyCode.F3, KeyModifier.None));
        m_runtime.HandleKeyPressed(new KeyPressedEvent(0, KeyCode.F4, KeyModifier.None));

        Assert.Equal(2, MultiShortcutAction.executeCount);
    }

    [Fact]
    public void ActionExecutionFailureCancelsStateActivatedBeforeTheException()
    {
        EditorInteraction interaction = m_runtime.interactions.For("tests/throw-after-activate");

        Assert.False(interaction.Execute("tests.throw-after-activate"));

        Assert.False(interaction.IsActive("tests.throw-after-activate"));
        Assert.Equal(1, ThrowAfterActivateAction.cancelCount);
    }

    [Fact]
    public void ActionOwnsValidatedMultiFrameState()
    {
        var target = new InteractionTarget();
        EditorInteraction interaction = m_runtime.interactions.For("tests/other", target);

        Assert.True(interaction.Execute("tests.interaction"));
        Assert.True(interaction.IsActive("tests.interaction"));

        Assert.True(interaction.Present(
            "tests.interaction",
            new InteractionPresentation(string.Empty, submit: true)));
        Assert.True(interaction.IsActive("tests.interaction"));
        Assert.Equal("A name is required.", target.validationMessage);
        Assert.Null(target.committedValue);

        Assert.True(interaction.Present(
            "tests.interaction",
            new InteractionPresentation("Renamed", submit: true)));
        Assert.Equal("Renamed", target.committedValue);
        Assert.False(interaction.IsActive("tests.interaction"));
        Assert.False(interaction.Present("tests.interaction"));
    }

    [Fact]
    public void ChangingSelectionFinishesThePreviousTargetsActivePresentation()
    {
        var target = new InteractionTarget();
        EditorInteraction interaction = m_runtime.interactions.For("tests/other", target);
        Assert.True(interaction.Select());
        Assert.True(interaction.Execute("tests.commit-on-presentation-lost"));

        Assert.True(m_runtime.interactions.For("tests/other", new DerivedTarget()).Select());

        Assert.Equal("Committed on focus loss", target.committedValue);
        Assert.False(interaction.IsActive("tests.commit-on-presentation-lost"));
    }

    [Fact]
    public void SelectionAndFocusUseTheLightweightAreaHandle()
    {
        var target = new DerivedTarget();
        EditorInteraction interaction = m_runtime.interactions.For("tests/other", target);

        interaction.Focus();
        Assert.Equal("tests/other", m_runtime.interactions.focusedArea);
        Assert.Same(target, m_runtime.interactions.focusedTarget);

        Assert.True(interaction.Select());
        Assert.Same(target, m_runtime.interactions.selection.selectedTarget);
        Assert.True(interaction.isSelected);

        Assert.True(m_runtime.interactions.For("tests/other").Select());
        Assert.Null(m_runtime.interactions.selection.selectedTarget);
        Assert.Null(typeof(EditorSelectionState).GetMethod("Select"));
        Assert.Null(typeof(EditorSelectionState).GetMethod("Clear"));
    }

    [Fact]
    public void TypedDropRoutesAndCancelsItsManagedSession()
    {
        DragSource supersededSource = Register(new DragSource());
        DragSource source = Register(new DragSource());
        var target = new DropTarget();
        var data = new EditorDragData(source, "source");
        RuntimeIdentity supersededIdentity = m_runtime.interactions
            .For("tests/other", supersededSource)
            .BeginDrag(new EditorDragData(supersededSource, "superseded"));
        RuntimeIdentity identity = m_runtime.interactions
            .For("tests/other", source)
            .BeginDrag(data);
        EditorInteraction dropTarget = m_runtime.interactions.For("tests/drop", target);

        Assert.NotEqual(supersededIdentity, identity);
        Assert.False(m_runtime.interactions.TryGetDragSource(supersededIdentity, out _));
        Assert.True(dropTarget.QueryDrop(identity, EditorDropPlacement.Into).canDrop);
        Assert.True(m_runtime.interactions.TryGetDragSource(identity, out _));
        EditorDropResult result = dropTarget.Drop(identity, EditorDropPlacement.Into);
        Assert.True(result.accepted, $"Drop target invoked: {target.wasDropped}.");
        Assert.True(target.wasDropped);
        Assert.False(m_runtime.interactions.TryGetDragSource(identity, out _));
    }

    [Fact]
    public void UnregisteredDragSourceCancelsTheManagedSession()
    {
        DragSource source = Register(new DragSource());
        RuntimeIdentity identity = m_runtime.interactions.For("tests/drag", source).BeginDrag(
            new EditorDragData(source, "source"));
        Assert.True(m_identities.Unregister(source));

        Assert.False(m_runtime.interactions.TryGetDragSource(identity, out _));
        Assert.False(m_runtime.interactions.TryGetDragSource(identity, out _));
    }

    private TIdentity Register<TIdentity>(TIdentity value)
        where TIdentity : IdentityObject
    {
        Assert.True(m_identities.Register(value));
        return value;
    }

    [Fact]
    public void ThrowingPanelPresentationPropertyQuarantinesOnlyThatPanel()
    {
        TestPanel.throwFromWindowPadding = true;
        EditorPanelExtension panel = Assert.Single(
            m_runtime.panels.Where(static value => value.id == "tests.panel"));

        Assert.False(panel.TryGetWindowPresentation(out _, out _));
        Assert.False(panel.isOpen);
    }

    [Fact]
    public void ShutdownDetachesPanelsBeforeStoppingModulesThenDisposesInteractionsAndExtensions()
    {
        ZThrowingDisposeModule.throwOnDispose = true;

        AggregateException failure = Assert.Throws<AggregateException>(m_runtime.Dispose);
        Assert.Contains("Injected extension disposal failure", failure.ToString());
        Assert.Equal(GenerationState.Faulted, m_modules.generations.state);
        Assert.Throws<InvalidOperationException>(() => m_modules.generations.EnsureReady("reload after failed extension retirement"));

        Assert.True(ADisposeObserverModule.historyAvailableDuringStop);
        Assert.True(ADisposeObserverModule.historyDisposedDuringDispose);
        Assert.True(
            ShutdownOrder.events.IndexOf("panel.detach") <
            ShutdownOrder.events.IndexOf("module.stop"));
        Assert.Equal(1, ADisposeObserverModule.disposeCount);
        Assert.Equal(1, ZThrowingDisposeModule.disposeCount);
    }

    [Fact]
    public void FailedModuleStartIsStoppedBeforeCandidateDisposal()
    {
        m_runtime.Dispose();
        int stops = TestModule.stopCount;
        TestModule.throwOnStart = true;
        using var runtime = CreateRuntime(new EditorContext(m_projectRoot));
        try
        {
            Assert.Throws<InvalidOperationException>(runtime.Start);
            Assert.Equal(stops + 1, TestModule.stopCount);
            Assert.Equal(GenerationState.Ready, m_modules.generations.state);
        }
        finally { TestModule.throwOnStart = false; }
    }

    [Fact]
    public void FailedPanelAttachmentIsCompensatedBeforeItIsQuarantined()
    {
        m_runtime.Dispose();
        int detaches = TestPanel.detachCount;
        TestPanel.throwOnAttach = true;
        using var runtime = CreateRuntime(new EditorContext(m_projectRoot));
        try
        {
            runtime.Start();
            Assert.Equal(detaches + 1, TestPanel.detachCount);
            Assert.DoesNotContain(runtime.panels, static panel => panel.id == "tests.panel");
            Assert.Equal(GenerationState.Ready, m_modules.generations.state);
            runtime.Dispose();
            Assert.Equal(detaches + 1, TestPanel.detachCount);
        }
        finally { TestPanel.throwOnAttach = false; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LifecycleFailureIsReportedAfterAllCompletedStagesAndFaultsAdmission(bool panelFailure)
    {
        TestModule.throwOnStop = !panelFailure;
        TestPanel.throwOnDetach = panelFailure;
        try
        {
            AggregateException failure = Assert.Throws<AggregateException>(m_runtime.Dispose);
            Assert.Contains(panelFailure ? "Injected panel detach failure" : "Injected module stop failure", failure.ToString());
            Assert.Equal(1, TestModule.stopCount);
            Assert.Equal(1, TestPanel.detachCount);
            Assert.Equal(1, ADisposeObserverModule.disposeCount);
            Assert.Equal(GenerationState.Faulted, m_modules.generations.state);
            m_runtime.Dispose();
            Assert.Equal(1, TestModule.stopCount);
        }
        finally
        {
            TestModule.throwOnStop = false;
            TestPanel.throwOnDetach = false;
        }
    }

    [Fact]
    public void PendingPanelAndModuleDrainBeforeHistoryAndExtensionResourcesAreDisposed()
    {
        TestPanel.pendingDetaches = 2;
        TestModule.pendingStops = 2;
        m_runtime.Dispose();
        Assert.Equal(3, TestPanel.detachCount);
        Assert.Equal(3, TestModule.stopCount);
        Assert.True(ADisposeObserverModule.historyAvailableDuringStop);
        Assert.True(ADisposeObserverModule.historyDisposedDuringDispose);
        Assert.Equal(1, ADisposeObserverModule.disposeCount);
        Assert.Equal(GenerationState.Ready, m_modules.generations.state);
        Assert.Equal(new[] { "panel.detach", "panel.detach", "panel.detach", "module.stop", "module.stop", "module.stop" },
            ShutdownOrder.events.Where(static value => value is "panel.detach" or "module.stop"));
    }

    [Fact]
    public void RebuildRetainsHostExtensionsInsteadOfRestartingModules()
    {
        int starts = TestModule.startCount;
        int attaches = TestPanel.attachCount;

        m_types.Rebuild();
        _ = m_runtime.panelCount;

        Assert.Equal(starts, TestModule.startCount);
        Assert.Equal(attaches, TestPanel.attachCount);
    }

    [Fact]
    public void ModuleStateAndPanelVisibilityRestoreForTheSameProject()
    {
        TestModule.stateValue = 42;
        EditorPanelExtension panel = Assert.Single(
            m_runtime.panels.Where(static value => value.id == "tests.panel"));
        panel.isOpen = true;
        m_runtime.Update(new EditorFrame(0.016f, 3f, isFocused: true));
        m_runtime.Dispose();

        string settingsPath = Path.Combine(m_projectRoot, "editor.ini");
        Assert.True(File.Exists(settingsPath));
        string document = File.ReadAllText(settingsPath);
        Assert.Contains("[InnoEditor][Module.tests.state]", document);
        Assert.DoesNotContain("[InnoEditor][Module.tests.update-barrier]", document);

        using EditorInteractionRuntime restored = CreateRuntime(new EditorContext(m_projectRoot));
        restored.Start();

        Assert.Equal(42, TestModule.restoredStateValue);
        Assert.True(Assert.Single(
            restored.panels.Where(static value => value.id == "tests.panel")).isOpen);
    }

    [Fact]
    public void MalformedModuleStateValueUsesTheExtensionFallback()
    {
        m_runtime.Dispose();
        var context = new EditorContext(m_projectRoot);
        context.SetLayoutSection("Module.tests.state", new Dictionary<string, string>
        {
            ["value"] = "not-json"
        });
        context.SaveLayout();
        TestModule.restoredStateValue = -1;

        using EditorInteractionRuntime restored = CreateRuntime(context);
        restored.Start();

        Assert.Equal(0, TestModule.restoredStateValue);
    }

    [Fact]
    public void ExtensionStateCaptureFailure_PublishesCurrentDiagnosticUntilRetrySucceeds()
    {
        var sink = new TestDiagnosticSink();
        m_diagnostics.RegisterSink(sink);
        try
        {
            TestModule.captureFailure = true;
            m_runtime.Update(new EditorFrame(0.016f, 3f, isFocused: true));

            DiagnosticReport report = Assert.Single(sink.reports.Values.Where(static value =>
                value.source.displayName == "Editor State Capture"));
            Assert.Equal("EDITOR-STATE-CAPTURE", Assert.Single(report.diagnostics).code);

            TestModule.captureFailure = false;
            m_runtime.Update(new EditorFrame(0.016f, 6f, isFocused: true));

            Assert.DoesNotContain(
                sink.reports.Values,
                static value => value.source.displayName == "Editor State Capture");
        }
        finally
        {
            TestModule.captureFailure = false;
            m_diagnostics.UnregisterSink(sink);
        }
    }

    [Fact]
    public void UnifiedEditorIniPreservesLayoutAndExtensionStateSectionsTogether()
    {
        const string layout = "[Window][Hierarchy]\nPos=10,20\nSize=300,400";
        var context = new EditorContext(m_projectRoot);
        context.SetImGuiLayout(layout);
        context.SetLayoutSection("Module.tests", new Dictionary<string, string>
        {
            ["openScenes"] = "[\"Scenes/Test.iscene\"]"
        });

        Assert.True(context.SaveLayoutIfChanged());
        var restored = new EditorContext(m_projectRoot);

        Assert.Equal(layout, restored.imguiLayout);
        Assert.True(restored.TryGetLayoutSection(
            "Module.tests",
            out IReadOnlyDictionary<string, string> values));
        Assert.Equal("[\"Scenes/Test.iscene\"]", values["openScenes"]);
        string document = File.ReadAllText(restored.layoutPath);
        Assert.Contains("[Window][Hierarchy]", document);
        Assert.Contains("[InnoEditor][Module.tests]", document);
        Assert.Contains("openScenes=[\"Scenes/Test.iscene\"]", document);
        Assert.DoesNotContain("Payload=", document);
    }

    [Fact]
    public void StartupRegistryRefreshCannotOverwriteModuleStateBeforeRestore()
    {
        m_runtime.Dispose();
        var context = new EditorContext(m_projectRoot);
        context.SetLayoutSection("Module.tests.state", new Dictionary<string, string>
        {
            ["value"] = "91"
        });
        context.SaveLayout();
        TestModule.startCount = 0;
        TestModule.restoredStateValue = 0;
        TestModule.rebuildDuringRestore = true;

        using EditorInteractionRuntime restored = CreateRuntime(new EditorContext(m_projectRoot));
        restored.Start();
        Assert.Equal(91, TestModule.restoredStateValue);
        TestModule.stateValue = TestModule.restoredStateValue;
        restored.Update(new EditorFrame(0.016f, 0.016f, isFocused: true));
        restored.SaveState();

        string document = File.ReadAllText(Path.Combine(m_projectRoot, "editor.ini"));
        Assert.Contains("[InnoEditor][Module.tests.state]", document);
        Assert.Contains("value=91", document);
        Assert.False(TestModule.rebuildDuringRestore);
    }

    private sealed class TestDiagnosticSink : IDiagnosticSink
    {
        internal Dictionary<string, DiagnosticReport> reports { get; } = new(StringComparer.Ordinal);

        public void Replace(DiagnosticReport report)
            => reports[report.source.id] = report;

        public void Clear(DiagnosticSource source)
            => reports.Remove(source.id);
    }

}

public class BaseTarget;
public sealed class DerivedTarget : BaseTarget;
public sealed class DragSource : IdentityObject;

public sealed class DropTarget
{
    public bool wasDropped { get; set; }
}

public sealed class InteractionTarget
{
    public string? committedValue { get; set; }
    public string? validationMessage { get; set; }
}

public sealed record InteractionPresentation(string value, bool submit, bool cancel = false);

[EditorHistoryHandler(NeutralHistoryHandler.KIND)]
public sealed class NeutralHistoryHandler : EditorHistoryHandler
{
    public const string KIND = "tests/neutral-value";

    public static int value;

    public static EditorHistoryChange CreateChange(int before, int after)
    {
        byte[] bytes = new byte[sizeof(int) * 2];
        BitConverter.GetBytes(before).CopyTo(bytes, 0);
        BitConverter.GetBytes(after).CopyTo(bytes, sizeof(int));
        return new EditorHistoryChange(KIND, EditorHistoryPayload.FromBytes(bytes));
    }

    protected override EditorHistoryAvailability Query(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
        => change.payload.length >= sizeof(int) * 2
            ? EditorHistoryAvailability.Available()
            : EditorHistoryAvailability.Unavailable("The neutral value payload is truncated.");

    protected override EditorHistoryResult Apply(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        byte[] bytes = change.payload.ReadBytes();
        value = BitConverter.ToInt32(
            bytes,
            direction == EditorHistoryDirection.Undo ? 0 : sizeof(int));
        return EditorHistoryResult.Success();
    }
}

public interface ITestHostService;

public sealed class TestHostService : ITestHostService;

[EditorPanel("tests.host-service", "Host Service")]
public sealed class HostServicePanel : EditorPanel
{
    public static ITestHostService? current;

    public HostServicePanel(ITestHostService service)
    {
        current = service;
    }

    protected override void OnDraw(EditorContext context)
        => _ = context;
}

[EditorModule("tests.state")]
public sealed class TestModule : EditorModule
{
    private readonly TypeCatalog m_types;

    public static int startCount;
    public static int stopCount;
    public static int stateValue;
    public static int restoredStateValue;
    public static bool rebuildDuringRestore;
    public static bool captureFailure;
    public static bool throwOnStart;
    public static bool throwOnStop;
    public static int pendingStops;

    public TestModule(TypeCatalog types)
    {
        m_types = types;
    }

    protected override void Capture(EditorState state)
    {
        if (captureFailure)
            throw new InvalidOperationException("The test module state cannot be captured.");
        state.Set("value", stateValue);
    }

    protected override void Restore(EditorState state)
    {
        restoredStateValue = state.Get("value", 0);
        if (!rebuildDuringRestore)
            return;
        rebuildDuringRestore = false;
        m_types.Rebuild();
    }

    protected override void OnStart(EditorContext context)
    {
        startCount++;
        if (throwOnStart)
            throw new InvalidOperationException("Injected module startup failure.");
        if (startCount == 1)
            m_types.Rebuild();
    }

    protected override void OnStop(EditorContext context)
    {
        stopCount++;
        ShutdownOrder.events.Add("module.stop");
        if (pendingStops-- > 0)
            throw new RetirementPendingException("Module-owned work has not drained.");
        if (throwOnStop)
            throw new InvalidOperationException("Injected module stop failure.");
    }
}

[EditorModule("tests.update-barrier", order: -200)]
public sealed class UpdateBarrierModule : EditorModule
{
    public static bool block;
    public static bool throwWhenRead;
    public static int barrierReadCount;

    public override bool blocksFollowingUpdates
    {
        get
        {
            barrierReadCount++;
            return throwWhenRead
                ? throw new InvalidOperationException("Injected module barrier failure.")
                : block;
        }
    }
}

[EditorModule("tests.following-update", order: -199)]
public sealed class FollowingUpdateModule : EditorModule
{
    public static int updateCount;

    protected override void OnUpdate(EditorContext context) => updateCount++;
}

[EditorPanel("tests.panel", "Test", defaultOpen: false, menuPath: "Testing")]
public sealed class TestPanel(TestModule module) : EditorPanel
{
    public static int attachCount;
    public static int detachCount;
    public static bool firstAttachPrecededModuleStart;
    public static bool throwFromWindowPadding;
    public static bool throwOnAttach;
    public static bool throwOnDetach;
    public static int pendingDetaches;

    public override bool useWindowPadding => throwFromWindowPadding
        ? throw new InvalidOperationException("Injected panel presentation failure.")
        : true;

    protected override void OnAttach(EditorContext context)
    {
        _ = module;
        if (attachCount == 0)
            firstAttachPrecededModuleStart = TestModule.startCount == 0;
        attachCount++;
        if (throwOnAttach)
            throw new InvalidOperationException("Injected panel attachment failure.");
    }

    protected override void OnDetach(EditorContext context)
    {
        detachCount++;
        ShutdownOrder.events.Add("panel.detach");
        if (pendingDetaches-- > 0)
            throw new RetirementPendingException("Panel-owned work has not drained.");
        if (throwOnDetach)
            throw new InvalidOperationException("Injected panel detach failure.");
    }

    protected override void OnDraw(EditorContext context)
    {
    }
}

[StableTypeId("0fbd66e2-303e-4b35-83a1-fc2b90d58360")]
[ProjectSettingDefinition("tests.editor.multi-presentation")]
public sealed class MultiPresentationSetting : ISerializable
{
    public static ProjectSettingId settingId => new("tests.editor.multi-presentation");

    [SerializableProperty]
    public int value { get; set; } = 7;
}

[ProjectSettingPath("Project/Tests/Multi/Primary")]
public sealed class PrimaryMultiPresentationEditor : ProjectSettingEditor<MultiPresentationSetting>
{
    public override ProjectSettingId settingId => MultiPresentationSetting.settingId;

    protected override void OnDraw(MultiPresentationSetting setting)
        => _ = setting;
}

[ProjectSettingPath("Project/Tests/Multi/Secondary")]
public sealed class SecondaryMultiPresentationEditor : ProjectSettingEditor<MultiPresentationSetting>
{
    public override ProjectSettingId settingId => MultiPresentationSetting.settingId;

    protected override void OnDraw(MultiPresentationSetting setting)
        => _ = setting;
}

[EditorAction("tests.resolve")]
public sealed class BaseResolveAction : EditorAction<BaseTarget>
{
    protected override EditorActionState Query(EditorActionContext<BaseTarget> context)
        => new(true, true, displayName: "base");

    protected override void Execute(EditorActionContext<BaseTarget> context)
    {
    }
}

[EditorAction("tests.resolve", "tests/special", priority: 100)]
public sealed class AreaResolveAction : EditorAction<DerivedTarget>
{
    protected override EditorActionState Query(EditorActionContext<DerivedTarget> context)
        => new(true, true, displayName: "area");

    protected override void Execute(EditorActionContext<DerivedTarget> context)
    {
    }
}

[EditorAction("tests.deferred")]
public sealed class DeferredAction : EditorAction
{
    public static int executeCount;

    protected override void Execute(EditorActionContext context) => executeCount++;
}

[EditorAction("tests.action-metadata", "tests/action-metadata")]
public sealed class MetadataAction : EditorAction
{
    public static int targetTypeReadCount;
    public static bool throwWhenRead;

    public override Type? targetType
    {
        get
        {
            targetTypeReadCount++;
            return throwWhenRead
                ? throw new InvalidOperationException("Action metadata was read at runtime.")
                : typeof(BaseTarget);
        }
    }

    protected override void Execute(EditorActionContext context)
    {
    }
}

[EditorAction("tests.multiple-shortcuts", "tests/multiple-shortcuts")]
[EditorShortcut("tests/multiple-shortcuts", KeyCode.F3)]
[EditorShortcut("tests/multiple-shortcuts", KeyCode.F4)]
public sealed class MultiShortcutAction : EditorAction
{
    public static int executeCount;

    protected override void Execute(EditorActionContext context) => executeCount++;
}

[EditorAction("tests.toolbar", "tests/toolbar")]
[EditorToolbarItem(
    "tests/toolbar",
    EditorToolbarIcon.Play,
    "Start Runtime",
    activeIcon: EditorToolbarIcon.Stop)]
public sealed class ToolbarAction : EditorAction
{
    public static bool isChecked;
    public static int executeCount;

    protected override EditorActionState Query(EditorActionContext context)
        => new(true, true, isChecked, isChecked ? "Stop Runtime" : "Start Runtime");

    protected override void Execute(EditorActionContext context) => executeCount++;
}

[EditorAction("tests.throw-after-activate", "tests/throw-after-activate")]
public sealed class ThrowAfterActivateAction : EditorAction
{
    public static int cancelCount;

    protected override void Execute(EditorActionContext context)
    {
        Activate(context);
        throw new InvalidOperationException("Injected action execution failure.");
    }

    protected override void OnCancelled() => cancelCount++;
}

[EditorModule("tests.dispose-observer", order: 900)]
public sealed class ADisposeObserverModule(EditorInteractions interactions) : EditorModule
{
    public static bool historyAvailableDuringStop;
    public static bool historyDisposedDuringDispose;
    public static int disposeCount;

    protected override void OnStop(EditorContext context)
    {
        if (interactions.history.isFaulted)
            return;
        using EditorHistoryTransaction transaction = interactions.history.BeginTransaction("Shutdown probe");
        historyAvailableDuringStop = transaction.Rollback().succeeded;
    }

    protected override void OnDispose()
    {
        disposeCount++;
        try
        {
            using EditorHistoryTransaction transaction = interactions.history.BeginTransaction("Disposed probe");
        }
        catch (ObjectDisposedException)
        {
            historyDisposedDuringDispose = true;
        }
    }
}

[EditorModule("tests.throwing-dispose", order: 901)]
public sealed class ZThrowingDisposeModule : EditorModule
{
    public static bool throwOnDispose;
    public static int disposeCount;

    protected override void OnDispose()
    {
        disposeCount++;
        if (throwOnDispose)
            throw new InvalidOperationException("Injected extension disposal failure.");
    }
}

internal static class ShutdownOrder
{
    internal static List<string> events { get; } = [];
}

[EditorAction("tests.interaction")]
public sealed class InteractionAction :
    EditorPresentationAction<InteractionTarget, InteractionPresentation>
{
    private string m_value = string.Empty;

    protected override void Execute(EditorActionContext<InteractionTarget> context)
    {
        m_value = "Initial";
        context.target.validationMessage = null;
        Activate(context);
    }

    protected override bool Present(
        EditorActionContext<InteractionTarget, InteractionPresentation> context)
    {
        InteractionPresentation presentation = context.argument;
        if (presentation.cancel)
        {
            Cancel();
            return true;
        }

        m_value = presentation.value;
        if (!presentation.submit)
            return true;
        if (string.IsNullOrWhiteSpace(m_value))
        {
            context.target.validationMessage = "A name is required.";
            return true;
        }

        context.target.committedValue = m_value;
        context.target.validationMessage = null;
        Complete();
        return true;
    }

    protected override void OnCancelled() => m_value = string.Empty;
}

[EditorAction("tests.commit-on-presentation-lost")]
public sealed class CommitOnPresentationLostAction : EditorAction<InteractionTarget>
{
    private InteractionTarget? m_target;

    protected override void Execute(EditorActionContext<InteractionTarget> context)
    {
        m_target = context.target;
        Activate(context);
    }

    protected override void OnPresentationLost()
    {
        if (m_target is not null)
            m_target.committedValue = "Committed on focus loss";
        m_target = null;
        Complete();
    }

    protected override void OnCancelled() => m_target = null;
}

[EditorAction("tests.menu")]
[EditorMenu("tests/menu", "Tools/Create/Asset", order: 100)]
public sealed class MenuAction : EditorAction
{
    protected override void Execute(EditorActionContext context)
    {
    }
}

[EditorMenuSource("tests/menu")]
public sealed class DynamicMenuSource : EditorMenuSource
{
    public override void Build(EditorMenuContext context, EditorMenuBuilder builder)
        => builder.Add("Tools/Create/Generated", "tests.menu", order: 200);
}

[EditorDrop("tests/drop")]
public sealed class TestDrop : EditorDrop<DragSource, DropTarget>
{
    protected override EditorDropStatus Query(EditorDropContext<DragSource, DropTarget> context)
        => EditorDropStatus.Accept();

    protected override EditorDropResult Drop(EditorDropContext<DragSource, DropTarget> context)
    {
        context.target.wasDropped = true;
        return EditorDropResult.Accepted();
    }
}

public sealed class TestDocumentProvider : EditorDocumentProvider
{
    public int openCount { get; private set; }
    public int drawCount { get; private set; }
    public int saveCount { get; private set; }
    public int closeCount { get; private set; }

    public override string id => "tests.documents";

    public override bool CanOpen(string assetPath)
        => assetPath.EndsWith(".ispriteatlas2d", StringComparison.Ordinal)
           || assetPath.EndsWith(".itilemap2d", StringComparison.Ordinal);

    public override void Open(EditorDocumentContext context) => openCount++;

    public override void Draw(EditorDocumentContext context) => drawCount++;

    public override bool Save(EditorDocumentContext context)
    {
        saveCount++;
        return true;
    }

    public override void Close(EditorDocumentContext context) => closeCount++;
}

public sealed class TestViewportCoordinates : IEditorViewportCoordinateConverter
{
    public Inno.Core.Mathematics.Vector2 ScreenToWorld(Inno.Core.Mathematics.Vector2 screenPosition)
        => screenPosition * 2f;

    public Inno.Core.Mathematics.Vector2 WorldToScreen(Inno.Core.Mathematics.Vector2 worldPosition)
        => worldPosition / 2f;
}

public sealed class TestViewportTool(IEditorHistory history) : EditorViewportTool
{
    public int downCount { get; private set; }
    public int upCount { get; private set; }

    public override string id => "tests.viewport.paint";

    public override EditorViewportCursor cursor => EditorViewportCursor.Crosshair;

    public override void OnPointerDown(
        EditorViewportToolContext context,
        EditorViewportPointerEvent pointer)
    {
        downCount++;
        context.CapturePointer(pointer.pointerId);
        context.BeginHistoryGesture("Paint Tile");
        NeutralHistoryHandler.value = 1;
        history.RecordApplied(
            "Paint Tile Cell",
            NeutralHistoryHandler.CreateChange(before: 0, after: 1));
    }

    public override void OnPointerUp(
        EditorViewportToolContext context,
        EditorViewportPointerEvent pointer)
    {
        upCount++;
        context.CompleteHistoryGesture(commit: true);
        context.ReleasePointer();
    }
}
