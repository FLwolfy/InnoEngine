using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Inno.Core.Assemblies;
using Inno.Core.Diagnose;
using Inno.Core.Events;
using Inno.Core.Input;
using Inno.Core.Reflection;
using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Xunit;

namespace Inno.Editor.Interactions.Tests;

public sealed class EditorRuntimeTests : IDisposable
{
    private readonly string m_projectRoot = Path.Combine(
        Path.GetTempPath(),
        "InnoEditorRuntimeTests",
        Guid.NewGuid().ToString("N"));
    private readonly EditorInteractionRuntime m_runtime;

    public EditorRuntimeTests()
    {
        Directory.CreateDirectory(Path.Combine(m_projectRoot, "Assets"));
        AssemblyManager.Initialize(new AssemblyManagerOptions
        {
            cacheDirectory = Path.Combine(m_projectRoot, "Library", "Assemblies")
        });
        TypeCacheManager.Initialize();

        TestModule.startCount = 0;
        TestModule.stopCount = 0;
        TestModule.workspaceValue = 0;
        TestModule.restoredWorkspaceValue = 0;
        TestModule.rebuildDuringRestore = false;
        TestModule.captureFailure = false;
        TestPanel.attachCount = 0;
        TestPanel.detachCount = 0;
        TestPanel.firstAttachPrecededModuleStart = false;
        DeferredAction.executeCount = 0;
        NeutralHistoryHandler.value = 0;
        UpdateBarrierModule.block = false;
        FollowingUpdateModule.updateCount = 0;

        m_runtime = new EditorInteractionRuntime(m_projectRoot);
        m_runtime.Start();
    }

    public void Dispose()
    {
        m_runtime.Dispose();
        TypeCacheManager.Shutdown();
        AssemblyManager.Shutdown();
        if (Directory.Exists(m_projectRoot))
            Directory.Delete(m_projectRoot, recursive: true);
    }

    [Fact]
    public void HistoryAndWorkspaceContractsDoNotExposeSchemaVersions()
    {
        Assert.Null(typeof(IEditorWorkspaceState).GetProperty("workspaceStateVersion"));
        Assert.Null(typeof(EditorHistoryChange).GetProperty("version"));
        ConstructorInfo constructor = Assert.Single(typeof(EditorHistoryHandlerAttribute).GetConstructors());
        Assert.Collection(
            constructor.GetParameters(),
            static parameter => Assert.Equal(typeof(string), parameter.ParameterType));
    }

