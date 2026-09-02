using System;
using System.IO;
using System.Linq;
using System.Reflection;

using Inno.Extensibility.Modules;
using Inno.Extensibility.Types;
using Inno.Scene;
using Inno.Scene.Components;

using Xunit;

namespace Inno.Scene.Tests;

[Collection(SceneTestsCollection.NAME)]
public sealed class PluginSceneReloadTests : IDisposable
{
    private static readonly Guid S_COMPONENT_TYPE_ID =
        Guid.Parse("9f67d41e-082b-46d5-aaf0-dfc76c693182");

    private readonly ModuleHost m_modules;
    private readonly TypeCatalog m_types;
    private readonly SceneWorld m_world;
    private readonly SceneReloadService m_reload;
    private readonly IDisposable m_sceneScope;
    private AssemblyModuleHandle? m_activeModule;

    public PluginSceneReloadTests(SceneTestsFixture fixture)
    {
        m_modules = fixture.modules;
        m_types = fixture.types;
        m_world = fixture.world;
        m_reload = new SceneReloadService(fixture.world, fixture.serialization);
        m_sceneScope = fixture.world.EnterScope();
    }

    public void Dispose()
    {
        m_world.UnloadAllScenes();
        if (m_activeModule is AssemblyModuleHandle activeModule)
            _ = m_modules.Unload(activeModule);
        m_sceneScope.Dispose();
    }

    [Fact]
    public void RemovingAndRestoringAPluginGenerationPreservesItsComponentAsMissingState()
    {
        m_activeModule = m_modules.Load(CreateRequest());
        Type pluginComponentType = new TypeRef(S_COMPONENT_TYPE_ID).Resolve(m_types);
        GameScene scene = m_world.LoadNewScene("Plugin Reload");
        GameObject owner = scene.CreateObject("Plugin Owner");
        GameComponent original = owner.AddComponent(pluginComponentType);
        SetValue(original, 47);
        Guid componentPersistentId = original.identity.persistentId;

        using (AssemblyReloadSession removal = m_modules.BeginReload(
                   Array.Empty<AssemblyLoadRequest>(),
                   [CreateRequest().moduleName]))
        {
            ISceneReloadStateTransfer transfer = m_reload.Capture(
                removal.context.GetContext<TypeCacheReloadContext>());
            transfer.PrepareForActivation();
            removal.Activate();
            transfer.Apply();
            _ = removal.Complete();
            transfer.Complete();
        }
        m_activeModule = null;

        MissingGameComponent missing = Assert.IsType<MissingGameComponent>(
            owner.GetComponents().Single(component => component is not Transform));
        Assert.Equal(componentPersistentId, missing.identity.persistentId);
        Assert.Equal(S_COMPONENT_TYPE_ID, missing.missingType.stableId);
        Assert.Equal(pluginComponentType.FullName, missing.missingTypeName);
        Assert.False(missing.missingType.IsValid(m_types));

        using (AssemblyReloadSession recovery = m_modules.BeginReload([CreateRequest()]))
        {
            m_activeModule = recovery.context.module;
            ISceneReloadStateTransfer transfer = m_reload.Capture(
                recovery.context.GetContext<TypeCacheReloadContext>());
            transfer.PrepareForActivation();
            recovery.Activate();
            transfer.Apply();
            _ = recovery.Complete();
            transfer.Complete();
        }

        GameComponent recovered = owner.GetComponents().Single(component => component is not Transform);
        Assert.IsNotType<MissingGameComponent>(recovered);
        Assert.Equal(S_COMPONENT_TYPE_ID, m_types.GetTypeRef(recovered.GetType()).stableId);
        Assert.Equal(componentPersistentId, recovered.identity.persistentId);
        Assert.Equal(47, GetValue(recovered));
    }

    private static AssemblyLoadRequest CreateRequest()
        => new()
        {
            moduleName = "SceneReloadPluginTests",
            mainAssemblyPath = Path.Combine(
                AppContext.BaseDirectory,
                "Modules",
                "SceneReload",
                "Inno.Scene.Reload.TestModule.dll"),
            domain = AssemblyDomain.InnoPlugin,
            scope = AssemblyScope.Runtime
        };

    private static int GetValue(GameComponent component)
        => (int)GetValueProperty(component).GetValue(component)!;

    private static void SetValue(GameComponent component, int value)
        => GetValueProperty(component).SetValue(component, value);

    private static PropertyInfo GetValueProperty(GameComponent component)
        => component.GetType().GetProperty("value", BindingFlags.Instance | BindingFlags.Public)
           ?? throw new InvalidOperationException(
               $"Reload test component '{component.GetType().FullName}' does not expose its public state contract.");
}
