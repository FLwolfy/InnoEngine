using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Inno.Core.Assemblies;
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
        TestPanel.attachCount = 0;
        TestPanel.detachCount = 0;
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
    public void RuntimeDiscoversModulesAndPanelsWithoutRegistrationCalls()
    {
        Assert.True(TestModule.startCount > 0);
        Assert.True(TestPanel.attachCount > 0);
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
            .For(TestAreas.Special, target)
            .Query(TestActionIds.Resolve);
        EditorActionState fallback = m_runtime.interactions
            .For(TestAreas.Other, target)
            .Query(TestActionIds.Resolve);

        Assert.Equal("area", exact.displayName);
        Assert.Equal("base", fallback.displayName);
    }

    [Fact]
    public void QueuedActionExecutesAtRuntimeSafePoint()
    {
        m_runtime.interactions.For(TestAreas.Other).Enqueue(TestActionIds.Deferred);
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
        EditorInteraction global = m_runtime.interactions.For(EditorAreas.Global);

        EditorActionState undo = global.Query(EditorActions.Undo);
        Assert.True(undo.isEnabled);
        Assert.Equal("Undo Change Test Value", undo.displayName);
        Assert.True(global.Execute(EditorActions.Undo));
        Assert.Equal(0, value);

        EditorActionState redo = global.Query(EditorActions.Redo);
        Assert.True(redo.isEnabled);
        Assert.Equal("Redo Change Test Value", redo.displayName);
        Assert.True(global.Execute(EditorActions.Redo));
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
        EditorMenuModel menu = m_runtime.interactions.For(TestAreas.Menu).BuildMenu();

        EditorMenuItem tools = Assert.Single(menu.items);
        Assert.Equal("Tools", tools.label);
        EditorMenuItem create = Assert.Single(tools.children);
        Assert.Equal("Create", create.label);
        Assert.Equal(
            ["Asset", "Generated"],
            create.children.Select(static item => item.label));
    }

    [Fact]
    public void ActionOwnsValidatedMultiFrameState()
    {
        var target = new InteractionTarget();
        EditorInteraction interaction = m_runtime.interactions.For(TestAreas.Other, target);

        Assert.True(interaction.Execute(TestActionIds.Interaction));
        Assert.True(interaction.IsActive(TestActionIds.Interaction));

        Assert.True(interaction.Present(
            TestActionIds.Interaction,
            new InteractionPresentation(string.Empty, submit: true)));
        Assert.True(interaction.IsActive(TestActionIds.Interaction));
        Assert.Equal("A name is required.", target.validationMessage);
        Assert.Null(target.committedValue);

        Assert.True(interaction.Present(
            TestActionIds.Interaction,
            new InteractionPresentation("Renamed", submit: true)));
        Assert.Equal("Renamed", target.committedValue);
        Assert.False(interaction.IsActive(TestActionIds.Interaction));
        Assert.False(interaction.Present(TestActionIds.Interaction));
    }

    [Fact]
    public void ChangingSelectionFinishesThePreviousTargetsActivePresentation()
    {
        var target = new InteractionTarget();
        EditorInteraction interaction = m_runtime.interactions.For(TestAreas.Other, target);
        Assert.True(interaction.Select());
        Assert.True(interaction.Execute(TestActionIds.CommitOnPresentationLost));

        Assert.True(m_runtime.interactions.For(TestAreas.Other, new DerivedTarget()).Select());

        Assert.Equal("Committed on focus loss", target.committedValue);
        Assert.False(interaction.IsActive(TestActionIds.CommitOnPresentationLost));
    }

    [Fact]
    public void SelectionAndFocusUseTheLightweightAreaHandle()
    {
        var target = new DerivedTarget();
        EditorInteraction interaction = m_runtime.interactions.For(TestAreas.Other, target);

        interaction.Focus();
        Assert.Equal(TestAreas.Other, m_runtime.interactions.focusedArea);
        Assert.Same(target, m_runtime.interactions.focusedTarget);

        Assert.True(interaction.Select());
        Assert.Same(target, m_runtime.interactions.selection.selectedTarget);
        Assert.True(interaction.isSelected);

        Assert.True(m_runtime.interactions.For(TestAreas.Other).Select());
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
            .For(TestAreas.Other, source)
            .BeginDrag(new EditorDragData(source, "source"));
        EditorInteraction dropTarget = m_runtime.interactions.For(TestAreas.Drop, target);

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
    public void UnifiedEditorIniPreservesLayoutAndWorkspaceSectionsTogether()
    {
        const string layout = "[Window][Hierarchy]\nPos=10,20\nSize=300,400";
        var settings = new EditorProjectSettings(m_projectRoot);
        settings.SetImGuiLayout(layout);
        settings.SetSection("Module.tests", new Dictionary<string, string>
        {
            ["openScenes"] = "[\"Scenes/Test.innoscene\"]"
        });

        Assert.True(settings.SaveIfChanged());
        var restored = new EditorProjectSettings(m_projectRoot);

        Assert.Equal(layout, restored.imguiLayout);
        Assert.True(restored.TryGetSection(
            "Module.tests",
            out IReadOnlyDictionary<string, string> values));
        Assert.Equal("[\"Scenes/Test.innoscene\"]", values["openScenes"]);
        string document = File.ReadAllText(restored.path);
        Assert.Contains("[Window][Hierarchy]", document);
        Assert.Contains("[InnoEditor][Module.tests]", document);
        Assert.Contains("openScenes=[\"Scenes/Test.innoscene\"]", document);
        Assert.DoesNotContain("Payload=", document);
    }

    [Fact]
    public void StartupRegistryRefreshCannotOverwriteWorkspaceBeforeProvidersRestore()
    {
        m_runtime.Dispose();
        var settings = new EditorProjectSettings(m_projectRoot);
        settings.SetSection("Module.tests.workspace", new Dictionary<string, string>
        {
            ["value"] = "91"
        });
        settings.Save();
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
}

public static class TestAreas
{
    public const string Special = "tests/special";
    public const string Other = "tests/other";
    public const string Menu = "tests/menu";
    public const string Drop = "tests/drop";
}

public static class TestActionIds
{
    public const string Resolve = "tests.resolve";
    public const string Deferred = "tests.deferred";
    public const string Menu = "tests.menu";
    public const string Interaction = "tests.interaction";
    public const string CommitOnPresentationLost = "tests.commit-on-presentation-lost";
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
public sealed class TestModule : EditorModule, IEditorWorkspaceState
{
    public static int startCount;
    public static int stopCount;
    public static int workspaceValue;
    public static int restoredWorkspaceValue;
    public static bool rebuildDuringRestore;

    public string workspaceStateId => "tests.workspace";


    public void CaptureWorkspaceState(EditorWorkspaceStateWriter writer)
        => writer.Set("value", workspaceValue);

    public void RestoreWorkspaceState(EditorWorkspaceStateReader reader)
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

    protected override void OnAttach(EditorContext context)
    {
        _ = module;
        attachCount++;
    }

    protected override void OnDetach(EditorContext context) => detachCount++;

    public override void Draw(EditorContext context)
    {
    }
}

[EditorAction(TestActionIds.Resolve)]
public sealed class BaseResolveAction : EditorAction<BaseTarget>
{
    protected override EditorActionState Query(EditorActionContext<BaseTarget> context)
        => new(true, true, displayName: "base");

    protected override void Execute(EditorActionContext<BaseTarget> context)
    {
    }
}

[EditorAction(TestActionIds.Resolve, TestAreas.Special, priority: 100)]
public sealed class AreaResolveAction : EditorAction<DerivedTarget>
{
    protected override EditorActionState Query(EditorActionContext<DerivedTarget> context)
        => new(true, true, displayName: "area");

    protected override void Execute(EditorActionContext<DerivedTarget> context)
    {
    }
}

[EditorAction(TestActionIds.Deferred)]
public sealed class DeferredAction : EditorAction
{
    public static int executeCount;

    protected override void Execute(EditorActionContext context) => executeCount++;
}

[EditorAction(TestActionIds.Interaction)]
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

[EditorAction(TestActionIds.CommitOnPresentationLost)]
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

[EditorAction(TestActionIds.Menu)]
[EditorMenu(TestAreas.Menu, "Tools/Create/Asset", order: 100)]
public sealed class MenuAction : EditorAction
{
    protected override void Execute(EditorActionContext context)
    {
    }
}

[EditorMenuSource(TestAreas.Menu)]
public sealed class DynamicMenuSource : EditorMenuSource
{
    public override void Build(EditorMenuContext context, EditorMenuBuilder builder)
        => builder.Add("Tools/Create/Generated", TestActionIds.Menu, order: 200);
}

[EditorDrop(TestAreas.Drop)]
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
