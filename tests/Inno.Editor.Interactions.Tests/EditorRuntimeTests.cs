using System;
using System.IO;
using System.Linq;

using Inno.Assets;
using Inno.Core.Assemblies;
using Inno.Core.Identity;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Editor.Core;
using Inno.Editor.Core.Commands;
using Inno.Editor.Core.DragDrop;
using Inno.Editor.Core.Menus;
using Inno.Editor.Core.Panels;
using Inno.Editor.Interactions;
using Inno.Engine.Scene;
using Xunit;

namespace Inno.Editor.Interactions.Tests;

public sealed class EditorRuntimeTests : IDisposable
{
    private readonly string m_projectRoot = Path.Combine(
        Path.GetTempPath(),
        "InnoEditorRuntimeTests",
        Guid.NewGuid().ToString("N"));
    private readonly EditorRuntime m_runtime;

    public EditorRuntimeTests()
    {
        Directory.CreateDirectory(Path.Combine(m_projectRoot, "Assets"));
        IdentityManager.Initialize();
        AssemblyManager.Initialize(new AssemblyManagerOptions
        {
            cacheDirectory = Path.Combine(m_projectRoot, "Library", "Assemblies")
        });
        TypeCacheManager.Initialize();
        SerializationManager.Initialize();
        AssetManager.Initialize(AssetManagerOptions.Create(
            Path.Combine(m_projectRoot, "Assets"),
            Path.Combine(m_projectRoot, "Library")) with
        {
            enableFileSystemWatcher = false
        });
        TestModule.startCount = 0;
        TestModule.stopCount = 0;
        TestPanel.attachCount = 0;
        TestPanel.detachCount = 0;
        DeferredAction.executeCount = 0;
        m_runtime = new EditorRuntime(m_projectRoot);
        m_runtime.Start();
    }

    public void Dispose()
    {
        m_runtime.Dispose();
        if (AssetManager.isInitialized)
            AssetManager.Shutdown();
        SceneManager.UnloadAllScenes();
        SerializationManager.Shutdown();
        TypeCacheManager.Shutdown();
        AssemblyManager.Shutdown();
        IdentityManager.Shutdown();
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
    public void ActionResolutionPrefersExactSurfaceAndTarget()
    {
        var target = new DerivedTarget();

        EditorActionState exact = m_runtime.context.Query(
            TestActionIds.Resolve,
            typeof(SpecialSurface),
            target);
        EditorActionState fallback = m_runtime.context.Query(
            TestActionIds.Resolve,
            typeof(OtherSurface),
            target);

        Assert.Equal("surface", exact.displayName);
        Assert.Equal("base", fallback.displayName);
    }

    [Fact]
    public void QueuedActionExecutesAtRuntimeSafePoint()
    {
        m_runtime.context.Enqueue(TestActionIds.Deferred, typeof(OtherSurface));
        Assert.Equal(0, DeferredAction.executeCount);

        m_runtime.Update(0f, 0f, isFocused: true);

        Assert.Equal(1, DeferredAction.executeCount);
    }

    [Fact]
    public void AttributeMenusSupportArbitraryDepthAndDynamicEntries()
    {
        EditorMenuModel menu = m_runtime.context.BuildMenu(typeof(TestMenuSurface));

        EditorMenuItem tools = Assert.Single(menu.items);
        Assert.Equal("Tools", tools.label);
        EditorMenuItem create = Assert.Single(tools.children);
        Assert.Equal("Create", create.label);
        Assert.Equal(["Asset", "Generated"], create.children.Select(static item => item.label));
    }

    [Fact]
    public void TypedDropRoutesAndCancelsItsManagedSession()
    {
        var source = new DragSource();
        var target = new DropTarget();
        var data = new EditorDragData(source, "source");
        Guid token = m_runtime.context.BeginDrag(new EditorDragContext(
            m_runtime.context,
            typeof(OtherSurface),
            data));
        var drop = new EditorDropContext(
            m_runtime.context,
            typeof(DropSurface),
            data,
            target,
            EditorDropPlacement.Into);

        Assert.True(m_runtime.context.QueryDrop(token, drop).canDrop);
        Assert.True(m_runtime.context.Drop(token, drop).accepted);
        Assert.True(target.wasDropped);
        Assert.False(m_runtime.context.TryGetDragData(token, out _));
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

public static class TestActionIds
{
    public const string Resolve = "tests.resolve";
    public const string Deferred = "tests.deferred";
    public const string Menu = "tests.menu";
}

public sealed class SpecialSurface;
public sealed class OtherSurface;
public sealed class TestMenuSurface;
public sealed class DropSurface;
public class BaseTarget;
public sealed class DerivedTarget : BaseTarget;

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

[EditorAction(TestActionIds.Resolve, typeof(SpecialSurface), priority: 100)]
public sealed class SurfaceResolveAction : EditorAction<DerivedTarget>
{
    protected override EditorActionState Query(EditorActionContext<DerivedTarget> context)
        => new(true, true, displayName: "surface");

    protected override void Execute(EditorActionContext<DerivedTarget> context)
    {
    }
}

[EditorAction(TestActionIds.Deferred)]
public sealed class DeferredAction : EditorAction
{
    public static int executeCount;

    public override void Execute(EditorActionContext context) => executeCount++;
}

[EditorAction(TestActionIds.Menu)]
[EditorMenu(typeof(TestMenuSurface), "Tools/Create/Asset", order: 100)]
public sealed class MenuAction : EditorAction
{
    public override void Execute(EditorActionContext context)
    {
    }
}

[EditorMenuSource(typeof(TestMenuSurface))]
public sealed class DynamicMenuSource : EditorMenuSource
{
    public override void Build(EditorMenuContext context, EditorMenuBuilder builder)
        => builder.Add("Tools/Create/Generated", TestActionIds.Menu, order: 200);
}

public sealed class DragSource;

public sealed class DropTarget
{
    public bool wasDropped { get; set; }
}

[EditorDrop(typeof(DropSurface))]
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