    [Fact]
    public void ModuleAndPanelBasesHideInfrastructureAdaptersBehindProtectedHooks()
    {
        Assert.Null(typeof(EditorModule).GetMethod("Dispose"));
        Assert.Null(typeof(EditorModule).GetProperty(
            "workspaceStateId",
            BindingFlags.Instance | BindingFlags.Public));
        Assert.Null(typeof(EditorPanel).GetProperty(
            "workspaceStateId",
            BindingFlags.Instance | BindingFlags.Public));
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(EditorModule)));
        Assert.True(typeof(IEditorWorkspaceState).IsAssignableFrom(typeof(EditorModule)));
        Assert.True(typeof(IEditorWorkspaceState).IsAssignableFrom(typeof(EditorPanel)));
    }

    [Fact]
    public void RuntimeDiscoversModulesAndPanelsWithoutRegistrationCalls()
    {
        Assert.True(TestModule.startCount > 0);
        Assert.True(TestPanel.attachCount > 0);
        Assert.True(TestPanel.firstAttachPrecededModuleStart);
        Assert.True(m_runtime.panelCount >= 1);
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
        int value = 0;
        Assert.True(m_runtime.interactions.history.Execute(
            "Change Test Value",
            () =>
            {
                value = 1;
                return EditorHistoryResult.Success();
            },
            () =>
            {
                value = 0;
                return EditorHistoryResult.Success();
            }).succeeded);
        EditorInteraction global = m_runtime.interactions.For("editor/global");

        EditorActionState undo = global.Query("editor/undo");
        Assert.True(undo.isEnabled);
        Assert.Equal("Undo Change Test Value", undo.displayName);
        Assert.True(global.Execute("editor/undo"));
        Assert.Equal(0, value);

        EditorActionState redo = global.Query("editor/redo");
        Assert.True(redo.isEnabled);
        Assert.Equal("Redo Change Test Value", redo.displayName);
        Assert.True(global.Execute("editor/redo"));
        Assert.Equal(1, value);
    }

    [Fact]
    public void NeutralHistorySurvivesATypeCatalogGenerationChange()
    {
        NeutralHistoryHandler.value = 14;
        m_runtime.interactions.history.RecordApplied(
            "Change Neutral Value",
            NeutralHistoryHandler.CreateChange(before: 3, after: 14));

        TypeCacheManager.Rebuild();
        _ = m_runtime.panelCount;

        Assert.True(m_runtime.interactions.history.canUndo);
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.Equal(3, NeutralHistoryHandler.value);
        Assert.True(m_runtime.interactions.history.Redo().succeeded);
        Assert.Equal(14, NeutralHistoryHandler.value);
    }

    [Fact]
    public void RuntimeBoundHistoryIsDiscardedWhenExtensionsRefresh()
    {
        int value = 0;
        Assert.True(m_runtime.interactions.history.Execute(
            "Runtime-bound Change",
            () =>
            {
                value = 1;
                return EditorHistoryResult.Success();
            },
            () =>
            {
                value = 0;
                return EditorHistoryResult.Success();
            }).succeeded);

        TypeCacheManager.Rebuild();
        _ = m_runtime.panelCount;

        Assert.Equal(1, value);
        Assert.False(m_runtime.interactions.history.canUndo);
        Assert.Null(m_runtime.interactions.history.undoName);
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
    public void MainMenuPlacesGeneratedPanelTogglesUnderPanel()
    {
        EditorMenuModel menu = m_runtime.interactions
            .For("editor/main-menu")
            .BuildMenu();

        EditorMenuItem panel = Assert.Single(menu.items.Where(static item => item.label == "Panel"));
        Assert.Contains(panel.children, static item => item.label == "Test");
        EditorMenuItem? view = menu.items.SingleOrDefault(static item => item.label == "View");
        Assert.True(view is null || view.children.All(static item => item.label != "Test"));
    }

    [Fact]
    public void PlusGestureTreatsPhysicalShiftAsPartOfTheSymbolicKey()
    {
        KeyModifier primary = OperatingSystem.IsMacOS()
            ? KeyModifier.Super
            : KeyModifier.Control;
        var gesture = new HotKeyGesture(KeyCode.Plus, primary);

        Assert.True(gesture.Matches(new KeyPressedEvent(
            windowId: 0,
            KeyCode.Plus,
            primary | KeyModifier.Shift)));
        Assert.Equal(primary, gesture.modifiers);
        Assert.DoesNotContain("Shift", gesture.ToString(), StringComparison.Ordinal);
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
        Assert.Null(typeof(EditorSelectionState).GetMethod(
            "Select",
            BindingFlags.Instance | BindingFlags.Public));
        Assert.Null(typeof(EditorSelectionState).GetMethod(
            "Clear",
            BindingFlags.Instance | BindingFlags.Public));
    }

    [Fact]
    public void TypedDropRoutesAndCancelsItsManagedSession()
    {
        var source = new DragSource();
        var target = new DropTarget();
        Guid token = m_runtime.interactions
            .For("tests/other", source)
            .BeginDrag(new EditorDragData(source, "source"));
        EditorInteraction dropTarget = m_runtime.interactions.For("tests/drop", target);

        Assert.True(dropTarget.QueryDrop(token, EditorDropPlacement.Into).canDrop);
        Assert.True(dropTarget.Drop(token, EditorDropPlacement.Into).accepted);
        Assert.True(target.wasDropped);
        Assert.False(m_runtime.interactions.TryGetDragData(token, out _));
    }

    [Fact]
    public void RebuildRetainsHostExtensionsInsteadOfRestartingModules()
    {
        int starts = TestModule.startCount;
        int attaches = TestPanel.attachCount;

        TypeCacheManager.Rebuild();
        _ = m_runtime.panelCount;

        Assert.Equal(starts, TestModule.startCount);
        Assert.Equal(attaches, TestPanel.attachCount);
    }

    [Fact]
    public void WorkspaceStateAndPanelVisibilityRestoreForTheSameProject()
    {
        TestModule.workspaceValue = 42;
        EditorPanelExtension panel = Assert.Single(
            m_runtime.panels.Where(static value => value.id == "tests.panel"));
        panel.panel.isOpen = true;
        m_runtime.Update(new EditorFrame(0.016f, 3f, isFocused: true));
        m_runtime.Dispose();

        string settingsPath = Path.Combine(m_projectRoot, "editor.ini");
        Assert.True(File.Exists(settingsPath));
        Assert.Contains("[InnoEditor][Module.tests.workspace]", File.ReadAllText(settingsPath));
        Assert.False(File.Exists(Path.Combine(m_projectRoot, "Library", "Editor", "Workspace.json")));

        using var restored = new EditorInteractionRuntime(m_projectRoot);
        restored.Start();

        Assert.Equal(42, TestModule.restoredWorkspaceValue);
        Assert.True(Assert.Single(
            restored.panels.Where(static value => value.id == "tests.panel")).panel.isOpen);
    }

    [Fact]
    public void WorkspaceCaptureFailure_PublishesCurrentDiagnosticUntilRetrySucceeds()
    {
        var sink = new TestDiagnosticSink();
        DiagnosticManager.RegisterSink(sink);
        try
        {
            TestModule.captureFailure = true;
            m_runtime.Update(new EditorFrame(0.016f, 3f, isFocused: true));

            DiagnosticReport report = Assert.Single(sink.reports.Values.Where(static value =>
                value.source.displayName == "Workspace Capture"));
            Assert.Equal("WORKSPACE-CAPTURE", Assert.Single(report.diagnostics).code);

            TestModule.captureFailure = false;
            m_runtime.Update(new EditorFrame(0.016f, 6f, isFocused: true));

            Assert.DoesNotContain(
                sink.reports.Values,
                static value => value.source.displayName == "Workspace Capture");
        }
        finally
        {
            TestModule.captureFailure = false;
            DiagnosticManager.UnregisterSink(sink);
        }
    }

    [Fact]
    public void UnifiedEditorIniPreservesLayoutAndWorkspaceSectionsTogether()
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
    public void StartupRegistryRefreshCannotOverwriteWorkspaceBeforeProvidersRestore()
    {
        m_runtime.Dispose();
        var context = new EditorContext(m_projectRoot);
        context.SetLayoutSection("Module.tests.workspace", new Dictionary<string, string>
        {
            ["value"] = "91"
        });
        context.SaveLayout();
        TestModule.startCount = 0;
        TestModule.restoredWorkspaceValue = 0;
        TestModule.rebuildDuringRestore = true;

        using var restored = new EditorInteractionRuntime(m_projectRoot);
        restored.Start();
        Assert.Equal(91, TestModule.restoredWorkspaceValue);
        TestModule.workspaceValue = TestModule.restoredWorkspaceValue;
        restored.Update(new EditorFrame(0.016f, 0.016f, isFocused: true));
        restored.SaveWorkspace();

        string document = File.ReadAllText(Path.Combine(m_projectRoot, "editor.ini"));
        Assert.Contains("[InnoEditor][Module.tests.workspace]", document);
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
public sealed class DragSource;

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

[EditorModule]
public sealed class TestModule : EditorModule
{
    public static int startCount;
    public static int stopCount;
    public static int workspaceValue;
    public static int restoredWorkspaceValue;
    public static bool rebuildDuringRestore;
    public static bool captureFailure;

    protected override string workspaceStateId => "tests.workspace";

    protected override void CaptureWorkspaceState(EditorWorkspaceStateWriter writer)
    {
        if (captureFailure)
            throw new InvalidOperationException("The test workspace cannot be captured.");
        writer.Set("value", workspaceValue);
    }

    protected override void RestoreWorkspaceState(EditorWorkspaceStateReader reader)
    {
        restoredWorkspaceValue = reader.Get("value", 0);
        if (!rebuildDuringRestore)
            return;
        rebuildDuringRestore = false;
        TypeCacheManager.Rebuild();
    }

    protected override void OnStart(EditorContext context)
    {
        startCount++;
        if (startCount == 1)
            TypeCacheManager.Rebuild();
    }

    protected override void OnStop(EditorContext context) => stopCount++;
}

[EditorModule(order: -200)]
public sealed class UpdateBarrierModule : EditorModule
{
    public static bool block;

    public override bool blocksFollowingUpdates => block;
}

[EditorModule(order: -199)]
public sealed class FollowingUpdateModule : EditorModule
{
    public static int updateCount;

    protected override void OnUpdate(EditorContext context) => updateCount++;
}

[EditorPanel("tests.panel", "Test", defaultOpen: false)]
public sealed class TestPanel(TestModule module) : EditorPanel
{
    public static int attachCount;
    public static int detachCount;
    public static bool firstAttachPrecededModuleStart;

    protected override void OnAttach(EditorContext context)
    {
        _ = module;
        if (attachCount == 0)
            firstAttachPrecededModuleStart = TestModule.startCount == 0;
        attachCount++;
    }

    protected override void OnDetach(EditorContext context) => detachCount++;

    public override void Draw(EditorContext context)
    {
    }
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

[EditorAction("tests.interaction")]
public sealed class InteractionAction : EditorAction<InteractionTarget>
{
    private string m_value = string.Empty;

    protected override void Execute(EditorActionContext<InteractionTarget> context)
    {
        m_value = "Initial";
        context.target.validationMessage = null;
        Activate(context);
    }

    protected override bool Present(EditorActionContext<InteractionTarget> context)
    {
        if (context.argument is not InteractionPresentation presentation)
            return false;
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
