using System;
using System.IO;
using System.Linq;
using System.Reflection;

using Inno.Core.Assemblies;
using Inno.Core.Reflection;
using Inno.Editor.Core;
using Inno.Editor.Core.Panels;
using Inno.Editor.Interactions;
using Inno.Editor.Interactions.Actions;
using Inno.Editor.Interactions.DragDrop;
using Inno.Editor.Interactions.Menus;
using Inno.Editor.Interactions.Runtime;
using Inno.Editor.Interactions.Selection;
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
        TestPanel.attachCount = 0;
        TestPanel.detachCount = 0;
        DeferredAction.executeCount = 0;

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
    public void RuntimeDiscoversModulesAndPanelsWithoutRegistrationCalls()
    {
        Assert.True(TestModule.startCount > 0);
        Assert.True(TestPanel.attachCount > 0);
        Assert.True(m_runtime.panelCount >= 1);
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

[EditorModule]
public sealed class TestModule : EditorModule
{
    public static int startCount;
    public static int stopCount;

    protected override void OnStart(EditorContext context)
    {
        startCount++;
        if (startCount == 1)
            TypeCacheManager.Rebuild();
    }

    protected override void OnStop(EditorContext context) => stopCount++;
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
